using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Integration tests for AbsenceController.
/// Covers:
///   - Confirm/dismiss: admin staff, non-admin + open class, non-admin + permission, no permission, no staff row, not found.
///   - GET /: admin list with optional class filter.
///   - DELETE: parent cancels own Reported absence; cannot cancel already-confirmed.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class AbsenceTests(ApiFactory factory)
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
		_adminClient.DefaultRequestHeaders.Add("X-Test-Roles", "admin");
		_adminClient.DefaultRequestHeaders.Add("X-Test-Subject", "admin-subject");
	}

	[After(Test)]
	public void TearDown()
	{
		_adminClient.Dispose();
	}

	// ── Private helpers ──────────────────────────────────────────────────────────

	private HttpClient CreateStaffClient(string subject, bool isAdmin = false)
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", isAdmin ? "admin" : "user");
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
	}

	private HttpClient CreateParentClient(string subject)
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "parent");
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
	}

	private async Task<Student> CreateStudentAsync(Guid classId, string name = "Mikkel Testsen")
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var student = new Student
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Name = name,
			ClassId = classId,
		};
		db.Students.Add(student);
		await db.SaveChangesAsync();
		return student;
	}

	private async Task<Parent> CreateParentAsync(string keycloakSubject, Guid studentId, string name = "Dorte Testsen")
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		var parent = new Parent
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Name = name,
			Email = $"{keycloakSubject}@test.dk",
			KeycloakSubject = keycloakSubject,
		};

		var studentRef = await db.Students.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == studentId);
		if (studentRef is not null)
		{
			parent.Students.Add(studentRef);
		}

		db.Parents.Add(parent);
		await db.SaveChangesAsync();
		return parent;
	}

	private async Task<AbsenceReport> CreateAbsenceReportAsync(
		Guid studentId, Guid parentId, AbsenceStatus status = AbsenceStatus.Reported,
		DateOnly? date = null)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var report = new AbsenceReport
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			StudentId = studentId,
			ReportedByParentId = parentId,
			Date = date ?? DateOnly.FromDateTime(DateTime.UtcNow),
			Status = status,
		};
		db.AbsenceReports.Add(report);
		await db.SaveChangesAsync();
		return report;
	}

	private async Task<ClassPermission> CreateClassPermissionAsync(Guid classId, Guid staffId)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var perm = new ClassPermission
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			ClassId = classId,
			StaffId = staffId,
		};
		db.ClassPermissions.Add(perm);
		await db.SaveChangesAsync();
		return perm;
	}

	// ── POST /{id}/confirm ────────────────────────────────────────────────────────

	[Test]
	public async Task ConfirmAbsence_AdminStaff_Returns204()
	{
		const string subject = "confirm-admin-staff";
		await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId,
			isAdmin: true, keycloakSubject: subject);
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "1.a");
		var student = await CreateStudentAsync(klass.Id);
		var parent = await CreateParentAsync("confirm-admin-parent", student.Id);
		var report = await CreateAbsenceReportAsync(student.Id, parent.Id);

		using var client = CreateStaffClient(subject, isAdmin: true);
		var response = await client.PostAsync($"/api/v1/absence/{report.Id}/confirm", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}

	[Test]
	public async Task ConfirmAbsence_NonAdminStaff_OpenClass_Returns204()
	{
		// Non-admin staff + class has no ClassPermission rows → open access → 204
		const string subject = "confirm-nonadmin-open";
		await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId,
			isAdmin: false, keycloakSubject: subject);
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "2.a");
		var student = await CreateStudentAsync(klass.Id);
		var parent = await CreateParentAsync("confirm-open-parent", student.Id);
		var report = await CreateAbsenceReportAsync(student.Id, parent.Id);

		using var client = CreateStaffClient(subject);
		var response = await client.PostAsync($"/api/v1/absence/{report.Id}/confirm", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}

	[Test]
	public async Task ConfirmAbsence_NonAdminStaff_WithPermission_Returns204()
	{
		// Non-admin staff + class has permissions + staff has one → 204
		const string subject = "confirm-nonadmin-with-perm";
		var staff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId,
			isAdmin: false, keycloakSubject: subject);
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "3.a");
		await CreateClassPermissionAsync(klass.Id, staff.Id);
		var student = await CreateStudentAsync(klass.Id);
		var parent = await CreateParentAsync("confirm-perm-parent", student.Id);
		var report = await CreateAbsenceReportAsync(student.Id, parent.Id);

		using var client = CreateStaffClient(subject);
		var response = await client.PostAsync($"/api/v1/absence/{report.Id}/confirm", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}

	[Test]
	public async Task ConfirmAbsence_NonAdminStaff_WithoutPermission_Returns403()
	{
		// Class has permission rows, but not for this staff member → 403
		const string subject = "confirm-nonadmin-no-perm";
		await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId,
			isAdmin: false, keycloakSubject: subject);
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "4.a");

		// Lock class to a different staff member to enable restricted mode
		var otherStaff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);
		await CreateClassPermissionAsync(klass.Id, otherStaff.Id);

		var student = await CreateStudentAsync(klass.Id);
		var parent = await CreateParentAsync("confirm-no-perm-parent", student.Id);
		var report = await CreateAbsenceReportAsync(student.Id, parent.Id);

		using var client = CreateStaffClient(subject);
		var response = await client.PostAsync($"/api/v1/absence/{report.Id}/confirm", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task ConfirmAbsence_NoStaffRecord_Returns403()
	{
		// Authenticated user whose sub has no Staff row in DB → 403
		const string subject = "confirm-no-staff-record";
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "5.a");
		var student = await CreateStudentAsync(klass.Id);
		var parent = await CreateParentAsync("confirm-no-staff-parent", student.Id);
		var report = await CreateAbsenceReportAsync(student.Id, parent.Id);

		// No Staff row created for subject "confirm-no-staff-record"
		using var client = CreateStaffClient(subject);
		var response = await client.PostAsync($"/api/v1/absence/{report.Id}/confirm", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task ConfirmAbsence_NotFound_Returns404()
	{
		const string subject = "confirm-notfound-staff";
		await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId,
			isAdmin: true, keycloakSubject: subject);

		using var client = CreateStaffClient(subject, isAdmin: true);
		var response = await client.PostAsync($"/api/v1/absence/{Guid.NewGuid()}/confirm", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	// ── POST /{id}/dismiss ────────────────────────────────────────────────────────

	[Test]
	public async Task DismissAbsence_AdminStaff_Returns204()
	{
		const string subject = "dismiss-admin-staff";
		await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId,
			isAdmin: true, keycloakSubject: subject);
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "6.a");
		var student = await CreateStudentAsync(klass.Id);
		var parent = await CreateParentAsync("dismiss-admin-parent", student.Id);
		var report = await CreateAbsenceReportAsync(student.Id, parent.Id);

		using var client = CreateStaffClient(subject, isAdmin: true);
		var response = await client.PostAsync($"/api/v1/absence/{report.Id}/dismiss", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}

	[Test]
	public async Task DismissAbsence_NonAdminStaff_WithoutPermission_Returns403()
	{
		// Class has permission rows, but not for this staff member → 403
		const string subject = "dismiss-nonadmin-no-perm";
		await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId,
			isAdmin: false, keycloakSubject: subject);
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "7.a");

		var otherStaff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);
		await CreateClassPermissionAsync(klass.Id, otherStaff.Id);

		var student = await CreateStudentAsync(klass.Id);
		var parent = await CreateParentAsync("dismiss-no-perm-parent", student.Id);
		var report = await CreateAbsenceReportAsync(student.Id, parent.Id);

		using var client = CreateStaffClient(subject);
		var response = await client.PostAsync($"/api/v1/absence/{report.Id}/dismiss", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	// ── GET / ────────────────────────────────────────────────────────────────────

	[Test]
	public async Task GetAbsences_Admin_Returns200()
	{
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "8.a");
		var student = await CreateStudentAsync(klass.Id);
		var parent = await CreateParentAsync("get-absences-parent", student.Id);
		await CreateAbsenceReportAsync(student.Id, parent.Id);

		var response = await _adminClient.GetAsync("/api/v1/absence");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var list = await response.Content.ReadFromJsonAsync<List<AbsenceController.AbsenceReportDto>>(JsonOpts);
		await Assert.That(list).IsNotNull();
		await Assert.That(list!.Count).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	public async Task GetAbsences_FilterByClass_ReturnsOnlyMatchingStudents()
	{
		var (targetClass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "9.a");
		var (otherClass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "9.b");

		var targetStudent = await CreateStudentAsync(targetClass.Id, "Targeted Elev");
		var otherStudent = await CreateStudentAsync(otherClass.Id, "Other Elev");

		var targetParent = await CreateParentAsync("filter-target-parent", targetStudent.Id);
		var otherParent = await CreateParentAsync("filter-other-parent", otherStudent.Id);

		var targetReport = await CreateAbsenceReportAsync(targetStudent.Id, targetParent.Id);
		await CreateAbsenceReportAsync(otherStudent.Id, otherParent.Id);

		var response = await _adminClient.GetAsync($"/api/v1/absence?classId={targetClass.Id}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var list = await response.Content.ReadFromJsonAsync<List<AbsenceController.AbsenceReportDto>>(JsonOpts);
		await Assert.That(list).IsNotNull();
		await Assert.That(list!.All(r => r.StudentId == targetStudent.Id)).IsTrue();
		await Assert.That(list.Any(r => r.Id == targetReport.Id)).IsTrue();
	}

	// ── DELETE /{id} ─────────────────────────────────────────────────────────────

	[Test]
	public async Task CancelAbsence_OwnerInReportedStatus_Returns204()
	{
		const string parentSubject = "cancel-owner-parent";
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "10.a");
		var student = await CreateStudentAsync(klass.Id);
		var parent = await CreateParentAsync(parentSubject, student.Id);
		var report = await CreateAbsenceReportAsync(student.Id, parent.Id, AbsenceStatus.Reported);

		using var client = CreateParentClient(parentSubject);
		var response = await client.DeleteAsync($"/api/v1/absence/{report.Id}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}

	[Test]
	public async Task CancelAbsence_AlreadyConfirmed_Returns400()
	{
		const string parentSubject = "cancel-confirmed-parent";
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "11.a");
		var student = await CreateStudentAsync(klass.Id);
		var parent = await CreateParentAsync(parentSubject, student.Id);
		var report = await CreateAbsenceReportAsync(student.Id, parent.Id, AbsenceStatus.Confirmed);

		using var client = CreateParentClient(parentSubject);
		var response = await client.DeleteAsync($"/api/v1/absence/{report.Id}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}
}
