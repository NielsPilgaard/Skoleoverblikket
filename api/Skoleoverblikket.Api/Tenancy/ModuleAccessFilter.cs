using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Tenancy;

/// <summary>
/// Enforces the paid modules (BoardModule, ParentModule). Runs after <see cref="SubscriptionAccessFilter"/>.
/// R1: board and parent users only exist while their module is active, so a request acting as board
/// or parent (one acting role per request, see SchoolRolesClaimsTransformer) gets 403 without it. R2: writes (POST/PUT/PATCH) on actions marked
/// <see cref="RequiresModuleAttribute"/> get 403 without the module; GET and DELETE still work,
/// unless the attribute sets <see cref="RequiresModuleAttribute.GatesReads"/>.
/// A trial counts as having every module. See docs/AUTHORIZATION.md, "Paid modules".
/// </summary>
public sealed class ModuleAccessFilter(SubscriptionService subscriptions, ITenantContext tenantContext) : IAsyncActionFilter
{
	private const string Title = "Modulet er ikke aktivt";

	private static readonly HashSet<string> WriteMethods =
		new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH" };

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

		// The module the acting role needs (R1). Staff is never gated, and the transformer leaves
		// at most one of board and parent.
		var roleModule = metadata.OfType<IAllowAnonymous>().Any() || IsRoleStateEndpoint(request)
			? null
			: GatedRoles.Where(g => user.IsInRole(g.Role)).Select(g => (SubscriptionModule?)g.Module).FirstOrDefault();
		var isWrite = WriteMethods.Contains(request.Method);
		var isRead = HttpMethods.IsGet(request.Method);
		var requiredModules = metadata
			.OfType<RequiresModuleAttribute>()
			.Where(a => isWrite || (isRead && a.GatesReads))
			.Select(a => a.Module)
			.Distinct()
			.ToList();

		if (roleModule is null && requiredModules.Count == 0)
		{
			await next();
			return;
		}

		// A module check is needed, so fail closed: without a tenant claim this throws
		// MissingTenantClaimException, which MissingTenantClaimExceptionHandler turns into a 401.
		var activeModules = await subscriptions.GetActiveModulesAsync(tenantContext.TenantId, context.HttpContext.RequestAborted);
		bool IsActive(SubscriptionModule module) => activeModules.Contains(module.ToString());

		if (roleModule is { } module && !IsActive(module))
		{
			context.Result = Forbidden($"Skolen har ikke længere {ModuleName(module)}. Kontakt skolens kontor.");
			return;
		}

		foreach (var required in requiredModules)
		{
			if (!IsActive(required))
			{
				context.Result = Forbidden($"Kræver {ModuleName(required).ToLowerInvariant()}. Aktivér det under Abonnement.");
				return;
			}
		}

		await next();
	}

	// The frontend reads the module state and the person's roles here, so a blocked user can still be
	// shown why and switch to another role.
	private static readonly string[] RoleStatePaths = ["/api/v1/modules", "/api/v1/me"];

	private static bool IsRoleStateEndpoint(HttpRequest request) =>
		HttpMethods.IsGet(request.Method)
		&& RoleStatePaths.Contains(request.Path.Value?.TrimEnd('/'), StringComparer.OrdinalIgnoreCase);

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
