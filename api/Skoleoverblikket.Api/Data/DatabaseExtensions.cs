using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.OpenApi;
using Skoleoverblikket.Api.Storage;

namespace Skoleoverblikket.Api.Data;

public static class DatabaseExtensions
{
	public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddDbContext<AppDbContext>(options =>
			options.UseNpgsql(configuration.GetConnectionString("skoleoverblikket-db")));

		return services;
	}

	public static async Task MigrateAndSeedAsync(this WebApplication app)
	{
		if (OpenApiGeneration.IsRunning)
		{
			return;
		}

		if (!app.Environment.IsProduction())
		{
			await using var scope = app.Services.CreateAsyncScope();
			var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
			await db.Database.MigrateAsync();
		}

		if (!app.Environment.IsEnvironment("Testing"))
		{
			if (!string.IsNullOrEmpty(app.Configuration["ObjectStorage:ServiceUrl"]))
			{
				// Awaited: the data protection key ring lives in this bucket and is read on first use,
				// so on a fresh stack it must exist before requests arrive. A storage outage must not
				// stop the API from starting, though; most features don't touch object storage.
				try
				{
					using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
					await app.Services.EnsureS3BucketAsync(cts.Token);
				}
				catch (Exception ex)
				{
					app.Logger.LogError(ex, "Could not ensure the object storage bucket exists");
				}
			}

			if (!string.IsNullOrEmpty(app.Configuration.GetConnectionString("skoleoverblikket-db")))
			{
				_ = Task.Run(() => app.Services.SeedAsync());
			}
		}
	}
}
