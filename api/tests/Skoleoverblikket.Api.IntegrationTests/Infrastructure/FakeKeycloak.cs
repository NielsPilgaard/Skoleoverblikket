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
	private readonly ConcurrentDictionary<string, string?> _tenantById = new();
	private readonly ConcurrentDictionary<string, string> _passwordById = new();

	/// <summary>Logins the API deleted.</summary>
	public ConcurrentBag<string> DeletedUserIds { get; } = [];

	/// <summary>Logins holding the Keycloak 'admin' realm role, for the Phase A backfill.</summary>
	public ConcurrentBag<string> AdminRoleMembers { get; } = [];

	/// <summary>
	/// Registers an existing login with a password, so creating another user with this email returns 409.
	/// Returns its subject. <paramref name="tenantId"/> is the school the login belongs to.
	/// </summary>
	public string AddExistingUser(string email, Guid? tenantId = null)
	{
		var id = _usersByEmail.GetOrAdd(email, _ => Guid.NewGuid().ToString());
		_tenantById[id] = tenantId?.ToString();
		_passwordById[id] = "existing-password";
		return id;
	}

	/// <summary>The Keycloak user id (JWT <c>sub</c>) created for <paramref name="email"/>.</summary>
	public string SubjectFor(string email) => _usersByEmail[email];

	/// <summary>The password the login has, or null when nobody has set one.</summary>
	public string? PasswordOf(string subject) => _passwordById.GetValueOrDefault(subject);

	public Task<HttpResponseMessage> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
	{
		var id = Guid.NewGuid().ToString();
		if (!_usersByEmail.TryAdd(request.Email, id))
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict));
		}

		_tenantById[id] = request.Attributes.GetValueOrDefault("tenant_id")?.FirstOrDefault();
		if (request.Credentials.FirstOrDefault(c => c.Type == "password") is { } credential)
		{
			_passwordById[id] = credential.Value;
		}

		var response = new HttpResponseMessage(HttpStatusCode.Created);
		response.Headers.Location = new Uri($"http://keycloak.test/admin/realms/test/users/{id}");
		return Task.FromResult(response);
	}

	public Task<RoleRepresentation> GetRoleAsync(string roleName, CancellationToken cancellationToken) =>
		Task.FromResult(new RoleRepresentation(roleName + "-id", roleName));

	public Task<IReadOnlyList<UserRepresentation>> GetRoleUsersAsync(string roleName, int first, int max, CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<UserRepresentation>>(roleName == Roles.Admin
			? AdminRoleMembers.Skip(first).Take(max).Select(id => new UserRepresentation(id, null)).ToList()
			: []);

	public Task RemoveRoleMappingsAsync(string userId, IReadOnlyList<RoleRepresentation> roles, CancellationToken cancellationToken) =>
		Task.CompletedTask;

	public Task<HttpResponseMessage> UpdateUserAsync(string userId, UpdateUserRequest request, CancellationToken cancellationToken) =>
		Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

	public Task<IReadOnlyList<UserRepresentation>> GetUsersByEmailAsync(string email, bool exact, CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<UserRepresentation>>(
			_usersByEmail.TryGetValue(email, out var id)
				? [new UserRepresentation(id, email, TenantAttributes(id))]
				: []);

	private Dictionary<string, IReadOnlyList<string>> TenantAttributes(string id) =>
		_tenantById.TryGetValue(id, out var tenant) && tenant is not null
			? new() { ["tenant_id"] = [tenant] }
			: [];

	public Task<HttpResponseMessage> DeleteUserAsync(string userId, CancellationToken cancellationToken)
	{
		DeletedUserIds.Add(userId);
		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
	}

	public Task<UserRepresentation> GetUserAsync(string userId, CancellationToken cancellationToken) =>
		Task.FromResult(new UserRepresentation(userId, null, TenantAttributes(userId)));

	public Task<IReadOnlyList<CredentialSummary>> GetCredentialsAsync(string userId, CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<CredentialSummary>>(
			_passwordById.ContainsKey(userId) ? [new CredentialSummary("password")] : []);

	public Task<HttpResponseMessage> ResetPasswordAsync(string userId, CredentialRepresentation credential, CancellationToken cancellationToken)
	{
		_passwordById[userId] = credential.Value;
		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
	}

	public Task<TokenResponse> GetTokenAsync(TokenRequest request, CancellationToken cancellationToken) =>
		Task.FromResult(new TokenResponse("service-token", null, 300, "Bearer"));

	public Task<TokenResponse> GetPasswordTokenAsync(PasswordTokenRequest request, CancellationToken cancellationToken) =>
		Task.FromResult(new TokenResponse("user-token", "refresh-token", 300, "Bearer"));
}
