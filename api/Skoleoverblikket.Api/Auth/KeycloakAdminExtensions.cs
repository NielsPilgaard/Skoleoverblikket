using Microsoft.Extensions.Options;
using Refit;

namespace Skoleoverblikket.Api.Auth;

public static class KeycloakAdminExtensions
{
	public static IServiceCollection AddKeycloakAdmin(this IServiceCollection services)
	{
		services
			.AddRefitClient<IKeycloakTokenApi>()
			.ConfigureHttpClient((provider, client) =>
			{
				var options = provider.GetRequiredService<IOptions<KeycloakOptions>>().Value;
				client.BaseAddress = new Uri(options.TokenBaseUrl);
			});

		services
			.AddTransient<KeycloakBearerHandler>()
			.AddRefitClient<IKeycloakAdminApi>()
			.ConfigureHttpClient((provider, client) =>
			{
				var options = provider.GetRequiredService<IOptions<KeycloakOptions>>().Value;
				client.BaseAddress = new Uri(options.AdminBaseUrl);
			})
			.AddHttpMessageHandler<KeycloakBearerHandler>();

		services.AddScoped<KeycloakAdminService>();
		services.AddExceptionHandler<KeycloakExceptionHandler>();

		return services;
	}
}
