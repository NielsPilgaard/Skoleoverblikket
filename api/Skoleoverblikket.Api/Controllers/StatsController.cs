using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/stats")]
[Authorize]
public sealed class StatsController(
	AppDbContext db,
	SubscriptionService subscriptionService,
	ITenantContext tenantContext) : ControllerBase
{
	private const int SchoolDaysPerWeek = 5; // Mon–Fri
	public record DashboardStats(
		int ClassCount,
		int StaffCount,
		int CourseCount,
		int RoomCount,
		int SchemasComplete,
		int SchemasTotal,
		IReadOnlyList<HoursPerCourse> HoursPerCourse,
		IReadOnlyList<HoursPerStaff> HoursPerStaff,
		IReadOnlyList<UnassignedClass> UnassignedClasses,
		int PendingAbsenceCount,
		OpenVacationWindowDto? OpenVacationWindow,
		int? UnreadMessageCount,
		int? UnreadKontaktbogCount);

	public record HoursPerCourse(Guid CourseId, string CourseName, Guid ClassId, string ClassName, double Hours);
	public record HoursPerStaff(Guid StaffId, string StaffName, StaffRole Role, double Hours);
	public record UnassignedClass(Guid ClassId, string ClassName, int EmptySlots, bool HasSchema);
	public record OpenVacationWindowDto(Guid WindowId, string Title, DateOnly RegistrationDeadline, int EntryCount);

	public record MyDashboardStats(
		IReadOnlyList<TodayLektion> TodaySchedule,
		int? UnreadMessageCount,
		int? UnreadKontaktbogCount);

	public record TodayLektion(
		Guid SlotId,
		TimeOnly StartTime,
		TimeOnly EndTime,
		string CourseName,
		string ClassName,
		string? RoomName);

	[HttpGet("dashboard")]
	[Authorize(Roles = $"{Roles.Admin},{Roles.Board}")]
	public async Task<ActionResult<DashboardStats>> GetDashboard(CancellationToken cancellationToken)
	{
		var classCount = await db.Classes.CountAsync(cancellationToken);
		var staffCount = await db.Staff.CountAsync(cancellationToken);
		var courseCount = await db.Courses.CountAsync(cancellationToken);
		var roomCount = await db.Rooms.CountAsync(cancellationToken);

		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		// TODO: Fetching all schemas is overkill, visualize something else
		var allSchemas = await db.Schemas.AsNoTracking().ToListAsync(cancellationToken);
		var schemasTotal = allSchemas.Count;
		var schemasComplete = allSchemas.Count(s => s.StartDate.HasValue && s.EndDate.HasValue);

		// Hours per course per class (active schemas only)
		var activeSlots = await db.SchemaSlots
			.AsNoTrackingWithIdentityResolution()
			.AsSplitQuery()
			.Where(s => s.Schema.StartDate <= today && s.Schema.EndDate >= today)
			.Include(s => s.Course)
			.Include(s => s.Schema).ThenInclude(sc => sc.Class)
			.Include(s => s.TimeSlot)
			.Include(s => s.Teacher)
			.Include(s => s.Aide)
			.ToListAsync(cancellationToken);

		var hoursPerCourse = activeSlots
			.GroupBy(s => (s.CourseId, CourseName: s.Course.Name, s.Schema.ClassId, ClassName: s.Schema.Class.Name))
			.Select(g => new HoursPerCourse(
				g.Key.CourseId, g.Key.CourseName,
				g.Key.ClassId, g.Key.ClassName,
				Math.Round(g.Sum(s => (s.TimeSlot.EndTime - s.TimeSlot.StartTime).TotalHours), 2)))
			.OrderBy(h => h.ClassName).ThenBy(h => h.CourseName)
			.ToList();

		// Hours per staff member (active schemas only)
		var teacherHours = activeSlots
			.GroupBy(s => (s.TeacherId, s.Teacher.Name, s.Teacher.Role))
			.Select(g => new HoursPerStaff(
				g.Key.TeacherId, g.Key.Name, g.Key.Role,
				Math.Round(g.Sum(s => (s.TimeSlot.EndTime - s.TimeSlot.StartTime).TotalHours), 2)));

		var aideHours = activeSlots
			.Where(s => s.AideId.HasValue)
			.GroupBy(s => (AideId: s.AideId!.Value, s.Aide!.Name, s.Aide.Role))
			.Select(g => new HoursPerStaff(
				g.Key.AideId, g.Key.Name, g.Key.Role,
				Math.Round(g.Sum(s => (s.TimeSlot.EndTime - s.TimeSlot.StartTime).TotalHours), 2)));

		var hoursPerStaff = teacherHours.Concat(aideHours)
			.OrderBy(h => h.StaffName)
			.ToList();

		// Unassigned slots: classes with no active schema, or active schemas with empty time slots
		var activeSchemas = await db.Schemas
			.AsNoTrackingWithIdentityResolution()
			.Where(s => s.StartDate <= today && s.EndDate >= today)
			.Include(s => s.Class)
			.Include(s => s.Slots)
			.ToListAsync(cancellationToken);

		var classTimeSlots = await db.TimeSlots.AsNoTracking().ToListAsync(cancellationToken);

		var activeSchemaClassIds = activeSchemas.Select(s => s.ClassId).ToHashSet();

		var allClasses = await db.Classes.AsNoTracking().ToListAsync(cancellationToken);

		// Classes with no active schema at all
		var classesWithoutSchema = allClasses
			.Where(c => !activeSchemaClassIds.Contains(c.Id))
			.Select(c => new UnassignedClass(c.Id, c.Name, 0, HasSchema: false));

		// Classes with an active schema but empty slots
		var classesWithGaps = activeSchemas.Select(schema =>
		{
			var applicable = classTimeSlots
				.Where(ts => ts.ClassId == schema.ClassId || ts.ClassId == null)
				.ToList();
			var emptySlots = applicable.Count * SchoolDaysPerWeek - schema.Slots.Count;
			return new UnassignedClass(schema.ClassId, schema.Class.Name, Math.Max(0, emptySlots), HasSchema: true);
		})
		.Where(u => u.EmptySlots > 0);

		var unassigned = classesWithoutSchema
			.Concat(classesWithGaps)
			.OrderBy(u => u.HasSchema)
			.ThenByDescending(u => u.EmptySlots)
			.ThenBy(u => u.ClassName)
			.ToList();

		// Attention alerts — pending absence and open vacation window are not module-gated.
		var pendingAbsenceCount = await db.AbsenceReports
			.CountAsync(a => a.Status == AbsenceStatus.Reported, cancellationToken);

		var openVacationWindow = await db.VacationRegistrationWindows
			.AsNoTracking()
			.Where(w => w.IsOpen && w.RegistrationDeadline >= today)
			.OrderBy(w => w.RegistrationDeadline)
			.Select(w => new OpenVacationWindowDto(w.Id, w.Title, w.RegistrationDeadline, w.Entries.Count))
			.FirstOrDefaultAsync(cancellationToken);

		// Beskeder / kontaktbog counts only exist when the parent module is active.
		var (unreadMessages, unreadKontaktbog) = await GetUnreadCountsAsync(cancellationToken);

		return Ok(new DashboardStats(
			classCount, staffCount, courseCount, roomCount,
			schemasComplete, schemasTotal,
			hoursPerCourse, hoursPerStaff, unassigned,
			pendingAbsenceCount, openVacationWindow,
			unreadMessages, unreadKontaktbog));
	}

	/// <summary>
	/// Staff landing dashboard: today's lektioner for the caller plus unread counts.
	/// Restricted to non-admin staff (Teacher/Aide/Vikar) at the API boundary — admins,
	/// parents, board members and super-admins are rejected regardless of frontend routing.
	/// </summary>
	[HttpGet("my-dashboard")]
	public async Task<ActionResult<MyDashboardStats>> GetMyDashboard(CancellationToken cancellationToken)
	{
		if (User.IsInRole(Roles.Admin)
			|| User.IsInRole(Roles.Parent)
			|| User.IsInRole(Roles.Board)
			|| User.IsInRole(Roles.SuperAdmin))
		{
			return Forbid();
		}

		var subject = User.GetKeycloakSubject();
		var staff = await db.Staff
			.AsNoTracking()
			.Where(s => s.KeycloakSubject == subject)
			.Select(s => new { s.Id, s.IsAdmin })
			.FirstOrDefaultAsync(cancellationToken);

		// A Staff row always carries a Teacher/Aide/Substitute role, so requiring one
		// is the positive staff check — StaffRole is a DB column, not a JWT claim, and
		// is therefore invisible to [Authorize(Roles = ...)]. IsAdmin is checked here
		// too: it can be set in the DB without the matching Keycloak admin claim.
		if (staff is null || staff.IsAdmin)
		{
			return Forbid();
		}

		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var weekday = today.DayOfWeek;

		var todaySchedule = await db.SchemaSlots
			.AsNoTracking()
			.Where(s => (s.Schema.StartDate == null || s.Schema.StartDate <= today)
					 && (s.Schema.EndDate == null || s.Schema.EndDate >= today))
			.Where(s => s.Weekday == weekday)
			.Where(s => s.TeacherId == staff.Id || s.AideId == staff.Id)
			.OrderBy(s => s.TimeSlot.StartTime)
			.Select(s => new TodayLektion(
				s.Id,
				s.TimeSlot.StartTime,
				s.TimeSlot.EndTime,
				s.Course.Name,
				s.Schema.Class.Name,
				s.Room != null ? s.Room.Name : null))
			.ToListAsync(cancellationToken);

		var (unreadMessages, unreadKontaktbog) = await GetUnreadCountsAsync(cancellationToken);

		return Ok(new MyDashboardStats(todaySchedule, unreadMessages, unreadKontaktbog));
	}

	/// <summary>
	/// Unread beskeder + kontaktbog counts for the calling staff member.
	/// Returns (null, null) when the parent module is inactive — both features live behind it.
	/// </summary>
	private async Task<(int? UnreadMessages, int? UnreadKontaktbog)> GetUnreadCountsAsync(
		CancellationToken cancellationToken)
	{
		var modules = await subscriptionService.GetActiveModulesAsync(tenantContext.TenantId, cancellationToken);
		if (!modules.Contains(nameof(SubscriptionModule.ParentModule)))
		{
			return (null, null);
		}

		var subject = User.GetKeycloakSubject();
		var staffId = await db.Staff
			.AsNoTracking()
			.Where(s => s.KeycloakSubject == subject)
			.Select(s => (Guid?)s.Id)
			.FirstOrDefaultAsync(cancellationToken);

		// No staff record (e.g. a superadmin viewing a tenant) means no personal inbox to count.
		var unreadMessages = staffId is null
			? 0
			: await db.Messages.CountAsync(
				m => m.RecipientId == staffId.Value && m.ReadAt == null, cancellationToken);

		// Kontaktbog threads are visible to all staff of the tenant (see ContactThreadsController),
		// so the unread count is tenant-wide: parent-sent messages that nobody has read yet.
		var unreadKontaktbog = await db.ContactMessages.CountAsync(
			m => m.ReadAt == null && m.SenderType == SenderType.Parent, cancellationToken);

		return (unreadMessages, unreadKontaktbog);
	}
}
