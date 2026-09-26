using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Integration tests for NotificationsController (/api/v1/notifications)
/// and NotificationPreferencesController (/api/v1/notification-preferences).
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class NotificationsTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		Converters = { new JsonStringEnumConverter() },
		PropertyNameCaseInsensitive = true,
	};

	private readonly ApiFactory _factory = factory;
	private readonly Guid _tenantId = Guid.NewGuid();

	[Before(Test)]
	public async Task SetUp()
	{
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, _tenantId);
	}

	// ── Helpers ──────────────────────────────────────────────────────────────────

	private HttpClient CreateStaffClient(string subject, string role = "user")
	{
		var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", role);
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
	}

	private async Task<Notification> SeedNotificationAsync(Guid staffId)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var notification = new Notification
		{
			Id = Guid.NewGuid(),
			TenantId = _tenantId,
			RecipientId = staffId,
			RecipientType = RecipientType.Staff,
			Type = NotificationType.NewMessage,
			Body = "Du har en ny besked.",
			CreatedAt = DateTimeOffset.UtcNow,
		};
		db.Notifications.Add(notification);
		await db.SaveChangesAsync();
		return notification;
	}

	// ── GET /api/v1/notifications ─────────────────────────────────────────────────

	[Test]
	public async Task GetNotifications_Staff_Returns200WithNotifications()
	{
		// Arrange
		const string subject = "get-notif-staff";
		var staff = await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId, keycloakSubject: subject);
		var seeded = await SeedNotificationAsync(staff.Id);

		using var client = CreateStaffClient(subject);

		// Act
		var response = await client.GetAsync("/api/v1/notifications");

		// Assert
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var list = await response.Content.ReadFromJsonAsync<List<NotificationsController.NotificationDto>>(JsonOpts);
		await Assert.That(list).IsNotNull();
		await Assert.That(list!.Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(list.Any(n => n.Id == seeded.Id)).IsTrue();
	}

	[Test]
	public async Task GetNotifications_NoStaffRecord_Returns200Empty()
	{
		// Arrange — authenticated with a subject that has no Staff row in the DB
		const string subject = "get-notif-no-staff";
		using var client = CreateStaffClient(subject);

		// Act
		var response = await client.GetAsync("/api/v1/notifications");

		// Assert — controller returns empty list, not an error
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var list = await response.Content.ReadFromJsonAsync<List<NotificationsController.NotificationDto>>(JsonOpts);
		await Assert.That(list).IsNotNull();
		await Assert.That(list!.Count).IsEqualTo(0);
	}

	// ── POST /api/v1/notifications/{id}/read ─────────────────────────────────────

	[Test]
	public async Task MarkRead_Staff_Returns204()
	{
		// Arrange
		const string subject = "mark-read-staff";
		var staff = await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId, keycloakSubject: subject);
		var notification = await SeedNotificationAsync(staff.Id);

		using var client = CreateStaffClient(subject);

		// Act — first call
		var firstResponse = await client.PostAsync($"/api/v1/notifications/{notification.Id}/read", null);
		await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		// Act — second call (idempotent)
		var secondResponse = await client.PostAsync($"/api/v1/notifications/{notification.Id}/read", null);
		await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}

	// ── POST /api/v1/notifications/read-all ──────────────────────────────────────

	[Test]
	public async Task MarkAllRead_Staff_Returns204Idempotent()
	{
		// Arrange
		const string subject = "mark-all-read-staff";
		var staff = await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId, keycloakSubject: subject);
		await SeedNotificationAsync(staff.Id);

		using var client = CreateStaffClient(subject);

		// Act — first call marks existing unread notifications as read
		var firstResponse = await client.PostAsync("/api/v1/notifications/read-all", null);
		await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		// Act — second call when there are no unread notifications is still 204
		var secondResponse = await client.PostAsync("/api/v1/notifications/read-all", null);
		await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}

	// ── PUT /api/v1/notification-preferences ─────────────────────────────────────

	[Test]
	public async Task UpsertPreferences_Staff_Returns204()
	{
		// Arrange
		const string subject = "upsert-prefs-staff";
		await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId, keycloakSubject: subject);

		var items = new[]
		{
			new NotificationPreferencesController.UpsertPreferenceItem(NotificationType.NewMessage, InApp: true, Email: false),
			new NotificationPreferencesController.UpsertPreferenceItem(NotificationType.AbsenceConfirmed, InApp: false, Email: true),
		};

		using var client = CreateStaffClient(subject);

		// Act
		var response = await client.PutAsJsonAsync("/api/v1/notification-preferences", items);

		// Assert
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}

	[Test]
	public async Task UpsertPreferences_Duplicate_Returns400()
	{
		// Arrange
		const string subject = "upsert-prefs-dup";
		await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId, keycloakSubject: subject);

		// Both items use the same NotificationType → duplicate
		var items = new[]
		{
			new NotificationPreferencesController.UpsertPreferenceItem(NotificationType.NewMessage, InApp: true, Email: false),
			new NotificationPreferencesController.UpsertPreferenceItem(NotificationType.NewMessage, InApp: false, Email: true),
		};

		using var client = CreateStaffClient(subject);

		// Act
		var response = await client.PutAsJsonAsync("/api/v1/notification-preferences", items);

		// Assert
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task GetPreferences_Staff_AfterUpsert_ReturnsCorrect()
	{
		// Arrange
		const string subject = "get-prefs-after-upsert";
		await TestDataBuilder.CreateStaffAsync(
			_factory.Services, _tenantId, keycloakSubject: subject);

		var items = new[]
		{
			new NotificationPreferencesController.UpsertPreferenceItem(NotificationType.WeekPlanChanged, InApp: true, Email: false),
		};

		using var client = CreateStaffClient(subject);

		// Upsert
		var putResponse = await client.PutAsJsonAsync("/api/v1/notification-preferences", items);
		putResponse.EnsureSuccessStatusCode();

		// Act
		var getResponse = await client.GetAsync("/api/v1/notification-preferences");

		// Assert
		await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var prefs = await getResponse.Content
			.ReadFromJsonAsync<List<NotificationPreferencesController.NotificationPreferenceDto>>(JsonOpts);
		await Assert.That(prefs).IsNotNull();
		await Assert.That(prefs!.Count).IsEqualTo(1);

		var pref = prefs[0];
		await Assert.That(pref.Type).IsEqualTo(NotificationType.WeekPlanChanged);
		await Assert.That(pref.InApp).IsTrue();
		await Assert.That(pref.Email).IsFalse();
	}
}
