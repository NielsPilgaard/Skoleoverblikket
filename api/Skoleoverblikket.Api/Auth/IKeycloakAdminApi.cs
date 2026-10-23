using System.Text.Json.Serialization;
using Refit;

namespace Skoleoverblikket.Api.Auth;

/// <summary>Admin REST API — base URL: {adminBase}</summary>
public interface IKeycloakAdminApi
{
	[Post("/users")]
	Task<HttpResponseMessage> CreateUserAsync([Body] CreateUserRequest request, CancellationToken cancellationToken);

	[Get("/roles/{roleName}")]
	Task<RoleRepresentation> GetRoleAsync(string roleName, CancellationToken cancellationToken);

	[Get("/roles/{roleName}/users")]
	Task<IReadOnlyList<UserRepresentation>> GetRoleUsersAsync(string roleName, [AliasAs("first")] int first, [AliasAs("max")] int max, CancellationToken cancellationToken);

	[Delete("/users/{userId}/role-mappings/realm")]
	Task RemoveRoleMappingsAsync(string userId, [Body] IReadOnlyList<RoleRepresentation> roles, CancellationToken cancellationToken);

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
}

public record RoleRepresentation(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("name")] string Name);

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

public record CredentialSummary(
	[property: JsonPropertyName("type")] string Type);
