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
///   - Parent entry lifecycle: read own entries, edit without duplicating, delete, 409 after the deadline,
///     and closed or expired windows hidden from the parent's open list.
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
	private async Task<VacationRegistrationWindow> CreateWindowAsync(bool isOpen = true, int deadlineInDays = 30)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var window = new VacationRegistrationWindow
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			Title = "Sommerferie tilmelding",
			RegistrationDeadline = today.AddDays(deadlineInDays),
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

	// ── Parent entry lifecycle ────────────────────────────────────────────────────

	[Test]
	public async Task GetMyEntries_NoEntryYet_ListsChildWithEmptySelection()
	{
		const string subject = "parent-my-entries-empty";
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "MyEntriesEmptyClass");
		var student = await CreateStudentAsync(klass.Id, "Elev Uden Svar");
		await CreateParentAsync(subject, student.Id);
		var window = await CreateWindowAsync(isOpen: true);

		using var parentClient = CreateParentClient(subject);
		var entries = await GetMyEntriesAsync(parentClient, window.Id);

		await Assert.That(entries.Count).IsEqualTo(1);
		await Assert.That(entries[0].StudentId).IsEqualTo(student.Id);
		await Assert.That(entries[0].SelectedDates).IsEmpty();
		await Assert.That(entries[0].SubmittedAt).IsNull();
	}

	[Test]
	public async Task UpsertEntry_SecondSubmission_UpdatesExistingEntry()
	{
		const string subject = "parent-upsert-twice";
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "UpsertTwiceClass");
		var student = await CreateStudentAsync(klass.Id, "Elev Ændrer");
		await CreateParentAsync(subject, student.Id);
		var window = await CreateWindowAsync(isOpen: true);
		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var firstDate = today.AddDays(65).ToString("yyyy-MM-dd");
		var secondDates = new[] { today.AddDays(70).ToString("yyyy-MM-dd"), today.AddDays(71).ToString("yyyy-MM-dd") };

		using var parentClient = CreateParentClient(subject);
		var url = $"/api/v1/vacation-registration/{window.Id}/entries/{student.Id}";
		var first = await parentClient.PutAsJsonAsync(
			url, new VacationRegistrationController.UpsertEntryRequest([firstDate], "Første svar"), JsonOpts);
		var second = await parentClient.PutAsJsonAsync(
			url, new VacationRegistrationController.UpsertEntryRequest(secondDates, "Rettet svar"), JsonOpts);

		await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
		await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var mine = await GetMyEntriesAsync(parentClient, window.Id);
		await Assert.That(mine.Single().SelectedDates).IsEquivalentTo(secondDates);
		await Assert.That(mine.Single().Note).IsEqualTo("Rettet svar");

		// Hanne's overview must show the corrected answer once, not both submissions.
		var adminResponse = await _adminClient.GetAsync($"/api/v1/vacation-registration/{window.Id}/entries");
		var adminEntries = await adminResponse.Content.ReadFromJsonAsync<List<VacationRegistrationController.EntryDto>>(JsonOpts);
		var studentEntries = adminEntries!.Where(e => e.StudentId == student.Id).ToList();
		await Assert.That(studentEntries.Count).IsEqualTo(1);
		await Assert.That(studentEntries[0].SelectedDates).IsEquivalentTo(secondDates);
	}

	[Test]
	public async Task DeleteEntry_OpenWindow_RemovesEntry()
	{
		const string subject = "parent-delete-entry";
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "DeleteEntryClass");
		var student = await CreateStudentAsync(klass.Id, "Elev Fortryder");
		await CreateParentAsync(subject, student.Id);
		var window = await CreateWindowAsync(isOpen: true);
		var today = DateOnly.FromDateTime(DateTime.UtcNow);

		using var parentClient = CreateParentClient(subject);
		var url = $"/api/v1/vacation-registration/{window.Id}/entries/{student.Id}";
		await parentClient.PutAsJsonAsync(
			url, new VacationRegistrationController.UpsertEntryRequest([today.AddDays(65).ToString("yyyy-MM-dd")], null), JsonOpts);

		var delete = await parentClient.DeleteAsync(url);
		await Assert.That(delete.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var mine = await GetMyEntriesAsync(parentClient, window.Id);
		await Assert.That(mine.Single().SelectedDates).IsEmpty();
		await Assert.That(mine.Single().SubmittedAt).IsNull();

		var deleteAgain = await parentClient.DeleteAsync(url);
		await Assert.That(deleteAgain.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task DeleteEntry_WindowClosedAfterSubmission_Returns409()
	{
		const string subject = "parent-delete-closed";
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "DeleteClosedClass");
		var student = await CreateStudentAsync(klass.Id, "Elev Lukket Sletning");
		await CreateParentAsync(subject, student.Id);
		var window = await CreateWindowAsync(isOpen: true);
		var today = DateOnly.FromDateTime(DateTime.UtcNow);

		using var parentClient = CreateParentClient(subject);
		var url = $"/api/v1/vacation-registration/{window.Id}/entries/{student.Id}";
		await parentClient.PutAsJsonAsync(
			url, new VacationRegistrationController.UpsertEntryRequest([today.AddDays(65).ToString("yyyy-MM-dd")], null), JsonOpts);

		var close = await _adminClient.PutAsJsonAsync(
			$"/api/v1/vacation-registration/{window.Id}",
			new VacationRegistrationController.UpdateWindowRequest(
				window.Title, window.RegistrationDeadline, window.CareStartDate, window.CareEndDate,
				window.Granularity, IsOpen: false),
			JsonOpts);
		await Assert.That(close.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var delete = await parentClient.DeleteAsync(url);

		await Assert.That(delete.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
		var mine = await GetMyEntriesAsync(parentClient, window.Id);
		await Assert.That(mine.Single().SelectedDates.Length).IsEqualTo(1);
	}

	[Test]
	public async Task UpsertEntry_DeadlinePassed_Returns409()
	{
		const string subject = "parent-deadline-passed";
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "DeadlinePassedClass");
		var student = await CreateStudentAsync(klass.Id, "Elev For Sent");
		await CreateParentAsync(subject, student.Id);
		var expiredWindow = await CreateWindowAsync(isOpen: true, deadlineInDays: -1);
		var today = DateOnly.FromDateTime(DateTime.UtcNow);

		using var parentClient = CreateParentClient(subject);
		var response = await parentClient.PutAsJsonAsync(
			$"/api/v1/vacation-registration/{expiredWindow.Id}/entries/{student.Id}",
			new VacationRegistrationController.UpsertEntryRequest([today.AddDays(65).ToString("yyyy-MM-dd")], null),
			JsonOpts);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
	}

	[Test]
	public async Task GetOpenWindows_Parent_HidesClosedAndExpiredWindows()
	{
		var openWindow = await CreateWindowAsync(isOpen: true);
		var closedWindow = await CreateWindowAsync(isOpen: false);
		var expiredWindow = await CreateWindowAsync(isOpen: true, deadlineInDays: -1);
		var lastDayWindow = await CreateWindowAsync(isOpen: true, deadlineInDays: 0);
		using var parentClient = CreateParentClient("parent-open-windows-filter");

		var response = await parentClient.GetAsync("/api/v1/vacation-registration/open");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var ids = (await response.Content.ReadFromJsonAsync<List<VacationRegistrationController.WindowDto>>(JsonOpts))!
			.Select(w => w.Id)
			.ToList();
		await Assert.That(ids).Contains(openWindow.Id);
		await Assert.That(ids).Contains(lastDayWindow.Id);
		await Assert.That(ids).DoesNotContain(closedWindow.Id);
		await Assert.That(ids).DoesNotContain(expiredWindow.Id);
	}

	private static async Task<List<VacationRegistrationController.MyEntryDto>> GetMyEntriesAsync(HttpClient parentClient, Guid windowId)
	{
		var response = await parentClient.GetAsync($"/api/v1/vacation-registration/{windowId}/my-entries");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return (await response.Content.ReadFromJsonAsync<List<VacationRegistrationController.MyEntryDto>>(JsonOpts))!;
	}
}
