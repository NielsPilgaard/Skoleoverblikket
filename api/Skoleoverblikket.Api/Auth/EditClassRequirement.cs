using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Auth;

public sealed class EditClassRequirement : IAuthorizationRequirement { }

/// <summary>
/// Resource-based handler. Resource is the classId (Guid).
/// Logic:
///   1. Not acting as staff → deny.
///   2. Admin (Staff.IsAdmin, as a role claim) → full access.
///   3. No staff row → deny.
///   4. No ClassPermission rows exist for the class → open, all staff succeed.
///   5. Permission rows exist and one matches this staff member → succeed.
///   6. Otherwise → deny.
/// </summary>
public sealed class EditClassAuthorizationHandler(AppDbContext db, ITenantContext tenant)
	: AuthorizationHandler<EditClassRequirement, Guid>
{
	protected override async Task HandleRequirementAsync(
		AuthorizationHandlerContext context,
		EditClassRequirement requirement,
		Guid classId)
	{
		// Only staff edit: a parent or board member who is also staff must switch to staff first.
		if (!context.User.IsInRole(Roles.Staff))
		{
			return;
		}

		// The admin claim is Staff.IsAdmin, worked out by SchoolRolesClaimsTransformer.
		if (context.User.IsInRole(Roles.Admin))
		{
			context.Succeed(requirement);
			return;
		}

		var subject = context.User.GetKeycloakSubject();
		var staff = await db.Staff
			.AsNoTracking()
			.Where(s => s.KeycloakSubject == subject && s.TenantId == tenant.TenantId)
			.Select(s => new { s.Id })
			.FirstOrDefaultAsync();
		if (staff is null)
		{
			return;
		}

		// Non-admin staff: class-scoped check.
		// If the class has no ClassPermission rows → open (no restrictions set for this class).
		// If rows exist for the class → staff must have one.
		var classHasPermissions = await db.ClassPermissions.AnyAsync(p => p.ClassId == classId);

		if (!classHasPermissions)
		{
			context.Succeed(requirement);
			return;
		}

		var hasPermission = await db.ClassPermissions
			.AnyAsync(p => p.ClassId == classId && p.StaffId == staff.Id);

		if (hasPermission)
		{
			context.Succeed(requirement);
		}
	}
}
