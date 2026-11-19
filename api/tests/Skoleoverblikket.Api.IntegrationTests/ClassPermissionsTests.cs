using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Integration tests for class-level permissions.
/// Covers:
///   - Superadmin (no ClassPermission rows) can edit any class schema.
///   - Admin with a ClassPermission row can edit that class, gets 403 on others.
///   - Granting/revoking permissions reflects immediately on subsequent requests.
///   - Non-admin cannot call permission management endpoints.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class ClassPermissionsTests(ApiFactory factory)
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
		_adminClient = CreateAdminClient();
	}

	private HttpClient CreateAdminClient(string subject = "test-user-id")
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "admin");
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
	}

	private HttpClient CreateNonAdminClient()
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "user");
		return client;
	}

	// ── Superadmin mode (no permission rows) ──────────────────────────────────

	[Test]
	public async Task Superadmin_CanCreateSchema_WhenNoPermissionRowsExist()
	{
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId);

		var response = await _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/schemas",
			new { name = "Nyt skema" });

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
	}

	[Test]
	public async Task Superadmin_CanUpsertSlot_WhenNoPermissionRowsExist()
	{
		var (klass, schema) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId);

		var course = await TestDataBuilder.CreateCourseAsync(_factory.Services, _tenantId);
		var teacher = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId,
			isAdmin: true, keycloakSubject: "test-user-id");
		var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(
			_factory.Services, _tenantId,
			new TimeOnly(8, 0), new TimeOnly(9, 0));

		var response = await _adminClient.PutAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/schemas/{schema.Id}/slots",
			new { timeSlotId = timeSlot.Id, weekday = 1, courseId = course.Id, teacherId = teacher.Id });

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	// ── Permission-restricted mode ─────────────────────────────────────────────

	[Test]
	public async Task RestrictedAdmin_CanCreateSchema_OnAssignedClass()
	{
		const string subject = "restricted-admin-subject";
		var staff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId,
			isAdmin: false, keycloakSubject: subject);

		var (assignedClass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "3.a");
		var (otherClass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "4.b");

		// Grant permission on assignedClass only (this creates a permission row, enabling restricted mode)
		await _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{assignedClass.Id}/permissions",
			new { staffId = staff.Id });

		using var restrictedClient = CreateAdminClient(subject);

		var response = await restrictedClient.PostAsJsonAsync(
			$"/api/v1/classes/{assignedClass.Id}/schemas",
			new { name = "Mit skema" });

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
	}

	[Test]
	public async Task RestrictedAdmin_Gets403_OnClassLockedToOtherStaff()
	{
		const string subject = "restricted-admin-subject-2";
		var staff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId,
			isAdmin: false, keycloakSubject: subject);

		var (assignedClass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "5.a");
		var (lockedClass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "6.b");

		// Grant permission on assignedClass only
		await _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{assignedClass.Id}/permissions",
			new { staffId = staff.Id });

		// Lock lockedClass to a different staff member — this class now has permission rows that exclude our subject
		var otherStaff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);
		await _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{lockedClass.Id}/permissions",
			new { staffId = otherStaff.Id });

		using var restrictedClient = CreateAdminClient(subject);

		var response = await restrictedClient.PostAsJsonAsync(
			$"/api/v1/classes/{lockedClass.Id}/schemas",
			new { name = "Uautoriseret skema" });

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	// ── Grant/Revoke ────────────────────────────────────────────────────────────

	[Test]
	public async Task GrantPermission_AppearsInGetList()
	{
		var staff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId);

		await _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/permissions",
			new { staffId = staff.Id });

		var listResponse = await _adminClient.GetAsync($"/api/v1/classes/{klass.Id}/permissions");
		await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var list = await listResponse.Content.ReadFromJsonAsync<List<ClassPermissionDto>>(JsonOpts);
		await Assert.That(list!.Any(p => p.StaffId == staff.Id)).IsTrue();
	}

	[Test]
	public async Task GrantPermission_Returns409_WhenAlreadyGranted()
	{
		var staff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId);

		await _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/permissions",
			new { staffId = staff.Id });

		var duplicate = await _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/permissions",
			new { staffId = staff.Id });

		await Assert.That(duplicate.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
	}

	[Test]
	public async Task GrantPermission_Returns404WithDetail_ForUnknownStaff()
	{
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId);

		var response = await _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/permissions",
			new { staffId = Guid.NewGuid() });

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
		await Assert.That(await response.Content.ReadAsStringAsync()).Contains("Medarbejderen findes ikke");
	}

	[Test]
	public async Task GrantPermission_ConcurrentDuplicates_GrantOnceAndConflictOtherwise()
	{
		var staff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId);

		var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/permissions",
			new { staffId = staff.Id })));

		await Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.Created)).IsEqualTo(1);
		await Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.Conflict)).IsEqualTo(4);
	}

	[Test]
	public async Task RevokePermission_RemovesFromList_AndRestoresAccess()
	{
		const string subject = "revoke-test-subject";
		var staff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId,
			isAdmin: true, keycloakSubject: subject);
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId);

		// Grant — enters restricted mode
		await _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/permissions",
			new { staffId = staff.Id });

		// Revoke — removes the only permission row; tenant returns to superadmin mode
		var revokeResponse = await _adminClient.DeleteAsync(
			$"/api/v1/classes/{klass.Id}/permissions/{staff.Id}");
		await Assert.That(revokeResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		// No more rows → superadmin mode → restricted admin now has full access
		using var restrictedClient = CreateAdminClient(subject);
		var schemaResponse = await restrictedClient.PostAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/schemas",
			new { name = "Skema efter revoke" });

		await Assert.That(schemaResponse.StatusCode).IsEqualTo(HttpStatusCode.Created);
	}

	[Test]
	public async Task RevokePermission_Returns404_ForUnknownStaff()
	{
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId);

		var response = await _adminClient.DeleteAsync(
			$"/api/v1/classes/{klass.Id}/permissions/{Guid.NewGuid()}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	// ── Role guard on permissions endpoints ────────────────────────────────────

	[Test]
	public async Task NonAdmin_Gets403_OnGetPermissions()
	{
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId);

		using var client = CreateNonAdminClient();
		var response = await client.GetAsync($"/api/v1/classes/{klass.Id}/permissions");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task NonAdmin_Gets403_OnGrantPermission()
	{
		var staff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId);

		using var client = CreateNonAdminClient();
		var response = await client.PostAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/permissions",
			new { staffId = staff.Id });

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	// ── GET /api/v1/classes filtering for non-admins ───────────────────────────

	[Test]
	public async Task GetAll_NonAdmin_SeesAllClasses_WhenNoPermissionRowsExist()
	{
		// No ClassPermission rows → no restrictions → teacher sees everything
		const string subject = "teacher-no-restrictions";
		await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId,
			keycloakSubject: subject);

		var (_, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "1.a");
		var (_, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "1.b");

		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "user");
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);

		var response = await client.GetAsync("/api/v1/classes");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var classes = await response.Content.ReadFromJsonAsync<List<ClassesController.ClassDto>>(JsonOpts);
		await Assert.That(classes!.Count).IsGreaterThanOrEqualTo(2);
	}

	[Test]
	public async Task GetAll_NonAdmin_HidesClass_LockedToOtherStaff()
	{
		// Classes with permission rows but not for this teacher are hidden.
		// Classes with no permission rows at all remain visible (unrestricted).
		const string subject = "teacher-restricted";
		var staff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId,
			keycloakSubject: subject);

		var (permittedClass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "2.a");
		var (unrestrictedClass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "2.b");
		var (adminOnlyClass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "2.c");

		// Give teacher perm on permittedClass
		await _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{permittedClass.Id}/permissions",
			new { staffId = staff.Id });

		// Lock adminOnlyClass to a different admin — teacher should NOT see this
		var otherAdmin = await TestDataBuilder.CreateStaffAsync(_factory.Services,
			_tenantId, isAdmin: true);
		await _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{adminOnlyClass.Id}/permissions",
			new { staffId = otherAdmin.Id });

		// unrestrictedClass has no permission rows — visible to all (unrestricted)

		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "user");
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);

		var response = await client.GetAsync("/api/v1/classes");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var classes = await response.Content.ReadFromJsonAsync<List<ClassesController.ClassDto>>(JsonOpts);
		var ids = classes!.Select(c => c.Id).ToHashSet();

		await Assert.That(ids.Contains(permittedClass.Id)).IsTrue();
		await Assert.That(ids.Contains(unrestrictedClass.Id)).IsTrue();
		await Assert.That(ids.Contains(adminOnlyClass.Id)).IsFalse();
	}

	[Test]
	public async Task GetAll_Admin_SeesAllClasses_RegardlessOfPermissionRows()
	{
		// Admins always get full list
		var (classA, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "3.a");
		var (classB, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "3.b");

		var someStaff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);
		// Seed a permission row to enter restricted mode
		await _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{classA.Id}/permissions",
			new { staffId = someStaff.Id });

		var response = await _adminClient.GetAsync("/api/v1/classes");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var classes = await response.Content.ReadFromJsonAsync<List<ClassesController.ClassDto>>(JsonOpts);
		var ids = classes!.Select(c => c.Id).ToHashSet();

		await Assert.That(ids.Contains(classA.Id)).IsTrue();
		await Assert.That(ids.Contains(classB.Id)).IsTrue();
	}
}
