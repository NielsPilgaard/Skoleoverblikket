using System.Diagnostics;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Tenancy;
using ZiggyCreatures.Caching.Fusion;

namespace Skoleoverblikket.Api.Cache;

/// <summary>
/// Read-through cache for tenant data whose factories may outlive the request (eager refresh, soft
/// and hard timeouts). Each factory gets its own <see cref="AppDbContext"/>, in a scope pinned to the
/// caller's tenant, and runs without the request's ExecutionContext. That holds on every call, so a
/// factory reaching for HttpContext fails at once instead of only on a rare background refresh.
/// Pass a static method as the factory and everything it needs as <c>state</c>: a closure over the
/// caller's DbContext would be disposed by the time a background refresh runs.
/// </summary>
public sealed class TenantCache(IFusionCache cache, ITenantContext tenant, IServiceScopeFactory scopeFactory)
{
	public ValueTask<TValue> GetOrSetAsync<TState, TValue>(
		string key,
		TState state,
		Func<AppDbContext, TState, CancellationToken, Task<TValue>> factory,
		Action<FusionCacheEntryOptions>? setupOptions = null,
		CancellationToken cancellationToken = default)
	{
		var tenantId = tenant.TenantId;

		return cache.GetOrSetAsync<TValue>(
			key,
			token => RunDetachedAsync(tenantId, state, factory, token),
			setupOptions ?? (static _ => { }),
			cancellationToken);
	}

	private Task<TValue> RunDetachedAsync<TState, TValue>(
		Guid tenantId,
		TState state,
		Func<AppDbContext, TState, CancellationToken, Task<TValue>> factory,
		CancellationToken cancellationToken)
	{
		var parentActivity = Activity.Current;

		using (ExecutionContext.SuppressFlow())
		{
			return Task.Run(
				async () =>
				{
					// Suppressing the flow drops the trace too; keep the factory's queries on the request's.
					Activity.Current = parentActivity;

					await using var scope = scopeFactory.CreateAsyncScope();
					scope.ServiceProvider.GetRequiredService<HttpTenantContext>().UseBackgroundTenant(tenantId);
					var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

					return await factory(db, state, cancellationToken);
				},
				cancellationToken);
		}
	}
}
