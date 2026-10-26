using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Skoleoverblikket.Api.Auth;

namespace Skoleoverblikket.Api.Observability;

/// <summary>
/// Public <c>/api/health</c>: is the API able to serve requests (database and Keycloak reachable)?
/// The CD watch job and uptime monitoring poll it. <c>/alive</c> stays the container healthcheck,
/// since a Keycloak outage must not make Docker restart the API.
/// </summary>
public static class ReadinessExtensions
{
	public const string Path = "/api/health";
	public const string VersionHeader = "X-App-Version";
	public const string KeycloakCheckName = "keycloak";
	private const string ReadyTag = "ready";

	public static IServiceCollection AddReadinessChecks(this IServiceCollection services)
	{
		services.AddHealthChecks()
			.AddNpgSql(
				sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("skoleoverblikket-db")!,
				name: "postgres",
				tags: [ReadyTag],
				timeout: TimeSpan.FromSeconds(5))
			.AddUrlGroup(
				sp => DiscoveryUri(sp.GetRequiredService<IOptions<KeycloakOptions>>().Value),
				name: KeycloakCheckName,
				tags: [ReadyTag],
				timeout: TimeSpan.FromSeconds(5));

		return services;
	}

	public static WebApplication MapReadinessEndpoint(this WebApplication app)
	{
		// Polled every 30 s by CD and the uptime monitor, so not logged.
		app.MapHealthChecks(Path, new HealthCheckOptions
		{
			Predicate = r => r.Tags.Contains(ReadyTag),
			ResponseWriter = WriteResponseAsync,
		})
			.AllowAnonymous()
			.WithHttpLogging(HttpLoggingFields.None);

		return app;
	}

	// Status only: the endpoint is public, so no check names, errors or durations.
	private static Task WriteResponseAsync(HttpContext context, HealthReport report)
	{
		var options = context.RequestServices.GetRequiredService<IOptions<ApplicationOptions>>().Value;
		context.Response.Headers[VersionHeader] = options.Version;
		context.Response.Headers.CacheControl = "no-store";
		context.Response.ContentType = "text/plain";
		return context.Response.WriteAsync(report.Status.ToString());
	}

	private static Uri DiscoveryUri(KeycloakOptions keycloak) =>
		new(string.IsNullOrEmpty(keycloak.MetadataAddress)
			? $"{keycloak.Authority.TrimEnd('/')}/.well-known/openid-configuration"
			: keycloak.MetadataAddress);
}
