using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// Nightly pass that applies absence retention (<see cref="AbsenceService.ApplyRetentionAsync"/>)
/// for every school: the 1 July warning and the deletion of absence data older than the previous
/// school year.
///
/// It spans every tenant, so it lists schools with <c>IgnoreQueryFilters()</c> — the only
/// cross-tenant read here — and then runs each school's work in its own DI scope pinned to that
/// tenant via <see cref="HttpTenantContext.UseBackgroundTenant"/>. All deletes therefore go through
/// the normal tenant query filter.
/// </summary>
public sealed class AbsenceRetentionJob(
	IServiceScopeFactory scopeFactory,
	ILogger<AbsenceRetentionJob> logger) : BackgroundService
{
	private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
		}
		catch (OperationCanceledException)
		{
			return;
		}

		using var timer = new PeriodicTimer(Interval);
		do
		{
			try
			{
				await RunAsync(scopeFactory, logger, DateTimeOffset.UtcNow, stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Absence retention pass failed");
			}
		}
		while (await timer.WaitForNextTickAsync(stoppingToken));
	}

	/// <summary>Runs retention for every school. One school failing does not stop the others.</summary>
	public static async Task RunAsync(
		IServiceScopeFactory scopeFactory, ILogger logger, DateTimeOffset now, CancellationToken cancellationToken)
	{
		List<Guid> tenantIds;
		await using (var scope = scopeFactory.CreateAsyncScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

			// Cross-tenant on purpose: the job has no request tenant and must visit every school.
			tenantIds = await db.Schools.IgnoreQueryFilters().Select(s => s.Id).ToListAsync(cancellationToken);
		}

		foreach (var tenantId in tenantIds)
		{
			try
			{
				await using var scope = scopeFactory.CreateAsyncScope();
				scope.ServiceProvider.GetRequiredService<HttpTenantContext>().UseBackgroundTenant(tenantId);
				var absence = scope.ServiceProvider.GetRequiredService<AbsenceService>();
				await absence.ApplyRetentionAsync(now, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Absence retention failed for tenant {TenantId}", tenantId);
			}
		}
	}
}
