using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize]
public sealed class SchedulesController(AppDbContext db, IAuthorizationService authz) : ControllerBase
{
	public record ScheduleSlotDto(
		DayOfWeek Weekday,
		string StartTime,
		string EndTime,
		string CourseName,
		string? CourseColor,
		string ClassName,
		Guid ClassId,
		Guid SchemaId,
		Guid? RoomId,
		string? RoomName,
		Guid? AideId,
		string? AideName,
		Guid? TeacherId,
		string? TeacherName);

	/// <summary>
	/// Weekly schedule for a class from its active schema.
	/// </summary>
	[HttpGet("classes/{classId:guid}/schedule")]
	public async Task<ActionResult<List<ScheduleSlotDto>>> GetClassSchedule(Guid classId, CancellationToken cancellationToken)
	{
		if (User.IsInRole(Roles.Board)
			&& !(await authz.AuthorizeAsync(User, null, Policies.CanAccessTeacherData)).Succeeded)
		{
			return Forbid();
		}

		if (!(await authz.AuthorizeAsync(User, classId, Policies.ParentClassAccess)).Succeeded)
		{
			return Forbid();
		}

		var classExists = await db.Classes.AnyAsync(c => c.Id == classId, cancellationToken);
		if (!classExists)
		{
			return NotFound();
		}

		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var slots = await db.SchemaSlots
							.AsNoTrackingWithIdentityResolution()
							.Where(s => s.Schema.StartDate <= today && s.Schema.EndDate >= today && s.Schema.ClassId == classId)
							.Include(s => s.TimeSlot)
							.Include(s => s.Course)
							.Include(s => s.Schema)
							.ThenInclude(sc => sc.Class)
							.Include(s => s.Room)
							.Include(s => s.Teacher)
							.Include(s => s.Aide)
							.OrderBy(s => s.Weekday)
							.ThenBy(s => s.TimeSlot.SortOrder)
							.Select(s => new ScheduleSlotDto(
										s.Weekday,
										s.TimeSlot.StartTime.ToString("HH:mm"),
										s.TimeSlot.EndTime.ToString("HH:mm"),
										s.Course.Name,
										s.Course.Color,
										s.Schema.Class.Name,
										s.Schema.ClassId,
										s.SchemaId,
										s.RoomId,
										s.Room != null ? s.Room.Name : null,
										s.AideId,
										s.Aide != null ? s.Aide.Name : null,
										s.TeacherId,
										s.Teacher.Name))
							.ToListAsync(cancellationToken);

		return Ok(slots);
	}

	/// <summary>
	/// Weekly schedule for a staff member (teacher or aide) across all active schemas.
	/// </summary>
	[HttpGet("staff/{staffId:guid}/schedule")]
	[Authorize(Roles = Roles.Staff)]
	public async Task<ActionResult<List<ScheduleSlotDto>>> GetStaffSchedule(Guid staffId, CancellationToken cancellationToken)
	{
		var staffExists = await db.Staff.AnyAsync(s => s.Id == staffId, cancellationToken);
		if (!staffExists)
		{
			return NotFound();
		}

		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var slots = await db.SchemaSlots
							.AsNoTrackingWithIdentityResolution()
							.Where(s => s.Schema.StartDate <= today && s.Schema.EndDate >= today && (s.TeacherId == staffId || s.AideId == staffId))
							.Include(s => s.TimeSlot)
							.Include(s => s.Course)
							.Include(s => s.Schema)
							.ThenInclude(sc => sc.Class)
							.Include(s => s.Room)
							.Include(s => s.Teacher)
							.Include(s => s.Aide)
							.OrderBy(s => s.Weekday)
							.ThenBy(s => s.TimeSlot.SortOrder)
							.Select(s => new ScheduleSlotDto(
										s.Weekday,
										s.TimeSlot.StartTime.ToString("HH:mm"),
										s.TimeSlot.EndTime.ToString("HH:mm"),
										s.Course.Name,
										s.Course.Color,
										s.Schema.Class.Name,
										s.Schema.ClassId,
										s.SchemaId,
										s.RoomId,
										s.Room != null ? s.Room.Name : null,
										s.AideId,
										s.Aide != null ? s.Aide.Name : null,
										s.TeacherId,
										s.Teacher.Name))
							.ToListAsync(cancellationToken);

		return Ok(slots);
	}

	/// <summary>
	/// Weekly schedule for a room across all active schemas.
	/// </summary>
	[HttpGet("rooms/{roomId:guid}/schedule")]
	[Authorize(Roles = Roles.Staff)]
	public async Task<ActionResult<List<ScheduleSlotDto>>> GetRoomSchedule(Guid roomId, CancellationToken cancellationToken)
	{
		var roomExists = await db.Rooms.AnyAsync(r => r.Id == roomId, cancellationToken);
		if (!roomExists)
		{
			return NotFound();
		}

		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var slots = await db.SchemaSlots
							.AsNoTrackingWithIdentityResolution()
							.Where(s => s.Schema.StartDate <= today && s.Schema.EndDate >= today && s.RoomId == roomId)
							.Include(s => s.TimeSlot)
							.Include(s => s.Course)
							.Include(s => s.Schema)
							.ThenInclude(sc => sc.Class)
							.Include(s => s.Room)
							.Include(s => s.Teacher)
							.Include(s => s.Aide)
							.OrderBy(s => s.Weekday)
							.ThenBy(s => s.TimeSlot.SortOrder)
							.Select(s => new ScheduleSlotDto(
										s.Weekday,
										s.TimeSlot.StartTime.ToString("HH:mm"),
										s.TimeSlot.EndTime.ToString("HH:mm"),
										s.Course.Name,
										s.Course.Color,
										s.Schema.Class.Name,
										s.Schema.ClassId,
										s.SchemaId,
										s.RoomId,
										s.Room != null ? s.Room.Name : null,
										s.AideId,
										s.Aide != null ? s.Aide.Name : null,
										s.TeacherId,
										s.Teacher.Name))
							.ToListAsync(cancellationToken);

		return Ok(slots);
	}
}
