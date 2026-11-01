using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Staff data (files, the staff list, staff and room schedules) is for staff. Parents see only their own
/// children's class schedule, board members need CanAccessTeacherData, and a login with no school role
/// sees none of it. Keycloak is replaced by <see cref="FakeKeycloak"/> so parents are invited through the API.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class RoleAccessTests(ApiFactory factory)
{
	private readonly FakeKeycloak _keycloak = new();
	private readonly Guid _tenantId = Guid.NewGuid();
	private WebApplicationFactory<Program> _app = null!;
	private HttpClient _admin = null!;
	private Guid _adminStaffId;

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
		var adminSubject = $"roles-admin-{Guid.NewGuid():N}";
		_adminStaffId = (await TestDataBuilder.CreateStaffAsync(_app.Services, _tenantId, "Hanne Kontor", isAdmin: true, keycloakSubject: adminSubject)).Id;
		_admin = Client(adminSubject, "admin");
	}

	[After(Test)]
	public async Task TearDown()
	{
		_admin.Dispose();
		await _app.DisposeAsync();
	}

	[Test]
	public async Task Parent_IsDeniedStaffData()
	{
		var room = await TestDataBuilder.CreateRoomAsync(_app.Services, _tenantId);
		using var parent = Client(await CreateParentAsync(await CreateClassAsync("3.a")), "parent");

		await AssertStaffDataDenied(parent, room.Id);
	}

	[Test]
	public async Task LoginWithNoSchoolRole_IsDeniedStaffAndClassData()
	{
		var classId = await CreateClassAsync("4.b");
		var room = await TestDataBuilder.CreateRoomAsync(_app.Services, _tenantId);
		using var nobody = Client($"roles-nobody-{Guid.NewGuid():N}", "user");

		await AssertStaffDataDenied(nobody, room.Id);
		await Assert.That((await nobody.GetAsync($"/api/v1/classes/{classId}/schedule")).StatusCode)
			.IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task Parent_SeesOwnChildsClassSchedule_NotOtherClasses()
	{
		var ownClass = await CreateClassAsync("2.a");
		var otherClass = await CreateClassAsync("6.c");
		using var parent = Client(await CreateParentAsync(ownClass), "parent");

		await Assert.That((await parent.GetAsync($"/api/v1/classes/{ownClass}/schedule")).StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That((await parent.GetAsync($"/api/v1/classes/{otherClass}/schedule")).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task BoardMemberWithoutTeacherData_IsDeniedClassSchedule()
	{
		var classId = await CreateClassAsync("7.a");
		var boardSubject = $"roles-board-{Guid.NewGuid():N}";
		await TestDataBuilder.CreateBoardMemberAsync(_app.Services, _tenantId, boardSubject);
		using var board = Client(boardSubject, "user");

		await Assert.That((await board.GetAsync($"/api/v1/classes/{classId}/schedule")).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That((await board.GetAsync("/api/v1/staff")).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task Teacher_ReachesStaffData()
	{
		var classId = await CreateClassAsync("1.a");
		var room = await TestDataBuilder.CreateRoomAsync(_app.Services, _tenantId);
		var teacherSubject = $"roles-teacher-{Guid.NewGuid():N}";
		await TestDataBuilder.CreateStaffAsync(_app.Services, _tenantId, "Thomas Lærer", keycloakSubject: teacherSubject);
		using var teacher = Client(teacherSubject, "user");

		await Assert.That((await teacher.GetAsync("/api/v1/files")).StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That((await teacher.GetAsync("/api/v1/staff")).StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That((await teacher.GetAsync($"/api/v1/staff/{_adminStaffId}/schedule")).StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That((await teacher.GetAsync($"/api/v1/rooms/{room.Id}/schedule")).StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That((await teacher.GetAsync($"/api/v1/classes/{classId}/schedule")).StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	private async Task AssertStaffDataDenied(HttpClient client, Guid roomId)
	{
		var presign = await client.PostAsJsonAsync("/api/v1/files/presign",
			new FilesController.PresignRequest("referat.pdf", 100, null, null));

		await Assert.That((await client.GetAsync("/api/v1/files")).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That(presign.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That((await client.GetAsync("/api/v1/staff")).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That((await client.GetAsync($"/api/v1/staff/{_adminStaffId}")).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That((await client.GetAsync($"/api/v1/staff/{_adminStaffId}/schedule")).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That((await client.GetAsync($"/api/v1/rooms/{roomId}/schedule")).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	private HttpClient Client(string subject, string realmRoles)
	{
		var client = _app.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", realmRoles);
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
	}

	private async Task<Guid> CreateClassAsync(string name)
	{
		var response = await _admin.PostAsJsonAsync("/api/v1/classes", new ClassesController.UpsertClassRequest(name, null, null));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
		return (await response.Content.ReadFromJsonAsync<ClassesController.ClassDto>())!.Id;
	}

	/// <summary>A parent login with one child in <paramref name="classId"/>. Returns the Keycloak subject.</summary>
	private async Task<string> CreateParentAsync(Guid classId)
	{
		var studentResponse = await _admin.PostAsJsonAsync("/api/v1/students",
			new StudentsController.UpsertStudentRequest("Elev Testsen", classId, false));
		await Assert.That(studentResponse.StatusCode).IsEqualTo(HttpStatusCode.Created);
		var student = await studentResponse.Content.ReadFromJsonAsync<StudentsController.StudentDto>();

		var email = $"roles-parent-{Guid.NewGuid():N}@test.dk";
		var invite = await _admin.PostAsJsonAsync("/api/v1/parents/invite",
			new ParentsController.InviteParentRequest("Forælder Testsen", email, [student!.Id]));
		await Assert.That(invite.StatusCode).IsEqualTo(HttpStatusCode.Created);
		return _keycloak.SubjectFor(email);
	}
}
