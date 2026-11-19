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
///   2. Admin (Staff.IsAdmin, as a role claim) → full access.
///   3. No staff row → deny.
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
