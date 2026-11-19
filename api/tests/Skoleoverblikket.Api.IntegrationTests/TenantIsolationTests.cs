using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Verifies that the EF Core global query filter correctly scopes all data
/// to the current tenant. A room created by tenant A must never be visible
/// to tenant B — even when both share the same PostgreSQL database.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class TenantIsolationTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		Converters = { new JsonStringEnumConverter() },
		PropertyNameCaseInsensitive = true,
	};

	private readonly ApiFactory _factory = factory;

	// Each test gets its own pair of tenant IDs seeded via [Before(Test)] to avoid cross-test pollution.
	private Guid _tenantA;
	private Guid _tenantB;

	[Before(Test)]
	public async Task SetUpTenants()
	{
		_tenantA = Guid.NewGuid();
		_tenantB = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, _tenantA, "Skole A");
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, _tenantB, "Skole B");
	}

	private HttpClient CreateAdminClient(Guid tenantId)
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "admin");
		return client;
	}

	[Test]
	public async Task RoomsCreatedByTenantA_AreNotVisibleToTenantB()
	{
		using var clientA = CreateAdminClient(_tenantA);
		using var clientB = CreateAdminClient(_tenantB);

		var createResponse = await clientA.PostAsJsonAsync("/api/v1/rooms",
			new RoomsController.UpsertRoomRequest("Lokale A-101", null, null));
		createResponse.EnsureSuccessStatusCode();
		var roomA = (await createResponse.Content.ReadFromJsonAsync<RoomsController.RoomDto>())!;

		var listResponse = await clientB.GetAsync("/api/v1/rooms");
		await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var rooms = await listResponse.Content.ReadFromJsonAsync<List<RoomsController.RoomDto>>();

		await Assert.That(rooms!.Any(r => r.Id == roomA.Id)).IsFalse();
	}

	[Test]
	public async Task GetByIdCrossTenant_Returns404_NotDataLeak()
	{
		using var clientA = CreateAdminClient(_tenantA);
		using var clientB = CreateAdminClient(_tenantB);

		var createResponse = await clientA.PostAsJsonAsync("/api/v1/rooms",
			new RoomsController.UpsertRoomRequest("Geografi A", null, null));
		createResponse.EnsureSuccessStatusCode();
		var roomA = (await createResponse.Content.ReadFromJsonAsync<RoomsController.RoomDto>())!;

		var getResponse = await clientB.GetAsync($"/api/v1/rooms/{roomA.Id}");

		await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task StaffCreatedByTenantA_AreNotVisibleToTenantB()
	{
		using var clientA = CreateAdminClient(_tenantA);
		using var clientB = CreateAdminClient(_tenantB);

		var createResponse = await clientA.PostAsJsonAsync("/api/v1/staff",
			new { name = "Hanne Hansen", role = "Teacher" });
		createResponse.EnsureSuccessStatusCode();
		var staffA = (await createResponse.Content.ReadFromJsonAsync<StaffController.StaffDto>(JsonOpts))!;

		var listResponse = await clientB.GetAsync("/api/v1/staff");
		await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var staff = await listResponse.Content.ReadFromJsonAsync<List<StaffController.StaffDto>>(JsonOpts);

		await Assert.That(staff!.Any(s => s.Id == staffA.Id)).IsFalse();
	}
}
