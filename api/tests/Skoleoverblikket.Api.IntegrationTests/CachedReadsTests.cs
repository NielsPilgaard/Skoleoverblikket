using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Cached reads refresh in the background, after the request that triggered them has ended and its
/// DbContext is disposed. They must still see the right data, for the right tenant.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class CachedReadsTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		Converters = { new JsonStringEnumConverter() },
		PropertyNameCaseInsensitive = true,
	};

	// The recipients list is cached for 30 seconds and refreshes eagerly from 27 seconds.
	private static readonly TimeSpan EagerRefreshWindowStart = TimeSpan.FromSeconds(27.5);
	private static readonly TimeSpan EntryExpiry = TimeSpan.FromSeconds(30);

	private HttpClient CreateClient(Guid tenantId, string roles, string subject)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", roles);
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
	}

	private static async Task<List<MessagesController.RecipientDto>> GetRecipientsAsync(HttpClient client)
	{
		var response = await client.GetAsync("/api/v1/messages/recipients");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return (await response.Content.ReadFromJsonAsync<List<MessagesController.RecipientDto>>(JsonOpts))!;
	}

	[Test]
	public async Task Recipients_RefreshInBackground_BeforeTheEntryExpires()
	{
		var tenantId = Guid.NewGuid();
		var otherTenantId = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(factory.Services, tenantId);
		await TestDataBuilder.CreateSchoolAsync(factory.Services, otherTenantId);
		await TestDataBuilder.CreateStaffAsync(factory.Services, tenantId, name: "Anne Afsender", keycloakSubject: "cache-refresh-caller");
		var otherTenantStaff = await TestDataBuilder.CreateStaffAsync(factory.Services, otherTenantId, name: "Oskar Andenskole");

		using var client = CreateClient(tenantId, "user", "cache-refresh-caller");

		// Started before the first read, so the entry cannot have expired while this is below EntryExpiry.
		var sinceFirstRead = Stopwatch.StartNew();
		await GetRecipientsAsync(client);

		var added = await TestDataBuilder.CreateStaffAsync(factory.Services, tenantId, name: "Nina Nyansat");

		await Task.Delay(EagerRefreshWindowStart - sinceFirstRead.Elapsed);
		var stale = await GetRecipientsAsync(client);
		await Assert.That(stale.Any(r => r.Id == added.Id)).IsFalse();

		var refreshed = stale;
		while (refreshed.All(r => r.Id != added.Id) && sinceFirstRead.Elapsed < EntryExpiry)
		{
			await Task.Delay(TimeSpan.FromMilliseconds(100));
			refreshed = await GetRecipientsAsync(client);
		}

		await Assert.That(sinceFirstRead.Elapsed).IsLessThan(EntryExpiry);
		await Assert.That(refreshed.Any(r => r.Id == added.Id)).IsTrue();
		await Assert.That(refreshed.Any(r => r.Id == otherTenantStaff.Id)).IsFalse();
	}

	[Test]
	public async Task OnboardingStatus_IsPerTenant_AndUpdatesWhenRoomIsAdded()
	{
		var tenantId = Guid.NewGuid();
		var otherTenantId = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(factory.Services, tenantId);
		await TestDataBuilder.CreateSchoolAsync(factory.Services, otherTenantId);
		await TestDataBuilder.CreateStaffAsync(factory.Services, otherTenantId);

		using var client = CreateClient(tenantId, "admin", "cache-onboarding-admin");
		using var otherClient = CreateClient(otherTenantId, "admin", "cache-onboarding-other-admin");

		var before = await client.GetFromJsonAsync<SchoolsController.OnboardingStatusDto>("/api/v1/schools/onboarding-status", JsonOpts);
		var other = await otherClient.GetFromJsonAsync<SchoolsController.OnboardingStatusDto>("/api/v1/schools/onboarding-status", JsonOpts);

		var created = await client.PostAsJsonAsync("/api/v1/rooms", new RoomsController.UpsertRoomRequest("Sløjdlokale", null, null));
		await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);

		var after = await client.GetFromJsonAsync<SchoolsController.OnboardingStatusDto>("/api/v1/schools/onboarding-status", JsonOpts);

		await Assert.That(other!.StaffCount).IsEqualTo(before!.StaffCount + 1);
		await Assert.That(after!.RoomCount).IsEqualTo(before.RoomCount + 1);
	}
}
