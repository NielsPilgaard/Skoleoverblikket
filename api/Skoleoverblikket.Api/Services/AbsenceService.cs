using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Services;

public enum AbsenceSource { Parent, Staff }

public sealed record ReportAbsenceRequest(
	Guid StudentId,
	DateOnly Date,
	DateOnly? EndDate,
	AbsenceCategory Category,
	[StringLength(500)] string? Reason);

public sealed record AbsenceRecordDto(
	Guid Id,
	Guid StudentId,
	string StudentName,
	Guid ClassId,
	string ClassName,
	DateOnly Date,
	DateOnly? EndDate,
	bool HalfDay,
	AbsenceCategory Category,
	LeaveStatus? LeaveStatus,
	AbsenceSource Source,
	string? RegisteredByName,
	string? Reason,
	DateTimeOffset CreatedAt,
	DateTimeOffset UpdatedAt,
	bool CanCancel);

public sealed record AbsenceMarkDto(
	Guid Id, AbsenceCategory Category, LeaveStatus? LeaveStatus, AbsenceSource Source, string? Reason);

/// <summary>
/// One student on the fremmøde list. <see cref="Morning"/> is the full-day record that counts for the
/// date (parent reports are read-only for staff); <see cref="ParentReport"/> is a parent's report or
/// leave request that staff should know about but that is not the counting record.
/// </summary>
public sealed record StudentAttendanceDto(
	Guid StudentId,
	string Name,
	string? AvatarUrl,
	AbsenceMarkDto? Morning,
	AbsenceMarkDto? EndOfDay,
	AbsenceMarkDto? ParentReport);

public sealed record AttendanceCheckDto(DateTimeOffset TakenAt, string? TakenByName);

public sealed record ClassAttendanceDto(
	Guid ClassId,
	string ClassName,
	DateOnly Date,
	bool IsSchoolDay,
	bool RequiresEndOfDay,
	bool CanEdit,
	AttendanceCheckDto? StartOfDay,
	AttendanceCheckDto? EndOfDay,
	IReadOnlyList<StudentAttendanceDto> Students);

public sealed record AbsentStudentRequest(Guid StudentId, AbsenceCategory Category);

public sealed record SaveAttendanceRequest(AttendanceCheckpoint Checkpoint, IReadOnlyList<AbsentStudentRequest> Absent);

public sealed record ClassAttendanceStatusDto(
	Guid ClassId,
	string ClassName,
	int? GradeLevel,
	bool RequiresEndOfDay,
	AttendanceCheckDto? StartOfDay,
	AttendanceCheckDto? EndOfDay,
	int StudentCount,
	int AbsentCount,
	bool Complete);

public sealed record AttendanceOverviewDto(DateOnly Date, bool IsSchoolDay, IReadOnlyList<ClassAttendanceStatusDto> Classes);

public sealed record PendingAttendanceDto(Guid ClassId, string ClassName, AttendanceCheckpoint Checkpoint);

public enum ParentReportResult { Created, NotYourChild, InvalidCategory, InvalidDates }

public enum ParentCancelResult { Cancelled, NotFound, NotCancellable }

public enum LeaveDecisionResult { Decided, NotFound, NotPending }

public enum CategoryChangeResult { Changed, NotFound, NotStaffRegistered, InvalidCategory, QuarterClosed }

public enum SaveAttendanceResult
{
	Saved,
	ClassNotFound,
	NoStaffRecord,
	NotASchoolDay,
	FutureDate,
	QuarterClosed,
	EndOfDayNotRequired,
	InvalidCategory,
	StudentNotInClass,
}

/// <summary>
/// The student absence register (fravær): parent sick reports and leave requests, fremmøde noted by
/// staff, category changes, the 10% follow-up and retention. The only writer of
/// <see cref="AbsenceReport"/>, <see cref="AttendanceCheck"/>, <see cref="AbsenceFollowUp"/> and
/// <see cref="AbsenceRetentionWarning"/>. Arithmetic lives in <see cref="AbsenceStatsService"/>.
/// </summary>
public sealed class AbsenceService(
	AppDbContext db,
	ITenantContext tenant,
	INotificationService notifications,
	ClassMembershipService membership,
	AbsenceStatsService stats)
{
	/// <summary>Classes from this klassetrin up note fremmøde at the end of the day too, and can have half days.</summary>
	public const int EndOfDayFromGrade = 7;

	/// <summary>How far back a parent may report sickness.</summary>
	private const int ParentReportDaysBack = 14;

	/// <summary>Longest single report a parent can file.</summary>
	private const int MaxReportDays = 90;

	/// <summary>Minimum time admins get between the deletion warning and the deletion.</summary>
	private static readonly TimeSpan RetentionNotice = TimeSpan.FromDays(30);

	private static readonly CultureInfo Danish = CultureInfo.GetCultureInfo("da-DK");

	// ── Parent reports ───────────────────────────────────────────────────────────

	public async Task<(ParentReportResult Result, Guid? Id)> ReportByParentAsync(
		string? parentSubject, ReportAbsenceRequest req, CancellationToken cancellationToken)
	{
		var parent = await db.Parents
			.AsNoTracking()
			.Where(p => p.KeycloakSubject == parentSubject && p.Students.Any(s => s.Id == req.StudentId))
			.Select(p => new { p.Id, p.Name })
			.FirstOrDefaultAsync(cancellationToken);

		if (parent is null)
		{
			return (ParentReportResult.NotYourChild, null);
		}

		if (req.Category is not (AbsenceCategory.Illness or AbsenceCategory.ExtraordinaryLeave))
		{
			return (ParentReportResult.InvalidCategory, null);
		}

		var today = SchoolDayCalendar.Today();
		var endDate = req.EndDate is { } end && end != req.Date ? end : (DateOnly?)null;
		var lastDay = endDate ?? req.Date;
		var earliest = req.Category == AbsenceCategory.ExtraordinaryLeave ? today : today.AddDays(-ParentReportDaysBack);
		if (lastDay < req.Date || req.Date < earliest || lastDay.DayNumber - req.Date.DayNumber >= MaxReportDays)
		{
			return (ParentReportResult.InvalidDates, null);
		}

		var report = new AbsenceReport
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			StudentId = req.StudentId,
			ReportedByParentId = parent.Id,
			Date = req.Date,
			EndDate = endDate,
			Category = req.Category,
			LeaveStatus = req.Category == AbsenceCategory.ExtraordinaryLeave ? LeaveStatus.Pending : null,
			Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim(),
		};

		db.AbsenceReports.Add(report);
		await db.SaveChangesAsync(cancellationToken);

		if (report.LeaveStatus == LeaveStatus.Pending)
		{
			var studentName = await db.Students
				.Where(s => s.Id == req.StudentId)
				.Select(s => s.Name)
				.FirstAsync(cancellationToken);
			var adminIds = await db.Staff.Where(s => s.IsAdmin).Select(s => s.Id).ToListAsync(cancellationToken);
			var body = $"{parent.Name} anmoder om fri til {studentName} {FormatRange(report.Date, report.EndDate)}";
			await notifications.CreateBatchAsync(
				adminIds.Select(id => new NotificationRequest(id, RecipientType.Staff, NotificationType.LeaveRequested, report.Id, body)),
				cancellationToken);
		}

		return (ParentReportResult.Created, report.Id);
	}

	/// <summary>Everything registered on the parent's own children — including staff-registered fravær.</summary>
	public async Task<List<AbsenceRecordDto>> GetForParentAsync(string? parentSubject, CancellationToken cancellationToken)
	{
		var today = SchoolDayCalendar.Today();
		return await Project(
				db.AbsenceReports.Where(a => a.Student.Parents.Any(p => p.KeycloakSubject == parentSubject)),
				parentView: true,
				today)
			.OrderByDescending(a => a.Date)
			.ThenByDescending(a => a.CreatedAt)
			.ToListAsync(cancellationToken);
	}

	/// <summary>
	/// A parent may withdraw a parent-filed report until the day it starts, and a pending or rejected
	/// leave request at any time. Staff registrations are never parent-cancellable.
	/// </summary>
	public async Task<ParentCancelResult> CancelByParentAsync(
		string? parentSubject, Guid id, CancellationToken cancellationToken)
	{
		var report = await db.AbsenceReports
			.FirstOrDefaultAsync(
				a => a.Id == id && a.Student.Parents.Any(p => p.KeycloakSubject == parentSubject),
				cancellationToken);

		if (report is null)
		{
			return ParentCancelResult.NotFound;
		}

		var cancellable = report.RegisteredByStaffId == null
			&& (report.LeaveStatus is LeaveStatus.Pending or LeaveStatus.Rejected
				|| report.Date >= SchoolDayCalendar.Today());

		if (!cancellable)
		{
			return ParentCancelResult.NotCancellable;
		}

		db.AbsenceReports.Remove(report);
		await db.SaveChangesAsync(cancellationToken);
		return ParentCancelResult.Cancelled;
	}

	// ── Staff register views ─────────────────────────────────────────────────────

	/// <summary>
	/// Class ids the staff member may register fravær for: every class for admins, otherwise the
	/// same rule as <c>EditClassRequirement</c> — classes without permission rows are open, the rest
	/// need a row. Null means "all classes".
	/// </summary>
	public async Task<IReadOnlyCollection<Guid>?> GetEditableClassIdsAsync(
		string? subject, bool isAdminRole, CancellationToken cancellationToken)
	{
		if (isAdminRole)
		{
			return null;
		}

		var staff = await db.Staff
			.AsNoTracking()
			.Where(s => s.KeycloakSubject == subject)
			.Select(s => new { s.Id, s.IsAdmin })
			.FirstOrDefaultAsync(cancellationToken);

		if (staff is null)
		{
			return [];
		}

		if (staff.IsAdmin)
		{
			return null;
		}

		return await db.Classes
			.Where(c => !db.ClassPermissions.Any(p => p.ClassId == c.Id)
					 || db.ClassPermissions.Any(p => p.ClassId == c.Id && p.StaffId == staff.Id))
			.Select(c => c.Id)
			.ToListAsync(cancellationToken);
	}

	public async Task<List<AbsenceRecordDto>> ListAsync(
		Guid? classId, DateOnly? from, DateOnly? to, AbsenceCategory? category,
		IReadOnlyCollection<Guid>? allowedClassIds, CancellationToken cancellationToken)
	{
		var query = db.AbsenceReports.AsQueryable();

		if (allowedClassIds is not null)
		{
			query = query.Where(a => allowedClassIds.Contains(a.Student.ClassId));
		}

		if (classId.HasValue)
		{
			query = query.Where(a => a.Student.ClassId == classId.Value);
		}

		if (from.HasValue)
		{
			query = query.Where(a => (a.EndDate ?? a.Date) >= from.Value);
		}

		if (to.HasValue)
		{
			query = query.Where(a => a.Date <= to.Value);
		}

		if (category.HasValue)
		{
			query = query.Where(a => a.Category == category.Value);
		}

		return await Project(query, parentView: false, SchoolDayCalendar.Today())
			.OrderByDescending(a => a.Date)
			.ThenBy(a => a.ClassName)
			.ThenBy(a => a.StudentName)
			.ToListAsync(cancellationToken);
	}

	/// <summary>Leave requests awaiting the principal, plus those decided in the last 30 days for context.</summary>
	public async Task<List<AbsenceRecordDto>> GetLeaveRequestsAsync(CancellationToken cancellationToken)
	{
		var today = SchoolDayCalendar.Today();
		var since = today.AddDays(-30);
		return await Project(
				db.AbsenceReports.Where(a => a.LeaveStatus == LeaveStatus.Pending
					|| (a.LeaveStatus != null && (a.EndDate ?? a.Date) >= since)),
				parentView: false,
				today)
			.OrderBy(a => a.LeaveStatus == LeaveStatus.Pending ? 0 : 1)
			.ThenBy(a => a.Date)
			.ToListAsync(cancellationToken);
	}

	public Task<int> CountPendingLeaveRequestsAsync(CancellationToken cancellationToken) =>
		db.AbsenceReports.CountAsync(a => a.LeaveStatus == LeaveStatus.Pending, cancellationToken);

	/// <summary>The klasse of the student a record belongs to — for the controller's class authorization.</summary>
	public Task<Guid?> GetRecordClassIdAsync(Guid id, CancellationToken cancellationToken) =>
		db.AbsenceReports
			.Where(a => a.Id == id)
			.Select(a => (Guid?)a.Student.ClassId)
			.FirstOrDefaultAsync(cancellationToken);

	public Task<Guid?> GetStudentClassIdAsync(Guid studentId, CancellationToken cancellationToken) =>
		db.Students
			.Where(s => s.Id == studentId)
			.Select(s => (Guid?)s.ClassId)
			.FirstOrDefaultAsync(cancellationToken);

	// ── Leave approval ───────────────────────────────────────────────────────────

	public async Task<LeaveDecisionResult> DecideLeaveAsync(
		Guid id, bool approve, string? subject, CancellationToken cancellationToken)
	{
		var report = await db.AbsenceReports
			.Include(a => a.Student)
			.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

		if (report is null)
		{
			return LeaveDecisionResult.NotFound;
		}

		if (report.LeaveStatus != LeaveStatus.Pending)
		{
			return LeaveDecisionResult.NotPending;
		}

		report.LeaveStatus = approve ? LeaveStatus.Approved : LeaveStatus.Rejected;
		report.DecidedByStaffId = await StaffIdAsync(subject, cancellationToken);
		report.DecidedAt = DateTimeOffset.UtcNow;
		report.UpdatedAt = DateTimeOffset.UtcNow;
		await db.SaveChangesAsync(cancellationToken);

		var body = approve
			? $"Skolen har godkendt fri til {report.Student.Name} {FormatRange(report.Date, report.EndDate)}"
			: $"Skolen har afvist anmodningen om fri til {report.Student.Name} {FormatRange(report.Date, report.EndDate)}";
		await NotifyParentsAsync(
			report.StudentId, approve ? NotificationType.LeaveApproved : NotificationType.LeaveRejected,
			report.Id, body, cancellationToken);

		return LeaveDecisionResult.Decided;
	}

	// ── Category change ──────────────────────────────────────────────────────────

	/// <summary>
	/// Staff may change the category of a staff-registered record (e.g. a parent phoned in sick, so
	/// ulovligt becomes sygdom) until the end of the record's calendar quarter.
	/// </summary>
	public async Task<CategoryChangeResult> ChangeCategoryAsync(
		Guid id, AbsenceCategory category, CancellationToken cancellationToken)
	{
		if (category is not (AbsenceCategory.Illness or AbsenceCategory.Unauthorized))
		{
			return CategoryChangeResult.InvalidCategory;
		}

		var report = await db.AbsenceReports
			.Include(a => a.Student)
			.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

		if (report is null)
		{
			return CategoryChangeResult.NotFound;
		}

		if (report.RegisteredByStaffId is null)
		{
			return CategoryChangeResult.NotStaffRegistered;
		}

		if (!IsQuarterOpen(report.Date))
		{
			return CategoryChangeResult.QuarterClosed;
		}

		if (report.Category == category)
		{
			return CategoryChangeResult.Changed;
		}

		report.Category = category;
		report.UpdatedAt = DateTimeOffset.UtcNow;
		await db.SaveChangesAsync(cancellationToken);

		if (category == AbsenceCategory.Unauthorized)
		{
			await NotifyParentsAsync(
				report.StudentId, NotificationType.UnauthorizedAbsence, report.Id,
				$"{report.Student.Name}s fravær {report.Date.ToString("d. MMMM", Danish)} er registreret som ulovligt fravær",
				cancellationToken);
			await CheckThresholdAsync(report.StudentId, report.Date, cancellationToken);
		}

		return CategoryChangeResult.Changed;
	}

	// ── Fremmøde ─────────────────────────────────────────────────────────────────

	public async Task<ClassAttendanceDto?> GetClassAttendanceAsync(
		Guid classId, DateOnly date, CancellationToken cancellationToken)
	{
		var klass = await db.Classes
			.AsNoTracking()
			.Where(c => c.Id == classId)
			.Select(c => new { c.Id, c.Name, c.GradeLevel })
			.FirstOrDefaultAsync(cancellationToken);

		if (klass is null)
		{
			return null;
		}

		var calendar = await SchoolDayCalendar.LoadAsync(db, date, date, cancellationToken);
		var checks = await LoadChecksAsync([classId], date, cancellationToken);

		var students = await db.Students
			.AsNoTracking()
			.Where(s => s.ClassId == classId)
			.OrderBy(s => s.Name)
			.Select(s => new { s.Id, s.Name, s.AvatarUrl })
			.ToListAsync(cancellationToken);

		var records = await RecordsOnDateAsync(classId, date, includeRejected: true, cancellationToken);

		var rows = students
			.Select(s =>
			{
				var own = records.Where(r => r.StudentId == s.Id).ToList();
				var staffFull = own.FirstOrDefault(r => r.RegisteredByStaffId != null && !r.HalfDay);
				var parentCounting = own.FirstOrDefault(r => r.ReportedByParentId != null && r.LeaveStatus != LeaveStatus.Rejected);
				var parentAny = parentCounting ?? own.FirstOrDefault(r => r.ReportedByParentId != null);
				var half = own.FirstOrDefault(r => r.RegisteredByStaffId != null && r.HalfDay);

				var morning = staffFull ?? parentCounting;
				var parentNote = morning == parentAny ? null : parentAny;
				return new StudentAttendanceDto(
					s.Id, s.Name, s.AvatarUrl,
					Mark(morning), staffFull is null && parentCounting is null ? Mark(half) : null, Mark(parentNote));
			})
			.ToList();

		return new ClassAttendanceDto(
			klass.Id, klass.Name, date,
			calendar.IsSchoolDay(date),
			RequiresEndOfDay(klass.GradeLevel),
			date <= SchoolDayCalendar.Today() && IsQuarterOpen(date),
			checks.GetValueOrDefault((classId, AttendanceCheckpoint.StartOfDay)),
			checks.GetValueOrDefault((classId, AttendanceCheckpoint.EndOfDay)),
			rows);
	}

	/// <summary>
	/// Saves one fremmøde checkpoint for a klasse. Start of day: listed students are absent all day,
	/// everyone else is present (staff registrations for them are removed, which is how a late
	/// arrival is corrected). End of day (7.–10. klasse): listed students who were there in the
	/// morning left early — a half day. Parent reports are never changed here. Present students get
	/// no rows; the <see cref="AttendanceCheck"/> records that fremmøde was noted.
	/// </summary>
	public async Task<SaveAttendanceResult> SaveClassAttendanceAsync(
		Guid classId, DateOnly date, SaveAttendanceRequest req, string? subject, CancellationToken cancellationToken)
	{
		var klass = await db.Classes
			.AsNoTracking()
			.Where(c => c.Id == classId)
			.Select(c => new { c.Id, c.GradeLevel })
			.FirstOrDefaultAsync(cancellationToken);

		if (klass is null)
		{
			return SaveAttendanceResult.ClassNotFound;
		}

		var staffId = await StaffIdAsync(subject, cancellationToken);
		if (staffId is null)
		{
			return SaveAttendanceResult.NoStaffRecord;
		}

		if (date > SchoolDayCalendar.Today())
		{
			return SaveAttendanceResult.FutureDate;
		}

		if (!IsQuarterOpen(date))
		{
			return SaveAttendanceResult.QuarterClosed;
		}

		var calendar = await SchoolDayCalendar.LoadAsync(db, date, date, cancellationToken);
		if (!calendar.IsSchoolDay(date))
		{
			return SaveAttendanceResult.NotASchoolDay;
		}

		if (req.Checkpoint == AttendanceCheckpoint.EndOfDay && !RequiresEndOfDay(klass.GradeLevel))
		{
			return SaveAttendanceResult.EndOfDayNotRequired;
		}

		if (req.Absent.Any(a => a.Category is not (AbsenceCategory.Illness or AbsenceCategory.Unauthorized)))
		{
			return SaveAttendanceResult.InvalidCategory;
		}

		var studentIds = await db.Students
			.Where(s => s.ClassId == classId)
			.Select(s => s.Id)
			.ToListAsync(cancellationToken);

		var absent = req.Absent
			.GroupBy(a => a.StudentId)
			.ToDictionary(g => g.Key, g => g.Last().Category);

		if (absent.Keys.Any(id => !studentIds.Contains(id)))
		{
			return SaveAttendanceResult.StudentNotInClass;
		}

		var records = await db.AbsenceReports
			.Where(a => a.Student.ClassId == classId && a.Date <= date && (a.EndDate ?? a.Date) >= date)
			.Where(a => a.LeaveStatus == null || a.LeaveStatus != LeaveStatus.Rejected)
			.ToListAsync(cancellationToken);

		var now = DateTimeOffset.UtcNow;
		var newUnauthorized = new List<AbsenceReport>();
		var becameUnauthorized = new List<AbsenceReport>();

		foreach (var studentId in studentIds)
		{
			var own = records.Where(r => r.StudentId == studentId).ToList();
			var staffFull = own.FirstOrDefault(r => r.RegisteredByStaffId != null && !r.HalfDay);
			var parentFull = own.FirstOrDefault(r => r.ReportedByParentId != null);
			var half = own.FirstOrDefault(r => r.RegisteredByStaffId != null && r.HalfDay);
			var isListed = absent.TryGetValue(studentId, out var category);

			if (req.Checkpoint == AttendanceCheckpoint.StartOfDay)
			{
				if (isListed)
				{
					if (half is not null)
					{
						// Absent from the morning means they were never there to leave early.
						db.AbsenceReports.Remove(half);
					}

					if (staffFull is not null)
					{
						if (staffFull.Category != category)
						{
							if (category == AbsenceCategory.Unauthorized)
							{
								becameUnauthorized.Add(staffFull);
							}

							staffFull.Category = category;
							staffFull.UpdatedAt = now;
						}
					}
					else if (parentFull is null)
					{
						var created = NewStaffRecord(studentId, date, category, halfDay: false, staffId.Value);
						if (category == AbsenceCategory.Unauthorized)
						{
							newUnauthorized.Add(created);
						}
					}
				}
				else if (staffFull is not null)
				{
					db.AbsenceReports.Remove(staffFull);
				}
			}
			else
			{
				var absentInMorning = staffFull is not null || parentFull is not null;
				if (isListed && !absentInMorning)
				{
					if (half is not null)
					{
						if (half.Category != category)
						{
							if (category == AbsenceCategory.Unauthorized)
							{
								becameUnauthorized.Add(half);
							}

							half.Category = category;
							half.UpdatedAt = now;
						}
					}
					else
					{
						var created = NewStaffRecord(studentId, date, category, halfDay: true, staffId.Value);
						if (category == AbsenceCategory.Unauthorized)
						{
							newUnauthorized.Add(created);
						}
					}
				}
				else if (!isListed && half is not null)
				{
					db.AbsenceReports.Remove(half);
				}
			}
		}

		var check = await db.AttendanceChecks
			.FirstOrDefaultAsync(c => c.ClassId == classId && c.Date == date && c.Checkpoint == req.Checkpoint, cancellationToken);

		if (check is null)
		{
			db.AttendanceChecks.Add(new AttendanceCheck
			{
				Id = Guid.NewGuid(),
				TenantId = tenant.TenantId,
				ClassId = classId,
				Date = date,
				Checkpoint = req.Checkpoint,
				TakenByStaffId = staffId,
				TakenAt = now,
			});
		}
		else
		{
			check.TakenByStaffId = staffId;
			check.TakenAt = now;
		}

		await db.SaveChangesAsync(cancellationToken);

		await NotifyUnauthorizedAsync(newUnauthorized.Concat(becameUnauthorized).ToList(), date, cancellationToken);

		foreach (var studentId in newUnauthorized.Concat(becameUnauthorized).Select(r => r.StudentId).Distinct())
		{
			await CheckThresholdAsync(studentId, date, cancellationToken);
		}

		return SaveAttendanceResult.Saved;
	}

	/// <summary>Every klasse's fremmøde status on a date — for the admin overview.</summary>
	public async Task<AttendanceOverviewDto> GetOverviewAsync(DateOnly date, CancellationToken cancellationToken)
	{
		var calendar = await SchoolDayCalendar.LoadAsync(db, date, date, cancellationToken);

		var classes = await db.Classes
			.AsNoTracking()
			.Select(c => new
			{
				c.Id,
				c.Name,
				c.GradeLevel,
				StudentCount = db.Students.Count(s => s.ClassId == c.Id),
			})
			.ToListAsync(cancellationToken);

		var classIds = classes.Select(c => c.Id).ToList();
		var checks = await LoadChecksAsync(classIds, date, cancellationToken);

		var absentCounts = await db.AbsenceReports
			.Where(a => a.Date <= date && (a.EndDate ?? a.Date) >= date)
			.Where(a => a.LeaveStatus == null || a.LeaveStatus != LeaveStatus.Rejected)
			.GroupBy(a => a.Student.ClassId)
			.Select(g => new { ClassId = g.Key, Count = g.Select(a => a.StudentId).Distinct().Count() })
			.ToDictionaryAsync(g => g.ClassId, g => g.Count, cancellationToken);

		var comparer = StringComparer.Create(Danish, CompareOptions.None);
		var rows = classes
			.Where(c => c.StudentCount > 0)
			.OrderBy(c => c.GradeLevel ?? int.MaxValue)
			.ThenBy(c => c.Name, comparer)
			.Select(c =>
			{
				var requiresEnd = RequiresEndOfDay(c.GradeLevel);
				var start = checks.GetValueOrDefault((c.Id, AttendanceCheckpoint.StartOfDay));
				var end = checks.GetValueOrDefault((c.Id, AttendanceCheckpoint.EndOfDay));
				return new ClassAttendanceStatusDto(
					c.Id, c.Name, c.GradeLevel, requiresEnd, start, end, c.StudentCount,
					absentCounts.GetValueOrDefault(c.Id),
					start is not null && (!requiresEnd || end is not null));
			})
			.ToList();

		return new AttendanceOverviewDto(date, calendar.IsSchoolDay(date), rows);
	}

	/// <summary>
	/// Today's missing fremmøde for the klasser the staff member teaches today. The end-of-day check
	/// only shows up once the klasse's last lektion has started.
	/// </summary>
	public async Task<List<PendingAttendanceDto>> GetMyPendingAsync(
		string? subject, DateTimeOffset now, CancellationToken cancellationToken)
	{
		var staffId = await StaffIdAsync(subject, cancellationToken);
		if (staffId is null)
		{
			return [];
		}

		var today = SchoolDayCalendar.DanishDate(now);
		var calendar = await SchoolDayCalendar.LoadAsync(db, today, today, cancellationToken);
		if (!calendar.IsSchoolDay(today))
		{
			return [];
		}

		var classIds = await db.SchemaSlots
			.AsNoTracking()
			.Where(s => s.Weekday == today.DayOfWeek
					 && (s.TeacherId == staffId || s.AideId == staffId)
					 && (s.Schema.StartDate == null || s.Schema.StartDate <= today)
					 && (s.Schema.EndDate == null || s.Schema.EndDate >= today))
			.Select(s => s.Schema.ClassId)
			.Distinct()
			.ToListAsync(cancellationToken);

		if (classIds.Count == 0)
		{
			return [];
		}

		var classes = await db.Classes
			.AsNoTracking()
			.Where(c => classIds.Contains(c.Id) && db.Students.Any(s => s.ClassId == c.Id))
			.Select(c => new { c.Id, c.Name, c.GradeLevel })
			.ToListAsync(cancellationToken);

		var lastLessonStart = await db.SchemaSlots
			.AsNoTracking()
			.Where(s => classIds.Contains(s.Schema.ClassId)
					 && s.Weekday == today.DayOfWeek
					 && (s.Schema.StartDate == null || s.Schema.StartDate <= today)
					 && (s.Schema.EndDate == null || s.Schema.EndDate >= today))
			.GroupBy(s => s.Schema.ClassId)
			.Select(g => new { ClassId = g.Key, Start = g.Max(s => s.TimeSlot.StartTime) })
			.ToDictionaryAsync(g => g.ClassId, g => g.Start, cancellationToken);

		var checks = await LoadChecksAsync(classIds, today, cancellationToken);
		var danishNow = SchoolDayCalendar.DanishTime(now);

		var pending = new List<PendingAttendanceDto>();
		foreach (var c in classes.OrderBy(c => c.Name, StringComparer.Create(Danish, CompareOptions.None)))
		{
			if (!checks.ContainsKey((c.Id, AttendanceCheckpoint.StartOfDay)))
			{
				pending.Add(new PendingAttendanceDto(c.Id, c.Name, AttendanceCheckpoint.StartOfDay));
			}
			else if (RequiresEndOfDay(c.GradeLevel)
				&& !checks.ContainsKey((c.Id, AttendanceCheckpoint.EndOfDay))
				&& lastLessonStart.TryGetValue(c.Id, out var start)
				&& danishNow >= start)
			{
				pending.Add(new PendingAttendanceDto(c.Id, c.Name, AttendanceCheckpoint.EndOfDay));
			}
		}

		return pending;
	}

	/// <summary>Classes with students that have not noted start-of-day fremmøde today. Zero when the school doesn't use fremmøde.</summary>
	public async Task<int> CountClassesMissingAttendanceTodayAsync(CancellationToken cancellationToken)
	{
		var today = SchoolDayCalendar.Today();
		var usesAttendance = await db.AttendanceChecks.AnyAsync(c => c.Date >= today.AddDays(-30), cancellationToken);
		if (!usesAttendance)
		{
			return 0;
		}

		var calendar = await SchoolDayCalendar.LoadAsync(db, today, today, cancellationToken);
		if (!calendar.IsSchoolDay(today))
		{
			return 0;
		}

		return await db.Classes.CountAsync(
			c => db.Students.Any(s => s.ClassId == c.Id)
			  && !db.AttendanceChecks.Any(a => a.ClassId == c.Id && a.Date == today && a.Checkpoint == AttendanceCheckpoint.StartOfDay),
			cancellationToken);
	}

	// ── Follow-up ────────────────────────────────────────────────────────────────

	/// <summary>"Markér som orienteret": the parents were told about the student's ulovligt fravær this quarter.</summary>
	public async Task<bool> MarkParentsInformedAsync(
		Guid studentId, int year, int quarter, string? subject, CancellationToken cancellationToken)
	{
		if (!await db.Students.AnyAsync(s => s.Id == studentId, cancellationToken))
		{
			return false;
		}

		var quarterStart = SchoolDayCalendar.QuarterStart(year, quarter);
		var followUp = await GetOrCreateFollowUpAsync(studentId, quarterStart, cancellationToken);
		followUp.ParentsInformedAt = DateTimeOffset.UtcNow;
		followUp.ParentsInformedByStaffId = await StaffIdAsync(subject, cancellationToken);
		await db.SaveChangesAsync(cancellationToken);
		return true;
	}

	private async Task CheckThresholdAsync(Guid studentId, DateOnly date, CancellationToken cancellationToken)
	{
		var quarterStart = SchoolDayCalendar.QuarterStart(date);
		var (unauthorizedDays, schoolDays) = await stats.GetUnauthorizedShareAsync(studentId, quarterStart, cancellationToken);
		if (AbsenceStatsService.FlagFor(unauthorizedDays, schoolDays) == AbsenceFlag.None)
		{
			return;
		}

		var followUp = await GetOrCreateFollowUpAsync(studentId, quarterStart, cancellationToken);
		if (followUp.WarningSentAt is not null)
		{
			return;
		}

		followUp.WarningSentAt = DateTimeOffset.UtcNow;
		await db.SaveChangesAsync(cancellationToken);

		var student = await db.Students
			.AsNoTracking()
			.Where(s => s.Id == studentId)
			.Select(s => new { s.Name, s.ClassId, ClassName = s.Class.Name })
			.FirstAsync(cancellationToken);

		var recipients = await GetFollowUpStaffIdsAsync(student.ClassId, cancellationToken);
		var percent = Math.Round(unauthorizedDays / schoolDays * 100, 1);
		var body = $"{student.Name} ({student.ClassName}) har {percent.ToString("0.#", Danish)}% ulovligt fravær i "
			+ $"{SchoolDayCalendar.QuarterNumber(quarterStart)}. kvartal. Orientér forældrene.";

		await notifications.CreateBatchAsync(
			recipients.Select(id => new NotificationRequest(id, RecipientType.Staff, NotificationType.AbsenceThreshold, studentId, body)),
			cancellationToken);
	}

	/// <summary>Admins, plus the klasse's permitted staff — or its teachers when the klasse has no permission rows.</summary>
	private async Task<List<Guid>> GetFollowUpStaffIdsAsync(Guid classId, CancellationToken cancellationToken)
	{
		var ids = await db.Staff.Where(s => s.IsAdmin).Select(s => s.Id).ToListAsync(cancellationToken);

		var permitted = await db.ClassPermissions
			.Where(p => p.ClassId == classId)
			.Select(p => p.StaffId)
			.ToListAsync(cancellationToken);

		ids.AddRange(permitted.Count > 0 ? permitted : await membership.GetStaffIdsAsync(classId, cancellationToken));
		return ids.Distinct().ToList();
	}

	private async Task<AbsenceFollowUp> GetOrCreateFollowUpAsync(
		Guid studentId, DateOnly quarterStart, CancellationToken cancellationToken)
	{
		var followUp = await db.AbsenceFollowUps
			.FirstOrDefaultAsync(f => f.StudentId == studentId && f.QuarterStart == quarterStart, cancellationToken);

		if (followUp is null)
		{
			followUp = new AbsenceFollowUp
			{
				Id = Guid.NewGuid(),
				TenantId = tenant.TenantId,
				StudentId = studentId,
				QuarterStart = quarterStart,
			};
			db.AbsenceFollowUps.Add(followUp);
		}

		return followUp;
	}

	// ── Retention ────────────────────────────────────────────────────────────────

	/// <summary>
	/// Keeps absence data for the current and previous school year. From 1 July admins are warned
	/// that the previous school year is deleted on 1 August; from 1 August everything older than the
	/// previous school year is deleted — but only once a warning for it has been out for at least
	/// <see cref="RetentionNotice"/>. If no warning went out (a tenant created in late July, a job
	/// outage), one is sent now and deletion waits.
	/// </summary>
	public async Task ApplyRetentionAsync(DateTimeOffset now, CancellationToken cancellationToken)
	{
		var today = SchoolDayCalendar.DanishDate(now);
		var current = SchoolDayCalendar.SchoolYearStart(today);

		if (today.Month == 7)
		{
			// On 1 August, current + 1 starts and everything before the school year `current` goes.
			await WarnIfDataExistsAsync(current - 1, SchoolDayCalendar.SchoolYearFirstDay(current), now, cancellationToken);
		}

		var cutoff = SchoolDayCalendar.SchoolYearFirstDay(current - 1);
		var expiringYear = current - 2;
		if (!await HasDataBeforeAsync(cutoff, cancellationToken))
		{
			return;
		}

		var warning = await db.AbsenceRetentionWarnings
			.AsNoTracking()
			.FirstOrDefaultAsync(w => w.SchoolYearStart == expiringYear, cancellationToken);

		if (warning is null)
		{
			await WarnIfDataExistsAsync(expiringYear, cutoff, now, cancellationToken);
			return;
		}

		if (warning.WarnedAt > now - RetentionNotice)
		{
			return;
		}

		await db.AbsenceReports.Where(a => (a.EndDate ?? a.Date) < cutoff).ExecuteDeleteAsync(cancellationToken);
		await db.AttendanceChecks.Where(c => c.Date < cutoff).ExecuteDeleteAsync(cancellationToken);
		var lastWholeQuarterBefore = SchoolDayCalendar.QuarterStart(cutoff);
		await db.AbsenceFollowUps.Where(f => f.QuarterStart < lastWholeQuarterBefore).ExecuteDeleteAsync(cancellationToken);
		await db.StaffAbsences.Where(a => (a.EndDate ?? a.Date) < cutoff).ExecuteDeleteAsync(cancellationToken);
	}

	private async Task<bool> HasDataBeforeAsync(DateOnly cutoff, CancellationToken cancellationToken) =>
		await db.AbsenceReports.AnyAsync(a => (a.EndDate ?? a.Date) < cutoff, cancellationToken)
		|| await db.AttendanceChecks.AnyAsync(c => c.Date < cutoff, cancellationToken)
		|| await db.StaffAbsences.AnyAsync(a => (a.EndDate ?? a.Date) < cutoff, cancellationToken);

	private async Task WarnIfDataExistsAsync(
		int schoolYearStart, DateOnly cutoff, DateTimeOffset now, CancellationToken cancellationToken)
	{
		if (await db.AbsenceRetentionWarnings.AnyAsync(w => w.SchoolYearStart == schoolYearStart, cancellationToken)
			|| !await HasDataBeforeAsync(cutoff, cancellationToken))
		{
			return;
		}

		db.AbsenceRetentionWarnings.Add(new AbsenceRetentionWarning
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			SchoolYearStart = schoolYearStart,
			WarnedAt = now,
		});
		await db.SaveChangesAsync(cancellationToken);

		var today = SchoolDayCalendar.DanishDate(now);
		var deletionDate = today.Month == 7 ? new DateOnly(today.Year, 8, 1) : today.AddDays(RetentionNotice.Days);
		var body = $"Fravær for skoleåret {SchoolDayCalendar.SchoolYearLabel(schoolYearStart)} slettes "
			+ $"{deletionDate.ToString("d. MMMM", Danish)}. Download det under Fravær nu, hvis I skal bruge det.";
		var adminIds = await db.Staff.Where(s => s.IsAdmin).Select(s => s.Id).ToListAsync(cancellationToken);
		await notifications.CreateBatchAsync(
			adminIds.Select(id => new NotificationRequest(id, RecipientType.Staff, NotificationType.AbsenceRetentionWarning, null, body)),
			cancellationToken);
	}

	// ── Helpers ──────────────────────────────────────────────────────────────────

	public static bool RequiresEndOfDay(int? gradeLevel) => gradeLevel >= EndOfDayFromGrade;

	/// <summary>Registrations can be corrected until the end of the quarter they fall in.</summary>
	public static bool IsQuarterOpen(DateOnly date) =>
		SchoolDayCalendar.QuarterEnd(SchoolDayCalendar.QuarterStart(date)) >= SchoolDayCalendar.Today();

	private AbsenceReport NewStaffRecord(Guid studentId, DateOnly date, AbsenceCategory category, bool halfDay, Guid staffId)
	{
		var record = new AbsenceReport
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			StudentId = studentId,
			RegisteredByStaffId = staffId,
			Date = date,
			Category = category,
			HalfDay = halfDay,
		};
		db.AbsenceReports.Add(record);
		return record;
	}

	private async Task NotifyUnauthorizedAsync(List<AbsenceReport> records, DateOnly date, CancellationToken cancellationToken)
	{
		if (records.Count == 0)
		{
			return;
		}

		var studentIds = records.Select(r => r.StudentId).Distinct().ToList();
		var students = await db.Students
			.AsNoTracking()
			.Where(s => studentIds.Contains(s.Id))
			.Select(s => new { s.Id, s.Name, ParentIds = s.Parents.Select(p => p.Id).ToList() })
			.ToListAsync(cancellationToken);

		var isToday = date == SchoolDayCalendar.Today();
		var dayText = isToday ? "i dag" : date.ToString("d. MMMM", Danish);
		var requests = new List<NotificationRequest>();
		foreach (var record in records)
		{
			var student = students.First(s => s.Id == record.StudentId);
			var body = record.HalfDay
				? $"{student.Name} gik før skoledagens slutning {dayText}. Det er registreret som ulovligt fravær (halv dag)."
				: isToday
					? $"{student.Name} er ikke mødt i skole i dag, og vi har ikke hørt fra jer."
					: $"{student.Name} er registreret med ulovligt fravær {dayText}.";
			requests.AddRange(student.ParentIds.Select(parentId =>
				new NotificationRequest(parentId, RecipientType.Parent, NotificationType.UnauthorizedAbsence, record.Id, body)));
		}

		await notifications.CreateBatchAsync(requests, cancellationToken);
	}

	private async Task NotifyParentsAsync(
		Guid studentId, NotificationType type, Guid referenceId, string body, CancellationToken cancellationToken)
	{
		var parentIds = await db.Parents
			.Where(p => p.Students.Any(s => s.Id == studentId))
			.Select(p => p.Id)
			.ToListAsync(cancellationToken);

		await notifications.CreateBatchAsync(
			parentIds.Select(id => new NotificationRequest(id, RecipientType.Parent, type, referenceId, body)),
			cancellationToken);
	}

	private Task<Guid?> StaffIdAsync(string? subject, CancellationToken cancellationToken) =>
		db.Staff
			.Where(s => s.KeycloakSubject == subject)
			.Select(s => (Guid?)s.Id)
			.FirstOrDefaultAsync(cancellationToken);

	private async Task<Dictionary<(Guid ClassId, AttendanceCheckpoint Checkpoint), AttendanceCheckDto>> LoadChecksAsync(
		List<Guid> classIds, DateOnly date, CancellationToken cancellationToken)
	{
		var checks = await db.AttendanceChecks
			.AsNoTracking()
			.Where(c => classIds.Contains(c.ClassId) && c.Date == date)
			.Select(c => new
			{
				c.ClassId,
				c.Checkpoint,
				c.TakenAt,
				TakenByName = c.TakenByStaff != null ? c.TakenByStaff.Name : null,
			})
			.ToListAsync(cancellationToken);

		return checks.ToDictionary(c => (c.ClassId, c.Checkpoint), c => new AttendanceCheckDto(c.TakenAt, c.TakenByName));
	}

	private Task<List<AbsenceReport>> RecordsOnDateAsync(
		Guid classId, DateOnly date, bool includeRejected, CancellationToken cancellationToken) =>
		db.AbsenceReports
			.AsNoTracking()
			.Where(a => a.Student.ClassId == classId && a.Date <= date && (a.EndDate ?? a.Date) >= date)
			.Where(a => includeRejected || a.LeaveStatus == null || a.LeaveStatus != LeaveStatus.Rejected)
			.OrderByDescending(a => a.CreatedAt)
			.ToListAsync(cancellationToken);

	private static AbsenceMarkDto? Mark(AbsenceReport? record) =>
		record is null
			? null
			: new AbsenceMarkDto(
				record.Id, record.Category, record.LeaveStatus,
				record.RegisteredByStaffId != null ? AbsenceSource.Staff : AbsenceSource.Parent,
				record.Reason);

	private static IQueryable<AbsenceRecordDto> Project(IQueryable<AbsenceReport> query, bool parentView, DateOnly today) =>
		query.AsNoTracking().Select(a => new AbsenceRecordDto(
			a.Id,
			a.StudentId,
			a.Student.Name,
			a.Student.ClassId,
			a.Student.Class.Name,
			a.Date,
			a.EndDate,
			a.HalfDay,
			a.Category,
			a.LeaveStatus,
			a.RegisteredByStaffId != null ? AbsenceSource.Staff : AbsenceSource.Parent,
			a.RegisteredByStaff != null ? a.RegisteredByStaff.Name : a.ReportedByParent != null ? a.ReportedByParent.Name : null,
			a.Reason,
			a.CreatedAt,
			a.UpdatedAt,
			parentView && a.RegisteredByStaffId == null
				&& (a.LeaveStatus == LeaveStatus.Pending || a.LeaveStatus == LeaveStatus.Rejected || a.Date >= today)));

	private static string FormatRange(DateOnly date, DateOnly? endDate) =>
		endDate is { } end && end != date
			? $"{date.ToString("d. MMMM", Danish)}–{end.ToString("d. MMMM", Danish)}"
			: date.ToString("d. MMMM", Danish);
}
