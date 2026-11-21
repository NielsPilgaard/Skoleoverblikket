using System.Collections.Concurrent;
using System.Net;
using Refit;
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

	private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, CredentialSummary>> _secondFactorsById = new();
	private readonly ConcurrentDictionary<string, byte> _superAdmins = new();

	/// <summary>Logins whose sessions the API ended.</summary>
	public ConcurrentBag<string> LoggedOutUserIds { get; } = [];

	/// <summary>Gives the login a credential such as "otp" or "recovery-authn-codes". Returns its id.</summary>
	public string AddCredential(string subject, string type, string? credentialData = null)
	{
		var id = Guid.NewGuid().ToString();
		_secondFactorsById.GetOrAdd(subject, _ => new())[id] =
			new CredentialSummary(type, id, null, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), credentialData);
		return id;
	}

	/// <summary>The non-password credential types the login has.</summary>
	public IReadOnlyList<string> CredentialTypesOf(string subject) =>
		_secondFactorsById.TryGetValue(subject, out var creds) ? [.. creds.Values.Select(c => c.Type)] : [];

	public void MakeSuperAdmin(string subject) => _superAdmins[subject] = 0;

	/// <summary>Logins the API deleted.</summary>
	public ConcurrentBag<string> DeletedUserIds { get; } = [];

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
		Task.FromResult(new UserRepresentation(
			userId, _usersByEmail.FirstOrDefault(u => u.Value == userId).Key, TenantAttributes(userId)));

	/// <summary>When true, reading a login's credentials fails as if Keycloak were down.</summary>
	public bool Unavailable { get; set; }

	public async Task<IReadOnlyList<CredentialSummary>> GetCredentialsAsync(string userId, CancellationToken cancellationToken)
	{
		if (Unavailable)
		{
			throw await ApiException.Create(
				new HttpRequestMessage(HttpMethod.Get, $"http://keycloak.test/admin/realms/test/users/{userId}/credentials"),
				HttpMethod.Get,
				new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
				new RefitSettings());
		}

		IEnumerable<CredentialSummary> password = _passwordById.ContainsKey(userId) ? [new CredentialSummary("password")] : [];
		return [.. password, .. _secondFactorsById.TryGetValue(userId, out var creds) ? creds.Values : []];
	}

	public Task<HttpResponseMessage> DeleteCredentialAsync(string userId, string credentialId, CancellationToken cancellationToken)
	{
		var removed = _secondFactorsById.TryGetValue(userId, out var creds) && creds.TryRemove(credentialId, out _);
		return Task.FromResult(new HttpResponseMessage(removed ? HttpStatusCode.NoContent : HttpStatusCode.NotFound));
	}

	public Task<HttpResponseMessage> LogoutUserAsync(string userId, CancellationToken cancellationToken)
	{
		LoggedOutUserIds.Add(userId);
		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
	}

	public Task<IReadOnlyList<FederatedIdentity>> GetFederatedIdentitiesAsync(string userId, CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<FederatedIdentity>>([]);

	public Task<IReadOnlyList<RoleSummary>> GetRealmRolesAsync(string userId, CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<RoleSummary>>(_superAdmins.ContainsKey(userId) ? [new RoleSummary(Roles.SuperAdmin)] : []);

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
