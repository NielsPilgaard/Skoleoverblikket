using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.IntegrationTests;

[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class CalendarCrudTests(ApiFactory factory)
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
	}

	[Test]
	public async Task GetAll_ReturnsEmptyList_WhenNoEntriesExist()
	{
		var response = await _client.GetAsync("/api/v1/calendar");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var entries = await response.Content.ReadFromJsonAsync<List<CalendarController.CalendarEntryDto>>(JsonOpts);
		await Assert.That(entries).IsNotNull();
		await Assert.That(entries!.Count).IsEqualTo(0);
	}

	[Test]
	public async Task Create_ThenGetAll_ReturnsCreatedEntry()
	{
		var request = new CalendarController.CreateCalendarEntryRequest(
			"Efterårsferie", CalendarEntryType.Ferie,
			new DateOnly(2025, 10, 13), new DateOnly(2025, 10, 17));

		var createResponse = await _client.PostAsJsonAsync("/api/v1/calendar", request);
		await Assert.That(createResponse.StatusCode).IsEqualTo(HttpStatusCode.Created);

		var created = await createResponse.Content.ReadFromJsonAsync<CalendarController.CalendarEntryDto>(JsonOpts);
		await Assert.That(created).IsNotNull();
		await Assert.That(created!.Title).IsEqualTo("Efterårsferie");

		var getResponse = await _client.GetAsync("/api/v1/calendar");
		await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var entries = await getResponse.Content.ReadFromJsonAsync<List<CalendarController.CalendarEntryDto>>(JsonOpts);
		await Assert.That(entries!.Count).IsEqualTo(1);
		await Assert.That(entries[0].Title).IsEqualTo("Efterårsferie");
	}

	[Test]
	public async Task Create_WithEndDateBeforeStartDate_Returns400()
	{
		var request = new CalendarController.CreateCalendarEntryRequest(
			"Ugyldig", CalendarEntryType.Ferie,
			new DateOnly(2025, 10, 17), new DateOnly(2025, 10, 13));

		var response = await _client.PostAsJsonAsync("/api/v1/calendar", request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task Update_ChangesTitle()
	{
		var created = await CreateEntryAsync("Gammel titel");

		var updateRequest = new CalendarController.UpdateCalendarEntryRequest(
			"Ny titel", CalendarEntryType.Begivenhed,
			new DateOnly(2025, 11, 1), new DateOnly(2025, 11, 1));
		var updateResponse = await _client.PutAsJsonAsync($"/api/v1/calendar/{created.Id}", updateRequest);
		await Assert.That(updateResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var updated = await updateResponse.Content.ReadFromJsonAsync<CalendarController.CalendarEntryDto>(JsonOpts);
		await Assert.That(updated!.Title).IsEqualTo("Ny titel");
		await Assert.That(updated.Type).IsEqualTo(CalendarEntryType.Begivenhed);
	}

	[Test]
	public async Task Delete_RemovesEntry()
	{
		var created = await CreateEntryAsync("Slet mig");

		var deleteResponse = await _client.DeleteAsync($"/api/v1/calendar/{created.Id}");
		await Assert.That(deleteResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var getResponse = await _client.GetAsync("/api/v1/calendar");
		var entries = await getResponse.Content.ReadFromJsonAsync<List<CalendarController.CalendarEntryDto>>(JsonOpts);
		await Assert.That(entries!.Count).IsEqualTo(0);
	}

	[Test]
	public async Task GetDefaults_ReturnsNonEmptyList()
	{
		var response = await _client.GetAsync("/api/v1/calendar/defaults?year=2025");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var defaults = await response.Content.ReadFromJsonAsync<List<CalendarController.DefaultHolidayDto>>(JsonOpts);
		await Assert.That(defaults).IsNotNull();
		await Assert.That(defaults!.Count).IsGreaterThan(0);
	}

	[Test]
	public async Task GetDefaults_2025SchoolYear_ReturnsAllEightStandardHolidays()
	{
		var response = await _client.GetAsync("/api/v1/calendar/defaults?year=2025");
		var defaults = await response.Content.ReadFromJsonAsync<List<CalendarController.DefaultHolidayDto>>(JsonOpts);

		var names = defaults!.Select(d => d.Title).ToHashSet();
		await Assert.That(names).Contains("Efterårsferie");
		await Assert.That(names).Contains("Juleferie");
		await Assert.That(names).Contains("Vinterferie");
		await Assert.That(names).Contains("Påskeferie");
		await Assert.That(names).Contains("Kristi Himmelfartsdag");
		await Assert.That(names).Contains("Pinse");
		await Assert.That(names).Contains("Grundlovsdag");
		await Assert.That(names).Contains("Sommerferie");
		await Assert.That(defaults!.Count).IsEqualTo(8);
	}

	[Test]
	public async Task GetDefaults_2025SchoolYear_FixedHolidayDatesCorrect()
	{
		// School year 2025/2026: year param 2025, schoolEndYear 2026
		var response = await _client.GetAsync("/api/v1/calendar/defaults?year=2025");
		var defaults = await response.Content.ReadFromJsonAsync<List<CalendarController.DefaultHolidayDto>>(JsonOpts);
		var byName = defaults!.ToDictionary(d => d.Title);

		// Efterårsferie: ISO week 42 of 2025 = Mon Oct 13 – Fri Oct 17
		await Assert.That(byName["Efterårsferie"].StartDate).IsEqualTo(new DateOnly(2025, 10, 13));
		await Assert.That(byName["Efterårsferie"].EndDate).IsEqualTo(new DateOnly(2025, 10, 17));

		// Juleferie: Dec 22, 2025 – Jan 2, 2026
		await Assert.That(byName["Juleferie"].StartDate).IsEqualTo(new DateOnly(2025, 12, 22));
		await Assert.That(byName["Juleferie"].EndDate).IsEqualTo(new DateOnly(2026, 1, 2));

		// Vinterferie: ISO week 7 of 2026 = Mon Feb 9 – Fri Feb 13
		await Assert.That(byName["Vinterferie"].StartDate).IsEqualTo(new DateOnly(2026, 2, 9));
		await Assert.That(byName["Vinterferie"].EndDate).IsEqualTo(new DateOnly(2026, 2, 13));

		// Sommerferie: Jun 26, 2026 – Aug 7, 2026
		await Assert.That(byName["Sommerferie"].StartDate).IsEqualTo(new DateOnly(2026, 6, 26));
		await Assert.That(byName["Sommerferie"].EndDate).IsEqualTo(new DateOnly(2026, 8, 7));

		// Grundlovsdag: Jun 5, 2026
		await Assert.That(byName["Grundlovsdag"].StartDate).IsEqualTo(new DateOnly(2026, 6, 5));
	}

	[Test]
	public async Task GetDefaults_2025SchoolYear_EasterDerivedHolidayDatesCorrect()
	{
		// Easter 2026 = April 5 (computed via Gregorian algorithm)
		var response = await _client.GetAsync("/api/v1/calendar/defaults?year=2025");
		var defaults = await response.Content.ReadFromJsonAsync<List<CalendarController.DefaultHolidayDto>>(JsonOpts);
		var byName = defaults!.ToDictionary(d => d.Title);

		// Påskeferie: Palm Sunday (Easter-7) = Mar 29 through Easter Monday (Easter+1) = Apr 6
		await Assert.That(byName["Påskeferie"].StartDate).IsEqualTo(new DateOnly(2026, 3, 29));
		await Assert.That(byName["Påskeferie"].EndDate).IsEqualTo(new DateOnly(2026, 4, 6));

		// Kristi Himmelfartsdag: Easter+39 = May 14
		await Assert.That(byName["Kristi Himmelfartsdag"].StartDate).IsEqualTo(new DateOnly(2026, 5, 14));
		await Assert.That(byName["Kristi Himmelfartsdag"].EndDate).IsEqualTo(new DateOnly(2026, 5, 14));

		// Pinse: Whit Friday (Easter+48) = May 23 through Whit Monday (Easter+50) = May 25
		await Assert.That(byName["Pinse"].StartDate).IsEqualTo(new DateOnly(2026, 5, 23));
		await Assert.That(byName["Pinse"].EndDate).IsEqualTo(new DateOnly(2026, 5, 25));
	}

	[Test]
	public async Task GetDefaults_HolidayTypes_FerieAndLukkedag()
	{
		var response = await _client.GetAsync("/api/v1/calendar/defaults?year=2025");
		var defaults = await response.Content.ReadFromJsonAsync<List<CalendarController.DefaultHolidayDto>>(JsonOpts);
		var byName = defaults!.ToDictionary(d => d.Title);

		await Assert.That(byName["Efterårsferie"].Type).IsEqualTo(CalendarEntryType.Ferie);
		await Assert.That(byName["Juleferie"].Type).IsEqualTo(CalendarEntryType.Ferie);
		await Assert.That(byName["Påskeferie"].Type).IsEqualTo(CalendarEntryType.Ferie);
		await Assert.That(byName["Pinse"].Type).IsEqualTo(CalendarEntryType.Ferie);
		await Assert.That(byName["Kristi Himmelfartsdag"].Type).IsEqualTo(CalendarEntryType.Lukkedag);
		await Assert.That(byName["Grundlovsdag"].Type).IsEqualTo(CalendarEntryType.Lukkedag);
	}

	[Test]
	public async Task GetAll_MonthlyEntryOnThe31st_KeepsDayAfterShortMonth()
	{
		var request = new CalendarController.CreateCalendarEntryRequest(
			"Personalemøde", CalendarEntryType.Begivenhed,
			new DateOnly(2026, 1, 31), new DateOnly(2026, 1, 31),
			"FREQ=MONTHLY", new DateOnly(2026, 5, 31));
		var createResponse = await _client.PostAsJsonAsync("/api/v1/calendar", request);
		createResponse.EnsureSuccessStatusCode();

		var response = await _client.GetAsync("/api/v1/calendar");
		var entries = await response.Content.ReadFromJsonAsync<List<CalendarController.CalendarEntryDto>>(JsonOpts);

		var dates = entries!.Select(e => e.StartDate).ToList();
		await Assert.That(dates).IsEquivalentTo(new List<DateOnly>
		{
			new(2026, 1, 31),
			new(2026, 2, 28),
			new(2026, 3, 31),
			new(2026, 4, 30),
			new(2026, 5, 31),
		});
	}

	[Test]
	public async Task TenantIsolation_EntryNotVisibleToOtherTenant()
	{
		// Create entry for default tenant
		await CreateEntryAsync("Tenant 1 ferie");

		// Seed a second tenant's entry directly into the shared DB
		var secondTenantId = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, secondTenantId, "Anden skole");
		await TestDataBuilder.CreateCalendarEntryAsync(
			_factory.Services, secondTenantId,
			CalendarEntryType.Ferie, "Tenant 2 ferie",
			new DateOnly(2025, 12, 22), new DateOnly(2026, 1, 2));

		// The default tenant's client should only see its own entry
		var response = await _client.GetAsync("/api/v1/calendar");
		var entries = await response.Content.ReadFromJsonAsync<List<CalendarController.CalendarEntryDto>>(JsonOpts);
		await Assert.That(entries!.Count).IsEqualTo(1);
		await Assert.That(entries[0].Title).IsEqualTo("Tenant 1 ferie");
	}

	private async Task<CalendarController.CalendarEntryDto> CreateEntryAsync(string title)
	{
		var request = new CalendarController.CreateCalendarEntryRequest(
			title, CalendarEntryType.Ferie,
			new DateOnly(2025, 10, 13), new DateOnly(2025, 10, 17));
		var response = await _client.PostAsJsonAsync("/api/v1/calendar", request);
		response.EnsureSuccessStatusCode();
		return (await response.Content.ReadFromJsonAsync<CalendarController.CalendarEntryDto>(JsonOpts))!;
	}
}
