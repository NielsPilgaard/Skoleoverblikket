using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Full CRUD lifecycle tests for /api/v1/courses.
/// Covers standard CRUD, admin-only authorization, 404 handling, name ordering,
/// and cross-tenant isolation via the EF Core global query filter.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class CoursesCrudTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		Converters = { new JsonStringEnumConverter() },
		PropertyNameCaseInsensitive = true,
	};

	private readonly ApiFactory _factory = factory;
	private readonly Guid _tenantId = Guid.NewGuid();
	private HttpClient _client = null!;

	[Before(Test)]
	public async Task SetUp()
	{
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, _tenantId);
		_client = _factory.CreateClient();
		_client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		_client.DefaultRequestHeaders.Add("X-Test-Roles", "admin");
	}

	// -------------------------------------------------------------------------
	// GET /api/v1/courses
	// -------------------------------------------------------------------------

	[Test]
	public async Task GetAll_ReturnsEmptyList_WhenNoCourses()
	{
		var response = await _client.GetAsync("/api/v1/courses");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var courses = await response.Content.ReadFromJsonAsync<List<CoursesController.CourseDto>>(JsonOpts);
		await Assert.That(courses).IsNotNull();
		await Assert.That(courses!.Count).IsEqualTo(0);
	}

	[Test]
	public async Task Create_ThenGetAll_ReturnsCourse()
	{
		var request = new CoursesController.UpsertCourseRequest("Dansk", null, null, null);

		var createResponse = await _client.PostAsJsonAsync("/api/v1/courses", request);
		createResponse.EnsureSuccessStatusCode();

		var listResponse = await _client.GetAsync("/api/v1/courses");
		await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var courses = await listResponse.Content.ReadFromJsonAsync<List<CoursesController.CourseDto>>(JsonOpts);
		await Assert.That(courses).IsNotNull();
		await Assert.That(courses!.Count).IsEqualTo(1);
		await Assert.That(courses[0].Name).IsEqualTo("Dansk");
	}

	[Test]
	public async Task GetAll_OrderedByName()
	{
		// Create in reverse alphabetical order
		await _client.PostAsJsonAsync("/api/v1/courses",
			new CoursesController.UpsertCourseRequest("Matematik", null, null, null));
		await _client.PostAsJsonAsync("/api/v1/courses",
			new CoursesController.UpsertCourseRequest("Dansk", null, null, null));

		var response = await _client.GetAsync("/api/v1/courses");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var courses = await response.Content.ReadFromJsonAsync<List<CoursesController.CourseDto>>(JsonOpts);
		await Assert.That(courses).IsNotNull();
		await Assert.That(courses!.Count).IsEqualTo(2);
		await Assert.That(courses[0].Name).IsEqualTo("Dansk");
		await Assert.That(courses[1].Name).IsEqualTo("Matematik");
	}

	// -------------------------------------------------------------------------
	// POST /api/v1/courses
	// -------------------------------------------------------------------------

	[Test]
	public async Task Create_ReturnsCreatedWithCorrectFields()
	{
		var request = new CoursesController.UpsertCourseRequest(
			"Engelsk",
			"Engelsk fra 1. klasse",
			"#4287f5",
			SubjectCategory.Engelsk);

		var response = await _client.PostAsJsonAsync("/api/v1/courses", request);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
		var created = await response.Content.ReadFromJsonAsync<CoursesController.CourseDto>(JsonOpts);
		await Assert.That(created).IsNotNull();
		await Assert.That(created!.Name).IsEqualTo("Engelsk");
		await Assert.That(created.Description).IsEqualTo("Engelsk fra 1. klasse");
		await Assert.That(created.Color).IsEqualTo("#4287f5");
		await Assert.That(created.Category).IsEqualTo(SubjectCategory.Engelsk);
		await Assert.That(created.Id).IsNotEqualTo(Guid.Empty);
	}

	[Test]
	public async Task Create_NonAdmin_Returns403()
	{
		var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/courses");
		request.Headers.Add("X-Test-Roles", "teacher");
		request.Content = JsonContent.Create(
			new CoursesController.UpsertCourseRequest("Matematik", null, null, null));

		var response = await _client.SendAsync(request);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	// -------------------------------------------------------------------------
	// GET /api/v1/courses/{id}
	// -------------------------------------------------------------------------

	[Test]
	public async Task GetById_ReturnsCourse()
	{
		var created = await CreateCourseAsync("Biologi");

		var response = await _client.GetAsync($"/api/v1/courses/{created.Id}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var fetched = await response.Content.ReadFromJsonAsync<CoursesController.CourseDto>(JsonOpts);
		await Assert.That(fetched).IsNotNull();
		await Assert.That(fetched!.Id).IsEqualTo(created.Id);
		await Assert.That(fetched.Name).IsEqualTo("Biologi");
	}

	[Test]
	public async Task GetById_NotFound_Returns404()
	{
		var response = await _client.GetAsync($"/api/v1/courses/{Guid.NewGuid()}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	// -------------------------------------------------------------------------
	// PUT /api/v1/courses/{id}
	// -------------------------------------------------------------------------

	[Test]
	public async Task Update_ChangesAllFields()
	{
		var created = await CreateCourseAsync("Geografi");

		var updateRequest = new CoursesController.UpsertCourseRequest(
			"Geografi (opdateret)",
			"Ny beskrivelse",
			"#ff0000",
			SubjectCategory.Geografi);
		var response = await _client.PutAsJsonAsync($"/api/v1/courses/{created.Id}", updateRequest);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var updated = await response.Content.ReadFromJsonAsync<CoursesController.CourseDto>(JsonOpts);
		await Assert.That(updated).IsNotNull();
		await Assert.That(updated!.Name).IsEqualTo("Geografi (opdateret)");
		await Assert.That(updated.Description).IsEqualTo("Ny beskrivelse");
		await Assert.That(updated.Color).IsEqualTo("#ff0000");
		await Assert.That(updated.Category).IsEqualTo(SubjectCategory.Geografi);
	}

	[Test]
	public async Task Update_NonAdmin_Returns403()
	{
		var created = await CreateCourseAsync("Musik");

		var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/courses/{created.Id}");
		request.Headers.Add("X-Test-Roles", "teacher");
		request.Content = JsonContent.Create(
			new CoursesController.UpsertCourseRequest("Musik (ændret)", null, null, null));

		var response = await _client.SendAsync(request);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task Update_NotFound_Returns404()
	{
		var request = new CoursesController.UpsertCourseRequest("Ukendt fag", null, null, null);
		var response = await _client.PutAsJsonAsync($"/api/v1/courses/{Guid.NewGuid()}", request);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	// -------------------------------------------------------------------------
	// DELETE /api/v1/courses/{id}
	// -------------------------------------------------------------------------

	[Test]
	public async Task Delete_RemovesCourse()
	{
		var created = await CreateCourseAsync("Idræt");

		var deleteResponse = await _client.DeleteAsync($"/api/v1/courses/{created.Id}");
		await Assert.That(deleteResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var getResponse = await _client.GetAsync($"/api/v1/courses/{created.Id}");
		await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task Delete_NonAdmin_Returns403()
	{
		var created = await CreateCourseAsync("Historie");

		var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/courses/{created.Id}");
		request.Headers.Add("X-Test-Roles", "teacher");

		var response = await _client.SendAsync(request);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task Delete_NotFound_Returns404()
	{
		var response = await _client.DeleteAsync($"/api/v1/courses/{Guid.NewGuid()}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	// -------------------------------------------------------------------------
	// Tenant isolation
	// -------------------------------------------------------------------------

	[Test]
	public async Task TenantIsolation_CourseNotVisibleToOtherTenant()
	{
		// Arrange — create a course as the default tenant
		var created = await CreateCourseAsync("Fysik/kemi");

		// Act — use shared factory with a different X-Test-TenantId header
		var secondTenantId = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, secondTenantId, "Anden skole");
		using var client2 = _factory.CreateClient();
		client2.DefaultRequestHeaders.Add("X-Test-TenantId", secondTenantId.ToString());
		client2.DefaultRequestHeaders.Add("X-Test-Roles", "admin");

		var listResponse = await client2.GetAsync("/api/v1/courses");
		await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var courses = await listResponse.Content.ReadFromJsonAsync<List<CoursesController.CourseDto>>(JsonOpts);

		// Assert — the second tenant sees an empty list
		await Assert.That(courses).IsNotNull();
		await Assert.That(courses!.Any(c => c.Id == created.Id)).IsFalse();
	}

	// -------------------------------------------------------------------------
	// Helpers
	// -------------------------------------------------------------------------

	private async Task<CoursesController.CourseDto> CreateCourseAsync(string name)
	{
		var response = await _client.PostAsJsonAsync("/api/v1/courses",
			new CoursesController.UpsertCourseRequest(name, null, null, null));
		response.EnsureSuccessStatusCode();
		return (await response.Content.ReadFromJsonAsync<CoursesController.CourseDto>(JsonOpts))!;
	}
}
