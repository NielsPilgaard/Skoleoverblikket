using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Paid modules are enforced server side (task 58). R1: board and parent users are blocked without
/// their module. R2: admin writes on a module's features need the module, reads and deletes don't.
/// R3: an invitation can't be accepted once the school dropped the module. A trial has every module.
/// Keycloak is replaced by <see cref="FakeKeycloak"/> so invitations create real accounts.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed partial class ModuleAccessTests(ApiFactory factory)
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
		_app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
		{
			services.RemoveAll<IKeycloakAdminApi>();
			services.RemoveAll<IKeycloakTokenApi>();
			services.AddSingleton<IKeycloakAdminApi>(_keycloak);
			services.AddSingleton<IKeycloakTokenApi>(_keycloak);
		}));
		await TestDataBuilder.CreateSchoolAsync(_app.Services, _tenantId);
		_admin = Client(Roles.Admin, "module-admin");
	}

	// ── R1: board and parent users ───────────────────────────────────────────────

	[Test]
	public async Task BoardUser_IsBlocked_WhenBoardModuleIsRemoved()
	{
		await TestDataBuilder.CreateActiveSubscriptionAsync(_app.Services, _tenantId, SubscriptionModule.BoardModule);
		var email = UniqueEmail("board");
		var member = await InviteBoardMemberAsync(email);
		var toggle = await _admin.PatchAsJsonAsync(
			$"/api/v1/board-members/{member.Id}/teacher-data-access",
			new BoardMembersController.ToggleTeacherDataRequest(true));
		await Assert.That(toggle.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_app.Services, _tenantId);

		var board = Client(Roles.Board, _keycloak.SubjectFor(email));
		string[] urls = ["/api/v1/board-members/me", "/api/v1/board-files", $"/api/v1/classes/{klass.Id}/schemas"];
		foreach (var url in urls)
		{
			await Assert.That((await board.GetAsync(url)).StatusCode).IsEqualTo(HttpStatusCode.OK);
		}

		await TestDataBuilder.RemoveModuleAsync(_app.Services, _tenantId, SubscriptionModule.BoardModule);

		foreach (var url in urls)
		{
			await AssertModuleInactiveAsync(await board.GetAsync(url));
		}

		// The frontend can still read the module state to show why.
		await Assert.That((await board.GetAsync("/api/v1/modules")).StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async Task ParentUser_IsBlocked_WhenParentModuleIsRemoved()
	{
		await TestDataBuilder.CreateActiveSubscriptionAsync(_app.Services, _tenantId, SubscriptionModule.ParentModule);
		var email = UniqueEmail("parent");
		var invite = await _admin.PostAsJsonAsync(
			"/api/v1/parents/invite",
			new ParentsController.InviteParentRequest("Pia Forælder", email, []));
		await Assert.That(invite.StatusCode).IsEqualTo(HttpStatusCode.Created);

		var parent = Client(Roles.Parent, _keycloak.SubjectFor(email));
		string[] urls = ["/api/v1/parents/me", "/api/v1/absence/mine"];
		foreach (var url in urls)
		{
			await Assert.That((await parent.GetAsync(url)).StatusCode).IsEqualTo(HttpStatusCode.OK);
		}

		await TestDataBuilder.RemoveModuleAsync(_app.Services, _tenantId, SubscriptionModule.ParentModule);

		foreach (var url in urls)
		{
			await AssertModuleInactiveAsync(await parent.GetAsync(url));
		}
	}

	[Test]
	public async Task AdminAndBoardUser_WithoutBoardModule_KeepsAdminAccessOnly()
	{
		await TestDataBuilder.CreateActiveSubscriptionAsync(_app.Services, _tenantId);
		var adminAndBoard = Client($"{Roles.Admin},{Roles.Board}", "admin-and-board");

		await Assert.That((await adminAndBoard.GetAsync("/api/v1/board-members")).StatusCode).IsEqualTo(HttpStatusCode.OK);
		await AssertModuleInactiveAsync(await adminAndBoard.GetAsync("/api/v1/board-members/me"));
	}

	// ── R2: admin writes on a module's features ──────────────────────────────────

	[Test]
	public async Task Admin_WithoutBoardModule_CanReadAndDeleteButNotInvite()
	{
		await TestDataBuilder.CreateActiveSubscriptionAsync(_app.Services, _tenantId, SubscriptionModule.BoardModule);
		var member = await InviteBoardMemberAsync(UniqueEmail("board"));
		await TestDataBuilder.RemoveModuleAsync(_app.Services, _tenantId, SubscriptionModule.BoardModule);

		await AssertModuleInactiveAsync(await _admin.PostAsJsonAsync(
			"/api/v1/board-members/invite",
			new BoardMembersController.InviteBoardMemberRequest("Bo Bestyrelse", UniqueEmail("board"))));
		await Assert.That((await _admin.GetAsync("/api/v1/board-members")).StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That((await _admin.DeleteAsync($"/api/v1/board-members/{member.Id}")).StatusCode)
			.IsEqualTo(HttpStatusCode.NoContent);
	}

	[Test]
	public async Task Admin_WithoutParentModule_CannotInviteParentsOrCreateStudents()
	{
		await TestDataBuilder.CreateActiveSubscriptionAsync(_app.Services, _tenantId);

		await AssertModuleInactiveAsync(await _admin.PostAsJsonAsync(
			"/api/v1/parents/invite",
			new ParentsController.InviteParentRequest("Pia Forælder", UniqueEmail("parent"), [])));
		await AssertModuleInactiveAsync(await _admin.PostAsJsonAsync(
			"/api/v1/students",
			new StudentsController.UpsertStudentRequest("Emil Elev", Guid.NewGuid(), false)));
	}

	// ── R3: invitations ──────────────────────────────────────────────────────────

	[Test]
	public async Task BoardInvitation_CannotBeAccepted_AfterBoardModuleIsRemoved()
	{
		await TestDataBuilder.CreateActiveSubscriptionAsync(_app.Services, _tenantId, SubscriptionModule.BoardModule);
		var email = UniqueEmail("board");
		await InviteBoardMemberAsync(email);
		var token = BoardInvitationToken().Match(factory.Emails.To(email).Single().HtmlBody).Groups[1].Value;
		await TestDataBuilder.RemoveModuleAsync(_app.Services, _tenantId, SubscriptionModule.BoardModule);

		await AssertModuleInactiveAsync(await _app.CreateClient().PostAsync($"/api/v1/board-invitations/{token}/accept", null));
	}

	// ── Trial ────────────────────────────────────────────────────────────────────

	[Test]
	public async Task Trial_WithoutBoughtModules_CanInviteBoardMembersAndParents()
	{
		await TestDataBuilder.CreateTrialSubscriptionAsync(_app.Services, _tenantId);

		await InviteBoardMemberAsync(UniqueEmail("board"));
		var invite = await _admin.PostAsJsonAsync(
			"/api/v1/parents/invite",
			new ParentsController.InviteParentRequest("Pia Forælder", UniqueEmail("parent"), []));
		await Assert.That(invite.StatusCode).IsEqualTo(HttpStatusCode.Created);
	}

	// ── Helpers ──────────────────────────────────────────────────────────────────

	private HttpClient Client(string roles, string subject)
	{
		var client = _app.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", roles);
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
	}

	private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@test.dk";

	private async Task<BoardMembersController.BoardMemberDto> InviteBoardMemberAsync(string email)
	{
		var response = await _admin.PostAsJsonAsync(
			"/api/v1/board-members/invite",
			new BoardMembersController.InviteBoardMemberRequest("Bente Bestyrelse", email));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
		return (await response.Content.ReadFromJsonAsync<BoardMembersController.BoardMemberDto>(JsonOpts))!;
	}

	private static async Task AssertModuleInactiveAsync(HttpResponseMessage response)
	{
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That(await response.Content.ReadAsStringAsync()).Contains("Modulet er ikke aktivt");
	}

	[GeneratedRegex(@"/board-invitation/([A-Za-z0-9_-]+)")]
	private static partial Regex BoardInvitationToken();
}
