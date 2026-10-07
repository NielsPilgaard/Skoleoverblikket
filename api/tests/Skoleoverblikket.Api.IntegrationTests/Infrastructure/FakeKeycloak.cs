using System.Collections.Concurrent;
using System.Net;
using Skoleoverblikket.Api.Auth;

namespace Skoleoverblikket.Api.IntegrationTests.Infrastructure;

/// <summary>
/// In-memory stand-in for the Keycloak admin and token APIs, for tests that need account creation
/// (signup) to succeed. Not registered in <see cref="ApiFactory"/> by default: other tests rely on
/// Keycloak calls failing. Swap it in with <c>WithWebHostBuilder</c>.
/// </summary>
public sealed class FakeKeycloak : IKeycloakAdminApi, IKeycloakTokenApi
{
	private readonly ConcurrentDictionary<string, string> _usersByEmail = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Registers an existing login, so creating another user with this email returns 409.</summary>
	public void AddExistingUser(string email) => _usersByEmail.TryAdd(email, Guid.NewGuid().ToString());

	public Task<HttpResponseMessage> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
	{
		var id = Guid.NewGuid().ToString();
		if (!_usersByEmail.TryAdd(request.Email, id))
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict));
		}

		var response = new HttpResponseMessage(HttpStatusCode.Created);
		response.Headers.Location = new Uri($"http://keycloak.test/admin/realms/test/users/{id}");
		return Task.FromResult(response);
	}

	public Task<RoleRepresentation> GetRoleAsync(string roleName, CancellationToken cancellationToken) =>
		Task.FromResult(new RoleRepresentation(roleName + "-id", roleName));

	public Task AssignRoleMappingsAsync(string userId, IReadOnlyList<RoleRepresentation> roles, CancellationToken cancellationToken) =>
		Task.CompletedTask;

	public Task RemoveRoleMappingsAsync(string userId, IReadOnlyList<RoleRepresentation> roles, CancellationToken cancellationToken) =>
		Task.CompletedTask;

	public Task<HttpResponseMessage> UpdateUserAsync(string userId, UpdateUserRequest request, CancellationToken cancellationToken) =>
		Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

	public Task<IReadOnlyList<UserRepresentation>> GetUsersByEmailAsync(string email, bool exact, CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<UserRepresentation>>(
			_usersByEmail.TryGetValue(email, out var id) ? [new UserRepresentation(id, email)] : []);

	public Task<HttpResponseMessage> DeleteUserAsync(string userId, CancellationToken cancellationToken) =>
		Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

	public Task<TokenResponse> GetTokenAsync(TokenRequest request, CancellationToken cancellationToken) =>
		Task.FromResult(new TokenResponse("service-token", null, 300, "Bearer"));

	public Task<TokenResponse> GetPasswordTokenAsync(PasswordTokenRequest request, CancellationToken cancellationToken) =>
		Task.FromResult(new TokenResponse("user-token", "refresh-token", 300, "Bearer"));
}
