using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Services;

/// <param name="StaffId">Who is absent. Null = the caller. Only admins may report for someone else.</param>
/// <param name="StartTime">Partial day: first time out. Set together with <paramref name="EndTime"/>, single day only.</param>
public sealed record ReportStaffAbsenceRequest(
	Guid? StaffId,
	DateOnly Date,
	DateOnly? EndDate,
	[StringLength(500)] string? Reason,
	TimeOnly? StartTime = null,
	TimeOnly? EndTime = null);

public sealed record UpdateStaffAbsenceRequest(
	DateOnly Date,
	DateOnly? EndDate,
	TimeOnly? StartTime,
	TimeOnly? EndTime,
	[StringLength(500)] string? Reason);

public sealed record StaffAbsenceDto(
	Guid Id,
	Guid StaffId,
	string StaffName,
	StaffRole Role,
	DateOnly Date,
	DateOnly? EndDate,
	TimeOnly? StartTime,
	TimeOnly? EndTime,
	string? Reason,
	string? ReportedByName,
	DateTimeOffset CreatedAt,
	int AffectedLessonCount,
	int CoveredLessonCount,
	bool CanDelete);

public sealed record AffectedLessonDto(
	DateOnly Date,
	Guid SchemaSlotId,
	Guid ClassId,
	string ClassName,
	string CourseName,
	TimeOnly StartTime,
	TimeOnly EndTime,
	SubstituteSeat Seat,
	Guid? SubstituteId,
	string? SubstituteName,
	IReadOnlyList<AvailableStaffDto> Candidates);

public sealed record StaffAbsenceDetailDto(StaffAbsenceDto Absence, IReadOnlyList<AffectedLessonDto> Lessons);

public sealed record AssignAbsenceSubstituteRequest(Guid SchemaSlotId, DateOnly Date, Guid? StaffId);

public enum StaffAbsenceReportResult { Created, NoStaffRecord, StaffNotFound, NotAllowed, InvalidDates, Overlaps }

public enum StaffAbsenceUpdateResult { Updated, NotFound, NotAllowed, InvalidDates, Overlaps }

public enum StaffAbsenceDeleteResult { Deleted, NotFound, NotAllowed }

public enum AbsenceSubstituteResult { Assigned, NotFound, NotAffected, CandidateNotFound, CandidateBusy }

/// <summary>
/// Staff absence (a teacher or pædagog is out) and the lektioner it leaves without cover. The only
/// writer of <see cref="StaffAbsence"/>. Vikar assignment itself is delegated to
/// <see cref="SubstituteService"/>, which owns the vikar fields on <see cref="WeekPlanSlot"/>.
/// </summary>
public sealed class StaffAbsenceService(
	AppDbContext db,
	ITenantContext tenant,
	SubstituteService substitutes,
	INotificationService notifications)
{
	private const int MaxAbsenceDays = 90;
	private static readonly CultureInfo Danish = CultureInfo.GetCultureInfo("da-DK");

	private sealed record LessonRow(
		Guid SchemaSlotId, Guid ClassId, string ClassName, string CourseName,
		DayOfWeek Weekday, TimeOnly StartTime, TimeOnly EndTime, DateOnly? SchemaStart, DateOnly? SchemaEnd,
		SubstituteSeat Seat);

	/// <summary>A validated range: <see cref="EndDate"/> null for one day, times only on a single day.</summary>
	private sealed record Range(DateOnly Date, DateOnly? EndDate, TimeOnly? StartTime, TimeOnly? EndTime)
	{
		public DateOnly Last => EndDate ?? Date;
	}

	private sealed record LoadedLesson(DateOnly Date, LessonRow Lesson, Guid? SubstituteId);

	private sealed record LoadedAbsence(
		Guid Id, Guid StaffId, string StaffName, StaffRole Role, DateOnly Date, DateOnly? EndDate,
		TimeOnly? StartTime, TimeOnly? EndTime, string? Reason, string? ReportedByName, DateTimeOffset CreatedAt,
		List<LoadedLesson> Lessons);

	public async Task<(StaffAbsenceReportResult Result, Guid? Id)> ReportAsync(
		string? subject, bool isAdminRole, ReportStaffAbsenceRequest req, CancellationToken cancellationToken)
	{
		var caller = await db.Staff
			.AsNoTracking()
			.Where(s => s.KeycloakSubject == subject)
			.Select(s => new { s.Id, s.Name, s.IsAdmin })
			.FirstOrDefaultAsync(cancellationToken);

		var isAdmin = isAdminRole || caller?.IsAdmin == true;
		var staffId = req.StaffId ?? caller?.Id;
		if (staffId is null)
		{
			return (StaffAbsenceReportResult.NoStaffRecord, null);
		}

		if (staffId != caller?.Id && !isAdmin)
		{
			return (StaffAbsenceReportResult.NotAllowed, null);
		}

		var absentName = await db.Staff
			.Where(s => s.Id == staffId)
			.Select(s => s.Name)
			.FirstOrDefaultAsync(cancellationToken);

		if (absentName is null)
		{
			return (StaffAbsenceReportResult.StaffNotFound, null);
		}

		var range = ValidateRange(req.Date, req.EndDate, req.StartTime, req.EndTime);
		if (range is null)
		{
			return (StaffAbsenceReportResult.InvalidDates, null);
		}

		var absence = await WithStaffLockAsync(staffId.Value, async () =>
		{
			if (await OverlapsAsync(staffId.Value, range, excludeId: null, cancellationToken))
			{
				return null;
			}

			var created = new StaffAbsence
			{
				Id = Guid.NewGuid(),
				TenantId = tenant.TenantId,
				StaffId = staffId.Value,
				ReportedByStaffId = caller?.Id,
				Date = range.Date,
				EndDate = range.EndDate,
				StartTime = range.StartTime,
				EndTime = range.EndTime,
				Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim(),
			};
			db.StaffAbsences.Add(created);
			await db.SaveChangesAsync(cancellationToken);
			return created;
		}, cancellationToken);

		if (absence is null)
		{
			return (StaffAbsenceReportResult.Overlaps, null);
		}

		// They can't cover for anyone while out: free their vikar bookings so those lektioner show as uncovered.
		var released = await substitutes.ReleaseBookingsAsync(
			absence.StaffId, range.Date, range.Last, range.StartTime, range.EndTime, cancellationToken);

		await NotifyAdminsAsync(absence.Id, caller?.Id, $"{absentName} er meldt fraværende {FormatRange(range)}", released, cancellationToken);

		return (StaffAbsenceReportResult.Created, absence.Id);
	}

	/// <summary>
	/// Changes dates, times or reason. Same permission as <see cref="DeleteAsync"/>. Vikar cover on
	/// lektioner the absence no longer touches is removed: the teacher is there after all.
	/// </summary>
	public async Task<StaffAbsenceUpdateResult> UpdateAsync(
		Guid id, string? subject, bool isAdminRole, UpdateStaffAbsenceRequest req, CancellationToken cancellationToken)
	{
		var absence = await db.StaffAbsences
			.Include(a => a.Staff)
			.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
		if (absence is null)
		{
			return StaffAbsenceUpdateResult.NotFound;
		}

		var caller = await db.Staff
			.AsNoTracking()
			.Where(s => s.KeycloakSubject == subject)
			.Select(s => new { s.Id, s.IsAdmin })
			.FirstOrDefaultAsync(cancellationToken);

		var today = SchoolDayCalendar.Today();
		var isAdmin = isAdminRole || caller?.IsAdmin == true;
		if (!isAdmin && (caller?.Id != absence.StaffId || absence.Date <= today))
		{
			return StaffAbsenceUpdateResult.NotAllowed;
		}

		var range = ValidateRange(req.Date, req.EndDate, req.StartTime, req.EndTime);
		if (range is null || (!isAdmin && range.Date <= today))
		{
			return StaffAbsenceUpdateResult.InvalidDates;
		}

		var saved = await WithStaffLockAsync(absence.StaffId, async () =>
		{
			if (await OverlapsAsync(absence.StaffId, range, excludeId: absence.Id, cancellationToken))
			{
				return false;
			}

			var before = await GetAffectedLessonsAsync(
				absence.StaffId, new Range(absence.Date, absence.EndDate, absence.StartTime, absence.EndTime), cancellationToken);
			var after = (await GetAffectedLessonsAsync(absence.StaffId, range, cancellationToken))
				.Select(l => (l.Date, l.Lesson.SchemaSlotId, l.Lesson.Seat))
				.ToHashSet();
			await ClearCoverAsync(
				absence.StaffId,
				absence.Id,
				before.Where(l => l.Date >= today && !after.Contains((l.Date, l.Lesson.SchemaSlotId, l.Lesson.Seat))).ToList(),
				cancellationToken);

			absence.Date = range.Date;
			absence.EndDate = range.EndDate;
			absence.StartTime = range.StartTime;
			absence.EndTime = range.EndTime;
			absence.Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim();
			await db.SaveChangesAsync(cancellationToken);
			return true;
		}, cancellationToken);

		if (!saved)
		{
			return StaffAbsenceUpdateResult.Overlaps;
		}

		var released = await substitutes.ReleaseBookingsAsync(
			absence.StaffId, range.Date, range.Last, range.StartTime, range.EndTime, cancellationToken);

		await NotifyAdminsAsync(absence.Id, caller?.Id, $"Fravær for {absence.Staff.Name} er ændret til {FormatRange(range)}", released, cancellationToken);

		return StaffAbsenceUpdateResult.Updated;
	}

	/// <summary>
	/// Lektioner from today through the next <paramref name="days"/> - 1 days that an absence leaves
	/// without a vikar: the dashboard's "mangler vikar" number.
	/// </summary>
	public async Task<int> CountUncoveredLessonsAsync(int days, CancellationToken cancellationToken)
	{
		var from = SchoolDayCalendar.Today();
		var to = from.AddDays(days - 1);
		var absences = await LoadAsync(a => a.Date <= to && (a.EndDate ?? a.Date) >= from, cancellationToken);
		// Distinct: two partial-day absences on one date can both touch the same lektion.
		return absences
			.SelectMany(a => a.Lessons)
			.Where(l => l.Date >= from && l.Date <= to && l.SubstituteId is null)
			.Select(l => (l.Date, l.Lesson.SchemaSlotId, l.Lesson.Seat))
			.Distinct()
			.Count();
	}

	public async Task<List<StaffAbsenceDto>> GetMineAsync(string? subject, CancellationToken cancellationToken)
	{
		var staffId = await db.Staff
			.Where(s => s.KeycloakSubject == subject)
			.Select(s => (Guid?)s.Id)
			.FirstOrDefaultAsync(cancellationToken);

		return staffId is null ? [] : await ListAsync(a => a.StaffId == staffId, isAdmin: false, staffId, cancellationToken);
	}

	/// <summary>All staff absences overlapping [<paramref name="from"/>, <paramref name="to"/>], newest first.</summary>
	public Task<List<StaffAbsenceDto>> GetAllAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
		ListAsync(a => a.Date <= to && (a.EndDate ?? a.Date) >= from, isAdmin: true, callerStaffId: null, cancellationToken);

	public async Task<StaffAbsenceDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken)
	{
		var list = await ListAsync(a => a.Id == id, isAdmin: true, callerStaffId: null, cancellationToken);
		if (list.Count == 0)
		{
			return null;
		}

		var absence = list[0];
		var lessons = await GetAffectedLessonsAsync(
			absence.StaffId, new Range(absence.Date, absence.EndDate, absence.StartTime, absence.EndTime), cancellationToken);
		var assignments = await substitutes.GetAssignmentsAsync(lessons.Select(l => l.Lesson.SchemaSlotId).Distinct().ToList(), cancellationToken);

		var result = new List<AffectedLessonDto>();
		foreach (var (date, lesson) in lessons)
		{
			var (substituteId, substituteName) = CurrentSubstitute(assignments, date, lesson);
			var candidates = await substitutes.GetCandidatesAsync(date, lesson.SchemaSlotId, lesson.StartTime, lesson.EndTime, cancellationToken);
			result.Add(new AffectedLessonDto(
				date, lesson.SchemaSlotId, lesson.ClassId, lesson.ClassName, lesson.CourseName,
				lesson.StartTime, lesson.EndTime, lesson.Seat, substituteId, substituteName, candidates));
		}

		return new StaffAbsenceDetailDto(absence, result);
	}

	public async Task<AbsenceSubstituteResult> AssignSubstituteAsync(
		Guid absenceId, AssignAbsenceSubstituteRequest req, CancellationToken cancellationToken)
	{
		var absence = await db.StaffAbsences
			.AsNoTracking()
			.FirstOrDefaultAsync(a => a.Id == absenceId, cancellationToken);

		if (absence is null)
		{
			return AbsenceSubstituteResult.NotFound;
		}

		var lessons = await GetAffectedLessonsAsync(
			absence.StaffId, new Range(req.Date, null, absence.StartTime, absence.EndTime), cancellationToken);
		var match = lessons.FirstOrDefault(l => l.Lesson.SchemaSlotId == req.SchemaSlotId);
		if (match.Lesson is null || req.Date < absence.Date || req.Date > (absence.EndDate ?? absence.Date))
		{
			return AbsenceSubstituteResult.NotAffected;
		}

		return await substitutes.AssignForLessonAsync(req.Date, req.SchemaSlotId, match.Lesson.Seat, req.StaffId, cancellationToken) switch
		{
			AssignLessonResult.Assigned => AbsenceSubstituteResult.Assigned,
			AssignLessonResult.CandidateNotFound => AbsenceSubstituteResult.CandidateNotFound,
			AssignLessonResult.CandidateBusy => AbsenceSubstituteResult.CandidateBusy,
			_ => AbsenceSubstituteResult.NotAffected,
		};
	}

	/// <summary>
	/// Admins can withdraw any absence; staff can withdraw their own until it starts. Vikar cover
	/// already booked for the remaining days is removed with it — the teacher is back.
	/// </summary>
	public async Task<StaffAbsenceDeleteResult> DeleteAsync(
		Guid id, string? subject, bool isAdminRole, CancellationToken cancellationToken)
	{
		var absence = await db.StaffAbsences.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
		if (absence is null)
		{
			return StaffAbsenceDeleteResult.NotFound;
		}

		var caller = await db.Staff
			.AsNoTracking()
			.Where(s => s.KeycloakSubject == subject)
			.Select(s => new { s.Id, s.IsAdmin })
			.FirstOrDefaultAsync(cancellationToken);

		var today = SchoolDayCalendar.Today();
		var isAdmin = isAdminRole || caller?.IsAdmin == true;
		if (!isAdmin && (caller?.Id != absence.StaffId || absence.Date <= today))
		{
			return StaffAbsenceDeleteResult.NotAllowed;
		}

		var lessons = await GetAffectedLessonsAsync(
			absence.StaffId, new Range(absence.Date, absence.EndDate, absence.StartTime, absence.EndTime), cancellationToken);

		// Clear cover first: if a clear fails the absence survives and the delete can be retried,
		// instead of leaving vikarer booked for an absence that no longer exists.
		await ClearCoverAsync(absence.StaffId, absence.Id, lessons.Where(l => l.Date >= today).ToList(), cancellationToken);

		db.StaffAbsences.Remove(absence);
		await db.SaveChangesAsync(cancellationToken);

		return StaffAbsenceDeleteResult.Deleted;
	}

	private async Task<List<StaffAbsenceDto>> ListAsync(
		System.Linq.Expressions.Expression<Func<StaffAbsence, bool>> filter,
		bool isAdmin,
		Guid? callerStaffId,
		CancellationToken cancellationToken)
	{
		var today = SchoolDayCalendar.Today();
		return (await LoadAsync(filter, cancellationToken))
			.Select(a => new StaffAbsenceDto(
				a.Id, a.StaffId, a.StaffName, a.Role, a.Date, a.EndDate, a.StartTime, a.EndTime,
				a.Reason, a.ReportedByName, a.CreatedAt,
				a.Lessons.Count, a.Lessons.Count(l => l.SubstituteId is not null),
				isAdmin || (a.StaffId == callerStaffId && a.Date > today)))
			.ToList();
	}

	/// <summary>Absences matching <paramref name="filter"/>, newest first, each with its affected lektioner and their current vikar.</summary>
	private async Task<List<LoadedAbsence>> LoadAsync(
		System.Linq.Expressions.Expression<Func<StaffAbsence, bool>> filter,
		CancellationToken cancellationToken)
	{
		var rows = await db.StaffAbsences
			.AsNoTracking()
			.Where(filter)
			.OrderByDescending(a => a.Date)
			.ThenByDescending(a => a.CreatedAt)
			.Select(a => new
			{
				a.Id,
				a.StaffId,
				StaffName = a.Staff.Name,
				a.Staff.Role,
				a.Date,
				a.EndDate,
				a.StartTime,
				a.EndTime,
				a.Reason,
				ReportedByName = a.ReportedByStaff != null ? a.ReportedByStaff.Name : null,
				a.CreatedAt,
			})
			.ToListAsync(cancellationToken);

		// One lesson lookup per staff member over the span of all their absences, not one per absence.
		var lessonsByStaff = new Dictionary<Guid, List<(DateOnly Date, LessonRow Lesson)>>();
		foreach (var group in rows.GroupBy(a => a.StaffId))
		{
			lessonsByStaff[group.Key] = await GetAffectedLessonsAsync(
				group.Key, new Range(group.Min(a => a.Date), group.Max(a => a.EndDate ?? a.Date), null, null), cancellationToken);
		}

		var assignments = await substitutes.GetAssignmentsAsync(
			lessonsByStaff.Values.SelectMany(l => l).Select(l => l.Lesson.SchemaSlotId).Distinct().ToList(), cancellationToken);

		return rows
			.Select(a =>
			{
				var range = new Range(a.Date, a.EndDate, a.StartTime, a.EndTime);
				var lessons = lessonsByStaff[a.StaffId]
					.Where(l => l.Date >= range.Date && l.Date <= range.Last && InWindow(l.Lesson, range))
					.Select(l => new LoadedLesson(l.Date, l.Lesson, CurrentSubstitute(assignments, l.Date, l.Lesson).Id))
					.ToList();
				return new LoadedAbsence(
					a.Id, a.StaffId, a.StaffName, a.Role, a.Date, a.EndDate, a.StartTime, a.EndTime,
					a.Reason, a.ReportedByName, a.CreatedAt, lessons);
			})
			.ToList();
	}

	/// <summary>
	/// Null when the dates are invalid: end before start, 90 days or more, more than 14 days back, or a
	/// time window that is half set, empty, or on a multi-day absence.
	/// </summary>
	private static Range? ValidateRange(DateOnly date, DateOnly? endDate, TimeOnly? startTime, TimeOnly? endTime)
	{
		var end = endDate is { } e && e != date ? e : (DateOnly?)null;
		var last = end ?? date;
		if (last < date || date < SchoolDayCalendar.Today().AddDays(-14) || last.DayNumber - date.DayNumber >= MaxAbsenceDays)
		{
			return null;
		}

		if (startTime.HasValue != endTime.HasValue || startTime >= endTime || (startTime.HasValue && end.HasValue))
		{
			return null;
		}

		return new Range(date, end, startTime, endTime);
	}

	/// <summary>Another absence for the same person touches the range. Two partial days only clash when their windows overlap.</summary>
	private async Task<bool> OverlapsAsync(Guid staffId, Range range, Guid? excludeId, CancellationToken cancellationToken)
	{
		var others = await db.StaffAbsences
			.AsNoTracking()
			.Where(a => a.StaffId == staffId && a.Id != excludeId)
			.Where(a => a.Date <= range.Last && (a.EndDate ?? a.Date) >= range.Date)
			.Select(a => new { a.StartTime, a.EndTime })
			.ToListAsync(cancellationToken);

		return others.Any(o => o.StartTime is null || o.EndTime is null || range.StartTime is null || range.EndTime is null
			|| (o.StartTime < range.EndTime && range.StartTime < o.EndTime));
	}

	/// <summary>
	/// Runs <paramref name="action"/> under a PostgreSQL advisory lock on the staff member, so two
	/// requests can't both pass the overlap check and save overlapping absences. A session lock, not a
	/// transaction lock: the action clears vikar cover through <see cref="SubstituteService"/>, which
	/// opens its own transactions. Same key as SubstituteService's booking lock: one lock per person.
	/// </summary>
	private async Task<T> WithStaffLockAsync<T>(Guid staffId, Func<Task<T>> action, CancellationToken cancellationToken)
	{
		var lockKey = BitConverter.ToInt64(staffId.ToByteArray(), 0);
		await db.Database.OpenConnectionAsync(cancellationToken);
		try
		{
			await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({lockKey})", cancellationToken);
			try
			{
				return await action();
			}
			finally
			{
				await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({lockKey})", CancellationToken.None);
			}
		}
		finally
		{
			await db.Database.CloseConnectionAsync();
		}
	}

	/// <summary>
	/// Removes vikar cover from <paramref name="lessons"/>, except where another absence for the same person
	/// still covers the lektion: two partial days on one date can both touch it.
	/// </summary>
	private async Task ClearCoverAsync(
		Guid staffId, Guid absenceId, List<(DateOnly Date, LessonRow Lesson)> lessons, CancellationToken cancellationToken)
	{
		if (lessons.Count == 0)
		{
			return;
		}

		var first = lessons.Min(l => l.Date);
		var last = lessons.Max(l => l.Date);
		var others = (await db.StaffAbsences
			.AsNoTracking()
			.Where(a => a.StaffId == staffId && a.Id != absenceId)
			.Where(a => a.Date <= last && (a.EndDate ?? a.Date) >= first)
			.Select(a => new { a.Date, a.EndDate, a.StartTime, a.EndTime })
			.ToListAsync(cancellationToken))
			.Select(a => new Range(a.Date, a.EndDate, a.StartTime, a.EndTime))
			.ToList();
		lessons = lessons
			.Where(l => !others.Any(o => l.Date >= o.Date && l.Date <= o.Last && InWindow(l.Lesson, o)))
			.ToList();

		var assignments = await substitutes.GetAssignmentsAsync(lessons.Select(l => l.Lesson.SchemaSlotId).Distinct().ToList(), cancellationToken);
		foreach (var (date, lesson) in lessons)
		{
			if (CurrentSubstitute(assignments, date, lesson).Id is not null)
			{
				await substitutes.AssignForLessonAsync(date, lesson.SchemaSlotId, lesson.Seat, null, cancellationToken);
			}
		}
	}

	/// <summary>Admins need to find vikarer. Tell them, except the one who just filed it.</summary>
	private async Task NotifyAdminsAsync(Guid absenceId, Guid? callerId, string body, int released, CancellationToken cancellationToken)
	{
		if (released > 0)
		{
			body += released == 1
				? ". 1 lektion, hvor de var vikar, mangler nu en ny vikar"
				: $". {released} lektioner, hvor de var vikar, mangler nu en ny vikar";
		}

		var adminIds = await db.Staff
			.Where(s => s.IsAdmin && s.Id != callerId)
			.Select(s => s.Id)
			.ToListAsync(cancellationToken);
		await notifications.CreateBatchAsync(
			adminIds.Select(id => new NotificationRequest(id, RecipientType.Staff, NotificationType.StaffAbsenceReported, absenceId, body)),
			cancellationToken);
	}

	private static bool InWindow(LessonRow lesson, Range range) =>
		range.StartTime is not { } start || range.EndTime is not { } end
		|| (lesson.StartTime < end && start < lesson.EndTime);

	/// <summary>
	/// Every lektion the staff member teaches or assists on school days in the range, using the
	/// schema active on each date. A partial day only counts lektioner overlapping its window.
	/// </summary>
	private async Task<List<(DateOnly Date, LessonRow Lesson)>> GetAffectedLessonsAsync(
		Guid staffId, Range range, CancellationToken cancellationToken)
	{
		var from = range.Date;
		var last = range.Last;
		var lessons = await db.SchemaSlots
			.AsNoTracking()
			.Where(s => s.TeacherId == staffId || s.AideId == staffId)
			.Where(s => (s.Schema.StartDate == null || s.Schema.StartDate <= last)
					 && (s.Schema.EndDate == null || s.Schema.EndDate >= from))
			.Select(s => new LessonRow(
				s.Id, s.Schema.ClassId, s.Schema.Class.Name, s.Course.Name,
				s.Weekday, s.TimeSlot.StartTime, s.TimeSlot.EndTime, s.Schema.StartDate, s.Schema.EndDate,
				s.TeacherId == staffId ? SubstituteSeat.Teacher : SubstituteSeat.Aide))
			.ToListAsync(cancellationToken);

		if (lessons.Count == 0)
		{
			return [];
		}

		var calendar = await SchoolDayCalendar.LoadAsync(db, from, last, cancellationToken);
		var result = new List<(DateOnly, LessonRow)>();
		for (var date = from; date <= last; date = date.AddDays(1))
		{
			if (!calendar.IsSchoolDay(date))
			{
				continue;
			}

			result.AddRange(lessons
				.Where(l => l.Weekday == date.DayOfWeek
						 && (l.SchemaStart == null || l.SchemaStart <= date)
						 && (l.SchemaEnd == null || l.SchemaEnd >= date)
						 && InWindow(l, range))
				.OrderBy(l => l.StartTime)
				.Select(l => (date, l)));
		}

		return result;
	}

	private static (Guid? Id, string? Name) CurrentSubstitute(
		Dictionary<(int IsoYear, int IsoWeek, Guid SchemaSlotId), (Guid? TeacherId, string? TeacherName, Guid? AideId, string? AideName)> assignments,
		DateOnly date,
		LessonRow lesson)
	{
		var dateTime = date.ToDateTime(TimeOnly.MinValue);
		if (!assignments.TryGetValue((ISOWeek.GetYear(dateTime), ISOWeek.GetWeekOfYear(dateTime), lesson.SchemaSlotId), out var a))
		{
			return (null, null);
		}

		return lesson.Seat == SubstituteSeat.Teacher ? (a.TeacherId, a.TeacherName) : (a.AideId, a.AideName);
	}

	private static string FormatRange(Range range) =>
		range switch
		{
			{ EndDate: { } end } => $"{range.Date.ToString("d. MMMM", Danish)}–{end.ToString("d. MMMM", Danish)}",
			{ StartTime: { } start, EndTime: { } stop } =>
				$"{range.Date.ToString("d. MMMM", Danish)} kl. {start:HH\\:mm}–{stop:HH\\:mm}",
			_ => range.Date.ToString("d. MMMM", Danish),
		};
}
