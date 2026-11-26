using System.Text.Json;
using System.Text.Json.Serialization;

namespace Skoleoverblikket.Api.Auth;

/// <summary>
/// Source-generated JSON for the Keycloak Refit clients. Refit 17 has no reflection-based JSON by default,
/// so every body and response type the clients send or read must be listed here.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CreateUserRequest))]
[JsonSerializable(typeof(UpdateUserRequest))]
[JsonSerializable(typeof(UserRepresentation))]
[JsonSerializable(typeof(IReadOnlyList<UserRepresentation>))]
[JsonSerializable(typeof(IReadOnlyList<CredentialSummary>))]
[JsonSerializable(typeof(IReadOnlyList<FederatedIdentity>))]
[JsonSerializable(typeof(IReadOnlyList<RoleSummary>))]
[JsonSerializable(typeof(CredentialRepresentation))]
[JsonSerializable(typeof(TokenResponse))]
internal sealed partial class KeycloakJsonContext : JsonSerializerContext;
