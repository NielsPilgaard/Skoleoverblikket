using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Verifies WeekPlan read access (staff: every class, parent: own children's classes, board: none)
/// and edit access rules:
///   1. Admin → always allowed.
///   2. No ClassPermissions for the class → open (all authenticated staff).
///   3. ClassPermissions exist and teacher has one → allowed.
///   4. ClassPermissions exist and teacher is assigned to the SchemaSlot → allowed.
///   5. ClassPermissions exist, teacher not assigned, no permission row → 403.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class WeekPlanPermissionsTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		Converters = { new JsonStringEnumConverter() },
		PropertyNameCaseInsensitive = true,
	};

	private readonly ApiFactory _factory = factory;
	private readonly Guid _tenantId = Guid.NewGuid();
	private HttpClient _adminClient = null!;

	private const int TestYear = 2025;
	private const int TestWeek = 20;

	[Before(Test)]
	public async Task SetUp()
	{
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, _tenantId);
		_adminClient = _factory.CreateClient();
		_adminClient.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		_adminClient.DefaultRequestHeaders.Add("X-Test-Roles", "admin");
		_adminClient.DefaultRequestHeaders.Add("X-Test-Subject", "admin-subject");
	}

	/// <summary>
	/// Staff read every class's WeekPlan, even a restricted class they have no permission row for.
	/// </summary>
	[Test]
	public async Task Staff_CanGetWeekPlan_OfRestrictedClass()
	{
		const string teacherSubject = "week-plan-reader-teacher";
		await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId, keycloakSubject: teacherSubject);
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId);
		var permittedOther = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);
		await _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/permissions",
			new { staffId = permittedOther.Id });

		using var client = CreateClient(_tenantId, "user", teacherSubject);

		var response = await client.GetAsync(WeekPlanUrl(klass.Id));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	/// <summary>
	/// A parent reads the WeekPlan of their child's class, and gets 403 for any other class.
	/// </summary>
	[Test]
	public async Task Parent_CanGetWeekPlan_OnlyOfOwnChildsClass()
	{
		var kit = new AbsenceTestKit(_factory);
		await kit.InitAsync();
		var (mine, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, kit.TenantId, "3.a");
		var (theirs, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, kit.TenantId, "3.b");
		var student = await kit.CreateStudentAsync(mine.Id);
		using var parent = await kit.ParentOfAsync(student.Id, "week-plan-parent");

		var own = await parent.GetAsync(WeekPlanUrl(mine.Id));
		var other = await parent.GetAsync(WeekPlanUrl(theirs.Id));

		await Assert.That(own.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(other.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	/// <summary>
	/// Board members have no week plan view, and a login with no school role reads nothing.
	/// </summary>
	[Test]
	public async Task BoardMemberAndNoRole_CannotGetWeekPlan()
	{
		const string boardSubject = "week-plan-board";
		await TestDataBuilder.CreateBoardMemberAsync(_factory.Services, _tenantId, boardSubject);
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId);

		using var board = CreateClient(_tenantId, "board", boardSubject);
		using var noRole = CreateClient(_tenantId, "user", "week-plan-no-school-role");

		var boardResponse = await board.GetAsync(WeekPlanUrl(klass.Id));
		var noRoleResponse = await noRole.GetAsync(WeekPlanUrl(klass.Id));

		await Assert.That(boardResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That(noRoleResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	private HttpClient CreateClient(Guid tenantId, string roles, string subject)
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", roles);
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
	}

	private static string WeekPlanUrl(Guid classId) =>
		$"/api/v1/classes/{classId}/week-plan?isoYear={TestYear}&isoWeek={TestWeek}";

	/// <summary>
	/// When no ClassPermissions exist for a class, any authenticated teacher can edit the WeekPlan.
	/// </summary>
	[Test]
	public async Task NoClassPermissions_AnyTeacher_CanUpsertWeekPlanSlot()
	{
		const string teacherSubject = "unassigned-no-permissions-teacher";
		await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId, keycloakSubject: teacherSubject);

		var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(
			_factory.Services, _tenantId, new TimeOnly(9, 0), new TimeOnly(9, 45));
		var course = await TestDataBuilder.CreateCourseAsync(_factory.Services, _tenantId);
		var assignedTeacher = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);
		var (klass, schema) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "5.a");

		var schemaSlot = await TestDataBuilder.CreateSchemaSlotAsync(
			_factory.Services, _tenantId,
			schema.Id, timeSlot.Id, course.Id, assignedTeacher.Id, DayOfWeek.Tuesday);

		// No ClassPermission rows → open class
		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "user");
		client.DefaultRequestHeaders.Add("X-Test-Subject", teacherSubject);

		var response = await client.PutAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/week-plan/slots?isoYear={TestYear}&isoWeek={TestWeek}",
			new WeekPlanController.UpsertWeekPlanSlotRequest(schemaSlot.Id, "Lektier: side 12-15", null, null));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	/// <summary>
	/// Teacher assigned to a SchemaSlot can edit the WeekPlan entry for that slot,
	/// even when ClassPermissions are active that would block a generic teacher.
	/// </summary>
	[Test]
	public async Task AssignedTeacher_CanUpsertWeekPlanSlot_EvenWhenClassPermissionsExist()
	{
		const string teacherSubject = "assigned-teacher-subject";
		var teacher = await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId, keycloakSubject: teacherSubject);

		var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(
			_factory.Services, _tenantId, new TimeOnly(8, 0), new TimeOnly(8, 45));
		var course = await TestDataBuilder.CreateCourseAsync(_factory.Services, _tenantId);
		var (klass, schema) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId);

		var schemaSlot = await TestDataBuilder.CreateSchemaSlotAsync(
			_factory.Services, _tenantId,
			schema.Id, timeSlot.Id, course.Id, teacher.Id, DayOfWeek.Monday);

		// Activate restricted mode by granting a different admin permission on this class
		var otherAdmin = await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId, isAdmin: true);
		await _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/permissions",
			new { staffId = otherAdmin.Id });

		using var teacherClient = _factory.CreateClient();
		teacherClient.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		teacherClient.DefaultRequestHeaders.Add("X-Test-Roles", "user");
		teacherClient.DefaultRequestHeaders.Add("X-Test-Subject", teacherSubject);

		var response = await teacherClient.PutAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/week-plan/slots?isoYear={TestYear}&isoWeek={TestWeek}",
			new WeekPlanController.UpsertWeekPlanSlotRequest(schemaSlot.Id, "Vi læser kapitel 5", null, null));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var slot = await response.Content.ReadFromJsonAsync<WeekPlanController.WeekPlanSlotDto>(JsonOpts);
		await Assert.That(slot!.Description).IsEqualTo("Vi læser kapitel 5");
	}

	/// <summary>
	/// Teacher with an explicit ClassPermission row can edit the WeekPlan.
	/// </summary>
	[Test]
	public async Task TeacherWithClassPermission_CanUpsertWeekPlanSlot()
	{
		const string teacherSubject = "permitted-teacher-subject";
		var teacher = await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId, keycloakSubject: teacherSubject);

		var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(
			_factory.Services, _tenantId, new TimeOnly(10, 0), new TimeOnly(10, 45));
		var course = await TestDataBuilder.CreateCourseAsync(_factory.Services, _tenantId);
		var otherTeacher = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);
		var (klass, schema) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "3.c");

		var schemaSlot = await TestDataBuilder.CreateSchemaSlotAsync(
			_factory.Services, _tenantId,
			schema.Id, timeSlot.Id, course.Id, otherTeacher.Id, DayOfWeek.Wednesday);

		// Grant permission to the teacher being tested
		await _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/permissions",
			new { staffId = teacher.Id });

		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "user");
		client.DefaultRequestHeaders.Add("X-Test-Subject", teacherSubject);

		var response = await client.PutAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/week-plan/slots?isoYear={TestYear}&isoWeek={TestWeek}",
			new WeekPlanController.UpsertWeekPlanSlotRequest(schemaSlot.Id, "Se film i dag", null, null));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	/// <summary>
	/// When ClassPermissions are active, a teacher with no permission row and not
	/// assigned to the SchemaSlot is denied (403).
	/// </summary>
	[Test]
	public async Task UnassignedTeacher_WithClassPermissionsActive_IsForbidden()
	{
		const string teacherSubject = "unassigned-blocked-teacher";
		await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId, keycloakSubject: teacherSubject);

		var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(
			_factory.Services, _tenantId, new TimeOnly(11, 0), new TimeOnly(11, 45));
		var course = await TestDataBuilder.CreateCourseAsync(_factory.Services, _tenantId);
		var assignedTeacher = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);
		var (klass, schema) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "4.b");

		var schemaSlot = await TestDataBuilder.CreateSchemaSlotAsync(
			_factory.Services, _tenantId,
			schema.Id, timeSlot.Id, course.Id, assignedTeacher.Id, DayOfWeek.Thursday);

		// Grant permission to a different staff member → activates restricted mode
		var permittedOther = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);
		await _adminClient.PostAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/permissions",
			new { staffId = permittedOther.Id });

		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "user");
		client.DefaultRequestHeaders.Add("X-Test-Subject", teacherSubject);

		var response = await client.PutAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/week-plan/slots?isoYear={TestYear}&isoWeek={TestWeek}",
			new WeekPlanController.UpsertWeekPlanSlotRequest(schemaSlot.Id, "Vil aldrig blive gemt", null, null));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}
}
