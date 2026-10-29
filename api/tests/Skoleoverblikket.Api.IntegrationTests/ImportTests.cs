using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// The CSV imports (/api/v1/imports) Hanne uses to set up a school: classes, students with parents,
/// staff and rooms. A broken import means typing in 300 students by hand, and a non-idempotent one
/// fills the school with duplicates when she uploads the same sheet twice.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class ImportTests(ApiFactory factory)
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
		await TestDataBuilder.CreateTrialSubscriptionAsync(_factory.Services, _tenantId);
		_adminClient = CreateClient(_tenantId, "admin");
	}

	[Test]
	public async Task StudentsAndParents_CreatesClassesStudentsAndLinksSiblingsToOneParent()
	{
		var mor = new ImportsController.ImportParentRow("Mette Hansen", "mette@example.dk", "12345678", null, null, null);
		var far = new ImportsController.ImportParentRow("Jens Hansen", null, null, null, null, null);

		var result = await ImportStudentsAsync(
			new ImportsController.ImportStudentRow("1.a", "Emma Hansen", mor, far),
			new ImportsController.ImportStudentRow("3.b", "Oskar Hansen", mor, null));

		await Assert.That(result.ClassesCreated).IsEqualTo(2);
		await Assert.That(result.StudentsCreated).IsEqualTo(2);
		await Assert.That(result.ParentsCreated).IsEqualTo(2);
		await Assert.That(result.ParentsUpdated).IsEqualTo(1);
		await Assert.That(result.ParentStudentLinksCreated).IsEqualTo(3);
		// Jens has no e-mail, so he can't log in and isn't offered an invitation
		await Assert.That(result.Warnings.Single().Message).Contains("mangler e-mail");
		await Assert.That(result.UninvitedParents.Single().Email).IsEqualTo("mette@example.dk");

		var parents = await _adminClient.GetFromJsonAsync<List<ParentsController.ParentSummaryDto>>("/api/v1/parents");
		var mette = parents!.Single(p => p.Name == "Mette Hansen");
		await Assert.That(mette.Students.Select(s => s.Name).Order()).IsEquivalentTo(["Emma Hansen", "Oskar Hansen"]);
		await Assert.That(parents!.Count).IsEqualTo(2);
	}

	[Test]
	public async Task StudentsAndParents_ImportingSameSheetTwice_CreatesNoDuplicates()
	{
		var row = new ImportsController.ImportStudentRow("2.a", "Lars Larsen",
			new ImportsController.ImportParentRow("Pia Larsen", "Pia@Example.dk", null, null, null, null), null);
		await ImportStudentsAsync(row);

		var second = await ImportStudentsAsync(row with { ClassName = " 2.A ", StudentName = "lars larsen" });

		await Assert.That(second.ClassesCreated).IsEqualTo(0);
		await Assert.That(second.StudentsCreated).IsEqualTo(0);
		await Assert.That(second.StudentsSkipped).IsEqualTo(1);
		await Assert.That(second.ParentsCreated).IsEqualTo(0);
		await Assert.That(second.ParentStudentLinksCreated).IsEqualTo(0);

		var students = await _adminClient.GetFromJsonAsync<List<StudentsController.StudentDto>>("/api/v1/students");
		await Assert.That(students!.Count).IsEqualTo(1);
		var parents = await _adminClient.GetFromJsonAsync<List<ParentsController.ParentSummaryDto>>("/api/v1/parents");
		await Assert.That(parents!.Count).IsEqualTo(1);
	}

	[Test]
	public async Task StudentsAndParents_DoesNotMatchParentWithSameEmailAtAnotherSchool()
	{
		var otherTenantId = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, otherTenantId, "Anden Skole");
		await TestDataBuilder.CreateTrialSubscriptionAsync(_factory.Services, otherTenantId);
		var parent = new ImportsController.ImportParentRow("Mette Hansen", "mette@example.dk", null, null, null, null);
		await ImportStudentsAsync(new ImportsController.ImportStudentRow("1.a", "Emma Hansen", parent, null));

		var otherClient = CreateClient(otherTenantId, "admin");
		var response = await otherClient.PostAsJsonAsync("/api/v1/imports/students-and-parents",
			new ImportsController.ImportStudentsAndParentsRequest(
				[new ImportsController.ImportStudentRow("1.a", "Emma Hansen", parent with { Name = "Mette H." }, null)]));
		var result = await response.Content.ReadFromJsonAsync<ImportsController.ImportStudentsAndParentsResponse>(JsonOpts);

		await Assert.That(result!.ClassesCreated).IsEqualTo(1);
		await Assert.That(result.StudentsCreated).IsEqualTo(1);
		await Assert.That(result.ParentsCreated).IsEqualTo(1);
		var ownParents = await _adminClient.GetFromJsonAsync<List<ParentsController.ParentSummaryDto>>("/api/v1/parents");
		await Assert.That(ownParents!.Single().Name).IsEqualTo("Mette Hansen");
	}

	[Test]
	public async Task Staff_CreatesThenUpdatesByEmail_AndParsesDanishRoles()
	{
		var first = await ImportStaffAsync(
			new ImportsController.ImportStaffRow("Karen Pædagog", "karen@skole.dk", null, "Pædagog", "nej"),
			new ImportsController.ImportStaffRow("Ole Lærer", null, null, "rektor", "ja"),
			new ImportsController.ImportStaffRow("Bad Mail", "ikke-en-mail", null, null, null));

		await Assert.That(first.StaffCreated).IsEqualTo(2);
		await Assert.That(first.StaffSkipped).IsEqualTo(1);
		await Assert.That(first.Warnings.Select(w => w.Row)).IsEquivalentTo([2, 3]);

		var second = await ImportStaffAsync(
			new ImportsController.ImportStaffRow("Karen Jensen", "KAREN@skole.dk", "87654321", "pædagog", null));
		await Assert.That(second.StaffCreated).IsEqualTo(0);
		await Assert.That(second.StaffUpdated).IsEqualTo(1);

		var staff = await GetStaffAsync();
		await Assert.That(staff.Count).IsEqualTo(2);
		var karen = staff.Single(s => s.Email == "karen@skole.dk");
		await Assert.That(karen.Name).IsEqualTo("Karen Jensen");
		await Assert.That(karen.Phone).IsEqualTo("87654321");
		await Assert.That(karen.Role).IsEqualTo(StaffRole.Aide);
		var ole = staff.Single(s => s.Name == "Ole Lærer");
		await Assert.That(ole.Role).IsEqualTo(StaffRole.Teacher);
		await Assert.That(ole.IsAdmin).IsTrue();
	}

	[Test]
	public async Task Staff_CannotRemoveTheImportingAdminsOwnAdminRights()
	{
		await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId,
			name: "Hanne Sekretær", isAdmin: true, keycloakSubject: "test-user-id");

		var result = await ImportStaffAsync(
			new ImportsController.ImportStaffRow("Hanne Sekretær", null, null, null, "nej"));

		await Assert.That(result.StaffUpdated).IsEqualTo(1);
		var hanne = (await GetStaffAsync()).Single();
		await Assert.That(hanne.IsAdmin).IsTrue();
	}

	[Test]
	public async Task Rooms_UpdatesExistingByNameAndWarnsOnBadCapacity()
	{
		await TestDataBuilder.CreateRoomAsync(_factory.Services, _tenantId, "Fysiklokale");

		var response = await _adminClient.PostAsJsonAsync("/api/v1/imports/rooms",
			new ImportsController.ImportRoomsRequest(
			[
				new ImportsController.ImportRoomRow("fysiklokale", "Med stinkskab", "24"),
				new ImportsController.ImportRoomRow("Gymnastiksal", null, "mange"),
				new ImportsController.ImportRoomRow(" ", null, null),
			]));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var result = await response.Content.ReadFromJsonAsync<ImportsController.ImportRoomsResponse>(JsonOpts);

		await Assert.That(result!.RoomsCreated).IsEqualTo(1);
		await Assert.That(result.RoomsUpdated).IsEqualTo(1);
		await Assert.That(result.RoomsSkipped).IsEqualTo(1);
		await Assert.That(result.Warnings.Single().Row).IsEqualTo(2);

		var rooms = await _adminClient.GetFromJsonAsync<List<RoomsController.RoomDto>>("/api/v1/rooms");
		var fysik = rooms!.Single(r => r.Name == "Fysiklokale");
		await Assert.That(fysik.Capacity).IsEqualTo(24);
		await Assert.That(fysik.Description).IsEqualTo("Med stinkskab");
		await Assert.That(rooms!.Single(r => r.Name == "Gymnastiksal").Capacity).IsNull();
	}

	[Test]
	public async Task Classes_CreatesThenUpdatesGradeLevel()
	{
		var first = await ImportClassesAsync(new ImportsController.ImportClassRow("5.a", null, "5"));
		await Assert.That(first.ClassesCreated).IsEqualTo(1);

		var second = await ImportClassesAsync(
			new ImportsController.ImportClassRow("5.A", "Klasselærer: Ole", "6"),
			new ImportsController.ImportClassRow("6.a", null, "11"));

		await Assert.That(second.ClassesUpdated).IsEqualTo(1);
		await Assert.That(second.ClassesCreated).IsEqualTo(1);
		await Assert.That(second.Warnings.Single().Row).IsEqualTo(2);

		var classes = await _adminClient.GetFromJsonAsync<List<ClassesController.ClassDto>>("/api/v1/classes");
		var fifth = classes!.Single(c => c.Name == "5.a");
		await Assert.That(fifth.GradeLevel).IsEqualTo(6);
		await Assert.That(fifth.Description).IsEqualTo("Klasselærer: Ole");
		await Assert.That(classes!.Single(c => c.Name == "6.a").GradeLevel).IsNull();
	}

	[Test]
	public async Task NonAdminStaff_GetsForbidden()
	{
		var staffClient = CreateClient(_tenantId, "user");

		var response = await staffClient.PostAsJsonAsync("/api/v1/imports/rooms",
			new ImportsController.ImportRoomsRequest([new ImportsController.ImportRoomRow("Musiklokale", null, null)]));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		var rooms = await _adminClient.GetFromJsonAsync<List<RoomsController.RoomDto>>("/api/v1/rooms");
		await Assert.That(rooms!.Count).IsEqualTo(0);
	}

	private async Task<ImportsController.ImportStudentsAndParentsResponse> ImportStudentsAsync(
		params ImportsController.ImportStudentRow[] rows)
	{
		var response = await _adminClient.PostAsJsonAsync("/api/v1/imports/students-and-parents",
			new ImportsController.ImportStudentsAndParentsRequest(rows));
		response.EnsureSuccessStatusCode();
		return (await response.Content.ReadFromJsonAsync<ImportsController.ImportStudentsAndParentsResponse>(JsonOpts))!;
	}

	private async Task<ImportsController.ImportStaffResponse> ImportStaffAsync(params ImportsController.ImportStaffRow[] rows)
	{
		var response = await _adminClient.PostAsJsonAsync("/api/v1/imports/staff", new ImportsController.ImportStaffRequest(rows));
		response.EnsureSuccessStatusCode();
		return (await response.Content.ReadFromJsonAsync<ImportsController.ImportStaffResponse>(JsonOpts))!;
	}

	private async Task<ImportsController.ImportClassesResponse> ImportClassesAsync(params ImportsController.ImportClassRow[] rows)
	{
		var response = await _adminClient.PostAsJsonAsync("/api/v1/imports/classes", new ImportsController.ImportClassesRequest(rows));
		response.EnsureSuccessStatusCode();
		return (await response.Content.ReadFromJsonAsync<ImportsController.ImportClassesResponse>(JsonOpts))!;
	}

	private async Task<List<StaffController.StaffDto>> GetStaffAsync() =>
		(await _adminClient.GetFromJsonAsync<List<StaffController.StaffDto>>("/api/v1/staff", JsonOpts))!;

	private HttpClient CreateClient(Guid tenantId, string roles)
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", roles);
		return client;
	}
}
