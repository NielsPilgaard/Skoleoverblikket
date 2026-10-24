using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Auth;

public sealed class EditWeekPlanRequirement : IAuthorizationRequirement { }

/// <summary>
/// Resource: (Guid ClassId, Guid SchemaSlotId).
/// Logic:
///   1. Not acting as staff → deny.
///   2. No staff row but admin role claim → full access (admin not enrolled as staff).
///   3. Staff row has IsAdmin = true → full access (guards against Keycloak/DB desync).
///   4. No ClassPermission rows for this class → open (no restrictions set).
///   5. Staff has a ClassPermission row for this class → allow.
///   6. Staff is the Teacher or Aide on the requested SchemaSlot → allow (own lesson).
///   7. Otherwise → deny.
/// </summary>
public sealed class EditWeekPlanAuthorizationHandler(AppDbContext db, ITenantContext tenant)
	: AuthorizationHandler<EditWeekPlanRequirement, (Guid ClassId, Guid SchemaSlotId)>
{
	protected override async Task HandleRequirementAsync(
		AuthorizationHandlerContext context,
		EditWeekPlanRequirement requirement,
		(Guid ClassId, Guid SchemaSlotId) resource)
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

		var classHasPermissions = await db.ClassPermissions.AnyAsync(p => p.ClassId == resource.ClassId);

		if (!classHasPermissions)
		{
			context.Succeed(requirement);
			return;
		}

		var hasClassPermission = await db.ClassPermissions
			.AnyAsync(p => p.ClassId == resource.ClassId && p.StaffId == staff.Id);

		if (hasClassPermission)
		{
			context.Succeed(requirement);
			return;
		}

		// Teacher or aide assigned to the specific schema slot can edit that slot.
		var isAssigned = await db.SchemaSlots
			.AnyAsync(s => s.Id == resource.SchemaSlotId &&
				(s.TeacherId == staff.Id || s.AideId == staff.Id));

		if (isAssigned)
		{
			context.Succeed(requirement);
		}
	}
}
