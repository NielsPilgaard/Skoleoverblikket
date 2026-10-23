using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
/// One login, several school roles (task 61). Roles come from Staff, BoardMember and Parent rows, not
/// from Keycloak, and X-Acting-As picks the one a request uses. Every client here sends no Keycloak
/// school role, so whatever access it gets comes from the database. Keycloak is replaced by
/// <see cref="FakeKeycloak"/> so invitations create real accounts.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class MultiRoleTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		Converters = { new JsonStringEnumConverter() },
		PropertyNameCaseInsensitive = true,
	};

	private readonly FakeKeycloak _keycloak = new();
	private readonly Guid _tenantId = Guid.NewGuid();
	private WebApplicationFactory<Program> _app = null!;
	private HttpClient _admin = null!;

	[Before(Test)]
	public async Task SetUp()
	{
		_app = WithFakeKeycloak();
		await TestDataBuilder.CreateSchoolAsync(_app.Services, _tenantId);
		var adminSubject = $"multi-admin-{Guid.NewGuid():N}";
		await TestDataBuilder.CreateStaffAsync(_app.Services, _tenantId, "Hanne Kontor", isAdmin: true, keycloakSubject: adminSubject);
		_admin = Client(adminSubject);
	}

	[After(Test)]
	public async Task TearDown() => await _app.DisposeAsync();

	[Test]
	public async Task TeacherInvitedAsParent_KeepsStaffView_AndSeesChildWhenActingAsParent()
	{
		var email = UniqueEmail("laerer-forælder");
		var subject = await InviteTeacherAsync(email);
		await ChoosePasswordAsync("staff-invitations", "invitation", email);
		var studentId = await CreateStudentAsync();

		var invite = await InviteParentAsync(email, studentId);

		await Assert.That(invite.StatusCode).IsEqualTo(HttpStatusCode.Created);
		var parentEmail = factory.Emails.To(email).Last();
		await Assert.That(parentEmail.PlainTextBody!).Contains("Log ind med din eksisterende konto");

		var person = Client(subject);
		var me = await GetMeAsync(person);
		await Assert.That(me.Roles.Select(r => r.Role)).IsEquivalentTo([Roles.Staff, Roles.Parent]);
		await Assert.That(me.ActingAs).IsEqualTo(Roles.Staff);
		await Assert.That((await person.GetAsync("/api/v1/staff/me")).StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That((await person.GetAsync("/api/v1/parents/me")).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

		var asParent = Client(subject, actingAs: Roles.Parent);
		var parentMe = await asParent.GetFromJsonAsync<ParentMeController.ParentMeDto>("/api/v1/parents/me", JsonOpts);
		await Assert.That(parentMe!.Students.Select(s => s.StudentId)).IsEquivalentTo([studentId]);
		await Assert.That((await asParent.GetAsync("/api/v1/staff-absences/mine")).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task RemovingOneRole_KeepsTheSharedLogin_UntilTheLastRoleGoes()
	{
		var email = UniqueEmail("delt-login");
		var subject = await InviteTeacherAsync(email);
		var invite = await InviteParentAsync(email, await CreateStudentAsync());
		var parent = await invite.Content.ReadFromJsonAsync<ParentsController.ParentDto>(JsonOpts);

		var deleteParent = await _admin.DeleteAsync($"/api/v1/parents/{parent!.Id}");

		await Assert.That(deleteParent.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
		await Assert.That(_keycloak.DeletedUserIds).DoesNotContain(subject);
		var person = Client(subject);
		await Assert.That((await person.GetAsync("/api/v1/staff/me")).StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That((await GetMeAsync(person)).Roles.Select(r => r.Role)).IsEquivalentTo([Roles.Staff]);

		var staff = await person.GetFromJsonAsync<StaffController.StaffDto>("/api/v1/staff/me", JsonOpts);
		var deleteStaff = await _admin.DeleteAsync($"/api/v1/staff/{staff!.Id}");

		await Assert.That(deleteStaff.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
		await Assert.That(_keycloak.DeletedUserIds).Contains(subject);
	}

	[Test]
	public async Task ActingAs_ARoleNotHeld_Is403_AndAnUnknownRole_Is400()
	{
		var subject = await InviteTeacherAsync(UniqueEmail("kun-laerer"));

		var notHeld = await Client(subject, actingAs: Roles.Parent).GetAsync("/api/v1/staff/me");
		var unknown = await Client(subject, actingAs: Roles.Admin).GetAsync("/api/v1/staff/me");

		await Assert.That(notHeld.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That(unknown.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task ActingAsBoard_WithoutBoardModule_Is403_WhileStaffStillWorks()
	{
		var subject = $"laerer-bestyrelse-{Guid.NewGuid():N}";
		await TestDataBuilder.CreateStaffAsync(_app.Services, _tenantId, "Thomas Lærer", keycloakSubject: subject);
		await TestDataBuilder.CreateBoardMemberAsync(_app.Services, _tenantId, subject);
		await TestDataBuilder.CreateActiveSubscriptionAsync(_app.Services, _tenantId);

		var asBoard = await Client(subject, actingAs: Roles.Board).GetAsync("/api/v1/board-members/me");
		var asStaff = await Client(subject, actingAs: Roles.Staff).GetAsync("/api/v1/staff/me");
		var me = await GetMeAsync(Client(subject));

		await Assert.That(asBoard.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That(await asBoard.Content.ReadAsStringAsync()).Contains("Modulet er ikke aktivt");
		await Assert.That(asStaff.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(me.Roles.Single(r => r.Role == Roles.Board).Active).IsFalse();
	}

	[Test]
	public async Task InvitingAnEmailFromAnotherSchool_Is409_AndSavesNothing()
	{
		var email = UniqueEmail("anden-skole");
		_keycloak.AddExistingUser(email, Guid.NewGuid());

		var parent = await InviteParentAsync(email);
		var board = await _admin.PostAsJsonAsync("/api/v1/board-members/invite",
			new BoardMembersController.InviteBoardMemberRequest("Bente Bestyrelse", email));
		var staff = await _admin.PostAsJsonAsync("/api/v1/staff",
			new StaffController.UpsertStaffRequest("Thomas Lærer", email, null, StaffRole.Teacher));
		var staffDto = await staff.Content.ReadFromJsonAsync<StaffController.StaffDto>(JsonOpts);
		var staffInvite = await _admin.PostAsync($"/api/v1/staff-invitations/invite/{staffDto!.Id}", null);

		await Assert.That(parent.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
		await Assert.That(board.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
		await Assert.That(staffInvite.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
		var parents = await _admin.GetFromJsonAsync<List<ParentsController.ParentSummaryDto>>("/api/v1/parents", JsonOpts);
		var boardMembers = await _admin.GetFromJsonAsync<List<BoardMembersController.BoardMemberDto>>("/api/v1/board-members", JsonOpts);
		await Assert.That(parents!.Count).IsEqualTo(0);
		await Assert.That(boardMembers!.Any(m => m.Email == email)).IsFalse();
	}

	[Test]
	public async Task RowsInOneSchool_GiveNoRolesWithATokenForAnother()
	{
		var subject = $"skole-a-{Guid.NewGuid():N}";
		await TestDataBuilder.CreateStaffAsync(_app.Services, _tenantId, "Thomas Lærer", isAdmin: true, keycloakSubject: subject);
		var otherSchool = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(_app.Services, otherSchool, "Anden Skole");

		var inOtherSchool = Client(subject, tenantId: otherSchool);

		var me = await GetMeAsync(inOtherSchool);
		await Assert.That(me.Roles).IsEmpty();
		await Assert.That(me.IsAdmin).IsFalse();
		await Assert.That((await inOtherSchool.GetAsync("/api/v1/board-members")).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That((await inOtherSchool.GetAsync("/api/v1/staff-absences/mine")).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task SignupAdmin_IsAdminWithoutAKeycloakAdminRole()
	{
		var email = UniqueEmail("signup");
		var signup = await _app.CreateClient().PostAsJsonAsync("/api/v1/tenants", new CreateTenantRequest(
			Name: "Rolle Friskole",
			AdminEmail: email,
			AdminFirstName: "Hanne",
			AdminLastName: "Kontor",
			AdminPassword: "hemmeligt-123",
			AcceptDataProcessingAgreement: true));
		await Assert.That(signup.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var school = await signup.Content.ReadFromJsonAsync<TenantCreatedDto>(JsonOpts);

		var admin = Client(_keycloak.SubjectFor(email), tenantId: school!.Id);

		await Assert.That((await GetMeAsync(admin)).IsAdmin).IsTrue();
		await Assert.That((await admin.GetAsync("/api/v1/board-members")).StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async Task Backfill_CopiesTheKeycloakAdminRoleToStaff()
	{
		var subject = $"gammel-admin-{Guid.NewGuid():N}";
		await TestDataBuilder.CreateStaffAsync(_app.Services, _tenantId, "Gammel Admin", isAdmin: false, keycloakSubject: subject);
		_keycloak.AdminRoleMembers.Add(subject);

		// AdminRoleBackfillJob runs when a host starts.
		await using var restarted = WithFakeKeycloak();
		var client = Client(restarted, subject, null, _tenantId);

		var isAdmin = false;
		for (var attempt = 0; attempt < 50 && !isAdmin; attempt++)
		{
			isAdmin = (await GetMeAsync(client)).IsAdmin;
			if (!isAdmin)
			{
				await Task.Delay(100);
			}
		}

		await Assert.That(isAdmin).IsTrue();
	}

	// ── Helpers ──────────────────────────────────────────────────────────────────

	private WebApplicationFactory<Program> WithFakeKeycloak() =>
		factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
		{
			services.RemoveAll<IKeycloakAdminApi>();
			services.RemoveAll<IKeycloakTokenApi>();
			services.AddSingleton<IKeycloakAdminApi>(_keycloak);
			services.AddSingleton<IKeycloakTokenApi>(_keycloak);
		}));

	/// <summary>A client with no Keycloak school role: its roles come from the database only.</summary>
	private HttpClient Client(string subject, string? actingAs = null, Guid? tenantId = null) =>
		Client(_app, subject, actingAs, tenantId ?? _tenantId);

	private static HttpClient Client(WebApplicationFactory<Program> app, string subject, string? actingAs, Guid tenantId)
	{
		var client = app.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "default-roles");
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		if (actingAs is not null)
		{
			client.DefaultRequestHeaders.Add(SchoolRolesClaimsTransformer.ActingAsHeader, actingAs);
		}

		return client;
	}

	private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@test.dk";

	private static async Task<MeController.MeDto> GetMeAsync(HttpClient client) =>
		(await client.GetFromJsonAsync<MeController.MeDto>("/api/v1/me", JsonOpts))!;

	/// <summary>Creates a teacher and sends the staff invitation, which creates the login. Returns its subject.</summary>
	private async Task<string> InviteTeacherAsync(string email)
	{
		var created = await _admin.PostAsJsonAsync("/api/v1/staff",
			new StaffController.UpsertStaffRequest("Thomas Lærer", email, null, StaffRole.Teacher));
		await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
		var staff = await created.Content.ReadFromJsonAsync<StaffController.StaffDto>(JsonOpts);
		var invite = await _admin.PostAsync($"/api/v1/staff-invitations/invite/{staff!.Id}", null);
		await Assert.That(invite.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return _keycloak.SubjectFor(email);
	}

	/// <summary>The invitee chooses a password from the newest invitation link, as on the invitation page.</summary>
	private async Task ChoosePasswordAsync(string api, string linkPath, string email)
	{
		var token = factory.Emails.LastInvitationToken(email, linkPath);
		var response = await _app.CreateClient().PostAsJsonAsync(
			$"/api/v1/{api}/password", new SetInvitationPasswordRequest(token, "mit-eget-kodeord"));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	private async Task<Guid> CreateStudentAsync()
	{
		var klass = await _admin.PostAsJsonAsync("/api/v1/classes",
			new ClassesController.UpsertClassRequest($"1.{Guid.NewGuid():N}"[..8], null, 1), JsonOpts);
		var classDto = await klass.Content.ReadFromJsonAsync<ClassesController.ClassDto>(JsonOpts);
		var student = await _admin.PostAsJsonAsync("/api/v1/students",
			new StudentsController.UpsertStudentRequest("Mikkel Elev", classDto!.Id, false), JsonOpts);
		await Assert.That(student.StatusCode).IsEqualTo(HttpStatusCode.Created);
		return (await student.Content.ReadFromJsonAsync<StudentsController.StudentDto>(JsonOpts))!.Id;
	}

	private Task<HttpResponseMessage> InviteParentAsync(string email, params Guid[] studentIds) =>
		_admin.PostAsJsonAsync("/api/v1/parents/invite",
			new ParentsController.InviteParentRequest("Pia Forælder", email, studentIds));
}
