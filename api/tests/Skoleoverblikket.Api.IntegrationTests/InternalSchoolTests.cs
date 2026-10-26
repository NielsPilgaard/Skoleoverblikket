using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Internal schools (the CD smoke test's school, task 44) stay out of the backoffice school list
/// unless the superadmin asks for them. That they get no welcome email is in
/// SchoolSignupWelcomeEmailTests.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class InternalSchoolTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		Converters = { new JsonStringEnumConverter() },
		PropertyNameCaseInsensitive = true,
	};

	private readonly Guid _customerId = Guid.NewGuid();
	private readonly Guid _internalId = Guid.NewGuid();
	private HttpClient _superAdminClient = null!;

	[Before(Test)]
	public async Task SetUp()
	{
		foreach (var id in new[] { _customerId, _internalId })
		{
			await TestDataBuilder.CreateSchoolAsync(factory.Services, id);
			await TestDataBuilder.CreateTrialSubscriptionAsync(factory.Services, id);
		}

		// Set by hand in prod, so there is no endpoint for it.
		await using var scope = factory.Services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		await db.Schools.IgnoreQueryFilters().Where(s => s.Id == _internalId)
			.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsInternal, true));

		_superAdminClient = factory.CreateClient();
		_superAdminClient.DefaultRequestHeaders.Add("X-Test-TenantId", Guid.NewGuid().ToString());
		_superAdminClient.DefaultRequestHeaders.Add("X-Test-Roles", "superadmin");
	}

	[After(Test)]
	public void TearDown() => _superAdminClient.Dispose();

	private async Task<List<TenantListItemDto>> ListAsync(string url) =>
		(await _superAdminClient.GetFromJsonAsync<List<TenantListItemDto>>(url, JsonOpts))!;

	[Test]
	public async Task TenantList_HidesInternalSchoolByDefault()
	{
		var ids = (await ListAsync("/api/v1/admin/tenants")).Select(t => t.Id).ToList();

		await Assert.That(ids).Contains(_customerId);
		await Assert.That(ids).DoesNotContain(_internalId);
	}

	[Test]
	public async Task TenantList_WithIncludeInternal_ListsAndFlagsInternalSchool()
	{
		var tenants = await ListAsync("/api/v1/admin/tenants?includeInternal=true");

		await Assert.That(tenants.Single(t => t.Id == _internalId).IsInternal).IsTrue();
		await Assert.That(tenants.Single(t => t.Id == _customerId).IsInternal).IsFalse();
	}
}
