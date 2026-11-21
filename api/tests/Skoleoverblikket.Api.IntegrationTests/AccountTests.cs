using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Email;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using static Skoleoverblikket.Api.Services.AccountService;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Min konto shows the caller's own login and ends its sessions. A school admin can turn off two-factor
/// for someone who lost their phone, but only for a login no other school uses. Keycloak is replaced by
/// <see cref="FakeKeycloak"/>.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class AccountTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		Converters = { new JsonStringEnumConverter() },
		PropertyNameCaseInsensitive = true,
	};

	private readonly FakeKeycloak _keycloak = new();
	private readonly Guid _tenantId = Guid.NewGuid();
	private WebApplicationFactory<Program> _app = null!;
	private string _adminSubject = null!;
	private HttpClient _admin = null!;

	[Before(Test)]
	public async Task SetUp()
	{
		_app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
		{
			services.RemoveAll<IKeycloakAdminApi>();
			services.RemoveAll<IKeycloakTokenApi>();
			services.AddSingleton<IKeycloakAdminApi>(_keycloak);
			services.AddSingleton<IKeycloakTokenApi>(_keycloak);
		}));
		await TestDataBuilder.CreateSchoolAsync(_app.Services, _tenantId, name: "Friskolen");
		_adminSubject = $"konto-admin-{Guid.NewGuid():N}";
		await TestDataBuilder.CreateStaffAsync(_app.Services, _tenantId, "Hanne Kontor", isAdmin: true, keycloakSubject: _adminSubject);
		_admin = Client(_tenantId, _adminSubject);
	}

	[After(Test)]
	public async Task TearDown() => await _app.DisposeAsync();

	[Test]
	public async Task Security_ShowsOnlyTheCallersOwnLogin()
	{
		var teacher = $"konto-laerer-{Guid.NewGuid():N}";
		await TestDataBuilder.CreateStaffAsync(_app.Services, _tenantId, keycloakSubject: teacher);
		var otpId = _keycloak.AddCredential(teacher, "otp");
		var secondAppId = _keycloak.AddCredential(teacher, "otp");
		_keycloak.AddCredential(teacher, "recovery-authn-codes", """{"remainingCodes":11}""");

		var own = await Client(_tenantId, teacher).GetFromJsonAsync<AccountSecurityDto>("/api/v1/account/security", JsonOpts);
		var adminsView = await _admin.GetFromJsonAsync<AccountSecurityDto>("/api/v1/account/security", JsonOpts);

		await Assert.That(own!.Otp).IsNotNull();
		await Assert.That(own.AuthenticatorApps.Select(a => a.Id)).IsEquivalentTo([otpId, secondAppId]);
		await Assert.That(own.RecoveryCodes?.Remaining).IsEqualTo(11);
		await Assert.That(adminsView!.Otp).IsNull();
		await Assert.That(adminsView.AuthenticatorApps).IsEmpty();
		await Assert.That(adminsView.RecoveryCodes).IsNull();
	}

	[Test]
	public async Task LogoutEverywhere_EndsTheCallersOwnSessions()
	{
		var response = await _admin.PostAsync("/api/v1/account/logout-everywhere", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
		await Assert.That(_keycloak.LoggedOutUserIds).IsEquivalentTo([_adminSubject]);
	}

	[Test]
	public async Task CleanupRecoveryCodes_RemovesThemOnlyWhenNoSecondFactorIsLeft()
	{
		_keycloak.AddCredential(_adminSubject, "recovery-authn-codes");
		var otpId = _keycloak.AddCredential(_adminSubject, "otp");

		await _admin.PostAsync("/api/v1/account/security/cleanup-recovery-codes", null);
		var whileOtp = _keycloak.CredentialTypesOf(_adminSubject);
		await _keycloak.DeleteCredentialAsync(_adminSubject, otpId, CancellationToken.None);
		await _admin.PostAsync("/api/v1/account/security/cleanup-recovery-codes", null);

		await Assert.That(whileOtp).Contains("recovery-authn-codes");
		await Assert.That(_keycloak.CredentialTypesOf(_adminSubject)).IsEmpty();
	}

	[Test]
	public async Task AdminReset_RemovesTwoFactor_LogsOut_AndEmailsTheLoginNotTheEditableRow()
	{
		var loginEmail = $"login-{Guid.NewGuid():N}@skole.test";
		var subject = _keycloak.AddExistingUser(loginEmail, _tenantId);
		var parent = await TestDataBuilder.CreateParentAsync(_app.Services, _tenantId, subject);
		_keycloak.AddCredential(subject, "otp");
		_keycloak.AddCredential(subject, "recovery-authn-codes");

		var response = await _admin.PostAsync($"/api/v1/parents/{parent.Id}/reset-two-factor", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
		await Assert.That(_keycloak.CredentialTypesOf(subject)).IsEmpty();
		await Assert.That(_keycloak.LoggedOutUserIds).Contains(subject);
		var email = factory.Emails.To(loginEmail).Single();
		await Assert.That(email.PlainTextBody!).Contains("Hanne Kontor på Friskolen");
		await Assert.That(factory.Emails.To(parent.Email)).IsEmpty();
	}

	[Test]
	public async Task AdminReset_OnlyRecoveryCodesLeft_RemovesThem()
	{
		// The app was removed but Min konto's cleanup never ran, so login still asks for a recovery code.
		var subject = $"konto-koder-{Guid.NewGuid():N}";
		var staff = await TestDataBuilder.CreateStaffAsync(_app.Services, _tenantId, keycloakSubject: subject);
		_keycloak.AddCredential(subject, "recovery-authn-codes");

		var response = await _admin.PostAsync($"/api/v1/staff/{staff.Id}/reset-two-factor", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
		await Assert.That(_keycloak.CredentialTypesOf(subject)).IsEmpty();
	}

	[Test]
	public async Task AdminReset_PersonWithoutLogin_SaysSo()
	{
		var staff = await TestDataBuilder.CreateStaffAsync(_app.Services, _tenantId);

		var response = await _admin.PostAsync($"/api/v1/staff/{staff.Id}/reset-two-factor", null);
		var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(JsonOpts);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
		await Assert.That(problem!.Title).IsEqualTo("Personen har ikke et login endnu");
	}

	[Test]
	public async Task AdminReset_EmailFails_StillReportsTheReset()
	{
		var loginEmail = $"login-{Guid.NewGuid():N}@skole.test";
		var subject = _keycloak.AddExistingUser(loginEmail, _tenantId);
		var staff = await TestDataBuilder.CreateStaffAsync(_app.Services, _tenantId, keycloakSubject: subject);
		_keycloak.AddCredential(subject, "otp");
		await using var app = _app.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
		{
			services.RemoveAll<IEmailSender>();
			services.AddSingleton<IEmailSender, FailingEmailSender>();
		}));
		var admin = app.CreateClient();
		admin.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		admin.DefaultRequestHeaders.Add("X-Test-Subject", _adminSubject);

		var response = await admin.PostAsync($"/api/v1/staff/{staff.Id}/reset-two-factor", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
		await Assert.That(_keycloak.CredentialTypesOf(subject)).IsEmpty();
	}

	[Test]
	public async Task AdminReset_StaffMember_Works()
	{
		var subject = $"konto-laerer-{Guid.NewGuid():N}";
		var staff = await TestDataBuilder.CreateStaffAsync(_app.Services, _tenantId, keycloakSubject: subject);
		_keycloak.AddCredential(subject, "otp");

		var response = await _admin.PostAsync($"/api/v1/staff/{staff.Id}/reset-two-factor", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
		await Assert.That(_keycloak.CredentialTypesOf(subject)).IsEmpty();
	}

	[Test]
	public async Task AdminReset_LoginUsedAtAnotherSchool_IsRefused()
	{
		var subject = $"konto-delt-{Guid.NewGuid():N}";
		var member = await TestDataBuilder.CreateBoardMemberAsync(_app.Services, _tenantId, subject);
		var otherSchool = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(_app.Services, otherSchool);
		await TestDataBuilder.CreateParentAsync(_app.Services, otherSchool, subject);
		_keycloak.AddCredential(subject, "otp");

		var response = await _admin.PostAsync($"/api/v1/board-members/{member.Id}/reset-two-factor", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
		await Assert.That(_keycloak.CredentialTypesOf(subject)).Contains("otp");
	}

	[Test]
	public async Task AdminReset_TheOperatorsLogin_IsRefused()
	{
		var subject = $"konto-super-{Guid.NewGuid():N}";
		var staff = await TestDataBuilder.CreateStaffAsync(_app.Services, _tenantId, keycloakSubject: subject);
		_keycloak.MakeSuperAdmin(subject);
		_keycloak.AddCredential(subject, "otp");

		var response = await _admin.PostAsync($"/api/v1/staff/{staff.Id}/reset-two-factor", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
		await Assert.That(_keycloak.CredentialTypesOf(subject)).Contains("otp");
	}

	[Test]
	public async Task AdminReset_OwnLogin_IsRefused()
	{
		var self = await TestDataBuilder.CreateStaffAsync(_app.Services, _tenantId, "Hanne Kontor 2", isAdmin: true, keycloakSubject: $"konto-selv-{Guid.NewGuid():N}");
		_keycloak.AddCredential(self.KeycloakSubject!, "otp");

		var response = await Client(_tenantId, self.KeycloakSubject!).PostAsync($"/api/v1/staff/{self.Id}/reset-two-factor", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That(_keycloak.CredentialTypesOf(self.KeycloakSubject!)).Contains("otp");
	}

	[Test]
	public async Task AdminReset_NonAdmin_IsForbidden()
	{
		var teacher = $"konto-laerer-{Guid.NewGuid():N}";
		await TestDataBuilder.CreateStaffAsync(_app.Services, _tenantId, keycloakSubject: teacher);
		var target = await TestDataBuilder.CreateStaffAsync(_app.Services, _tenantId, keycloakSubject: $"konto-mål-{Guid.NewGuid():N}");
		_keycloak.AddCredential(target.KeycloakSubject!, "otp");

		var response = await Client(_tenantId, teacher).PostAsync($"/api/v1/staff/{target.Id}/reset-two-factor", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That(_keycloak.CredentialTypesOf(target.KeycloakSubject!)).Contains("otp");
	}

	[Test]
	public async Task AdminReset_AnotherSchoolsStaff_IsNotFound()
	{
		var otherSchool = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(_app.Services, otherSchool);
		var target = await TestDataBuilder.CreateStaffAsync(_app.Services, otherSchool, keycloakSubject: $"konto-anden-{Guid.NewGuid():N}");
		_keycloak.AddCredential(target.KeycloakSubject!, "otp");

		var response = await _admin.PostAsync($"/api/v1/staff/{target.Id}/reset-two-factor", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
		await Assert.That(_keycloak.CredentialTypesOf(target.KeycloakSubject!)).Contains("otp");
	}

	[Test]
	public async Task OperatorReset_FindsTheLoginByEmail()
	{
		var email = $"delt-{Guid.NewGuid():N}@skole.test";
		var subject = _keycloak.AddExistingUser(email);
		_keycloak.AddCredential(subject, "otp");
		var superAdmin = Client(_tenantId, $"konto-operator-{Guid.NewGuid():N}");
		superAdmin.DefaultRequestHeaders.Add("X-Test-Roles", Roles.SuperAdmin);

		var response = await superAdmin.PostAsJsonAsync("/api/v1/admin/logins/reset-two-factor", new { email });
		var asSchoolAdmin = await _admin.PostAsJsonAsync("/api/v1/admin/logins/reset-two-factor", new { email });

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
		await Assert.That(asSchoolAdmin.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That(_keycloak.CredentialTypesOf(subject)).IsEmpty();
		await Assert.That(factory.Emails.To(email).Single().PlainTextBody!).Contains("Skoleoverblikkets support");
	}

	private HttpClient Client(Guid tenantId, string subject)
	{
		var client = _app.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
	}

	private sealed class FailingEmailSender : IEmailSender
	{
		public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("SMTP is down");
	}
}
