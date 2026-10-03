using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Tenancy;
using static Skoleoverblikket.Api.IntegrationTests.Infrastructure.AbsenceTestKit;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Absence retention: data is kept for the current and previous school year (boundary 1 August),
/// admins are warned before anything is deleted — on 1 July, or 30 days ahead when the July warning
/// never went out — and each school's pass only touches that school. Data is created today and the
/// pass is run with a future "now", per tenant, so other tests' tenants are never touched.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class AbsenceRetentionTests(ApiFactory factory)
{
	private static readonly TimeSpan Danish = TimeSpan.FromHours(2);

	/// <summary>The school year today's data belongs to (2025 = 2025/26).</summary>
	private static readonly int DataYear = DanishToday().Month >= 8 ? DanishToday().Year : DanishToday().Year - 1;

	private async Task<(AbsenceTestKit Kit, HttpClient Admin)> SeedTenantAsync()
	{
		var kit = new AbsenceTestKit(factory);
		await kit.InitAsync();
		var admin = await kit.AdminAsync($"retention-admin-{kit.TenantId}");
		var classId = await kit.CreateClassAsync(admin, "5.a", 5);
		var student = await kit.CreateStudentAsync(classId);
		var parent = await kit.ParentOfAsync(student.Id, $"parent-{Guid.NewGuid()}");
		(await ReportAsync(parent, student.Id, AbsenceCategory.Illness, DanishToday())).EnsureSuccessStatusCode();

		var (_, teacher) = await kit.TeacherAsync($"retention-teacher-{kit.TenantId}");
		(await admin.PostAsJsonAsync("/api/v1/staff-absences",
			new ReportStaffAbsenceRequest(teacher.Id, DanishToday(), null, null), JsonOpts)).EnsureSuccessStatusCode();
		return (kit, admin);
	}

	private async Task RunAsync(Guid tenantId, DateTimeOffset now)
	{
		await using var scope = factory.Services.CreateAsyncScope();
		scope.ServiceProvider.GetRequiredService<HttpTenantContext>().UseBackgroundTenant(tenantId);
		await scope.ServiceProvider.GetRequiredService<AbsenceService>().ApplyRetentionAsync(now, CancellationToken.None);
	}

	private async Task<List<int>> WarningsAsync(Guid tenantId)
	{
		await using var scope = factory.Services.CreateAsyncScope();
		scope.ServiceProvider.GetRequiredService<HttpTenantContext>().UseBackgroundTenant(tenantId);
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		return await db.AbsenceRetentionWarnings.Select(w => w.SchoolYearStart).ToListAsync();
	}

	private static async Task<int> AbsenceCountAsync(HttpClient admin) =>
		(await admin.GetFromJsonAsync<List<AbsenceRecordDto>>("/api/v1/absence", JsonOpts))!.Count;

	private static async Task<int> StaffAbsenceCountAsync(HttpClient admin)
	{
		var today = DanishToday();
		return (await admin.GetFromJsonAsync<List<StaffAbsenceDto>>(
			$"/api/v1/staff-absences?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}", JsonOpts))!.Count;
	}

	[Test]
	public async Task PreviousSchoolYear_IsKept()
	{
		var (kit, admin) = await SeedTenantAsync();

		// July and August of the following school year: the data is now "forrige skoleår".
		await RunAsync(kit.TenantId, new DateTimeOffset(DataYear + 1, 7, 1, 3, 0, 0, Danish));
		await RunAsync(kit.TenantId, new DateTimeOffset(DataYear + 1, 8, 2, 3, 0, 0, Danish));
		await RunAsync(kit.TenantId, new DateTimeOffset(DataYear + 2, 6, 30, 3, 0, 0, Danish));

		await Assert.That(await AbsenceCountAsync(admin)).IsEqualTo(1);
		await Assert.That(await StaffAbsenceCountAsync(admin)).IsEqualTo(1);
		await Assert.That((await WarningsAsync(kit.TenantId)).Count).IsEqualTo(0);
	}

	[Test]
	public async Task JulyWarning_OnceOnly_ThenDeletedOnFirstAugust()
	{
		var (kit, admin) = await SeedTenantAsync();

		await RunAsync(kit.TenantId, new DateTimeOffset(DataYear + 2, 7, 1, 3, 0, 0, Danish));
		await RunAsync(kit.TenantId, new DateTimeOffset(DataYear + 2, 7, 15, 3, 0, 0, Danish));
		await Assert.That(await WarningsAsync(kit.TenantId)).IsEquivalentTo(new[] { DataYear });
		await Assert.That(await AbsenceCountAsync(admin)).IsEqualTo(1);

		await RunAsync(kit.TenantId, new DateTimeOffset(DataYear + 2, 8, 1, 3, 0, 0, Danish));
		await Assert.That(await AbsenceCountAsync(admin)).IsEqualTo(0);
		await Assert.That(await StaffAbsenceCountAsync(admin)).IsEqualTo(0);
	}

	[Test]
	public async Task NoJulyWarning_DeletesOnlyThirtyDaysAfterLateWarning()
	{
		var (kit, admin) = await SeedTenantAsync();
		var lateWarning = new DateTimeOffset(DataYear + 2, 8, 2, 3, 0, 0, Danish);

		await RunAsync(kit.TenantId, lateWarning);
		await Assert.That(await WarningsAsync(kit.TenantId)).IsEquivalentTo(new[] { DataYear });
		await Assert.That(await AbsenceCountAsync(admin)).IsEqualTo(1);

		await RunAsync(kit.TenantId, lateWarning.AddDays(10));
		await Assert.That(await AbsenceCountAsync(admin)).IsEqualTo(1);

		await RunAsync(kit.TenantId, lateWarning.AddDays(31));
		await Assert.That(await AbsenceCountAsync(admin)).IsEqualTo(0);
	}

	[Test]
	public async Task Retention_OnlyTouchesItsOwnTenant()
	{
		var (school, schoolAdmin) = await SeedTenantAsync();
		var (other, otherAdmin) = await SeedTenantAsync();

		await RunAsync(school.TenantId, new DateTimeOffset(DataYear + 2, 7, 1, 3, 0, 0, Danish));
		await RunAsync(school.TenantId, new DateTimeOffset(DataYear + 2, 8, 1, 3, 0, 0, Danish));

		await Assert.That(await AbsenceCountAsync(schoolAdmin)).IsEqualTo(0);
		await Assert.That(await AbsenceCountAsync(otherAdmin)).IsEqualTo(1);
		await Assert.That(await StaffAbsenceCountAsync(otherAdmin)).IsEqualTo(1);
		await Assert.That((await WarningsAsync(other.TenantId)).Count).IsEqualTo(0);
	}

	[Test]
	public async Task Job_VisitsEverySchool_WithoutDeletingCurrentData()
	{
		var (kit, admin) = await SeedTenantAsync();

		// The job lists schools across tenants; with today's clock nothing is old enough to go.
		await AbsenceRetentionJob.RunAsync(
			factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance,
			DateTimeOffset.UtcNow, CancellationToken.None);

		await Assert.That(await AbsenceCountAsync(admin)).IsEqualTo(1);
		await Assert.That((await WarningsAsync(kit.TenantId)).Count).IsEqualTo(0);
	}
}
