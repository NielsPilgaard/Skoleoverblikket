using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Skoleoverblikket.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Fake authentication handler used in tests. Always returns an authenticated principal so endpoints
/// don't 401, shaped like a Keycloak token: <c>sub</c>, <c>tenant_id</c> and the realm roles in
/// <c>realm_access</c>. As in production, SchoolRolesClaimsTransformer then works out the school roles
/// from Staff, BoardMember and Parent rows, so a test acting as staff, parent or board needs that row.
/// Realm roles only grant <c>superadmin</c>. <see cref="TestDataBuilder.CreateSchoolAsync"/> makes the
/// default subject the school's admin.
/// </summary>
public sealed class TestAuthHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	public const string SchemeName = "Test";

	protected override Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		// X-Test-Roles: comma-separated Keycloak realm roles; only superadmin counts.
		var roles = Request.Headers["X-Test-Roles"].FirstOrDefault()?
			.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

		var subject = Request.Headers["X-Test-Subject"].FirstOrDefault() ?? TestDataBuilder.DefaultSubject;

		var tenantIdHeader = Request.Headers["X-Test-TenantId"].FirstOrDefault();
		var tenantId = string.IsNullOrWhiteSpace(tenantIdHeader)
			? TestTenantContext.DefaultTenantId.ToString()
			: tenantIdHeader;

		var claims = new List<Claim>
		{
			new(ClaimTypes.Name, "Test User"),
			new(ClaimTypes.NameIdentifier, subject),
			new("sub", subject),
			new("tenant_id", tenantId),
		};
		claims.Add(new Claim("realm_access", JsonSerializer.Serialize(new { roles })));

		var identity = new ClaimsIdentity(claims, SchemeName);
		var principal = new ClaimsPrincipal(identity);
		var ticket = new AuthenticationTicket(principal, SchemeName);

		return Task.FromResult(AuthenticateResult.Success(ticket));
	}
}
