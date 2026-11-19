using System.Net;
using System.Net.Http.Json;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;

namespace Skoleoverblikket.Api.IntegrationTests;

[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class SfoTests(ApiFactory factory)
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
	}

	[Test]
	public async Task GetShifts_Returns200_ForAdmin()
	{
		var response = await _adminClient.GetAsync("/api/v1/sfo/shifts");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var list = await response.Content.ReadFromJsonAsync<List<SfoController.SfoShiftDto>>();
		await Assert.That(list).IsNotNull();
	}

	[Test]
	public async Task GetShifts_Returns403_ForNonAdmin()
	{
		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "teacher");

		var response = await client.GetAsync("/api/v1/sfo/shifts");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task CreateShift_Returns201_WithValidData()
	{
		var req = new SfoController.UpsertSfoShiftRequest(
			DayOfWeek: 1,
			StartTime: "06:30",
			EndTime: "08:00",
			Label: "Morgen-SFO");

		var response = await _adminClient.PostAsJsonAsync("/api/v1/sfo/shifts", req);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
		var dto = await response.Content.ReadFromJsonAsync<SfoController.SfoShiftDto>();
		await Assert.That(dto).IsNotNull();
		await Assert.That(dto!.DayOfWeek).IsEqualTo(1);
		await Assert.That(dto.StartTime).IsEqualTo("06:30");
		await Assert.That(dto.EndTime).IsEqualTo("08:00");
		await Assert.That(dto.Label).IsEqualTo("Morgen-SFO");
	}

	[Test]
	public async Task AssignStaff_Returns204()
	{
		// Seed a staff member and a shift
		var staff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);

		var createRes = await _adminClient.PostAsJsonAsync("/api/v1/sfo/shifts",
			new SfoController.UpsertSfoShiftRequest(1, "13:00", "17:00", null));
		await Assert.That(createRes.StatusCode).IsEqualTo(HttpStatusCode.Created);
		var shift = await createRes.Content.ReadFromJsonAsync<SfoController.SfoShiftDto>();

		var response = await _adminClient.PostAsync($"/api/v1/sfo/shifts/{shift!.Id}/staff/{staff.Id}", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}

	[Test]
	public async Task UnassignStaff_Returns204()
	{
		var staff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);

		var createRes = await _adminClient.PostAsJsonAsync("/api/v1/sfo/shifts",
			new SfoController.UpsertSfoShiftRequest(2, "13:00", "17:00", null));
		var shift = await createRes.Content.ReadFromJsonAsync<SfoController.SfoShiftDto>();

		await _adminClient.PostAsync($"/api/v1/sfo/shifts/{shift!.Id}/staff/{staff.Id}", null);

		var response = await _adminClient.DeleteAsync($"/api/v1/sfo/shifts/{shift.Id}/staff/{staff.Id}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}
}
