using Skoleoverblikket.Api.Auth;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// Phase A of task 61: copies Keycloak's 'admin' realm role to <see cref="Models.Staff.IsAdmin"/>, so
/// Phase B can stop reading the Keycloak role without locking anyone out. Signup schools created before
/// task 61 have an admin whose Staff.IsAdmin is false. Runs once per start and is idempotent. If
/// Keycloak can't be reached it retries a few times, then waits for the next start.
/// Remove in Phase B, together with <see cref="KeycloakAdminService.GetAdminRoleMemberIdsAsync"/>.
/// </summary>
public sealed class AdminRoleBackfillJob(
	IServiceScopeFactory scopeFactory,
	ILogger<AdminRoleBackfillJob> logger) : BackgroundService
{
	private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);
	private const int Attempts = 6;

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		for (var attempt = 1; attempt <= Attempts; attempt++)
		{
			try
			{
				await using var scope = scopeFactory.CreateAsyncScope();
				var adminIds = await scope.ServiceProvider.GetRequiredService<KeycloakAdminService>()
					.GetAdminRoleMemberIdsAsync(stoppingToken);
				var updated = await scope.ServiceProvider.GetRequiredService<StaffService>()
					.BackfillAdminsAsync(adminIds, stoppingToken);
				logger.LogInformation(
					"Admin role backfill: {AdminCount} Keycloak admins, {UpdatedCount} staff rows set to IsAdmin",
					adminIds.Count, updated);
				return;
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Admin role backfill attempt {Attempt} of {Attempts} failed", attempt, Attempts);
			}

			try
			{
				await Task.Delay(RetryDelay, stoppingToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}
}
