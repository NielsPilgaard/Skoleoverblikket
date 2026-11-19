using System.Net;
using System.Net.Http.Json;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// The admin student register (/api/v1/students). Parent invitations, fravær and SFO all
/// hang off these students, so a broken create/move/delete blocks the whole parent module.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class StudentsTests(ApiFactory factory)
{
	private readonly ApiFactory _factory = factory;
	private readonly Guid _tenantId = Guid.NewGuid();
	private HttpClient _adminClient = null!;

	[Before(Test)]
	public async Task SetUp()
	{
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, _tenantId);
		_adminClient = CreateAdminClient(_tenantId);
	}

	[Test]
	public async Task Create_ThenList_ReturnsStudentWithClassName()
	{
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, className: "3.a");

		var createResponse = await _adminClient.PostAsJsonAsync("/api/v1/students",
			new StudentsController.UpsertStudentRequest("Lars Larsen", klass.Id, IsEnrolledInSfo: true));
		await Assert.That(createResponse.StatusCode).IsEqualTo(HttpStatusCode.Created);
		var created = await createResponse.Content.ReadFromJsonAsync<StudentsController.StudentDto>();
		await Assert.That(created!.ClassName).IsEqualTo("3.a");
		await Assert.That(created.IsEnrolledInSfo).IsTrue();

		var students = await _adminClient.GetFromJsonAsync<List<StudentsController.StudentDto>>("/api/v1/students");
		await Assert.That(students!.Count).IsEqualTo(1);
		await Assert.That(students[0].Id).IsEqualTo(created.Id);
		await Assert.That(students[0].Name).IsEqualTo("Lars Larsen");
		await Assert.That(students[0].ClassName).IsEqualTo("3.a");
	}

	[Test]
	public async Task GetAll_FiltersByClass_AndSortsByClassThenName()
	{
		var (klass3a, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, className: "3.a");
		var (klass1b, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, className: "1.b");
		await CreateStudentAsync("Signe", klass3a.Id);
		await CreateStudentAsync("Anders", klass3a.Id);
		await CreateStudentAsync("Mads", klass1b.Id);

		var all = await _adminClient.GetFromJsonAsync<List<StudentsController.StudentDto>>("/api/v1/students");
		await Assert.That(string.Join(",", all!.Select(s => s.Name))).IsEqualTo("Mads,Anders,Signe");

		var only3a = await _adminClient.GetFromJsonAsync<List<StudentsController.StudentDto>>($"/api/v1/students?classId={klass3a.Id}");
		await Assert.That(string.Join(",", only3a!.Select(s => s.Name))).IsEqualTo("Anders,Signe");
	}

	[Test]
	public async Task Create_Returns400_WhenClassDoesNotExist()
	{
		var response = await _adminClient.PostAsJsonAsync("/api/v1/students",
			new StudentsController.UpsertStudentRequest("Lars Larsen", Guid.NewGuid(), IsEnrolledInSfo: false));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task Create_Returns400_WhenClassBelongsToAnotherSchool()
	{
		var otherTenantId = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, otherTenantId);
		var (otherClass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, otherTenantId);

		var response = await _adminClient.PostAsJsonAsync("/api/v1/students",
			new StudentsController.UpsertStudentRequest("Lars Larsen", otherClass.Id, IsEnrolledInSfo: false));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task Update_MovesStudentToAnotherClass_AndChangesSfoEnrollment()
	{
		var (klass3a, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, className: "3.a");
		var (klass4a, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, className: "4.a");
		var student = await CreateStudentAsync("Lars Larsen", klass3a.Id);

		var response = await _adminClient.PutAsJsonAsync($"/api/v1/students/{student.Id}",
			new StudentsController.UpsertStudentRequest("Lars B. Larsen", klass4a.Id, IsEnrolledInSfo: true));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var updated = await response.Content.ReadFromJsonAsync<StudentsController.StudentDto>();
		await Assert.That(updated!.ClassName).IsEqualTo("4.a");

		var in4a = await _adminClient.GetFromJsonAsync<List<StudentsController.StudentDto>>($"/api/v1/students?classId={klass4a.Id}");
		await Assert.That(in4a!.Count).IsEqualTo(1);
		await Assert.That(in4a[0].Name).IsEqualTo("Lars B. Larsen");
		await Assert.That(in4a[0].IsEnrolledInSfo).IsTrue();
		var in3a = await _adminClient.GetFromJsonAsync<List<StudentsController.StudentDto>>($"/api/v1/students?classId={klass3a.Id}");
		await Assert.That(in3a!.Count).IsEqualTo(0);
	}

	[Test]
	public async Task Delete_RemovesStudent()
	{
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId);
		var student = await CreateStudentAsync("Lars Larsen", klass.Id);

		var response = await _adminClient.DeleteAsync($"/api/v1/students/{student.Id}");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var students = await _adminClient.GetFromJsonAsync<List<StudentsController.StudentDto>>("/api/v1/students");
		await Assert.That(students!.Count).IsEqualTo(0);
	}

	[Test]
	public async Task OtherSchoolAdmin_CannotSeeUpdateOrDeleteStudent()
	{
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId);
		var student = await CreateStudentAsync("Lars Larsen", klass.Id);

		var otherTenantId = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, otherTenantId);
		var (otherClass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, otherTenantId);
		using var otherAdmin = CreateAdminClient(otherTenantId);

		var list = await otherAdmin.GetFromJsonAsync<List<StudentsController.StudentDto>>("/api/v1/students");
		await Assert.That(list!.Count).IsEqualTo(0);

		var update = await otherAdmin.PutAsJsonAsync($"/api/v1/students/{student.Id}",
			new StudentsController.UpsertStudentRequest("Hijacked", otherClass.Id, IsEnrolledInSfo: false));
		await Assert.That(update.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

		var delete = await otherAdmin.DeleteAsync($"/api/v1/students/{student.Id}");
		await Assert.That(delete.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

		var ownList = await _adminClient.GetFromJsonAsync<List<StudentsController.StudentDto>>("/api/v1/students");
		await Assert.That(ownList!.Count).IsEqualTo(1);
		await Assert.That(ownList[0].Name).IsEqualTo("Lars Larsen");
	}

	private HttpClient CreateAdminClient(Guid tenantId)
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "admin");
		return client;
	}

	private async Task<StudentsController.StudentDto> CreateStudentAsync(string name, Guid classId)
	{
		var response = await _adminClient.PostAsJsonAsync("/api/v1/students",
			new StudentsController.UpsertStudentRequest(name, classId, IsEnrolledInSfo: false));
		response.EnsureSuccessStatusCode();
		return (await response.Content.ReadFromJsonAsync<StudentsController.StudentDto>())!;
	}
}
