using System.Net;
using System.Net.Http.Json;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;

namespace Skoleoverblikket.Api.IntegrationTests;

[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class SfoWeekPlanTests(ApiFactory factory)
{
	private readonly ApiFactory _factory = factory;
	private readonly Guid _tenantId = Guid.NewGuid();
	private HttpClient _adminClient = null!;

	[Before(Test)]
	public async Task SetUp()
	{
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, _tenantId);
		_adminClient = CreateClient(_tenantId, "admin");
	}

	[Test]
	public async Task Get_WeekWithoutPlan_ListsEveryShiftWithStaffAndNoDescription()
	{
		var staff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId, name: "Pia Pædagog");
		var afternoon = await CreateShiftAsync(_adminClient, 1, "13:00", "17:00", "Eftermiddag");
		var morning = await CreateShiftAsync(_adminClient, 1, "06:30", "08:00", "Morgen");
		await _adminClient.PostAsync($"/api/v1/sfo/shifts/{afternoon.Id}/staff/{staff.Id}", null);

		var plan = await GetPlanAsync(_adminClient, 2026, 10);

		await Assert.That(plan.Id).IsEqualTo(Guid.Empty);
		await Assert.That(plan.Notes).IsNull();
		await Assert.That(plan.Shifts.Select(s => s.SfoShiftId)).IsEquivalentTo([morning.Id, afternoon.Id]);
		await Assert.That(plan.Shifts[0].StartTime).IsEqualTo("06:30");
		await Assert.That(plan.Shifts.All(s => s.Description is null)).IsTrue();
		await Assert.That(plan.Shifts[1].Staff.Select(s => s.Name)).IsEquivalentTo(["Pia Pædagog"]);
	}

	[Test]
	public async Task UpsertShift_ThenEditAgain_UpdatesSameEntryAndOnlyThatWeek()
	{
		var shift = await CreateShiftAsync(_adminClient, 2, "13:00", "17:00", null);

		var first = await _adminClient.PutAsJsonAsync("/api/v1/sfo/week-plan/shifts",
			new SfoWeekPlanController.UpsertSfoWeekPlanShiftRequest(2026, 10, shift.Id, "Bålmad"));
		await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var created = await first.Content.ReadFromJsonAsync<SfoWeekPlanController.SfoWeekPlanShiftDto>();

		var second = await _adminClient.PutAsJsonAsync("/api/v1/sfo/week-plan/shifts",
			new SfoWeekPlanController.UpsertSfoWeekPlanShiftRequest(2026, 10, shift.Id, "Tur i skoven"));
		await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var updated = await second.Content.ReadFromJsonAsync<SfoWeekPlanController.SfoWeekPlanShiftDto>();

		await Assert.That(created!.Id).IsNotEqualTo(Guid.Empty);
		await Assert.That(updated!.Id).IsEqualTo(created.Id);

		var week10 = await GetPlanAsync(_adminClient, 2026, 10);
		await Assert.That(week10.Id).IsNotEqualTo(Guid.Empty);
		await Assert.That(week10.Shifts.Single().Description).IsEqualTo("Tur i skoven");

		var week11 = await GetPlanAsync(_adminClient, 2026, 11);
		await Assert.That(week11.Shifts.Single().Description).IsNull();
	}

	[Test]
	public async Task UpdateNotes_IsReturnedForThatWeek()
	{
		var response = await _adminClient.PutAsJsonAsync("/api/v1/sfo/week-plan/notes",
			new SfoWeekPlanController.UpdateSfoNotesRequest(2026, 10, "Husk regntøj"));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var notes = await response.Content.ReadFromJsonAsync<SfoWeekPlanController.NotesDto>();
		await Assert.That(notes!.Notes).IsEqualTo("Husk regntøj");

		await Assert.That((await GetPlanAsync(_adminClient, 2026, 10)).Notes).IsEqualTo("Husk regntøj");
		await Assert.That((await GetPlanAsync(_adminClient, 2026, 11)).Notes).IsNull();
	}

	[Test]
	public async Task UpsertShift_ForAnotherSchoolsShift_Returns404()
	{
		var otherTenantId = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, otherTenantId);
		using var otherAdmin = CreateClient(otherTenantId, "admin");
		var otherShift = await CreateShiftAsync(otherAdmin, 1, "13:00", "17:00", null);

		var response = await _adminClient.PutAsJsonAsync("/api/v1/sfo/week-plan/shifts",
			new SfoWeekPlanController.UpsertSfoWeekPlanShiftRequest(2026, 10, otherShift.Id, "Lækket"));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
		var otherPlan = await GetPlanAsync(otherAdmin, 2026, 10);
		await Assert.That(otherPlan.Shifts.Single().Description).IsNull();
	}

	[Test]
	public async Task Get_OtherSchoolsShiftsAndNotes_AreNotVisible()
	{
		var otherTenantId = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, otherTenantId);
		using var otherAdmin = CreateClient(otherTenantId, "admin");
		await CreateShiftAsync(otherAdmin, 1, "13:00", "17:00", null);
		await otherAdmin.PutAsJsonAsync("/api/v1/sfo/week-plan/notes",
			new SfoWeekPlanController.UpdateSfoNotesRequest(2026, 10, "Hemmelig"));

		var plan = await GetPlanAsync(_adminClient, 2026, 10);

		await Assert.That(plan.Shifts).IsEmpty();
		await Assert.That(plan.Notes).IsNull();
	}

	[Test]
	[Arguments(2025, 53)]
	[Arguments(2026, 0)]
	[Arguments(2019, 10)]
	public async Task InvalidWeek_Returns400(int isoYear, int isoWeek)
	{
		var get = await _adminClient.GetAsync($"/api/v1/sfo/week-plan?isoYear={isoYear}&isoWeek={isoWeek}");
		var notes = await _adminClient.PutAsJsonAsync("/api/v1/sfo/week-plan/notes",
			new SfoWeekPlanController.UpdateSfoNotesRequest(isoYear, isoWeek, "x"));

		await Assert.That(get.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That(notes.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task Get_Returns403_ForNonAdmin()
	{
		using var teacher = CreateClient(_tenantId, "teacher");

		var response = await teacher.GetAsync("/api/v1/sfo/week-plan?isoYear=2026&isoWeek=10");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	private HttpClient CreateClient(Guid tenantId, string roles)
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", roles);
		return client;
	}

	private static async Task<SfoController.SfoShiftDto> CreateShiftAsync(
		HttpClient client, int dayOfWeek, string start, string end, string? label)
	{
		var response = await client.PostAsJsonAsync("/api/v1/sfo/shifts",
			new SfoController.UpsertSfoShiftRequest(dayOfWeek, start, end, label));
		response.EnsureSuccessStatusCode();
		return (await response.Content.ReadFromJsonAsync<SfoController.SfoShiftDto>())!;
	}

	private static async Task<SfoWeekPlanController.SfoWeekPlanDto> GetPlanAsync(HttpClient client, int isoYear, int isoWeek)
	{
		var response = await client.GetAsync($"/api/v1/sfo/week-plan?isoYear={isoYear}&isoWeek={isoWeek}");
		response.EnsureSuccessStatusCode();
		return (await response.Content.ReadFromJsonAsync<SfoWeekPlanController.SfoWeekPlanDto>())!;
	}
}
