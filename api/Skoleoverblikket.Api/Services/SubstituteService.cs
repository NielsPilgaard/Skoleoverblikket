using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.Services;

public sealed record AvailableStaffDto(Guid Id, string Name, StaffRole Role);

public sealed record BusyStaffDto(Guid Id, string Name, StaffRole Role, string ConflictDescription);

public sealed record StaffAvailabilityDto(IReadOnlyList<AvailableStaffDto> Available, IReadOnlyList<BusyStaffDto> Busy);

public sealed record AssignSubstituteRequest(Guid? SubstituteTeacherId, Guid? SubstituteAideId);

public sealed record SubstituteAssignmentDto(
	Guid Id, Guid? SubstituteTeacherId, string? SubstituteTeacherName, Guid? SubstituteAideId, string? SubstituteAideName);

public sealed record MySubstitutionDto(
	DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, string ClassName, string CourseName, string? RoomName, string AbsentStaffName);

/// <summary>Which seat in the lektion a vikar covers.</summary>
public enum SubstituteSeat { Teacher, Aide }

/// <summary>One lektion on a date to find free vikar candidates for.</summary>
public sealed record CandidateLesson(DateOnly Date, Guid SchemaSlotId, TimeOnly Start, TimeOnly End);

public enum SetSubstituteResult { Saved, NotFound, SamePersonBothRoles, UnknownStaff, StaffBusy }

public enum AssignLessonResult { Assigned, LessonNotFound, CandidateNotFound, CandidateBusy }

/// <summary>
/// Vikar cover. A vikar is stored per lektion per week on <see cref="WeekPlanSlot"/>
/// (SubstituteTeacherId / SubstituteAideId) — the recurring <see cref="SchemaSlot"/> template is
/// never touched. The only writer of those two fields. Availability treats a staff member as busy
/// when they teach or assist an overlapping lektion that day, already cover one as vikar, or are
/// themself registered absent.
/// </summary>
public sealed class SubstituteService(AppDbContext db, WeekPlanService weekPlans, INotificationService notifications)
{
	private static readonly CultureInfo Danish = CultureInfo.GetCultureInfo("da-DK");

	/// <summary>Free/busy for every staff member at a weekday + time slot in an ISO week.</summary>
	public async Task<StaffAvailabilityDto?> GetAvailabilityAsync(
		int isoYear, int isoWeek, DayOfWeek weekday, Guid timeSlotId, CancellationToken cancellationToken)
	{
		var timeSlot = await db.TimeSlots
			.AsNoTracking()
			.Where(t => t.Id == timeSlotId)
			.Select(t => new { t.StartTime, t.EndTime })
			.FirstOrDefaultAsync(cancellationToken);

		if (timeSlot is null)
		{
			return null;
		}

		var date = DateOnly.FromDateTime(ISOWeek.ToDateTime(isoYear, isoWeek, weekday));
		var conflicts = await GetConflictsAsync(date, timeSlot.StartTime, timeSlot.EndTime, cancellationToken);

		var allStaff = await db.Staff
			.AsNoTracking()
			.OrderBy(s => s.Name)
			.Select(s => new { s.Id, s.Name, s.Role })
			.ToListAsync(cancellationToken);

		var available = new List<AvailableStaffDto>();
		var busy = new List<BusyStaffDto>();
		foreach (var s in allStaff)
		{
			if (conflicts.TryGetValue(s.Id, out var reason))
			{
				busy.Add(new BusyStaffDto(s.Id, s.Name, s.Role, reason));
			}
			else
			{
				available.Add(new AvailableStaffDto(s.Id, s.Name, s.Role));
			}
		}

		return new StaffAvailabilityDto(available, busy);
	}

	/// <summary>
	/// Free staff for each lektion on its date, ranked: vikarer first, then everyone else, by name. The
	/// absent staff member is never a candidate (they are busy by their own absence). The lektion
	/// itself does not make anyone busy, so its aide can be offered to take over from the teacher.
	/// Staff and each date's bookings are loaded once, so a week-long absence costs a few queries,
	/// not a few per lektion. Results are in the same order as <paramref name="lessons"/>.
	/// </summary>
	public async Task<List<List<AvailableStaffDto>>> GetCandidatesAsync(
		IReadOnlyList<CandidateLesson> lessons, CancellationToken cancellationToken)
	{
		var allStaff = (await db.Staff
				.AsNoTracking()
				.Select(s => new AvailableStaffDto(s.Id, s.Name, s.Role))
				.ToListAsync(cancellationToken))
			.OrderBy(s => s.Role == StaffRole.Substitute ? 0 : 1)
			.ThenBy(s => s.Name, StringComparer.Create(Danish, CompareOptions.None))
			.ToList();

		var bookingsByDate = new Dictionary<DateOnly, DayBookings>();
		var result = new List<List<AvailableStaffDto>>(lessons.Count);
		foreach (var lesson in lessons)
		{
			if (!bookingsByDate.TryGetValue(lesson.Date, out var bookings))
			{
				bookings = await LoadDayBookingsAsync(lesson.Date, cancellationToken);
				bookingsByDate[lesson.Date] = bookings;
			}

			var conflicts = bookings.ConflictsAt(lesson.Start, lesson.End, ignoreSchemaSlotId: lesson.SchemaSlotId);
			result.Add(allStaff.Where(s => !conflicts.ContainsKey(s.Id)).ToList());
		}

		return result;
	}

	public Task<Guid?> GetWeekPlanClassIdAsync(Guid weekPlanId, CancellationToken cancellationToken) =>
		db.WeekPlans
			.Where(w => w.Id == weekPlanId)
			.Select(w => (Guid?)w.ClassId)
			.FirstOrDefaultAsync(cancellationToken);

	/// <summary>Sets or clears the vikar on an existing week plan slot (the ugeplan's "Tildel vikar" panel).</summary>
	public async Task<(SetSubstituteResult Result, SubstituteAssignmentDto? Assignment)> SetWeekPlanSlotSubstituteAsync(
		Guid weekPlanId, Guid slotId, AssignSubstituteRequest req, CancellationToken cancellationToken)
	{
		if (req.SubstituteTeacherId.HasValue && req.SubstituteTeacherId == req.SubstituteAideId)
		{
			return (SetSubstituteResult.SamePersonBothRoles, null);
		}

		foreach (var staffId in new[] { req.SubstituteTeacherId, req.SubstituteAideId })
		{
			if (staffId.HasValue && !await db.Staff.AnyAsync(s => s.Id == staffId.Value, cancellationToken))
			{
				return (SetSubstituteResult.UnknownStaff, null);
			}
		}

		var slot = await db.WeekPlanSlots
			.Include(s => s.WeekPlan)
			.Include(s => s.SchemaSlot).ThenInclude(ss => ss.TimeSlot)
			.FirstOrDefaultAsync(s => s.Id == slotId && s.WeekPlanId == weekPlanId, cancellationToken);

		if (slot is null)
		{
			return (SetSubstituteResult.NotFound, null);
		}

		var newlyAssigned = new[] { req.SubstituteTeacherId, req.SubstituteAideId }
			.Where(id => id.HasValue && id != slot.SubstituteTeacherId && id != slot.SubstituteAideId)
			.Select(id => id!.Value)
			.ToList();

		var date = DateOnly.FromDateTime(ISOWeek.ToDateTime(slot.WeekPlan.IsoYear, slot.WeekPlan.IsoWeek, slot.SchemaSlot.Weekday));

		await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
		if (newlyAssigned.Count > 0)
		{
			// Same lock as AssignForLessonAsync, taken in a fixed order so two requests can't deadlock.
			foreach (var lockKey in newlyAssigned.Select(id => BitConverter.ToInt64(id.ToByteArray(), 0)).Order())
			{
				await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
			}

			var conflicts = await GetConflictsAsync(
				date, slot.SchemaSlot.TimeSlot.StartTime, slot.SchemaSlot.TimeSlot.EndTime, cancellationToken,
				ignoreSchemaSlotId: slot.SchemaSlotId);
			if (newlyAssigned.Any(conflicts.ContainsKey))
			{
				return (SetSubstituteResult.StaffBusy, null);
			}
		}

		slot.SubstituteTeacherId = req.SubstituteTeacherId;
		slot.SubstituteAideId = req.SubstituteAideId;
		slot.UpdatedAt = DateTimeOffset.UtcNow;
		await db.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);

		foreach (var staffId in newlyAssigned)
		{
			await NotifyAssignedAsync(staffId, slot.SchemaSlotId, date, cancellationToken);
		}

		var names = await db.Staff
			.Where(s => s.Id == slot.SubstituteTeacherId || s.Id == slot.SubstituteAideId)
			.ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

		return (SetSubstituteResult.Saved, new SubstituteAssignmentDto(
			slot.Id,
			slot.SubstituteTeacherId,
			slot.SubstituteTeacherId is { } t ? names.GetValueOrDefault(t) : null,
			slot.SubstituteAideId,
			slot.SubstituteAideId is { } a ? names.GetValueOrDefault(a) : null));
	}

	/// <summary>
	/// Assigns (or with a null <paramref name="staffId"/> removes) the vikar for one seat of one
	/// lektion on one date. The availability re-check and the write run under a PostgreSQL advisory
	/// lock on the candidate, so two admins cannot book the same person into overlapping lektioner.
	/// </summary>
	public async Task<AssignLessonResult> AssignForLessonAsync(
		DateOnly date, Guid schemaSlotId, SubstituteSeat seat, Guid? staffId, CancellationToken cancellationToken)
	{
		var lesson = await db.SchemaSlots
			.AsNoTracking()
			.Where(s => s.Id == schemaSlotId && s.Weekday == date.DayOfWeek)
			.Where(s => (s.Schema.StartDate == null || s.Schema.StartDate <= date)
					 && (s.Schema.EndDate == null || s.Schema.EndDate >= date))
			.Select(s => new { s.Id, s.Schema.ClassId, s.TimeSlot.StartTime, s.TimeSlot.EndTime })
			.FirstOrDefaultAsync(cancellationToken);

		if (lesson is null)
		{
			return AssignLessonResult.LessonNotFound;
		}

		if (staffId.HasValue && !await db.Staff.AnyAsync(s => s.Id == staffId.Value, cancellationToken))
		{
			return AssignLessonResult.CandidateNotFound;
		}

		var isoYear = ISOWeek.GetYear(date.ToDateTime(TimeOnly.MinValue));
		var isoWeek = ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue));
		var weekPlan = await weekPlans.GetOrCreateWeekPlanAsync(lesson.ClassId, isoYear, isoWeek, cancellationToken);

		await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
		if (staffId.HasValue)
		{
			var lockKey = BitConverter.ToInt64(staffId.Value.ToByteArray(), 0);
			await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);

			var conflicts = await GetConflictsAsync(date, lesson.StartTime, lesson.EndTime, cancellationToken, ignoreSchemaSlotId: schemaSlotId);
			if (conflicts.ContainsKey(staffId.Value))
			{
				return AssignLessonResult.CandidateBusy;
			}
		}

		var slot = await weekPlans.GetOrAddSlotAsync(weekPlan, schemaSlotId, cancellationToken);
		var previous = seat == SubstituteSeat.Teacher ? slot.SubstituteTeacherId : slot.SubstituteAideId;
		if (seat == SubstituteSeat.Teacher)
		{
			slot.SubstituteTeacherId = staffId;
		}
		else
		{
			slot.SubstituteAideId = staffId;
		}

		slot.UpdatedAt = DateTimeOffset.UtcNow;
		await db.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);

		if (staffId.HasValue && staffId != previous)
		{
			await NotifyAssignedAsync(staffId.Value, schemaSlotId, date, cancellationToken);
		}

		return AssignLessonResult.Assigned;
	}

	/// <summary>Current vikar per (date, lektion) for the given ISO weeks — keyed by week plan.</summary>
	public async Task<Dictionary<(int IsoYear, int IsoWeek, Guid SchemaSlotId), (Guid? TeacherId, string? TeacherName, Guid? AideId, string? AideName)>> GetAssignmentsAsync(
		IReadOnlyCollection<Guid> schemaSlotIds, CancellationToken cancellationToken)
	{
		var rows = await db.WeekPlanSlots
			.AsNoTracking()
			.Where(s => schemaSlotIds.Contains(s.SchemaSlotId) && (s.SubstituteTeacherId != null || s.SubstituteAideId != null))
			.Select(s => new
			{
				s.WeekPlan.IsoYear,
				s.WeekPlan.IsoWeek,
				s.SchemaSlotId,
				s.SubstituteTeacherId,
				TeacherName = s.SubstituteTeacher != null ? s.SubstituteTeacher.Name : null,
				s.SubstituteAideId,
				AideName = s.SubstituteAide != null ? s.SubstituteAide.Name : null,
			})
			.ToListAsync(cancellationToken);

		return rows.ToDictionary(
			r => (r.IsoYear, r.IsoWeek, r.SchemaSlotId),
			r => (r.SubstituteTeacherId, r.TeacherName, r.SubstituteAideId, r.AideName));
	}

	/// <summary>
	/// Lektioner the caller covers as vikar from <paramref name="from"/> through <paramref name="to"/>.
	/// Empty when the caller has no staff record.
	/// </summary>
	public async Task<List<MySubstitutionDto>> GetMySubstitutionsAsync(
		string? subject, DateOnly from, DateOnly to, CancellationToken cancellationToken)
	{
		var staffId = await db.Staff
			.AsNoTracking()
			.Where(s => s.KeycloakSubject == subject)
			.Select(s => (Guid?)s.Id)
			.FirstOrDefaultAsync(cancellationToken);

		if (staffId is null)
		{
			return [];
		}

		// ISO years can straddle calendar years by a few days, hence the ±1 margin.
		var minYear = from.Year - 1;
		var maxYear = to.Year + 1;
		var rows = await db.WeekPlanSlots
			.AsNoTracking()
			.Where(s => s.SubstituteTeacherId == staffId || s.SubstituteAideId == staffId)
			.Where(s => s.WeekPlan.IsoYear >= minYear && s.WeekPlan.IsoYear <= maxYear)
			.Select(s => new
			{
				s.WeekPlan.IsoYear,
				s.WeekPlan.IsoWeek,
				s.SchemaSlot.Weekday,
				s.SchemaSlot.TimeSlot.StartTime,
				s.SchemaSlot.TimeSlot.EndTime,
				ClassName = s.WeekPlan.Class.Name,
				CourseName = s.OverrideCourse != null ? s.OverrideCourse.Name : s.SchemaSlot.Course.Name,
				RoomName = s.SchemaSlot.Room != null ? s.SchemaSlot.Room.Name : null,
				AbsentName = s.SubstituteTeacherId == staffId
					? s.SchemaSlot.Teacher.Name
					: s.SchemaSlot.Aide != null ? s.SchemaSlot.Aide.Name : "",
			})
			.ToListAsync(cancellationToken);

		return rows
			.Select(r => new MySubstitutionDto(
				DateOnly.FromDateTime(ISOWeek.ToDateTime(r.IsoYear, r.IsoWeek, r.Weekday)),
				r.StartTime, r.EndTime, r.ClassName, r.CourseName, r.RoomName, r.AbsentName))
			.Where(r => r.Date >= from && r.Date <= to)
			.OrderBy(r => r.Date)
			.ThenBy(r => r.StartTime)
			.ToList();
	}

	/// <summary>
	/// Removes every vikar booking <paramref name="staffId"/> holds from <paramref name="from"/> through
	/// <paramref name="to"/> — used when the vikar is themself reported absent, so the lektioner they
	/// were covering show up as uncovered again. With a time window, only lektioner overlapping it are
	/// released. Returns how many bookings were removed.
	/// </summary>
	public async Task<int> ReleaseBookingsAsync(
		Guid staffId, DateOnly from, DateOnly to, TimeOnly? start, TimeOnly? end, CancellationToken cancellationToken)
	{
		// ISO years can straddle calendar years by a few days, hence the ±1 margin.
		var minYear = from.Year - 1;
		var maxYear = to.Year + 1;
		var slots = await db.WeekPlanSlots
			.Include(s => s.WeekPlan)
			.Include(s => s.SchemaSlot).ThenInclude(ss => ss.TimeSlot)
			.Where(s => s.SubstituteTeacherId == staffId || s.SubstituteAideId == staffId)
			.Where(s => s.WeekPlan.IsoYear >= minYear && s.WeekPlan.IsoYear <= maxYear)
			.ToListAsync(cancellationToken);

		var released = 0;
		foreach (var slot in slots)
		{
			var date = DateOnly.FromDateTime(ISOWeek.ToDateTime(slot.WeekPlan.IsoYear, slot.WeekPlan.IsoWeek, slot.SchemaSlot.Weekday));
			if (date < from || date > to)
			{
				continue;
			}

			if (start is { } s && end is { } e
				&& !(slot.SchemaSlot.TimeSlot.StartTime < e && s < slot.SchemaSlot.TimeSlot.EndTime))
			{
				continue;
			}

			if (slot.SubstituteTeacherId == staffId)
			{
				slot.SubstituteTeacherId = null;
			}

			if (slot.SubstituteAideId == staffId)
			{
				slot.SubstituteAideId = null;
			}

			slot.UpdatedAt = DateTimeOffset.UtcNow;
			released++;
		}

		if (released > 0)
		{
			await db.SaveChangesAsync(cancellationToken);
		}

		return released;
	}

	/// <summary>Staff id → why they are busy for a time range on a date.</summary>
	private async Task<Dictionary<Guid, string>> GetConflictsAsync(
		DateOnly date, TimeOnly start, TimeOnly end, CancellationToken cancellationToken, Guid? ignoreSchemaSlotId = null) =>
		(await LoadDayBookingsAsync(date, cancellationToken)).ConflictsAt(start, end, ignoreSchemaSlotId);

	/// <summary>Everything that can make a staff member busy on a date: absences, lektioner and vikar cover.</summary>
	private async Task<DayBookings> LoadDayBookingsAsync(DateOnly date, CancellationToken cancellationToken)
	{
		var weekday = date.DayOfWeek;

		var absences = await db.StaffAbsences
			.AsNoTracking()
			.Where(a => a.Date <= date && (a.EndDate ?? a.Date) >= date)
			.Select(a => new AbsenceBooking(a.StaffId, a.StartTime, a.EndTime))
			.ToListAsync(cancellationToken);

		var lessons = await db.SchemaSlots
			.AsNoTracking()
			.Where(s => s.Weekday == weekday
					 && (s.Schema.StartDate == null || s.Schema.StartDate <= date)
					 && (s.Schema.EndDate == null || s.Schema.EndDate >= date))
			.Select(s => new LessonBooking(
				s.Id, s.TimeSlot.StartTime, s.TimeSlot.EndTime, s.TeacherId, s.AideId, s.Schema.Class.Name, s.Course.Name))
			.ToListAsync(cancellationToken);

		var isoYear = ISOWeek.GetYear(date.ToDateTime(TimeOnly.MinValue));
		var isoWeek = ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue));
		var covering = await db.WeekPlanSlots
			.AsNoTracking()
			.Where(s => s.WeekPlan.IsoYear == isoYear && s.WeekPlan.IsoWeek == isoWeek
					 && s.SchemaSlot.Weekday == weekday
					 && (s.SubstituteTeacherId != null || s.SubstituteAideId != null))
			.Select(s => new LessonBooking(
				s.SchemaSlotId, s.SchemaSlot.TimeSlot.StartTime, s.SchemaSlot.TimeSlot.EndTime,
				s.SubstituteTeacherId, s.SubstituteAideId, s.WeekPlan.Class.Name, s.SchemaSlot.Course.Name))
			.ToListAsync(cancellationToken);

		return new DayBookings(absences, lessons, covering);
	}

	private sealed record AbsenceBooking(Guid StaffId, TimeOnly? StartTime, TimeOnly? EndTime);

	/// <summary>A lektion on the day: its own teacher/aide, or the vikarer covering it.</summary>
	private sealed record LessonBooking(
		Guid SchemaSlotId, TimeOnly StartTime, TimeOnly EndTime, Guid? FirstStaffId, Guid? SecondStaffId, string ClassName, string CourseName);

	private sealed record DayBookings(
		List<AbsenceBooking> Absences, List<LessonBooking> Lessons, List<LessonBooking> Covering)
	{
		public Dictionary<Guid, string> ConflictsAt(TimeOnly start, TimeOnly end, Guid? ignoreSchemaSlotId)
		{
			var conflicts = new Dictionary<Guid, string>();

			// A partial-day absence only makes them busy when its window overlaps the lektion.
			foreach (var a in Absences.Where(a => a.StartTime is null || a.EndTime is null || (a.StartTime < end && start < a.EndTime)))
			{
				conflicts.TryAdd(a.StaffId, "Fraværende");
			}

			Add(Lessons, "Optaget");
			Add(Covering, "Vikar");
			return conflicts;

			void Add(List<LessonBooking> bookings, string label)
			{
				foreach (var b in bookings.Where(b => b.StartTime < end && start < b.EndTime && b.SchemaSlotId != ignoreSchemaSlotId))
				{
					var reason = $"{label}: {b.ClassName} – {b.CourseName}";
					if (b.FirstStaffId.HasValue)
					{
						conflicts.TryAdd(b.FirstStaffId.Value, reason);
					}

					if (b.SecondStaffId.HasValue)
					{
						conflicts.TryAdd(b.SecondStaffId.Value, reason);
					}
				}
			}
		}
	}

	private async Task NotifyAssignedAsync(Guid staffId, Guid schemaSlotId, DateOnly date, CancellationToken cancellationToken)
	{
		var lesson = await db.SchemaSlots
			.AsNoTracking()
			.Where(s => s.Id == schemaSlotId)
			.Select(s => new { s.TimeSlot.StartTime, ClassName = s.Schema.Class.Name, CourseName = s.Course.Name })
			.FirstAsync(cancellationToken);

		var body = $"Du er vikar i {lesson.ClassName} ({lesson.CourseName}) {date.ToString("dddd d. MMMM", Danish)} kl. {lesson.StartTime:HH\\:mm}";
		await notifications.CreateAsync(staffId, RecipientType.Staff, NotificationType.SubstituteAssigned, schemaSlotId, body, cancellationToken);
	}
}
