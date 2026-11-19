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
///   2. No staff row but admin role claim → full access (admin not enrolled as staff).
///   3. Staff row has IsAdmin = true → full access (guards against Keycloak/DB desync).
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

		var subject = context.User.GetKeycloakSubject();
		var staff = await db.Staff
			.AsNoTracking()
			.Where(s => s.KeycloakSubject == subject && s.TenantId == tenant.TenantId)
			.Select(s => new { s.Id, s.IsAdmin })
			.FirstOrDefaultAsync();

		// Phase A of task 61: the admin claim can still come from Keycloak, so Staff.IsAdmin stays
		// authoritative for a staff row. Phase B makes the claim equal Staff.IsAdmin; then this
		// collapses to a single admin-claim check.
		if (staff is null)
		{
			if (context.User.IsInRole(Roles.Admin))
			{
				context.Succeed(requirement);
			}

			return;
		}

		if (staff.IsAdmin)
		{
			context.Succeed(requirement);
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
