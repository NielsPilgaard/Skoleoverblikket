using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Integration tests for /api/v1/board-members.
/// Covers admin GET access, 404 for unknown IDs, role-based access control (403),
/// the teacher-data-access PATCH flag toggle, and the /me endpoint for board members.
/// POST /invite and DELETE /{id} are excluded as they call external services
/// (Keycloak and email) that are not available in the test environment.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class BoardMemberTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		Converters = { new JsonStringEnumConverter() },
		PropertyNameCaseInsensitive = true,
	};

	private readonly ApiFactory _factory = factory;
	private readonly Guid _tenantId = Guid.NewGuid();
	private HttpClient _adminClient = null!;

	[Before(Test)]
	public async Task SetUp()
	{
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, _tenantId);
		_adminClient = _factory.CreateClient();
		_adminClient.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
	}

	// -- helper: seed a BoardMember directly via DbContext --
	private async Task<BoardMember> SeedBoardMemberAsync(
		string name = "Bestyrelses Testsen",
		string email = "board@test.dk",
		bool canAccessTeacherData = false,
		string? keycloakSubject = null)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		var member = new BoardMember
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Name = name,
			Email = email,
			CanAccessTeacherData = canAccessTeacherData,
			KeycloakSubject = keycloakSubject,
		};
		db.BoardMembers.Add(member);
		await db.SaveChangesAsync();
		return member;
	}

	[Test]
	public async Task GetAll_Admin_Returns200()
	{
		await SeedBoardMemberAsync();

		var response = await _adminClient.GetAsync("/api/v1/board-members");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var list = await response.Content.ReadFromJsonAsync<List<BoardMembersController.BoardMemberDto>>(JsonOpts);
		await Assert.That(list).IsNotNull();
		await Assert.That(list!.Count).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	public async Task GetById_Admin_Returns200()
	{
		var member = await SeedBoardMemberAsync(name: "Hanne Bestyrelsesmedlem", email: "hanne.bestyrelse@test.dk");

		var response = await _adminClient.GetAsync($"/api/v1/board-members/{member.Id}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<BoardMembersController.BoardMemberDto>(JsonOpts);
		await Assert.That(dto).IsNotNull();
		await Assert.That(dto!.Id).IsEqualTo(member.Id);
		await Assert.That(dto.Name).IsEqualTo("Hanne Bestyrelsesmedlem");
		await Assert.That(dto.Email).IsEqualTo("hanne.bestyrelse@test.dk");
		await Assert.That(dto.CanAccessTeacherData).IsFalse();
		await Assert.That(dto.HasAccount).IsFalse();
	}

	[Test]
	public async Task GetById_NotFound_Returns404()
	{
		var response = await _adminClient.GetAsync($"/api/v1/board-members/{Guid.NewGuid()}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task OtherSchoolAdmin_CannotSeeOrChangeMember()
	{
		var member = await SeedBoardMemberAsync(canAccessTeacherData: false);
		var otherTenantId = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, otherTenantId);
		using var otherAdmin = _factory.CreateClient();
		otherAdmin.DefaultRequestHeaders.Add("X-Test-TenantId", otherTenantId.ToString());

		var list = await otherAdmin.GetFromJsonAsync<List<BoardMembersController.BoardMemberDto>>("/api/v1/board-members", JsonOpts);
		var get = await otherAdmin.GetAsync($"/api/v1/board-members/{member.Id}");
		var patch = await otherAdmin.PatchAsJsonAsync(
			$"/api/v1/board-members/{member.Id}/teacher-data-access",
			new BoardMembersController.ToggleTeacherDataRequest(true));

		await Assert.That(list!.Any(m => m.Id == member.Id)).IsFalse();
		await Assert.That(get.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
		await Assert.That(patch.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
		var unchanged = await _adminClient.GetFromJsonAsync<BoardMembersController.BoardMemberDto>($"/api/v1/board-members/{member.Id}", JsonOpts);
		await Assert.That(unchanged!.CanAccessTeacherData).IsFalse();
	}

	[Test]
	public async Task GetAll_NonAdmin_Returns403()
	{
		using var userClient = _factory.CreateClient();
		userClient.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		userClient.DefaultRequestHeaders.Add("X-Test-Roles", "user");

		var response = await userClient.GetAsync("/api/v1/board-members");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task ToggleTeacherDataAccess_Admin_CanEnable_Returns200()
	{
		var member = await SeedBoardMemberAsync(canAccessTeacherData: false);

		var response = await _adminClient.PatchAsJsonAsync(
			$"/api/v1/board-members/{member.Id}/teacher-data-access",
			new BoardMembersController.ToggleTeacherDataRequest(true));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<BoardMembersController.BoardMemberDto>(JsonOpts);
		await Assert.That(dto).IsNotNull();
		await Assert.That(dto!.CanAccessTeacherData).IsTrue();
	}

	[Test]
	public async Task ToggleTeacherDataAccess_Admin_CanDisable_Returns200()
	{
		var member = await SeedBoardMemberAsync(canAccessTeacherData: true);

		var response = await _adminClient.PatchAsJsonAsync(
			$"/api/v1/board-members/{member.Id}/teacher-data-access",
			new BoardMembersController.ToggleTeacherDataRequest(false));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<BoardMembersController.BoardMemberDto>(JsonOpts);
		await Assert.That(dto).IsNotNull();
		await Assert.That(dto!.CanAccessTeacherData).IsFalse();
	}

	[Test]
	public async Task ToggleTeacherDataAccess_NonAdmin_Returns403()
	{
		var member = await SeedBoardMemberAsync();

		using var userClient = _factory.CreateClient();
		userClient.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		userClient.DefaultRequestHeaders.Add("X-Test-Roles", "user");

		var response = await userClient.PatchAsJsonAsync(
			$"/api/v1/board-members/{member.Id}/teacher-data-access",
			new BoardMembersController.ToggleTeacherDataRequest(true));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task GetMe_BoardRole_Returns200()
	{
		const string boardSubject = "board-me-subject";
		var member = await SeedBoardMemberAsync(
			name: "Mette Bestyrelsesformand",
			email: "mette.bestyrelse@test.dk",
			keycloakSubject: boardSubject);

		using var boardClient = _factory.CreateClient();
		boardClient.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		boardClient.DefaultRequestHeaders.Add("X-Test-Roles", "board");
		boardClient.DefaultRequestHeaders.Add("X-Test-Subject", boardSubject);

		var response = await boardClient.GetAsync("/api/v1/board-members/me");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<BoardMembersController.BoardMemberDto>(JsonOpts);
		await Assert.That(dto).IsNotNull();
		await Assert.That(dto!.Id).IsEqualTo(member.Id);
		await Assert.That(dto.Name).IsEqualTo("Mette Bestyrelsesformand");
		await Assert.That(dto.HasAccount).IsTrue();
	}
}
