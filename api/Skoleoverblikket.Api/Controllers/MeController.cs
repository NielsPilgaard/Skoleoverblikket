using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Controllers;

/// <summary>
/// Who the caller is at their school: every school role they hold, and whether its module is on. The
/// frontend shows a "Vis som" switcher when there are two or more, and sends the choice as X-Acting-As.
/// Not gated by modules (ModuleAccessFilter), so a blocked board member or parent can still be told why.
/// </summary>
[ApiController]
[Route("api/v1/me")]
[Authorize]
public sealed class MeController(SubscriptionService subscriptions) : ControllerBase
{
	/// <param name="Role">staff, parent or board: the values X-Acting-As accepts.</param>
	/// <param name="Active">False when the school no longer has the module this role needs.</param>
	public sealed record MeRoleDto(string Role, bool Active);

	/// <param name="Roles">Held school roles, in the default order staff, parent, board.</param>
	/// <param name="ActingAs">The role this request acted as: the X-Acting-As header, or the default.</param>
	/// <param name="IsAdmin">Admin at the school. Only applies while acting as staff.</param>
	public sealed record MeDto(IReadOnlyList<MeRoleDto> Roles, string? ActingAs, bool IsAdmin, bool IsSuperAdmin);

	[HttpGet]
	public async Task<ActionResult<MeDto>> Get(CancellationToken cancellationToken)
	{
		if (SchoolRolesClaimsTransformer.For(HttpContext) is not { } roles)
		{
			return Ok(new MeDto([], null, IsAdmin: false, User.IsInRole(Auth.Roles.SuperAdmin)));
		}

		var activeModules = roles.TenantId is { } tenantId && roles.Held.Count > 0
			? await subscriptions.GetActiveModulesAsync(tenantId, cancellationToken)
			: [];

		return Ok(new MeDto(
			roles.Held.Select(r => new MeRoleDto(r, IsActive(r, activeModules))).ToList(),
			roles.Acting,
			roles.IsAdmin,
			roles.IsSuperAdmin));
	}

	private static bool IsActive(string role, IReadOnlyList<string> activeModules) => role switch
	{
		Auth.Roles.Parent => activeModules.Contains(nameof(SubscriptionModule.ParentModule)),
		Auth.Roles.Board => activeModules.Contains(nameof(SubscriptionModule.BoardModule)),
		_ => true,
	};
}
