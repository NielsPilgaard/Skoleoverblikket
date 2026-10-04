using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// Hourly pass that sends the founder's welcome email to schools that signed up
/// <see cref="SchoolSignupService.WelcomeEmailDelay"/> ago. Each school runs in its own DI scope
/// pinned to that school, like <see cref="SchoolRetentionJob"/>.
/// </summary>
public sealed class WelcomeEmailJob(
	IServiceScopeFactory scopeFactory,
	ILogger<WelcomeEmailJob> logger) : BackgroundService
{
	private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

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
				logger.LogError(ex, "Welcome email pass failed");
			}
		}
		while (await timer.WaitForNextTickAsync(stoppingToken));
	}

	/// <summary>Sends every due welcome email. One school failing does not stop the others.</summary>
	public static async Task RunAsync(
		IServiceScopeFactory scopeFactory, ILogger logger, DateTimeOffset now, CancellationToken cancellationToken)
	{
		IReadOnlyList<Guid> schoolIds;
		await using (var scope = scopeFactory.CreateAsyncScope())
		{
			schoolIds = await scope.ServiceProvider.GetRequiredService<SchoolSignupService>()
				.ListWelcomeEmailsDueAsync(now, cancellationToken);
		}

		foreach (var schoolId in schoolIds)
		{
			try
			{
				await using var scope = scopeFactory.CreateAsyncScope();
				scope.ServiceProvider.GetRequiredService<HttpTenantContext>().UseBackgroundTenant(schoolId);
				await scope.ServiceProvider.GetRequiredService<SchoolSignupService>().SendWelcomeEmailAsync(now, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				// Retried on the next pass until SchoolSignupService gives up on it.
				logger.LogError(ex, "Welcome email failed for school {SchoolId}", schoolId);
			}
		}
	}
}
