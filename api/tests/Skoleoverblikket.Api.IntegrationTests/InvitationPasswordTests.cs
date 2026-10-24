using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Invitees choose their own password on the invitation page; no email carries one. A lost or failed
/// invitation email can therefore be resent, and an invitation link never replaces a password someone
/// already chose. Keycloak is replaced by <see cref="FakeKeycloak"/>.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class InvitationPasswordTests(ApiFactory factory)
{
	private const string ChosenPassword = "mit-eget-kodeord";

	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		Converters = { new JsonStringEnumConverter() },
		PropertyNameCaseInsensitive = true,
	};

	private readonly FakeKeycloak _keycloak = new();
	private readonly Guid _tenantId = Guid.NewGuid();
	private WebApplicationFactory<Program> _app = null!;
	private HttpClient _admin = null!;
	private HttpClient _anonymous = null!;

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
		await TestDataBuilder.CreateSchoolAsync(_app.Services, _tenantId);
		var adminSubject = $"kode-admin-{Guid.NewGuid():N}";
		await TestDataBuilder.CreateStaffAsync(_app.Services, _tenantId, "Hanne Kontor", isAdmin: true, keycloakSubject: adminSubject);

		_admin = _app.CreateClient();
		_admin.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		_admin.DefaultRequestHeaders.Add("X-Test-Roles", "default-roles");
		_admin.DefaultRequestHeaders.Add("X-Test-Subject", adminSubject);
		_anonymous = _app.CreateClient();
	}

	[After(Test)]
	public async Task TearDown() => await _app.DisposeAsync();

	[Test]
	public async Task NewTeacher_ChoosesTheirOwnPassword_FromTheInvitationLink()
	{
		var email = UniqueEmail("ny-laerer");
		await InviteTeacherAsync(email);
		var invitationEmail = factory.Emails.To(email).Last();
		var token = factory.Emails.LastInvitationToken(email, "invitation");

		var before = await PreviewAsync<StaffInvitationService.InvitationPreview>("staff-invitations", token);
		var chosen = await SetPasswordAsync("staff-invitations", token, ChosenPassword);
		var after = await PreviewAsync<StaffInvitationService.InvitationPreview>("staff-invitations", token);
		var again = await SetPasswordAsync("staff-invitations", token, "et-andet-kodeord");

		await Assert.That(invitationEmail.PlainTextBody!).Contains("Du vælger selv din adgangskode");
		await Assert.That(invitationEmail.PlainTextBody!).DoesNotContain("midlertidig");
		await Assert.That(before.NeedsPassword).IsTrue();
		await Assert.That(chosen.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var login = await chosen.Content.ReadFromJsonAsync<InvitationLoginDto>(JsonOpts);
		await Assert.That(login!.AccessToken).IsNotEmpty();
		await Assert.That(after.NeedsPassword).IsFalse();
		await Assert.That(again.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
		await Assert.That(_keycloak.PasswordOf(_keycloak.SubjectFor(email))).IsEqualTo(ChosenPassword);
	}

	[Test]
	public async Task ResentParentInvitation_StillLetsTheParentChooseAPassword()
	{
		var email = UniqueEmail("mistet-mail");
		var invite = await _admin.PostAsJsonAsync("/api/v1/parents/invite",
			new ParentsController.InviteParentRequest("Pia Forælder", email, [await CreateStudentAsync()]));
		var parent = await invite.Content.ReadFromJsonAsync<ParentsController.ParentDto>(JsonOpts);
		var lostToken = factory.Emails.LastInvitationToken(email, "parent-invitation");

		var resend = await _admin.PostAsync($"/api/v1/parent-invitations/{parent!.Id}/resend", null);
		var resentEmail = factory.Emails.To(email).Last();
		var token = factory.Emails.LastInvitationToken(email, "parent-invitation");
		var chosen = await SetPasswordAsync("parent-invitations", token, ChosenPassword);

		await Assert.That(resend.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
		await Assert.That(resentEmail.PlainTextBody!).Contains("Du vælger selv din adgangskode");
		await Assert.That((await _anonymous.GetAsync($"/api/v1/parent-invitations/preview?token={lostToken}")).StatusCode)
			.IsEqualTo(HttpStatusCode.NotFound);
		await Assert.That(chosen.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(_keycloak.PasswordOf(_keycloak.SubjectFor(email))).IsEqualTo(ChosenPassword);
	}

	[Test]
	public async Task InvitationLink_NeverReplacesAPasswordSomeoneAlreadyChose()
	{
		var email = UniqueEmail("har-kode");
		var subject = _keycloak.AddExistingUser(email, _tenantId);

		var invite = await _admin.PostAsJsonAsync("/api/v1/board-members/invite",
			new BoardMembersController.InviteBoardMemberRequest("Bente Bestyrelse", email));
		var token = factory.Emails.LastInvitationToken(email, "board-invitation");
		var preview = await PreviewAsync<BoardMemberInvitationService.InvitationPreview>("board-invitations", token);
		var attempt = await SetPasswordAsync("board-invitations", token, "overtaget-kodeord");

		await Assert.That(invite.IsSuccessStatusCode).IsTrue();
		await Assert.That(factory.Emails.To(email).Last().PlainTextBody!).Contains("Log ind med din eksisterende konto");
		await Assert.That(preview.NeedsPassword).IsFalse();
		await Assert.That(attempt.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
		await Assert.That(_keycloak.PasswordOf(subject)).IsEqualTo("existing-password");
	}

	[Test]
	public async Task PasswordShorterThanEightCharacters_Is400()
	{
		var email = UniqueEmail("kort-kode");
		await InviteTeacherAsync(email);
		var token = factory.Emails.LastInvitationToken(email, "invitation");

		var response = await SetPasswordAsync("staff-invitations", token, "kort");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That(_keycloak.PasswordOf(_keycloak.SubjectFor(email))).IsNull();
	}

	[Test]
	public async Task KeycloakDown_PreviewIs503_NotAnInvalidLink()
	{
		var email = UniqueEmail("keycloak-nede");
		await InviteTeacherAsync(email);
		var token = factory.Emails.LastInvitationToken(email, "invitation");

		_keycloak.Unavailable = true;
		var response = await _anonymous.GetAsync($"/api/v1/staff-invitations/preview?token={token}");
		var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOpts);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
		await Assert.That(problem!.Status).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
	}

	// ── Helpers ──────────────────────────────────────────────────────────────────

	private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@test.dk";

	private Task<HttpResponseMessage> SetPasswordAsync(string api, string token, string password) =>
		_anonymous.PostAsJsonAsync($"/api/v1/{api}/password", new SetInvitationPasswordRequest(token, password));

	private async Task<T> PreviewAsync<T>(string api, string token) =>
		(await _anonymous.GetFromJsonAsync<T>($"/api/v1/{api}/preview?token={token}", JsonOpts))!;

	private async Task InviteTeacherAsync(string email)
	{
		var created = await _admin.PostAsJsonAsync("/api/v1/staff",
			new StaffController.UpsertStaffRequest("Thomas Lærer", email, null, StaffRole.Teacher));
		var staff = await created.Content.ReadFromJsonAsync<StaffController.StaffDto>(JsonOpts);
		var invite = await _admin.PostAsync($"/api/v1/staff-invitations/invite/{staff!.Id}", null);
		await Assert.That(invite.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	private async Task<Guid> CreateStudentAsync()
	{
		var klass = await _admin.PostAsJsonAsync("/api/v1/classes",
			new ClassesController.UpsertClassRequest($"1.{Guid.NewGuid():N}"[..8], null, 1), JsonOpts);
		var classDto = await klass.Content.ReadFromJsonAsync<ClassesController.ClassDto>(JsonOpts);
		var student = await _admin.PostAsJsonAsync("/api/v1/students",
			new StudentsController.UpsertStudentRequest("Mikkel Elev", classDto!.Id, false), JsonOpts);
		return (await student.Content.ReadFromJsonAsync<StudentsController.StudentDto>(JsonOpts))!.Id;
	}
}
