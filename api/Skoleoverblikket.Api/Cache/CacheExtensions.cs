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
				// Eager refresh and timeouts run the factory after the response is sent, so factories
				// must not touch the request's services. Go through TenantCache, never IFusionCache.
				o.EagerRefreshThreshold = 0.9f;
				o.FactorySoftTimeout = TimeSpan.FromSeconds(1);
				o.FactoryHardTimeout = TimeSpan.FromSeconds(5);
			});
		services.AddScoped<TenantCache>();

		return services;
	}
}
