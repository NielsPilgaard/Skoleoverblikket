using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Verifies GET /api/v1/rooms/{id}/schedule returns valid responses.
///
/// The RoomSchema page crashed with "Cannot read properties of undefined (reading 'sort')"
/// when a room had no active schema slots — the frontend received an unexpected shape.
/// These tests guard the API contract: the endpoint must always return a valid (possibly
/// empty) list, never an error or unexpected null structure.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class RoomScheduleTests(ApiFactory factory)
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
		await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId, keycloakSubject: "room-schedule-user");
		_client = _factory.CreateClient();
		_client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		_client.DefaultRequestHeaders.Add("X-Test-Roles", "user");
		_client.DefaultRequestHeaders.Add("X-Test-Subject", "room-schedule-user");
	}

	[Test]
	public async Task GetRoomSchedule_RoomWithNoSlots_ReturnsEmptyList()
	{
		var room = await TestDataBuilder.CreateRoomAsync(_factory.Services, _tenantId, "Lokale 101");

		var response = await _client.GetAsync($"/api/v1/rooms/{room.Id}/schedule");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var slots = await response.Content.ReadFromJsonAsync<List<SchedulesController.ScheduleSlotDto>>(JsonOpts);
		await Assert.That(slots).IsNotNull();
		await Assert.That(slots!.Count).IsEqualTo(0);
	}

	[Test]
	public async Task GetRoomSchedule_UnknownRoom_Returns404()
	{
		var response = await _client.GetAsync($"/api/v1/rooms/{Guid.NewGuid()}/schedule");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task GetRoomSchedule_RoomWithActiveSlot_ReturnsSlot()
	{
		var room = await TestDataBuilder.CreateRoomAsync(_factory.Services, _tenantId, "Gymnastiksalen");
		var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(
			_factory.Services, _tenantId, new TimeOnly(8, 0), new TimeOnly(8, 45));
		var course = await TestDataBuilder.CreateCourseAsync(_factory.Services, _tenantId, "Idræt");
		var teacher = await TestDataBuilder.CreateStaffAsync(_factory.Services, _tenantId);
		var (klass, schema) = await TestDataBuilder.CreateClassWithSchemaAsync(_factory.Services, _tenantId, "6.a");

		// Place the schema slot in this room via the API (admin client)
		using var adminClient = _factory.CreateClient();
		adminClient.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		adminClient.DefaultRequestHeaders.Add("X-Test-Roles", "admin");
		adminClient.DefaultRequestHeaders.Add("X-Test-Subject", "room-schedule-admin");
		var upsertResp = await adminClient.PutAsJsonAsync(
			$"/api/v1/classes/{klass.Id}/schemas/{schema.Id}/slots",
			new
			{
				timeSlotId = timeSlot.Id,
				weekday = (int)DayOfWeek.Wednesday,
				courseId = course.Id,
				teacherId = teacher.Id,
				roomId = room.Id,
			});
		upsertResp.EnsureSuccessStatusCode();

		var response = await _client.GetAsync($"/api/v1/rooms/{room.Id}/schedule");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var slots = await response.Content.ReadFromJsonAsync<List<SchedulesController.ScheduleSlotDto>>(JsonOpts);
		await Assert.That(slots!.Count).IsEqualTo(1);
		await Assert.That(slots[0].CourseName).IsEqualTo("Idræt");
		await Assert.That(slots[0].RoomId).IsEqualTo(room.Id);
		await Assert.That(slots[0].RoomName).IsEqualTo("Gymnastiksalen");
	}
}
