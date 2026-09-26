using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/contact-directory")]
[Authorize]
public sealed class ContactDirectoryController(AppDbContext db) : ControllerBase
{
	public record ContactDirectoryParentDto(
		Guid Id,
		string Name,
		string? Phone,
		string? Address,
		string? PostalCode,
		string? City,
		string? Email,
		string? AvatarUrl,
		IReadOnlyList<string> StudentNames,
		IReadOnlyList<ContactDirectoryStudentDto> Students);

	public record ContactDirectoryStudentDto(Guid Id, string Name);

	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<ContactDirectoryParentDto>>> GetDirectory(CancellationToken cancellationToken)
	{
		var subject = User.GetKeycloakSubject();

		if (string.IsNullOrEmpty(subject))
		{
			return Unauthorized();
		}

		var isAdmin = User.IsInRole(Roles.Admin);
		var isParent = User.IsInRole(Roles.Parent);
		var isStaff = !isAdmin && !isParent;

		IQueryable<Models.Parent> query;
		HashSet<Guid>? visibleStudentIds = null;

		if (isAdmin)
		{
			// Admin sees all parents, all fields, no consent filter
			query = db.Parents.Include(p => p.Students);
		}
		else if (isParent)
		{
			// Parent sees only co-class parents with ShareContactInfo=true
			var myStudentIds = db.Parents
				.Where(p => p.KeycloakSubject == subject)
				.SelectMany(p => p.Students.Select(s => s.Id));

			var myClassIds = db.Students
				.Where(s => myStudentIds.Contains(s.Id))
				.Select(s => s.ClassId);

			var coClassStudentIds = db.Students
				.Where(s => myClassIds.Contains(s.ClassId))
				.Select(s => s.Id);

			visibleStudentIds = await coClassStudentIds.ToHashSetAsync(cancellationToken);

			query = db.Parents
				.Include(p => p.Students)
				.Where(p => p.ShareContactInfo
					&& p.Students.Any(s => coClassStudentIds.Contains(s.Id)));
		}
		else if (isStaff)
		{
			// Staff sees all parents with ShareContactInfo=true
			query = db.Parents
				.Include(p => p.Students)
				.Where(p => p.ShareContactInfo);
		}
		else
		{
			return Forbid();
		}

		var parents = await query
			.AsNoTracking()
			.OrderBy(p => p.Name)
			.ToListAsync(cancellationToken);

		var dtos = parents.Select(p =>
		{
			var hideDetails = p.AdresseBeskyttet && !isAdmin;
			// For a parent caller, only surface each contact's students that are in a class
			// shared with the caller — a co-class parent's other children (different, unrelated
			// class) must not leak through here just because the parent row itself is visible.
			var visibleStudents = visibleStudentIds is null
				? p.Students
				: p.Students.Where(s => visibleStudentIds.Contains(s.Id));
			return new ContactDirectoryParentDto(
				p.Id,
				p.Name,
				hideDetails ? null : p.Phone,
				hideDetails ? null : p.Address,
				hideDetails ? null : p.PostalCode,
				hideDetails ? null : p.City,
				hideDetails ? null : p.Email,
				p.AvatarUrl,
				visibleStudents.Select(s => s.Name).OrderBy(n => n).ToList(),
				visibleStudents.Select(s => new ContactDirectoryStudentDto(s.Id, s.Name)).OrderBy(s => s.Name).ToList());
		}).ToList();

		return Ok(dtos);
	}
}
