using System.Net;
using System.Net.Http.Json;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Verifies that non-admin staff can edit schemas on classes where either:
///   (a) they have a ClassPermission row, or
///   (b) the class has no ClassPermission rows at all.
///
/// A class with permission rows locked to other staff must return 403 for non-admin staff.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class NonAdminSchemaEditTests(ApiFactory factory)
{
	private readonly ApiFactory _factory = factory;
	private readonly Guid _tenantId = Guid.NewGuid();
	private HttpClient _adminClient = null!;

	[Before(Test)]
	public async Task SetUp()
	{
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, _tenantId);
		_adminClient = _factory.CreateClient();
		_adminClient.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		_adminClient.DefaultRequestHeaders.Add("X-Test-Roles", "admin");
		_adminClient.DefaultRequestHeaders.Add("X-Test-Subject", "admin-subject");
	}

	private HttpClient CreateNonAdminClient(string subject)
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "user");
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
	}

	/// <summary>
	/// No ClassPermission rows for this class → non-admin can create a schema.
	/// </summary>
	[Test]
	public async Task NonAdmin_CanCreateSchema_WhenClassHasNoPermissionRows()
	{
		const string subject = "teacher-no-class-perm";
		await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId, keycloakSubject: subject);
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "1.a");

		using var client = CreateNonAdminClient(subject);
		var response = await client.PostAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/schemas",
			new { name = "Nyt skema" });

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
	}

	/// <summary>
	/// Non-admin with an explicit ClassPermission row can create a schema on that class.
	/// </summary>
	[Test]
	public async Task NonAdmin_WithPermissionRow_CanCreateSchema()
	{
		const string subject = "teacher-with-perm";
		var staff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId, keycloakSubject: subject);
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "2.a");

		await _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/permissions",
			new { staffId = staff.Id });

		using var client = CreateNonAdminClient(subject);
		var response = await client.PostAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/schemas",
			new { name = "Mit skema" });

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
	}

	/// <summary>
	/// Non-admin without a permission row gets 403 on a class that has permission rows for others.
	/// </summary>
	[Test]
	public async Task NonAdmin_WithoutPermissionRow_Gets403_WhenClassIsRestricted()
	{
		const string subject = "teacher-no-perm";
		await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId, keycloakSubject: subject);

		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "3.a");

		// Lock class to a different staff member
		var otherStaff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);
		await _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/permissions",
			new { staffId = otherStaff.Id });

		using var client = CreateNonAdminClient(subject);
		var response = await client.PostAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/schemas",
			new { name = "Uautoriseret skema" });

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	/// <summary>
	/// Class A is restricted (other staff has perm), class B is unrestricted.
	/// Non-admin can edit B but not A — class-level isolation.
	/// </summary>
	[Test]
	public async Task NonAdmin_CanEditUnrestrictedClass_EvenWhenOtherClassIsRestricted()
	{
		const string subject = "teacher-mixed";
		await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId, keycloakSubject: subject);

		var (restrictedClass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "4.a");
		var (openClass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "4.b");

		// Lock restrictedClass to someone else — this does NOT affect openClass
		var otherStaff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);
		await _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{restrictedClass.Id}/permissions",
			new { staffId = otherStaff.Id });

		using var client = CreateNonAdminClient(subject);

		var forbiddenResponse = await client.PostAsJsonAsync(
			$"/api/v1/classes/{restrictedClass.Id}/schemas",
			new { name = "Forbudt" });
		await Assert.That(forbiddenResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

		var allowedResponse = await client.PostAsJsonAsync(
			$"/api/v1/classes/{openClass.Id}/schemas",
			new { name = "Tilladt" });
		await Assert.That(allowedResponse.StatusCode).IsEqualTo(HttpStatusCode.Created);
	}

	/// <summary>
	/// Non-admin can upsert slots on a class they have a permission row for.
	/// </summary>
	[Test]
	public async Task NonAdmin_WithPermissionRow_CanUpsertSlot()
	{
		const string subject = "teacher-slot-perm";
		var staff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId, keycloakSubject: subject);
		var (klass, schema) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "5.a");
		var course = await TestDataBuilder.CreateCourseAsync(_factory.Services, _tenantId);
		var teacher = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);
		var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(
			_factory.Services, _tenantId, new TimeOnly(8, 0), new TimeOnly(9, 0));

		await _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/permissions",
			new { staffId = staff.Id });

		using var client = CreateNonAdminClient(subject);
		var response = await client.PutAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/schemas/{schema.Id}/slots",
			new { timeSlotId = timeSlot.Id, weekday = 1, courseId = course.Id, teacherId = teacher.Id });

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}
}
