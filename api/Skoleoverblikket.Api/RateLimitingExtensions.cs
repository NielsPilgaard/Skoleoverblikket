using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;

namespace Skoleoverblikket.Api;

public static class RateLimitingExtensions
{
	public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
	{
		// Behind Traefik, RemoteIpAddress is the proxy's until UseForwardedHeaders has run. Without it,
		// every visitor would share one bucket and five demo requests would block everyone.
		services.Configure<ForwardedHeadersOptions>(options =>
		{
			options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
			// The API is reachable only through Traefik, which replaces X-Forwarded-For from the internet
			// with the client's address. ForwardLimit 1 reads just that last entry, so it can't be spoofed.
			options.KnownIPNetworks.Clear();
			options.KnownProxies.Clear();
		});

		services.AddRateLimiter(options =>
		{
			options.AddPolicy("demo-request", context => PerClientIp(context, permits: 5, TimeSpan.FromMinutes(15)));
			// A school signs up once. This stops scripts creating schools and logins in bulk.
			options.AddPolicy("signup", context => PerClientIp(context, permits: 20, TimeSpan.FromHours(1)));
			options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
		});

		return services;
	}

	private static RateLimitPartition<string> PerClientIp(HttpContext context, int permits, TimeSpan window) =>
		RateLimitPartition.GetFixedWindowLimiter(
			partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
			factory: _ => new FixedWindowRateLimiterOptions
			{
				PermitLimit = permits,
				Window = window,
				QueueLimit = 0,
			});
}
