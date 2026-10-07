using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// Pass every 6 hours that warns and then deletes schools whose subscription was canceled more
/// than <see cref="SchoolDeletionService.RetentionPeriod"/> ago, and forgets deleted schools once they
/// are out of every backup. Each school runs in its own DI scope pinned to that school, like
/// <see cref="AbsenceRetentionJob"/>.
/// </summary>
public sealed class SchoolRetentionJob(
	IServiceScopeFactory scopeFactory,
	ILogger<SchoolRetentionJob> logger) : BackgroundService
{
	private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);
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
				logger.LogError(ex, "School retention pass failed");
			}
		}
		while (await timer.WaitForNextTickAsync(stoppingToken));
	}

	/// <summary>Runs retention for every due school. One school failing does not stop the others.</summary>
	public static async Task RunAsync(
		IServiceScopeFactory scopeFactory, ILogger logger, DateTimeOffset now, CancellationToken cancellationToken)
	{
		IReadOnlyList<Guid> schoolIds;
		await using (var scope = scopeFactory.CreateAsyncScope())
		{
			var deletion = scope.ServiceProvider.GetRequiredService<SchoolDeletionService>();
			await deletion.PruneDeletionRecordsAsync(now, cancellationToken);
			schoolIds = await deletion.ListSchoolsDueAsync(now, cancellationToken);
		}

		foreach (var schoolId in schoolIds)
		{
			try
			{
				await using var scope = scopeFactory.CreateAsyncScope();
				scope.ServiceProvider.GetRequiredService<HttpTenantContext>().UseBackgroundTenant(schoolId);
				await scope.ServiceProvider.GetRequiredService<SchoolDeletionService>().ApplyRetentionAsync(now, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "School retention failed for school {SchoolId}", schoolId);
			}
		}
	}
}
