using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
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
public sealed class KeycloakAdminService(IKeycloakAdminApi adminApi, IKeycloakTokenApi tokenApi, IOptions<KeycloakOptions> options, ILogger<KeycloakAdminService> logger)
{
	/// <summary>
	/// Creates a Keycloak user and optionally assigns a realm role.
	/// Returns the new user's Keycloak subject (UUID), or the existing user's if the email is taken.
	/// </summary>
	public Task<string> CreateUserAsync(
		string email,
		string firstName,
		string lastName,
		string password,
		Guid? tenantId,
		string? realmRole,
		bool forcePasswordReset,
		CancellationToken cancellationToken) =>
		CreateUserAsync(email, firstName, lastName, password, tenantId, realmRole, forcePasswordReset, reuseExisting: true, cancellationToken);

	private async Task<string> CreateUserAsync(
		string email,
		string firstName,
		string lastName,
		string password,
		Guid? tenantId,
		string? realmRole,
		bool forcePasswordReset,
		bool reuseExisting,
		CancellationToken cancellationToken)
	{
		var attributes = tenantId.HasValue
			? new Dictionary<string, IReadOnlyList<string>> { ["tenant_id"] = [tenantId.Value.ToString()] }
			: new Dictionary<string, IReadOnlyList<string>>();

		var payload = new CreateUserRequest(
			Username: email,
			Email: email,
			FirstName: firstName,
			LastName: lastName,
			Enabled: true,
			EmailVerified: true,
			Credentials: [new CredentialRepresentation("password", password, Temporary: forcePasswordReset)],
			Attributes: attributes,
			RequiredActions: forcePasswordReset ? ["UPDATE_PASSWORD"] : null);

		var createResponse = await adminApi.CreateUserAsync(payload, cancellationToken);

		if (createResponse.StatusCode == System.Net.HttpStatusCode.Conflict)
		{
			if (!reuseExisting)
			{
				throw new KeycloakUserExistsException();
			}

			var existing = await adminApi.GetUsersByEmailAsync(email, exact: true, cancellationToken);
			return existing.FirstOrDefault()?.Id
				?? throw new KeycloakException($"Keycloak rejected duplicate user but no existing user found for {email}");
		}

		if (!createResponse.IsSuccessStatusCode)
		{
			var err = await createResponse.Content.ReadAsStringAsync(cancellationToken);
			throw new KeycloakException($"Failed to create Keycloak user: {createResponse.StatusCode} — {err}");
		}

		var location = createResponse.Headers.Location
					   ?? throw new KeycloakException("Keycloak did not return a Location header after user creation");
		var keycloakUserId = location.Segments.Last().TrimEnd('/');

		if (!string.IsNullOrEmpty(realmRole))
		{
			await AssignRealmRoleAsync(keycloakUserId, realmRole, cancellationToken);
		}

		return keycloakUserId;
	}

	/// <summary>
	/// Creates a Keycloak admin user with a permanent password and admin realm role. Unlike the
	/// invitation flows, an existing user with the same email is never reused: signup must not
	/// attach someone else's account to a new school.
	/// </summary>
	/// <exception cref="KeycloakUserExistsException">A user with this email already exists.</exception>
	public Task<string> CreateAdminUserAsync(
		string email,
		string firstName,
		string lastName,
		string password,
		Guid tenantId,
		CancellationToken cancellationToken) =>
		CreateUserAsync(email, firstName, lastName, password, tenantId, realmRole: "admin", forcePasswordReset: false, reuseExisting: false, cancellationToken);

	/// <summary>Creates a Keycloak staff user with a temporary password and UPDATE_PASSWORD required action.</summary>
	public Task<string> CreateStaffUserAsync(
		string email,
		string firstName,
		string lastName,
		string temporaryPassword,
		Guid tenantId,
		CancellationToken cancellationToken) =>
		CreateUserAsync(email, firstName, lastName, temporaryPassword, tenantId, realmRole: null, forcePasswordReset: true, cancellationToken);

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
	/// Assigns or removes the Keycloak 'admin' realm role for an existing user.
	/// Throws <see cref="KeycloakException"/> on failure so callers can roll back DB changes.
	/// </summary>
	public async Task SetAdminRoleAsync(string keycloakUserId, bool grant, CancellationToken cancellationToken)
	{
		RoleRepresentation role;
		try
		{
			role = await adminApi.GetRoleAsync("admin", cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			throw new KeycloakException($"Failed to fetch 'admin' role from Keycloak: {ex.Message}");
		}

		try
		{
			if (grant)
			{
				await adminApi.AssignRoleMappingsAsync(keycloakUserId, [role], cancellationToken);
			}
			else
			{
				await adminApi.RemoveRoleMappingsAsync(keycloakUserId, [role], cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			throw new KeycloakException($"Failed to {(grant ? "assign" : "remove")} 'admin' role for user {keycloakUserId}: {ex.Message}");
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

	private async Task AssignRealmRoleAsync(string userId, string roleName, CancellationToken cancellationToken)
	{
		try
		{
			var role = await adminApi.GetRoleAsync(roleName, cancellationToken);
			await adminApi.AssignRoleMappingsAsync(userId, [role], cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to assign role {RoleName} to user {UserId}", roleName, userId);
		}
	}
}

public class KeycloakException(string message) : Exception(message);

public sealed class KeycloakUserExistsException() : KeycloakException("A Keycloak user with this email already exists.");
