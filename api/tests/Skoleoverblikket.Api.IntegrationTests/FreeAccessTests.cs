using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// A superadmin gives a school free base access after its trial expired: writes work again,
/// modules stay off, and revoking blocks writes again. How free access holds off data deletion
/// for a canceled school is in SchoolRetentionTests.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class FreeAccessTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		Converters = { new JsonStringEnumConverter() },
		PropertyNameCaseInsensitive = true,
	};

	private readonly Guid _tenantId = Guid.NewGuid();
	private HttpClient _adminClient = null!;
	private HttpClient _superAdminClient = null!;

	[Before(Test)]
	public async Task SetUp()
	{
		await TestDataBuilder.CreateSchoolAsync(factory.Services, _tenantId);
		await TestDataBuilder.CreateTrialSubscriptionAsync(factory.Services, _tenantId);

		_adminClient = factory.CreateClient();
		_adminClient.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		_adminClient.DefaultRequestHeaders.Add("X-Test-Roles", "admin");

		_superAdminClient = factory.CreateClient();
		_superAdminClient.DefaultRequestHeaders.Add("X-Test-TenantId", Guid.NewGuid().ToString());
		_superAdminClient.DefaultRequestHeaders.Add("X-Test-Roles", "superadmin");
	}

	[After(Test)]
	public void TearDown()
	{
		_adminClient.Dispose();
		_superAdminClient.Dispose();
	}

	private string FreeAccessUrl => $"/api/v1/admin/tenants/{_tenantId}/free-access";

	// No endpoint ages a subscription, so the test moves its dates directly.
	private async Task UpdateSubscriptionAsync(Action<UpdateSettersBuilder<Models.Subscription>> set)
	{
		await using var scope = factory.Services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		await db.Subscriptions.Where(s => s.SchoolId == _tenantId).ExecuteUpdateAsync(set);
	}

	private Task ExpireTrialAsync() =>
		UpdateSubscriptionAsync(s => s.SetProperty(x => x.TrialEnd, DateTimeOffset.UtcNow.AddDays(-1)));

	private async Task<Models.Subscription> GetSubscriptionAsync()
	{
		await using var scope = factory.Services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		return await db.Subscriptions.AsNoTracking().SingleAsync(s => s.SchoolId == _tenantId);
	}

	private Task<HttpResponseMessage> CreateRoomAsync() =>
		_adminClient.PostAsJsonAsync("/api/v1/rooms", new RoomsController.UpsertRoomRequest($"Lokale {Guid.NewGuid():N}", null, null));

	private Task<HttpResponseMessage> GrantAsync(DateOnly? lastDay) =>
		_superAdminClient.PutAsJsonAsync(FreeAccessUrl, new SuperAdminFreeAccessController.GrantFreeAccessRequest(lastDay));

	private async Task<BillingController.SubscriptionDto> GetBillingAsync() =>
		(await _adminClient.GetFromJsonAsync<BillingController.SubscriptionDto>("/api/v1/billing/subscription", JsonOpts))!;

	[Test]
	public async Task ExpiredTrial_FreeAccess_AllowsWritesWithoutModules()
	{
		await ExpireTrialAsync();
		await Assert.That((await CreateRoomAsync()).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

		var lastDay = SchoolDayCalendar.Today().AddDays(30);
		await Assert.That((await GrantAsync(lastDay)).StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		await Assert.That((await CreateRoomAsync()).StatusCode).IsEqualTo(HttpStatusCode.Created);
		var billing = await GetBillingAsync();
		await Assert.That(billing.HasAccess).IsTrue();
		await Assert.That(billing.HasFreeAccess).IsTrue();
		await Assert.That(billing.FreeAccessLastDay).IsEqualTo(lastDay);
		await Assert.That(billing.IsTrialing).IsFalse();
		await Assert.That(billing.ActiveModules.Count).IsEqualTo(0);
	}

	[Test]
	public async Task FreeAccess_LastDayIsToday_StillAllowsWrites()
	{
		await ExpireTrialAsync();

		await GrantAsync(SchoolDayCalendar.Today());

		await Assert.That((await CreateRoomAsync()).StatusCode).IsEqualTo(HttpStatusCode.Created);
	}

	[Test]
	public async Task FreeAccess_EndedYesterday_BlocksWrites()
	{
		await ExpireTrialAsync();
		await GrantAsync(SchoolDayCalendar.Today());
		await UpdateSubscriptionAsync(s => s.SetProperty(x => x.FreeAccessLastDay, SchoolDayCalendar.Today().AddDays(-1)));

		await Assert.That((await CreateRoomAsync()).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That((await GetBillingAsync()).HasFreeAccess).IsFalse();
	}

	[Test]
	public async Task FreeAccess_NoEndDate_AllowsWrites()
	{
		await ExpireTrialAsync();

		await Assert.That((await GrantAsync(null)).StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		await Assert.That((await CreateRoomAsync()).StatusCode).IsEqualTo(HttpStatusCode.Created);
		var billing = await GetBillingAsync();
		await Assert.That(billing.HasFreeAccess).IsTrue();
		await Assert.That(billing.FreeAccessLastDay).IsNull();
	}

	[Test]
	public async Task RevokeFreeAccess_BlocksWritesAgain()
	{
		await ExpireTrialAsync();
		await GrantAsync(null);

		var response = await _superAdminClient.DeleteAsync(FreeAccessUrl);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
		await Assert.That((await CreateRoomAsync()).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That((await GetBillingAsync()).HasAccess).IsFalse();
	}

	[Test]
	public async Task SchoolAdmin_CannotGrantFreeAccess()
	{
		await ExpireTrialAsync();

		var response = await _adminClient.PutAsJsonAsync(FreeAccessUrl, new SuperAdminFreeAccessController.GrantFreeAccessRequest(null));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That((await GetSubscriptionAsync()).FreeAccessGrantedAt).IsNull();
	}

	[Test]
	public async Task GrantFreeAccess_LastDayInPast_Returns400()
	{
		var response = await GrantAsync(SchoolDayCalendar.Today().AddDays(-1));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task GrantFreeAccess_PayingSchool_Returns409()
	{
		await TestDataBuilder.CreateActiveSubscriptionAsync(factory.Services, _tenantId);

		var response = await GrantAsync(null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
	}

	[Test]
	public async Task GrantFreeAccess_UnknownSchool_Returns404()
	{
		var response = await _superAdminClient.PutAsJsonAsync(
			$"/api/v1/admin/tenants/{Guid.NewGuid()}/free-access",
			new SuperAdminFreeAccessController.GrantFreeAccessRequest(null));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}
}
