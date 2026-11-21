using System.Text.Json.Serialization;
using Refit;

namespace Skoleoverblikket.Api.Auth;

/// <summary>Admin REST API — base URL: {adminBase}</summary>
public interface IKeycloakAdminApi
{
	[Post("/users")]
	Task<HttpResponseMessage> CreateUserAsync([Body] CreateUserRequest request, CancellationToken cancellationToken);

	[Put("/users/{userId}")]
	Task<HttpResponseMessage> UpdateUserAsync(string userId, [Body] UpdateUserRequest request, CancellationToken cancellationToken);

	[Get("/users")]
	Task<IReadOnlyList<UserRepresentation>> GetUsersByEmailAsync([AliasAs("email")] string email, [AliasAs("exact")] bool exact, CancellationToken cancellationToken);

	[Delete("/users/{userId}")]
	Task<HttpResponseMessage> DeleteUserAsync(string userId, CancellationToken cancellationToken);

	[Get("/users/{userId}")]
	Task<UserRepresentation> GetUserAsync(string userId, CancellationToken cancellationToken);

	[Get("/users/{userId}/credentials")]
	Task<IReadOnlyList<CredentialSummary>> GetCredentialsAsync(string userId, CancellationToken cancellationToken);

	[Put("/users/{userId}/reset-password")]
	Task<HttpResponseMessage> ResetPasswordAsync(string userId, [Body] CredentialRepresentation credential, CancellationToken cancellationToken);

	[Delete("/users/{userId}/credentials/{credentialId}")]
	Task<HttpResponseMessage> DeleteCredentialAsync(string userId, string credentialId, CancellationToken cancellationToken);

	/// <summary>Ends every session of the user, on every device.</summary>
	[Post("/users/{userId}/logout")]
	Task<HttpResponseMessage> LogoutUserAsync(string userId, CancellationToken cancellationToken);

	[Get("/users/{userId}/federated-identity")]
	Task<IReadOnlyList<FederatedIdentity>> GetFederatedIdentitiesAsync(string userId, CancellationToken cancellationToken);

	/// <summary>Realm roles, including those from groups and composites.</summary>
	[Get("/users/{userId}/role-mappings/realm/composite")]
	Task<IReadOnlyList<RoleSummary>> GetRealmRolesAsync(string userId, CancellationToken cancellationToken);
}

public record CreateUserRequest(
	[property: JsonPropertyName("username")] string Username,
	[property: JsonPropertyName("email")] string Email,
	[property: JsonPropertyName("firstName")] string FirstName,
	[property: JsonPropertyName("lastName")] string LastName,
	[property: JsonPropertyName("enabled")] bool Enabled,
	[property: JsonPropertyName("emailVerified")] bool EmailVerified,
	[property: JsonPropertyName("credentials")] IReadOnlyList<CredentialRepresentation> Credentials,
	[property: JsonPropertyName("attributes")] Dictionary<string, IReadOnlyList<string>> Attributes);

public record UpdateUserRequest(
	[property: JsonPropertyName("attributes")] Dictionary<string, IReadOnlyList<string>> Attributes);

public record UserRepresentation(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("email")] string? Email,
	[property: JsonPropertyName("attributes")] Dictionary<string, IReadOnlyList<string>>? Attributes = null,
	[property: JsonPropertyName("requiredActions")] IReadOnlyList<string>? RequiredActions = null);

/// <summary>
/// A credential's metadata. The admin API never returns secrets. <see cref="CreatedDate"/> is Unix
/// milliseconds. <see cref="CredentialData"/> is a JSON string whose shape depends on the type.
/// </summary>
public record CredentialSummary(
	[property: JsonPropertyName("type")] string Type,
	[property: JsonPropertyName("id")] string? Id = null,
	[property: JsonPropertyName("userLabel")] string? UserLabel = null,
	[property: JsonPropertyName("createdDate")] long? CreatedDate = null,
	[property: JsonPropertyName("credentialData")] string? CredentialData = null);

public record FederatedIdentity(
	[property: JsonPropertyName("identityProvider")] string IdentityProvider);

public record RoleSummary(
	[property: JsonPropertyName("name")] string Name);
