using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Tenancy;

/// <summary>
/// Enforces the paid modules (BoardModule, ParentModule). Runs after <see cref="SubscriptionAccessFilter"/>.
/// R1: board and parent users only exist while their module is active, so a principal acting only as
/// such a user gets 403 without it. R2: writes (POST/PUT/PATCH) on actions marked
/// <see cref="RequiresModuleAttribute"/> get 403 without the module; GET and DELETE still work.
/// A trial counts as having every module. See docs/AUTHORIZATION.md, "Paid modules".
/// </summary>
public sealed class ModuleAccessFilter(SubscriptionService subscriptions, ITenantContext tenantContext) : IAsyncActionFilter
{
	private const string Title = "Modulet er ikke aktivt";

	private static readonly HashSet<string> WriteMethods =
		new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH" };

	// Realm roles that decide what a principal can do. Staff have none of them.
	private static readonly string[] AppRoles = [Roles.Admin, Roles.SuperAdmin, Roles.Board, Roles.Parent];

	private static readonly (string Role, SubscriptionModule Module)[] GatedRoles =
	[
		(Roles.Board, SubscriptionModule.BoardModule),
		(Roles.Parent, SubscriptionModule.ParentModule),
	];

	public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
	{
		var request = context.HttpContext.Request;
		var user = context.HttpContext.User;
		var metadata = context.ActionDescriptor.EndpointMetadata;

		var heldGatedRoles = GatedRoles.Where(g => user.IsInRole(g.Role)).ToList();
		var checkRoles = heldGatedRoles.Count > 0
			&& !metadata.OfType<IAllowAnonymous>().Any()
			&& !IsModulesEndpoint(request);
		var requiredModules = WriteMethods.Contains(request.Method)
			? metadata.OfType<RequiresModuleAttribute>().Select(a => a.Module).Distinct().ToList()
			: [];

		if (!checkRoles && requiredModules.Count == 0)
		{
			await next();
			return;
		}

		Guid schoolId;
		try
		{
			schoolId = tenantContext.TenantId;
		}
		catch (MissingTenantClaimException)
		{
			// No tenant claim — let auth handle it.
			await next();
			return;
		}

		var activeModules = await subscriptions.GetActiveModulesAsync(schoolId, context.HttpContext.RequestAborted);
		bool IsActive(SubscriptionModule module) => activeModules.Contains(module.ToString());

		if (checkRoles)
		{
			var inactiveRoles = heldGatedRoles.Where(g => !IsActive(g.Module)).ToList();
			if (inactiveRoles.Count > 0)
			{
				var usableRoles = AppRoles
					.Where(r => user.IsInRole(r) && inactiveRoles.All(g => g.Role != r))
					.ToHashSet();
				if (!CanActWith(usableRoles, metadata))
				{
					context.Result = Forbidden($"Skolen har ikke længere {ModuleName(inactiveRoles[0].Module)}. Kontakt skolens kontor.");
					return;
				}
			}
		}

		foreach (var module in requiredModules)
		{
			if (!IsActive(module))
			{
				context.Result = Forbidden($"Kræver {ModuleName(module).ToLowerInvariant()}. Aktivér det under Abonnement.");
				return;
			}
		}

		await next();
	}

	// The frontend reads the module state here, so a blocked user can still be shown why.
	private static bool IsModulesEndpoint(HttpRequest request) =>
		HttpMethods.IsGet(request.Method)
		&& string.Equals(request.Path.Value?.TrimEnd('/'), "/api/v1/modules", StringComparison.OrdinalIgnoreCase);

	// Each [Authorize(Roles = ...)] on the controller and action must be met by a role whose module is
	// active. Without a role list, any such role will do: a principal is only blocked when every role it
	// holds is gated and off.
	private static bool CanActWith(HashSet<string> usableRoles, IList<object> metadata)
	{
		var roleLists = metadata
			.OfType<IAuthorizeData>()
			.Where(a => !string.IsNullOrWhiteSpace(a.Roles))
			.Select(a => a.Roles!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			.ToList();

		return roleLists.Count == 0
			? usableRoles.Count > 0
			: roleLists.All(roles => roles.Any(usableRoles.Contains));
	}

	private static string ModuleName(SubscriptionModule module) => module switch
	{
		SubscriptionModule.BoardModule => "Bestyrelsesmodulet",
		SubscriptionModule.ParentModule => "Forældremodulet",
		_ => throw new ArgumentOutOfRangeException(nameof(module), module, null),
	};

	private static ObjectResult Forbidden(string detail) =>
		new(new ProblemDetails
		{
			Title = Title,
			Detail = detail,
			Status = StatusCodes.Status403Forbidden,
		})
		{
			StatusCode = StatusCodes.Status403Forbidden,
		};
}
