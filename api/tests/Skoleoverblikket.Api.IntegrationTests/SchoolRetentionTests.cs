using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Storage;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// School data retention after cancellation (privacy policy: 90 days, then permanent deletion).
/// The subscription is canceled through signed Stripe webhooks, then <see cref="SchoolRetentionJob"/>
/// runs with a future "now". Covers: warning email 7 days ahead, nothing deleted before 90 days or
/// before the warning is 7 days old, every row and file of the school deleted afterwards, other
/// schools untouched, resubscribing stops the clock, and failed login-account deletion keeps the data.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel(nameof(SchoolRetentionTests))]
public sealed class SchoolRetentionTests(ApiFactory factory)
{
	private static readonly TimeSpan Day = TimeSpan.FromDays(1);

	/// <summary>Creates a school with data in many tables and a file under every storage prefix.</summary>
	private async Task<(Guid SchoolId, string AdminEmail)> SeedSchoolAsync()
	{
		var schoolId = Guid.NewGuid();
		var adminEmail = $"admin-{schoolId:N}@skole.dk";
		var services = factory.Services;

		await TestDataBuilder.CreateSchoolAsync(services, schoolId, "Retention Friskole");
		var admin = await TestDataBuilder.CreateStaffAsync(services, schoolId, "Hanne Kontor", isAdmin: true);
		var teacher = await TestDataBuilder.CreateStaffAsync(services, schoolId, "Thomas Lærer");
		var course = await TestDataBuilder.CreateCourseAsync(services, schoolId);
		var timeSlot = await TestDataBuilder.CreateTimeSlotAsync(services, schoolId, new TimeOnly(8, 0), new TimeOnly(8, 45));
		var (klass, schema) = await TestDataBuilder.CreateClassWithSchemaAsync(services, schoolId);
		await TestDataBuilder.CreateSchemaSlotAsync(services, schoolId, schema.Id, timeSlot.Id, course.Id, teacher.Id);
		await TestDataBuilder.CreateRoomAsync(services, schoolId);
		await TestDataBuilder.CreateCalendarEntryAsync(services, schoolId, CalendarEntryType.Ferie, "Vinterferie",
			new DateOnly(2026, 2, 9), new DateOnly(2026, 2, 13));
		await TestDataBuilder.CreateSchoolFileAsync(services, schoolId);

		using (var scope = services.CreateScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
			db.Staff.Attach(admin).Entity.Email = adminEmail;

			// Student references its class with ON DELETE RESTRICT, a reply references its message.
			db.Students.Add(new Student { Id = Guid.NewGuid(), TenantId = schoolId, Name = "Mikkel Elev", ClassId = klass.Id });
			var first = NewMessage(schoolId, admin.Id, teacher.Id, null);
			db.Messages.AddRange(first, NewMessage(schoolId, teacher.Id, admin.Id, first.Id));
			await db.SaveChangesAsync();

			var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
			foreach (var prefix in SchoolDeletionService.StoragePrefixes(schoolId))
			{
				using var content = new MemoryStream([1, 2, 3]);
				await storage.UploadAsync($"{prefix}{(prefix.EndsWith('/') ? "test.pdf" : ".png")}", "application/octet-stream", content);
			}
		}

		using (var scope = services.CreateScope())
		{
			await scope.ServiceProvider.GetRequiredService<SubscriptionService>().GetOrCreateAsync(schoolId);
		}

		await PostWebhookAsync("checkout.session.completed", $$"""
			{ "id": "cs_test_{{schoolId:N}}", "object": "checkout.session", "customer": "cus_{{schoolId:N}}",
			  "subscription": "sub_{{schoolId:N}}", "metadata": { "school_id": "{{schoolId}}" } }
			""", schoolId);
		return (schoolId, adminEmail);
	}

	private static Message NewMessage(Guid schoolId, Guid from, Guid to, Guid? inReplyTo) => new()
	{
		Id = Guid.NewGuid(),
		TenantId = schoolId,
		SenderId = from,
		SenderType = RecipientType.Staff,
		RecipientId = to,
		RecipientType = RecipientType.Staff,
		Subject = "Skema",
		Body = "Hej",
		SentAt = DateTimeOffset.UtcNow,
		InReplyToId = inReplyTo,
	};

	private Task CancelAsync(Guid schoolId) => SubscriptionEventAsync(schoolId, "customer.subscription.deleted", "canceled");

	private Task SubscriptionEventAsync(Guid schoolId, string eventType, string status) => PostWebhookAsync(eventType, $$"""
		{ "id": "sub_{{schoolId:N}}", "object": "subscription", "status": "{{status}}", "items": { "object": "list", "data": [] } }
		""", schoolId);

	private async Task PostWebhookAsync(string eventType, string dataObject, Guid schoolId)
	{
		var payload = $$"""
			{ "id": "evt_{{Guid.NewGuid():N}}", "object": "event", "type": "{{eventType}}", "request": null,
			  "data": { "object": {{dataObject}} } }
			""";
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("whsec_stub"));
		var signature = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}")));

		using var client = factory.CreateClient();
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/stripe/webhook")
		{
			Content = new StringContent(payload, Encoding.UTF8, "application/json"),
		};
		request.Headers.Add("Stripe-Signature", $"t={timestamp},v1={signature}");
		var response = await client.SendAsync(request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because($"webhook {eventType} for {schoolId}");
	}

	/// <summary>Deletion warnings only: other tests may email every school (sub-processor notice).</summary>
	private List<Email.EmailMessage> Warnings(string address) =>
		[.. factory.Emails.To(address).Where(m => m.Subject == "Skolens data slettes om 7 dage")];

	private Task RunAsync(DateTimeOffset now) =>
		SchoolRetentionJob.RunAsync(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance, now, CancellationToken.None);

	/// <summary>Rows left for the school across every table with a TenantId, plus its subscription.</summary>
	private async Task<int> RowCountAsync(Guid schoolId)
	{
		using var scope = factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var total = await db.Subscriptions.CountAsync(s => s.SchoolId == schoolId);
		total += await db.Schools.IgnoreQueryFilters().CountAsync(s => s.Id == schoolId);

		foreach (var entityType in SchoolDeletionService.DeletionOrder(db.Model))
		{
			var table = entityType.GetTableName();
			var column = entityType.FindProperty(nameof(ITenantScoped.TenantId))!
				.GetColumnName(StoreObjectIdentifier.Table(table!, entityType.GetSchema()));
			total += await db.Database
				.SqlQueryRaw<int>($"SELECT count(*)::int AS \"Value\" FROM \"{table}\" WHERE \"{column}\" = {{0}}", schoolId)
				.SingleAsync();
		}

		return total;
	}

	private async Task<int> FileCountAsync(Guid schoolId)
	{
		using var scope = factory.Services.CreateScope();
		var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
		var count = 0;
		foreach (var prefix in SchoolDeletionService.StoragePrefixes(schoolId))
		{
			count += await storage.GetObjectSizeAsync($"{prefix}{(prefix.EndsWith('/') ? "test.pdf" : ".png")}") is null ? 0 : 1;
		}

		return count;
	}

	private async Task<Subscription> SubscriptionAsync(Guid schoolId)
	{
		using var scope = factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		return await db.Subscriptions.AsNoTracking().SingleAsync(s => s.SchoolId == schoolId);
	}

	[Test]
	public async Task CancelWebhook_StartsClock_AndResubscribing_StopsIt()
	{
		var (schoolId, adminEmail) = await SeedSchoolAsync();
		await Assert.That((await SubscriptionAsync(schoolId)).CanceledAt).IsNull();

		var before = DateTimeOffset.UtcNow;
		await CancelAsync(schoolId);
		await Assert.That((await SubscriptionAsync(schoolId)).CanceledAt).IsNotNull().And.IsGreaterThanOrEqualTo(before.AddSeconds(-1));

		await SubscriptionEventAsync(schoolId, "customer.subscription.updated", "active");
		await Assert.That((await SubscriptionAsync(schoolId)).CanceledAt).IsNull();

		await RunAsync(DateTimeOffset.UtcNow + 400 * Day);
		await Assert.That(await RowCountAsync(schoolId)).IsGreaterThan(0);
		await Assert.That(Warnings(adminEmail).Count).IsEqualTo(0);
	}

	[Test]
	public async Task Warns7DaysAhead_ThenDeletesEverything_AfterRetentionPeriod()
	{
		var (schoolId, adminEmail) = await SeedSchoolAsync();
		var (otherSchoolId, otherAdminEmail) = await SeedSchoolAsync();
		await CancelAsync(schoolId);
		var canceledAt = (await SubscriptionAsync(schoolId)).CanceledAt!.Value;
		var rows = await RowCountAsync(schoolId);
		var otherRows = await RowCountAsync(otherSchoolId);

		// Day 82: too early for anything.
		await RunAsync(canceledAt + 82 * Day);
		await Assert.That(Warnings(adminEmail).Count).IsEqualTo(0);

		// Day 84: warning to the school's contact email and its admins, once.
		await RunAsync(canceledAt + 84 * Day);
		await RunAsync(canceledAt + 85 * Day);
		var warnings = Warnings(adminEmail);
		await Assert.That(warnings.Count).IsEqualTo(1);
		await Assert.That(warnings[0].Subject).IsEqualTo("Skolens data slettes om 7 dage");
		await Assert.That(warnings[0].HtmlBody).Contains("/eksporter").And.Contains("kontakt@skoleoverblikket.dk");

		// Day 89: warned, but the 90 days are not over.
		await RunAsync(canceledAt + 89 * Day);
		await Assert.That(await RowCountAsync(schoolId)).IsEqualTo(rows);
		await Assert.That(await FileCountAsync(schoolId)).IsEqualTo(SchoolDeletionService.StoragePrefixes(schoolId).Count);

		// Day 91: 90 days since cancellation and 7 since the warning. Everything goes.
		await RunAsync(canceledAt + 91 * Day);
		await Assert.That(await RowCountAsync(schoolId)).IsEqualTo(0);
		await Assert.That(await FileCountAsync(schoolId)).IsEqualTo(0);

		// The other school is untouched and was never warned.
		await Assert.That(await RowCountAsync(otherSchoolId)).IsEqualTo(otherRows);
		await Assert.That(await FileCountAsync(otherSchoolId)).IsEqualTo(SchoolDeletionService.StoragePrefixes(otherSchoolId).Count);
		await Assert.That(Warnings(otherAdminEmail).Count).IsEqualTo(0);
	}

	[Test]
	public async Task NeverWarned_IsWarnedFirst_AndKeptFor7MoreDays()
	{
		var (schoolId, adminEmail) = await SeedSchoolAsync();
		await CancelAsync(schoolId);
		var canceledAt = (await SubscriptionAsync(schoolId)).CanceledAt!.Value;
		var rows = await RowCountAsync(schoolId);

		// The job was down for months: the first pass only warns.
		await RunAsync(canceledAt + 200 * Day);
		await RunAsync(canceledAt + 206 * Day);
		await Assert.That(Warnings(adminEmail).Count).IsEqualTo(1);
		await Assert.That(await RowCountAsync(schoolId)).IsEqualTo(rows);

		await RunAsync(canceledAt + 207 * Day);
		await Assert.That(await RowCountAsync(schoolId)).IsEqualTo(0);
	}

	[Test]
	public async Task LoginAccountDeletionFails_KeepsTheData()
	{
		var (schoolId, _) = await SeedSchoolAsync();

		// No Keycloak runs in tests, so deleting this login fails.
		await TestDataBuilder.CreateStaffAsync(factory.Services, schoolId, "Birgitte", keycloakSubject: $"kc-{schoolId:N}");
		await CancelAsync(schoolId);
		var canceledAt = (await SubscriptionAsync(schoolId)).CanceledAt!.Value;
		var rows = await RowCountAsync(schoolId);

		await RunAsync(canceledAt + 84 * Day);
		await RunAsync(canceledAt + 91 * Day);

		await Assert.That(await RowCountAsync(schoolId)).IsEqualTo(rows);
	}
}
