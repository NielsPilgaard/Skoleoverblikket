using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Auth;

/// <summary>
/// The school roles of the request's principal, worked out once per request by
/// <see cref="SchoolRolesClaimsTransformer"/>. <see cref="Held"/> is every school role the person has
/// (staff, parent, board, in that order); <see cref="Acting"/> is the one this request uses.
/// </summary>
public sealed record SchoolRoles(
	string? Subject,
	Guid? TenantId,
	IReadOnlyList<string> Held,
	string? Acting,
	bool IsAdmin,
	bool IsSuperAdmin,
	ActingAsError? Error);

public enum ActingAsError
{
	/// <summary>The X-Acting-As header is not staff, parent or board.</summary>
	InvalidValue,

	/// <summary>The X-Acting-As header names a role the person doesn't hold.</summary>
	NotHeld,
}

/// <summary>
/// Keycloak authenticates, the API authorizes. School roles (admin, staff, board, parent) come from the
/// Staff, BoardMember and Parent rows that match the token's <c>sub</c> in the token's school, never from
/// the token, so a stale Keycloak role grants nothing. Only <c>superadmin</c> is read from Keycloak's
/// <c>realm_access</c>. A person can hold several school roles; the request acts as exactly one, chosen
/// by the <c>X-Acting-As</c> header or the default order staff, parent, board. <c>admin</c> is only
/// kept when acting as staff. See docs/AUTHORIZATION.md, "Roles".
/// <para>
/// Phase A of task 61: Keycloak's <c>admin</c> role still counts alongside <c>Staff.IsAdmin</c> until
/// <see cref="AdminRoleBackfillJob"/> has copied it to the database in production.
/// </para>
/// </summary>
public sealed class SchoolRolesClaimsTransformer(
	AppDbContext db,
	SubscriptionService subscriptions,
	IHttpContextAccessor accessor) : IClaimsTransformation
{
	public const string ActingAsHeader = "X-Acting-As";

	private const string ItemsKey = "SchoolRoles";
	private const string ResolvedClaim = "school_roles_resolved";

	private static readonly (string Role, SubscriptionModule? Module)[] SchoolRoleOrder =
	[
		(Roles.Staff, null),
		(Roles.Parent, SubscriptionModule.ParentModule),
		(Roles.Board, SubscriptionModule.BoardModule),
	];

	/// <summary>The roles worked out for this request, or null before authentication.</summary>
	public static SchoolRoles? For(HttpContext context) => context.Items[ItemsKey] as SchoolRoles;

	public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
	{
		// ASP.NET can transform the same principal more than once per request.
		if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity
			|| principal.HasClaim(c => c.Type == ResolvedClaim))
		{
			return principal;
		}

		var http = accessor.HttpContext;
		var subject = principal.GetKeycloakSubject();
		var tenantId = Guid.TryParse(principal.FindFirstValue("tenant_id"), out var parsed) ? parsed : (Guid?)null;

		var roles = http is not null && For(http) is { } cached && cached.Subject == subject && cached.TenantId == tenantId
			? cached
			: await ResolveAsync(principal, subject, tenantId, http?.Request.Headers[ActingAsHeader].FirstOrDefault());
		if (http is not null)
		{
			http.Items[ItemsKey] = roles;
		}

		// Role claims from the token (or a test or export-link handler) are dropped; only ours count.
		var claims = identity.Claims.Where(c => c.Type != identity.RoleClaimType).ToList();
		claims.Add(new Claim(ResolvedClaim, "true"));
		if (roles.IsSuperAdmin)
		{
			claims.Add(new Claim(identity.RoleClaimType, Roles.SuperAdmin));
		}

		if (roles.Acting is not null)
		{
			claims.Add(new Claim(identity.RoleClaimType, roles.Acting));
		}

		if (roles.IsAdmin && roles.Acting == Roles.Staff)
		{
			claims.Add(new Claim(identity.RoleClaimType, Roles.Admin));
		}

		return new ClaimsPrincipal(new ClaimsIdentity(claims, identity.AuthenticationType, identity.NameClaimType, identity.RoleClaimType));
	}

	private async Task<SchoolRoles> ResolveAsync(ClaimsPrincipal principal, string? subject, Guid? tenantId, string? actingAsHeader)
	{
		var tokenRoles = ReadRealmRoles(principal);
		var isSuperAdmin = tokenRoles.Contains(Roles.SuperAdmin);
		if (string.IsNullOrEmpty(subject) || tenantId is not { } schoolId)
		{
			return new SchoolRoles(subject, tenantId, [], null, false, isSuperAdmin, null);
		}

		// HttpTenantContext reads HttpContext.User, which isn't set while authentication runs, so the
		// tenant query filter can't be used here. Every lookup filters on the token's tenant explicitly.
		// IgnoreQueryFilters on the root applies to the whole query, subqueries included.
		var rows = await db.Schools
			.IgnoreQueryFilters()
			.AsNoTracking()
			.Where(s => s.Id == schoolId)
			.Select(s => new
			{
				Staff = db.Staff.Any(x => x.TenantId == s.Id && x.KeycloakSubject == subject),
				StaffAdmin = db.Staff.Any(x => x.TenantId == s.Id && x.KeycloakSubject == subject && x.IsAdmin),
				Parent = db.Parents.Any(x => x.TenantId == s.Id && x.KeycloakSubject == subject),
				Board = db.BoardMembers.Any(x => x.TenantId == s.Id && x.KeycloakSubject == subject),
			})
			.FirstOrDefaultAsync();
		if (rows is null)
		{
			return new SchoolRoles(subject, tenantId, [], null, false, isSuperAdmin, null);
		}

		// Phase A: Keycloak admin still counts, but only for someone who is staff or holds no other school
		// role (an admin from before signup created a Staff row). A former teacher who is still a parent
		// keeps the login but must not keep admin through a leftover Keycloak role.
		var keycloakAdmin = tokenRoles.Contains(Roles.Admin) && (rows.Staff || (!rows.Parent && !rows.Board));
		var isAdmin = rows.StaffAdmin || keycloakAdmin;

		var held = new List<string>();
		if (rows.Staff || isAdmin)
		{
			held.Add(Roles.Staff);
		}

		if (rows.Parent)
		{
			held.Add(Roles.Parent);
		}

		if (rows.Board)
		{
			held.Add(Roles.Board);
		}

		if (!string.IsNullOrEmpty(actingAsHeader))
		{
			var requested = actingAsHeader.Trim().ToLowerInvariant();
			ActingAsError? error = SchoolRoleOrder.All(r => r.Role != requested) ? ActingAsError.InvalidValue
				: !held.Contains(requested) ? ActingAsError.NotHeld
				: null;
			return error is null
				? new SchoolRoles(subject, tenantId, held, requested, isAdmin, isSuperAdmin, null)
				: new SchoolRoles(subject, tenantId, held, null, false, isSuperAdmin, error);
		}

		return new SchoolRoles(subject, tenantId, held, await DefaultRoleAsync(held, schoolId), isAdmin, isSuperAdmin, null);
	}

	/// <summary>
	/// The first held role whose module is on. If every role is off, the first one, so
	/// ModuleAccessFilter can explain why the person is blocked.
	/// </summary>
	private async Task<string?> DefaultRoleAsync(List<string> held, Guid schoolId)
	{
		if (held.Count <= 1)
		{
			return held.FirstOrDefault();
		}

		var activeModules = await subscriptions.GetActiveModulesAsync(schoolId);
		return SchoolRoleOrder
			.Where(r => held.Contains(r.Role))
			.FirstOrDefault(r => r.Module is not { } module || activeModules.Contains(module.ToString()))
			.Role ?? held[0];
	}

	/// <summary>Keycloak emits realm roles as a JSON-valued claim: realm_access = {"roles":["superadmin",...]}.</summary>
	private static HashSet<string> ReadRealmRoles(ClaimsPrincipal principal)
	{
		var roles = new HashSet<string>(StringComparer.Ordinal);
		var realmAccess = principal.FindFirst("realm_access");
		if (realmAccess is null)
		{
			return roles;
		}

		try
		{
			using var doc = JsonDocument.Parse(realmAccess.Value);
			if (doc.RootElement.TryGetProperty("roles", out var array) && array.ValueKind == JsonValueKind.Array)
			{
				foreach (var role in array.EnumerateArray())
				{
					if (role.GetString() is { Length: > 0 } name)
					{
						roles.Add(name);
					}
				}
			}
		}
		catch (JsonException) { }

		return roles;
	}
}
