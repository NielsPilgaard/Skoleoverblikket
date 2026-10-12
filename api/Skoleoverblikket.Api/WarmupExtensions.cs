using System.Diagnostics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api;

public static class WarmupExtensions
{
	/// <summary>
	/// Pays the one-off costs of the first request before Kestrel listens: building the EF Core
	/// model, opening the first database connection, and fetching Keycloak's OIDC metadata and
	/// signing keys. Deploys are start-first and only route traffic once <c>/alive</c> answers, so
	/// this delays health instead of making the first page load after a deploy take ~1 second.
	/// A failure here only logs: the request path does the same work lazily anyway.
	/// </summary>
	public static async Task WarmUpAsync(this WebApplication app)
	{
		var isOpenApiGeneration = string.Equals(
			Environment.GetEnvironmentVariable("OPENAPI_GENERATE"), "true",
			StringComparison.OrdinalIgnoreCase);

		if (app.Environment.IsEnvironment("Testing") || isOpenApiGeneration)
		{
			return;
		}

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
		var start = Stopwatch.GetTimestamp();

		await Task.WhenAll(
			WarmUpDatabaseAsync(app, cts.Token),
			WarmUpJwtBearerAsync(app, cts.Token));

		var elapsed = Stopwatch.GetElapsedTime(start);

		app.Logger.LogInformation("Warmup finished in {ElapsedMs} ms", elapsed.TotalMilliseconds);
	}

	private static async Task WarmUpDatabaseAsync(WebApplication app, CancellationToken ct)
	{
		if (string.IsNullOrEmpty(app.Configuration.GetConnectionString("skoleoverblikket-db")))
		{
			return;
		}

		try
		{
			await using var scope = app.Services.CreateAsyncScope();
			// Pinned to no tenant, so the query runs through the real tenant filter and matches nothing.
			scope.ServiceProvider.GetRequiredService<HttpTenantContext>().UseBackgroundTenant(Guid.Empty);
			var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
			await db.Staff.AsNoTracking().AnyAsync(ct);
		}
		catch (Exception ex)
		{
			app.Logger.LogWarning(ex, "Database warmup failed");
		}
	}

	private static async Task WarmUpJwtBearerAsync(WebApplication app, CancellationToken ct)
	{
		try
		{
			// The handler reuses this ConfigurationManager, so the metadata and keys fetched here are
			// the ones the first request validates its token against.
			var options = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
				.Get(JwtBearerDefaults.AuthenticationScheme);
			if (options.ConfigurationManager is { } configurationManager)
			{
				await configurationManager.GetConfigurationAsync(ct);
			}
		}
		catch (Exception ex)
		{
			app.Logger.LogWarning(ex, "Keycloak metadata warmup failed");
		}
	}
}
