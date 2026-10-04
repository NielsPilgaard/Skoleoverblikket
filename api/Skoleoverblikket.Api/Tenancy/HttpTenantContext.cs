using System.Security.Claims;

namespace Skoleoverblikket.Api.Tenancy;

/// <summary>
/// Resolves the current tenant from the authenticated JWT claim.
/// The tenant_id claim is set by Keycloak after the user's school is looked up at login.
/// Never trust a URL slug — always resolve to a TenantId at the middleware boundary.
/// </summary>
public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
	private Guid? _backgroundTenantId;

	public Guid TenantId
	{
		get
		{
			if (_backgroundTenantId is { } backgroundTenantId)
			{
				return backgroundTenantId;
			}

			var claim = accessor.HttpContext?.User.FindFirstValue("tenant_id")
				?? throw new MissingTenantClaimException();

			return Guid.Parse(claim);
		}
	}

	/// <summary>
	/// Pins this scope to one tenant for work that runs outside a request (background jobs).
	/// Every scoped service resolved from the same scope — AppDbContext's query filter included —
	/// then sees this tenant. Never call it from request code: the JWT claim is the only trusted
	/// tenant source there.
	/// </summary>
	public void UseBackgroundTenant(Guid tenantId)
	{
		if (accessor.HttpContext is not null)
		{
			throw new InvalidOperationException("A background tenant cannot be set inside an HTTP request.");
		}

		_backgroundTenantId = tenantId;
	}
}

/// <summary>
/// Thrown when a request reaches tenant-scoped code without a tenant_id claim.
/// This happens when a user's JWT was issued without the Keycloak attribute mapper
/// configured, or when an unauthenticated request bypasses auth middleware.
/// </summary>
public sealed class MissingTenantClaimException()
	: Exception("tenant_id claim not present in JWT. Check Keycloak client mapper configuration.");
