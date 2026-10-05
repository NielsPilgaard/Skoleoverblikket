using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Skoleoverblikket.Api.Auth;

/// <summary>
/// Signed, single-use download links for the full school export. The ZIP can be large, so the
/// browser must download it natively, and a plain link can't send the bearer token. A logged-in
/// admin asks for a link; the token in it carries the admin's tenant and subject, is encrypted
/// and signed by ASP.NET Core data protection, expires after <see cref="Lifetime"/> and works once.
/// </summary>
public sealed class ExportLinkTokens(IDataProtectionProvider dataProtection, IMemoryCache used)
{
	public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

	private readonly ITimeLimitedDataProtector _protector =
		dataProtection.CreateProtector("Skoleoverblikket.SchoolExportLink").ToTimeLimitedDataProtector();

	public string Create(Guid tenantId, string subject) =>
		_protector.Protect($"{tenantId:N}|{subject}", Lifetime);

	/// <summary>The link's admin, or null if the token is forged, expired or already used.</summary>
	public (Guid TenantId, string Subject)? Redeem(string token)
	{
		string payload;
		try
		{
			payload = _protector.Unprotect(token);
		}
		catch (System.Security.Cryptography.CryptographicException)
		{
			return null;
		}

		// Each token is unique (random IV), so the token itself marks the link as used. Single use
		// also means a token that ends up in the request log or elmah.io is already dead.
		var key = $"export-link:{token}";
		if (used.TryGetValue(key, out _))
		{
			return null;
		}

		used.Set(key, true, Lifetime);

		var parts = payload.Split('|', 2);
		return Guid.TryParse(parts[0], out var tenantId) && parts.Length == 2
			? (tenantId, parts[1])
			: null;
	}
}

/// <summary>Authenticates a request by the <c>token</c> query parameter of an export link.</summary>
public sealed class ExportLinkAuthHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder,
	ExportLinkTokens tokens)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	public const string SchemeName = "ExportLink";

	protected override Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		var token = Request.Query["token"].FirstOrDefault();
		if (string.IsNullOrEmpty(token))
		{
			return Task.FromResult(AuthenticateResult.NoResult());
		}

		if (tokens.Redeem(token) is not { } link)
		{
			return Task.FromResult(AuthenticateResult.Fail("Invalid or expired export link."));
		}

		// Same claims HttpTenantContext and [Authorize(Roles = Admin)] read from a Keycloak token.
		var identity = new ClaimsIdentity(
			[
				new Claim("sub", link.Subject),
				new Claim("tenant_id", link.TenantId.ToString()),
				new Claim(ClaimTypes.Role, Roles.Admin),
			],
			SchemeName,
			nameType: "sub",
			roleType: ClaimTypes.Role);

		return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
	}
}
