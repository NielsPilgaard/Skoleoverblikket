using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.IntegrationTests;

[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class VikarTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		Converters = { new JsonStringEnumConverter() },
		PropertyNameCaseInsensitive = true,
	};

	private readonly ApiFactory _factory = factory;
	private readonly Guid _tenantId = Guid.NewGuid();
	private HttpClient _client = null!;

	private const int TestYear = 2025;
	private const int TestWeek = 10;

	[Before(Test)]
	public async Task SetUp()
	{
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, _tenantId);
		_client = _factory.CreateClient();
		_client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
	}

	/// <summary>
	/// Staff assigned as teacher on an active schema slot appear in Busy;
	/// unassigned staff appear in Available.
	/// </summary>
	[Test]
	public async Task GetAvailable_StaffBusyOnSchemaSlot_AppearsInBusy()
	{
		var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(_factory.Services, _tenantId,
			new TimeOnly(8, 0), new TimeOnly(8, 45), sortOrder: 1);
		var busyTeacher = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId, "Optaget Lærer");
		var freeTeacher = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId, "Ledig Lærer");
		var course = await TestDataBuilder.CreateCourseAsync(_factory.Services, _tenantId);
		var (_, schema) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId);

		await TestDataBuilder.CreateSchemaSlotAsync(_factory.Services, _tenantId,
			schema.Id, timeSlot.Id, course.Id, busyTeacher.Id, DayOfWeek.Monday);

		var response = await _client.GetAsync(
			$"/api/v1/staff/available?isoYear={TestYear}&isoWeek={TestWeek}&weekday=1&timeSlotId={timeSlot.Id}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<VikarController.StaffAvailabilityDto>(JsonOpts);
		await Assert.That(dto).IsNotNull();
		await Assert.That(dto!.Available.Any(s => s.Id == freeTeacher.Id)).IsTrue();
		await Assert.That(dto.Busy.Any(s => s.Id == busyTeacher.Id)).IsTrue();
	}

	/// <summary>
	/// After assigning a substitute to a WeekPlanSlot, a subsequent call to
	/// GET /staff/available shows that substitute as Busy.
	/// </summary>
	[Test]
	public async Task GetAvailable_AfterSubstituteAssigned_SubstituteAppearsBusy()
	{
		var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(_factory.Services, _tenantId,
			new TimeOnly(9, 0), new TimeOnly(9, 45), sortOrder: 2);
		var regularTeacher = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId, "Fast Lærer");
		var substituteStaff = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId, "Vikar Hansen", StaffRole.Substitute);
		var course = await TestDataBuilder.CreateCourseAsync(_factory.Services, _tenantId, "Matematik");
		var (klass, schema) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "2.a");

		// Create schema slot with regular teacher
		await _client.PutAsJsonAsync($"/api/v1/classes/{klass.Id}/schemas/{schema.Id}/slots",
			new { timeSlotId = timeSlot.Id, weekday = (int)DayOfWeek.Tuesday, courseId = course.Id, teacherId = regularTeacher.Id });

		// Get the schema slot id
		var planResponse = await _client.GetAsync(
			$"/api/v1/classes/{klass.Id}/week-plan?isoYear={TestYear}&isoWeek={TestWeek}");
		var planDto = await planResponse.Content.ReadFromJsonAsync<WeekPlanController.WeekPlanDto>(JsonOpts);
		var schemaSlotId = planDto!.Slots[0].SchemaSlotId;

		// Upsert week plan slot to get a real slot row
		var upsertResponse = await _client.PutAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/week-plan/slots?isoYear={TestYear}&isoWeek={TestWeek}",
			new WeekPlanController.UpsertWeekPlanSlotRequest(schemaSlotId, null, null, null));
		upsertResponse.EnsureSuccessStatusCode();
		var slotDto = await upsertResponse.Content.ReadFromJsonAsync<WeekPlanController.WeekPlanSlotDto>(JsonOpts);
		var weekPlanSlotId = slotDto!.Id;
		var weekPlanId = slotDto.WeekPlanId;

		// Assign substitute
		var assignResponse = await _client.PutAsJsonAsync(
			$"/api/v1/week-plans/{weekPlanId}/slots/{weekPlanSlotId}/substitute",
			new VikarController.AssignSubstituteRequest(substituteStaff.Id, null));
		await Assert.That(assignResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

		// Now check availability — substitute should be busy
		var availResponse = await _client.GetAsync(
			$"/api/v1/staff/available?isoYear={TestYear}&isoWeek={TestWeek}&weekday=2&timeSlotId={timeSlot.Id}");
		await Assert.That(availResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var availability = await availResponse.Content.ReadFromJsonAsync<VikarController.StaffAvailabilityDto>(JsonOpts);
		await Assert.That(availability!.Busy.Any(s => s.Id == substituteStaff.Id)).IsTrue();
		await Assert.That(availability.Available.Any(s => s.Id == substituteStaff.Id)).IsFalse();
	}

	/// <summary>
	/// After assigning a substitute, GET /week-plan returns the substitute name and id on the slot.
	/// </summary>
	[Test]
	public async Task AssignSubstitute_ThenGetUgeplan_ReturnsSubstituteOnSlot()
	{
		var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(_factory.Services, _tenantId,
			new TimeOnly(10, 0), new TimeOnly(10, 45), sortOrder: 3);
		var teacher = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId, "Klassens Lærer");
		var vikar = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId, "Vikar Pedersen", StaffRole.Substitute);
		var course = await TestDataBuilder.CreateCourseAsync(_factory.Services, _tenantId, "Dansk");
		var (klass, schema) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "3.b");

		await _client.PutAsJsonAsync($"/api/v1/classes/{klass.Id}/schemas/{schema.Id}/slots",
			new { timeSlotId = timeSlot.Id, weekday = (int)DayOfWeek.Wednesday, courseId = course.Id, teacherId = teacher.Id });

		var planResponse1 = await _client.GetAsync(
			$"/api/v1/classes/{klass.Id}/week-plan?isoYear={TestYear}&isoWeek={TestWeek}");
		var planDto1 = await planResponse1.Content.ReadFromJsonAsync<WeekPlanController.WeekPlanDto>(JsonOpts);
		var schemaSlotId = planDto1!.Slots[0].SchemaSlotId;

		var upsertResponse = await _client.PutAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/week-plan/slots?isoYear={TestYear}&isoWeek={TestWeek}",
			new WeekPlanController.UpsertWeekPlanSlotRequest(schemaSlotId, null, null, null));
		upsertResponse.EnsureSuccessStatusCode();
		var slotDto = await upsertResponse.Content.ReadFromJsonAsync<WeekPlanController.WeekPlanSlotDto>(JsonOpts);

		// Assign substitute
		var assignResponse = await _client.PutAsJsonAsync(
			$"/api/v1/week-plans/{slotDto!.WeekPlanId}/slots/{slotDto.Id}/substitute",
			new VikarController.AssignSubstituteRequest(vikar.Id, null));
		await Assert.That(assignResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

		// GET week-plan — substitute should be visible on the slot
		var planResponse2 = await _client.GetAsync(
			$"/api/v1/classes/{klass.Id}/week-plan?isoYear={TestYear}&isoWeek={TestWeek}");
		var planDto2 = await planResponse2.Content.ReadFromJsonAsync<WeekPlanController.WeekPlanDto>(JsonOpts);
		var slot = planDto2!.Slots.First(s => s.SchemaSlotId == schemaSlotId);
		await Assert.That(slot.SubstituteTeacherId).IsEqualTo(vikar.Id);
		await Assert.That(slot.SubstituteTeacherName).IsEqualTo("Vikar Pedersen");
	}

	/// <summary>
	/// Assigning the same person as both teacher and aide returns 400.
	/// </summary>
	[Test]
	public async Task AssignSubstitute_SamePersonBothRoles_Returns400()
	{
		var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(_factory.Services, _tenantId,
			new TimeOnly(11, 0), new TimeOnly(11, 45), sortOrder: 4);
		var teacher = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId, "Lærer");
		var vikar = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId, "Vikar");
		var course = await TestDataBuilder.CreateCourseAsync(_factory.Services, _tenantId);
		var (klass, schema) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "4.c");

		await _client.PutAsJsonAsync($"/api/v1/classes/{klass.Id}/schemas/{schema.Id}/slots",
			new { timeSlotId = timeSlot.Id, weekday = (int)DayOfWeek.Thursday, courseId = course.Id, teacherId = teacher.Id });

		var planResponse = await _client.GetAsync(
			$"/api/v1/classes/{klass.Id}/week-plan?isoYear={TestYear}&isoWeek={TestWeek}");
		var planDto = await planResponse.Content.ReadFromJsonAsync<WeekPlanController.WeekPlanDto>(JsonOpts);
		var schemaSlotId = planDto!.Slots[0].SchemaSlotId;

		var upsertResponse = await _client.PutAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/week-plan/slots?isoYear={TestYear}&isoWeek={TestWeek}",
			new WeekPlanController.UpsertWeekPlanSlotRequest(schemaSlotId, null, null, null));
		upsertResponse.EnsureSuccessStatusCode();
		var slotDto = await upsertResponse.Content.ReadFromJsonAsync<WeekPlanController.WeekPlanSlotDto>(JsonOpts);

		var response = await _client.PutAsJsonAsync(
			$"/api/v1/week-plans/{slotDto!.WeekPlanId}/slots/{slotDto.Id}/substitute",
			new VikarController.AssignSubstituteRequest(vikar.Id, vikar.Id));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	/// <summary>
	/// Clearing a substitute (both null) removes it from the slot.
	/// </summary>
	[Test]
	public async Task AssignSubstitute_Clear_RemovesSubstitute()
	{
		var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(_factory.Services, _tenantId,
			new TimeOnly(12, 0), new TimeOnly(12, 45), sortOrder: 5);
		var teacher = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId, "Lærer B");
		var vikar = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId, "Vikar B", StaffRole.Substitute);
		var course = await TestDataBuilder.CreateCourseAsync(_factory.Services, _tenantId, "Natur/teknik");
		var (klass, schema) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "5.a");

		await _client.PutAsJsonAsync($"/api/v1/classes/{klass.Id}/schemas/{schema.Id}/slots",
			new { timeSlotId = timeSlot.Id, weekday = (int)DayOfWeek.Friday, courseId = course.Id, teacherId = teacher.Id });

		var planResponse = await _client.GetAsync(
			$"/api/v1/classes/{klass.Id}/week-plan?isoYear={TestYear}&isoWeek={TestWeek}");
		var planDto = await planResponse.Content.ReadFromJsonAsync<WeekPlanController.WeekPlanDto>(JsonOpts);
		var schemaSlotId = planDto!.Slots[0].SchemaSlotId;

		var upsertResponse = await _client.PutAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/week-plan/slots?isoYear={TestYear}&isoWeek={TestWeek}",
			new WeekPlanController.UpsertWeekPlanSlotRequest(schemaSlotId, null, null, null));
		upsertResponse.EnsureSuccessStatusCode();
		var slotDto = await upsertResponse.Content.ReadFromJsonAsync<WeekPlanController.WeekPlanSlotDto>(JsonOpts);

		// Assign
		var assignResponse = await _client.PutAsJsonAsync(
			$"/api/v1/week-plans/{slotDto!.WeekPlanId}/slots/{slotDto.Id}/substitute",
			new VikarController.AssignSubstituteRequest(vikar.Id, null));
		await Assert.That(assignResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

		// Clear
		var clearResponse = await _client.PutAsJsonAsync(
			$"/api/v1/week-plans/{slotDto.WeekPlanId}/slots/{slotDto.Id}/substitute",
			new VikarController.AssignSubstituteRequest(null, null));
		await Assert.That(clearResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

		// Verify cleared in GET
		var planResponse2 = await _client.GetAsync(
			$"/api/v1/classes/{klass.Id}/week-plan?isoYear={TestYear}&isoWeek={TestWeek}");
		var planDto2 = await planResponse2.Content.ReadFromJsonAsync<WeekPlanController.WeekPlanDto>(JsonOpts);
		var slot = planDto2!.Slots.First(s => s.SchemaSlotId == schemaSlotId);
		await Assert.That(slot.SubstituteTeacherId).IsNull();
		await Assert.That(slot.SubstituteTeacherName).IsNull();
	}
}
