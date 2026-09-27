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
/// Integration tests for VacationRegistrationController.
/// Covers:
///   - Admin window CRUD: create, list, update, delete, 403 for non-admin, list entries.
///   - Parent operations: list open windows, upsert own student, 403 for other student, 409 for closed window.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class VacationRegistrationTests(ApiFactory factory)
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
		_adminClient.DefaultRequestHeaders.Add("X-Test-Subject", "vacation-admin-subject");
	}

	[After(Test)]
	public void TearDown()
	{
		_adminClient.Dispose();
	}

	// ── Private helpers ──────────────────────────────────────────────────────────

	private HttpClient CreateParentClient(string subject)
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "parent");
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
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
	/// Creates a Student in the DB, requires an existing Class.
	/// </summary>
	private async Task<Student> CreateStudentAsync(Guid classId, string name = "Elev Testsen")
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

	/// <summary>
	/// Creates a Parent linked to the given Student in the DB.
	/// </summary>
	private async Task<Parent> CreateParentAsync(string keycloakSubject, Guid studentId, string name = "Forælder Testsen")
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

	/// <summary>
	/// Creates a VacationRegistrationWindow directly in the DB for use as test prerequisite.
	/// </summary>
	private async Task<VacationRegistrationWindow> CreateWindowAsync(bool isOpen = true)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var window = new VacationRegistrationWindow
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Title = "Sommerferie tilmelding",
			RegistrationDeadline = today.AddDays(30),
			CareStartDate = today.AddDays(60),
			CareEndDate = today.AddDays(90),
			Granularity = VacationRegistrationGranularity.Weeks,
			IsOpen = isOpen,
		};

		db.VacationRegistrationWindows.Add(window);
		await db.SaveChangesAsync();
		return window;
	}

	// ── Admin window CRUD ─────────────────────────────────────────────────────────

	[Test]
	public async Task CreateWindow_Admin_Returns201()
	{
		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var request = new VacationRegistrationController.CreateWindowRequest(
			Title: "Vinterferietilmelding",
			RegistrationDeadline: today.AddDays(14),
			CareStartDate: today.AddDays(30),
			CareEndDate: today.AddDays(44),
			Granularity: VacationRegistrationGranularity.Days,
			IsOpen: false);

		var response = await _adminClient.PostAsJsonAsync("/api/v1/vacation-registration", request, JsonOpts);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
	}

	[Test]
	public async Task GetWindows_Admin_ReturnsList()
	{
		var window = await CreateWindowAsync();

		var response = await _adminClient.GetAsync("/api/v1/vacation-registration");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var list = await response.Content.ReadFromJsonAsync<List<VacationRegistrationController.WindowDto>>(JsonOpts);
		await Assert.That(list).IsNotNull();
		await Assert.That(list!.Any(w => w.Id == window.Id)).IsTrue();
	}

	[Test]
	public async Task UpdateWindow_Admin_Returns204()
	{
		var window = await CreateWindowAsync(isOpen: false);
		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var updateRequest = new VacationRegistrationController.UpdateWindowRequest(
			Title: "Opdateret titel",
			RegistrationDeadline: today.AddDays(20),
			CareStartDate: today.AddDays(50),
			CareEndDate: today.AddDays(70),
			Granularity: VacationRegistrationGranularity.Days,
			IsOpen: false);

		var response = await _adminClient.PutAsJsonAsync($"/api/v1/vacation-registration/{window.Id}", updateRequest, JsonOpts);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}

	[Test]
	public async Task DeleteWindow_Admin_Returns204()
	{
		var window = await CreateWindowAsync(isOpen: false);

		var response = await _adminClient.DeleteAsync($"/api/v1/vacation-registration/{window.Id}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}

	[Test]
	public async Task CreateWindow_NonAdmin_Returns403()
	{
		const string subject = "nonadmin-create-window";
		using var nonAdminClient = CreateNonAdminClient(subject);

		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var request = new VacationRegistrationController.CreateWindowRequest(
			Title: "Ulovlig oprettelse",
			RegistrationDeadline: today.AddDays(10),
			CareStartDate: today.AddDays(20),
			CareEndDate: today.AddDays(30),
			Granularity: VacationRegistrationGranularity.Weeks,
			IsOpen: false);

		var response = await nonAdminClient.PostAsJsonAsync("/api/v1/vacation-registration", request, JsonOpts);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task GetEntries_Admin_ReturnsList()
	{
		var window = await CreateWindowAsync();

		var response = await _adminClient.GetAsync($"/api/v1/vacation-registration/{window.Id}/entries");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var entries = await response.Content.ReadFromJsonAsync<List<VacationRegistrationController.EntryDto>>(JsonOpts);
		await Assert.That(entries).IsNotNull();
	}

	// ── Parent operations ─────────────────────────────────────────────────────────

	[Test]
	public async Task GetOpenWindows_Parent_ReturnsOpenWindows()
	{
		var openWindow = await CreateWindowAsync(isOpen: true);
		const string subject = "parent-open-windows";
		using var parentClient = CreateParentClient(subject);

		var response = await parentClient.GetAsync("/api/v1/vacation-registration/open");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var list = await response.Content.ReadFromJsonAsync<List<VacationRegistrationController.WindowDto>>(JsonOpts);
		await Assert.That(list).IsNotNull();
		await Assert.That(list!.Any(w => w.Id == openWindow.Id)).IsTrue();
	}

	[Test]
	public async Task UpsertEntry_Parent_OwnStudent_Returns204()
	{
		const string subject = "parent-upsert-own-student";
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "UpsertOwnClass");
		var student = await CreateStudentAsync(klass.Id, "Mikkel Upsert");
		await CreateParentAsync(subject, student.Id, "Forælder Upsert");

		var window = await CreateWindowAsync(isOpen: true);
		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var req = new VacationRegistrationController.UpsertEntryRequest(
			SelectedDates: [today.AddDays(65).ToString("yyyy-MM-dd")],
			Note: "Ingen særlige ønsker");

		using var parentClient = CreateParentClient(subject);
		var response = await parentClient.PutAsJsonAsync(
			$"/api/v1/vacation-registration/{window.Id}/entries/{student.Id}", req, JsonOpts);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}

	[Test]
	public async Task UpsertEntry_Parent_OtherParentsStudent_Returns403()
	{
		const string ownerSubject = "parent-owner-student";
		const string otherSubject = "parent-other-student";

		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "OtherParentClass");
		var student = await CreateStudentAsync(klass.Id, "Student Andenforældres");
		await CreateParentAsync(ownerSubject, student.Id, "Ejer Forælder");

		// otherParent has no students linked — their student link is to a different student
		var (klass2, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "OtherParentClass2");
		var otherStudent = await CreateStudentAsync(klass2.Id, "Anden Elev");
		await CreateParentAsync(otherSubject, otherStudent.Id, "Anden Forælder");

		var window = await CreateWindowAsync(isOpen: true);
		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var req = new VacationRegistrationController.UpsertEntryRequest(
			SelectedDates: [today.AddDays(65).ToString("yyyy-MM-dd")],
			Note: null);

		// otherSubject tries to upsert an entry for ownerSubject's student
		using var otherParentClient = CreateParentClient(otherSubject);
		var response = await otherParentClient.PutAsJsonAsync(
			$"/api/v1/vacation-registration/{window.Id}/entries/{student.Id}", req, JsonOpts);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task UpsertEntry_ClosedWindow_Returns409()
	{
		const string subject = "parent-closed-window";
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "ClosedWindowClass");
		var student = await CreateStudentAsync(klass.Id, "Elev Lukket");
		await CreateParentAsync(subject, student.Id, "Forælder Lukket");

		var closedWindow = await CreateWindowAsync(isOpen: false);
		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var req = new VacationRegistrationController.UpsertEntryRequest(
			SelectedDates: [today.AddDays(65).ToString("yyyy-MM-dd")],
			Note: null);

		using var parentClient = CreateParentClient(subject);
		var response = await parentClient.PutAsJsonAsync(
			$"/api/v1/vacation-registration/{closedWindow.Id}/entries/{student.Id}", req, JsonOpts);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
	}
}
