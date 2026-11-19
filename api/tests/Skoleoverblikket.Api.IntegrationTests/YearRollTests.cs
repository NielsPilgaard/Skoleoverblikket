using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;

namespace Skoleoverblikket.Api.IntegrationTests;

[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class YearRollTests(ApiFactory factory)
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
	}

	[Test]
	public async Task YearRoll_RenamesClasses_AndArchivesGraduatingClass_AndCreatesEntryClass()
	{
		var (class1a, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "1.a");
		var (class8a, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "8.a");

		var response = await _adminClient.PostAsJsonAsync("/api/v1/classes/year-roll", new
		{
			renames = new[] { new { classId = class1a.Id, newName = "2.a" } },
			archive = new[] { class8a.Id },
			create = new[] { new { name = "0.a" } },
		});

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		// Renamed class appears under new name
		var allClasses = await _adminClient.GetFromJsonAsync<List<ClassesController.ClassDto>>(
			"/api/v1/classes", JsonOpts) ?? [];

		await Assert.That(allClasses.Any(c => c.Name == "2.a")).IsTrue();
		await Assert.That(allClasses.Any(c => c.Name == "1.a")).IsFalse();

		// New entry class appears
		await Assert.That(allClasses.Any(c => c.Name == "0.a")).IsTrue();

		// Archived class no longer visible in normal list
		await Assert.That(allClasses.Any(c => c.Id == class8a.Id)).IsFalse();
	}

	[Test]
	public async Task YearRoll_ArchivedClass_AppearsInArchivedEndpoint()
	{
		var (graduating, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "9.b");

		var response = await _adminClient.PostAsJsonAsync("/api/v1/classes/year-roll", new
		{
			renames = Array.Empty<object>(),
			archive = new[] { graduating.Id },
			create = Array.Empty<object>(),
		});

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var archived = await _adminClient.GetFromJsonAsync<List<ClassesController.ClassDto>>(
			"/api/v1/classes/archived", JsonOpts);

		await Assert.That(archived!.Any(c => c.Id == graduating.Id)).IsTrue();
	}

	[Test]
	public async Task YearRoll_Returns400_WhenTwoClassesGetSameName()
	{
		var (classA, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "3.a");
		var (classB, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "4.a");

		// Both try to rename to "5.a"
		var response = await _adminClient.PostAsJsonAsync("/api/v1/classes/year-roll", new
		{
			renames = new[]
			{
				new { classId = classA.Id, newName = "5.a" },
				new { classId = classB.Id, newName = "5.a" },
			},
			archive = Array.Empty<object>(),
			create = Array.Empty<object>(),
		});

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task YearRoll_Returns400_WhenRenameAndArchiveSameClass()
	{
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(
			_factory.Services, _tenantId, "5.c");

		var response = await _adminClient.PostAsJsonAsync("/api/v1/classes/year-roll", new
		{
			renames = new[] { new { classId = klass.Id, newName = "6.c" } },
			archive = new[] { klass.Id },
			create = Array.Empty<object>(),
		});

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task YearRoll_Returns403_ForNonAdmin()
	{
		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "user");

		var response = await client.PostAsJsonAsync("/api/v1/classes/year-roll", new
		{
			renames = Array.Empty<object>(),
			archive = Array.Empty<object>(),
			create = Array.Empty<object>(),
		});

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}
}
