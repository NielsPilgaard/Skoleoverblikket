using ZiggyCreatures.Caching.Fusion;

namespace Skoleoverblikket.Api.Cache;

public static class CacheExtensions
{
	public static IServiceCollection AddFusionCacheDefaults(this IServiceCollection services)
	{
		services.AddMemoryCache();
		services.AddFusionCache()
			.WithDefaultEntryOptions(o =>
			{
				o.Duration = TimeSpan.FromMinutes(5);
				o.IsFailSafeEnabled = true;
				o.FailSafeMaxDuration = TimeSpan.FromHours(1);
				o.FailSafeThrottleDuration = TimeSpan.FromSeconds(30);
				// Factories query the request's AppDbContext, and the tenant comes from the request's
				// JWT, so a factory must never outlive its request. No eager refresh, no soft timeout
				// and no background completion: each of those runs the factory after the response is
				// sent, against a disposed DbContext.
				o.FactoryHardTimeout = TimeSpan.FromSeconds(5);
				o.AllowTimedOutFactoryBackgroundCompletion = false;
			});

		return services;
	}
}
