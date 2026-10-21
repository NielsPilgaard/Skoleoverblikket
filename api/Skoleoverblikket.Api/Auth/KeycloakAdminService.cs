using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Skoleoverblikket.Api.Auth;

public record CredentialRepresentation(
	[property: JsonPropertyName("type")] string Type,
	[property: JsonPropertyName("value")] string Value,
	[property: JsonPropertyName("temporary")] bool Temporary);

/// <summary>
/// Talks to the Keycloak Admin REST API to create users on behalf of the application.
/// Uses client_credentials flow with the skoleoverblikket-admin service account.
/// </summary>
public sealed class KeycloakAdminService(IKeycloakAdminApi adminApi, IKeycloakTokenApi tokenApi, IOptions<KeycloakOptions> options)
{
	/// <summary>
	/// The login for an invited staff member, board member or parent: a new account with a temporary
	/// password, or the person's existing account at the same school. Roles come from the Staff,
	/// BoardMember and Parent rows (SchoolRolesClaimsTransformer), so no realm role is assigned.
	/// </summary>
	/// <exception cref="KeycloakUserInOtherSchoolException">The email belongs to a login at another school.</exception>
	public async Task<KeycloakLogin> CreateOrFindInviteeAsync(
		string email,
		string firstName,
		string lastName,
		string temporaryPassword,
		Guid tenantId,
		CancellationToken cancellationToken)
	{
		var createResponse = await adminApi.CreateUserAsync(
			NewUser(email, firstName, lastName, temporaryPassword, tenantId, forcePasswordReset: true),
			cancellationToken);
		if (createResponse.StatusCode != System.Net.HttpStatusCode.Conflict)
		{
			return new KeycloakLogin(await ReadCreatedUserIdAsync(createResponse, cancellationToken), Existing: false);
		}

		var existing = (await adminApi.GetUsersByEmailAsync(email, exact: true, cancellationToken)).FirstOrDefault()
			?? throw new KeycloakException("Keycloak rejected duplicate user but found no existing user with that email");

		// tenant_id is single-valued, so a login belongs to one school. One login across several
		// schools is a separate task; until then such an invite is refused.
		var existingTenant = existing.Attributes?.GetValueOrDefault("tenant_id")?.FirstOrDefault();
		if (!string.Equals(existingTenant, tenantId.ToString(), StringComparison.OrdinalIgnoreCase))
		{
			throw new KeycloakUserInOtherSchoolException();
		}

		return new KeycloakLogin(existing.Id, Existing: true);
	}

	/// <summary>
	/// Creates the login for a new school's first admin, with a permanent password. Admin itself comes
	/// from Staff.IsAdmin. An existing user with the same email is never reused: signup must not attach
	/// someone else's account to a new school.
	/// </summary>
	/// <exception cref="KeycloakUserExistsException">A user with this email already exists.</exception>
	public async Task<string> CreateAdminUserAsync(
		string email,
		string firstName,
		string lastName,
		string password,
		Guid tenantId,
		CancellationToken cancellationToken)
	{
		var createResponse = await adminApi.CreateUserAsync(
			NewUser(email, firstName, lastName, password, tenantId, forcePasswordReset: false),
			cancellationToken);
		if (createResponse.StatusCode == System.Net.HttpStatusCode.Conflict)
		{
			throw new KeycloakUserExistsException();
		}

		return await ReadCreatedUserIdAsync(createResponse, cancellationToken);
	}

	private static CreateUserRequest NewUser(string email, string firstName, string lastName, string password, Guid tenantId, bool forcePasswordReset) =>
		new(
			Username: email,
			Email: email,
			FirstName: firstName,
			LastName: lastName,
			Enabled: true,
			EmailVerified: true,
			Credentials: [new CredentialRepresentation("password", password, Temporary: forcePasswordReset)],
			Attributes: new Dictionary<string, IReadOnlyList<string>> { ["tenant_id"] = [tenantId.ToString()] },
			RequiredActions: forcePasswordReset ? ["UPDATE_PASSWORD"] : null);

	private static async Task<string> ReadCreatedUserIdAsync(HttpResponseMessage createResponse, CancellationToken cancellationToken)
	{
		if (!createResponse.IsSuccessStatusCode)
		{
			var err = await createResponse.Content.ReadAsStringAsync(cancellationToken);
			throw new KeycloakException($"Failed to create Keycloak user: {createResponse.StatusCode} — {err}");
		}

		var location = createResponse.Headers.Location
					   ?? throw new KeycloakException("Keycloak did not return a Location header after user creation");
		return location.Segments.Last().TrimEnd('/');
	}

	/// <summary>
	/// Exchanges user credentials for a token via the password grant on the web client.
	/// Used immediately after signup so the frontend gets a JWT with tenant_id already embedded.
	/// </summary>
	public async Task<TokenResponse> GetTokenForUserAsync(string email, string password, CancellationToken cancellationToken)
	{
		var request = new PasswordTokenRequest(
			GrantType: "password",
			ClientId: options.Value.WebClientId,
			Username: email,
			Password: password,
			Scope: "openid profile roles tenant");

		return await tokenApi.GetPasswordTokenAsync(request, cancellationToken);
	}

	/// <summary>
	/// Removes the Keycloak 'admin' realm role. Phase A of task 61 still reads that role, so revoking
	/// admin in the database alone would leave the person admin. Phase B stops reading it and removes
	/// this method. Throws <see cref="KeycloakException"/> on failure so callers can roll back.
	/// </summary>
	public async Task RemoveAdminRoleAsync(string keycloakUserId, CancellationToken cancellationToken)
	{
		try
		{
			var role = await adminApi.GetRoleAsync(Roles.Admin, cancellationToken);
			await adminApi.RemoveRoleMappingsAsync(keycloakUserId, [role], cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			throw new KeycloakException($"Failed to remove 'admin' role for user {keycloakUserId}: {ex.Message}");
		}
	}

	/// <summary>The ids of every user holding the Keycloak 'admin' realm role (Phase A backfill).</summary>
	public async Task<IReadOnlyList<string>> GetAdminRoleMemberIdsAsync(CancellationToken cancellationToken)
	{
		const int pageSize = 100;
		var ids = new List<string>();
		for (var first = 0; ; first += pageSize)
		{
			var page = await adminApi.GetRoleUsersAsync(Roles.Admin, first, pageSize, cancellationToken);
			ids.AddRange(page.Select(u => u.Id));
			if (page.Count < pageSize)
			{
				return ids;
			}
		}
	}

	public async Task DeleteStaffUserAsync(string keycloakUserId, CancellationToken cancellationToken)
	{
		var response = await adminApi.DeleteUserAsync(keycloakUserId, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			var err = await response.Content.ReadAsStringAsync(cancellationToken);
			throw new KeycloakException($"Failed to delete Keycloak user {keycloakUserId}: {response.StatusCode} — {err}");
		}
	}

	/// <summary>Like <see cref="DeleteStaffUserAsync"/>, but a user that is already gone counts as deleted.</summary>
	public async Task DeleteUserIfExistsAsync(string keycloakUserId, CancellationToken cancellationToken)
	{
		var response = await adminApi.DeleteUserAsync(keycloakUserId, cancellationToken);
		if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
		{
			var err = await response.Content.ReadAsStringAsync(cancellationToken);
			throw new KeycloakException($"Failed to delete Keycloak user {keycloakUserId}: {response.StatusCode} — {err}");
		}
	}
}

public class KeycloakException(string message) : Exception(message);

public sealed class KeycloakUserExistsException() : KeycloakException("A Keycloak user with this email already exists.");

/// <summary>
/// The invitee's email belongs to a login at another school. Not a <see cref="KeycloakException"/>:
/// callers that tolerate Keycloak being down must still refuse this invite.
/// </summary>
public sealed class KeycloakUserInOtherSchoolException() : Exception("A Keycloak user with this email belongs to another school.");

/// <summary>A Keycloak login. <see cref="Existing"/> is true when the person already had an account.</summary>
public sealed record KeycloakLogin(string Subject, bool Existing);
