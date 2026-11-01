using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Auth;

public sealed class ParentClassAccessRequirement : IAuthorizationRequirement { }

/// <summary>
/// Resource-based handler. Resource is classId (Guid).
/// Admin role: always succeed.
/// Staff and board: always succeed. No school role: fail.
/// Parent role: resolve parent by JWT sub → student class IDs → check classId is in set.
/// </summary>
public sealed class ParentClassAccessHandler(AppDbContext db, ITenantContext tenant)
	: AuthorizationHandler<ParentClassAccessRequirement, Guid>
{
	protected override async Task HandleRequirementAsync(
		AuthorizationHandlerContext context,
		ParentClassAccessRequirement requirement,
		Guid classId)
	{
		if (context.User.IsInRole(Roles.Admin))
		{
			context.Succeed(requirement);
			return;
		}

		// Staff read all class data. Board members are checked against CanAccessTeacherData by the caller.
		// A login with no school role gets nothing.
		if (context.User.IsInRole(Roles.Staff) || context.User.IsInRole(Roles.Board))
		{
			context.Succeed(requirement);
			return;
		}

		if (!context.User.IsInRole(Roles.Parent))
		{
			return;
		}

		var subject = context.User.GetKeycloakSubject();
		if (string.IsNullOrEmpty(subject))
		{
			return;
		}

		var classIds = await db.Parents
			.AsNoTracking()
			.Where(p => p.KeycloakSubject == subject && p.TenantId == tenant.TenantId)
			.SelectMany(p => p.Students)
			.Select(s => s.ClassId)
			.Distinct()
			.ToListAsync();

		if (classIds.Contains(classId))
		{
			context.Succeed(requirement);
		}
	}
}
