using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Refit;

namespace Skoleoverblikket.Api.Auth;

public static class KeycloakAdminExtensions
{
	public static IServiceCollection AddKeycloakAdmin(this IServiceCollection services)
	{
		var refitSettings = new RefitSettings { TransportExceptionFactory = ToKeycloakException };

		services
			.AddRefitGeneratedClient<IKeycloakTokenApi>(refitSettings)
			.ConfigureHttpClient((provider, client) =>
			{
				var options = provider.GetRequiredService<IOptions<KeycloakOptions>>().Value;
				client.BaseAddress = new Uri(options.TokenBaseUrl);
			})
			.WithKeycloakResilience();

		services
			.AddTransient<KeycloakBearerHandler>()
			.AddRefitGeneratedClient<IKeycloakAdminApi>(refitSettings)
			.ConfigureHttpClient((provider, client) =>
			{
				var options = provider.GetRequiredService<IOptions<KeycloakOptions>>().Value;
				client.BaseAddress = new Uri(options.AdminBaseUrl);
			})
			.WithKeycloakResilience()
			.AddHttpMessageHandler<KeycloakBearerHandler>();

		services.AddScoped<KeycloakAdminService>();
		services.AddExceptionHandler<KeycloakExceptionHandler>();

		return services;
	}

	/// <summary>
	/// A Keycloak that can't be reached becomes a <see cref="KeycloakException"/>, which callers handle and
	/// <see cref="KeycloakExceptionHandler"/> turns into a 503. The admin client's token call runs inside its
	/// own pipeline, so that exception passes through here twice. It isn't transient, so the admin client
	/// doesn't retry the token client's retries. A cancelled request stays cancelled.
	/// </summary>
	private static Exception ToKeycloakException(HttpRequestMessage request, Exception exception, CancellationToken cancellationToken) =>
		exception is KeycloakException || (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
			? exception
			: new KeycloakException($"Keycloak could not be reached: {exception.Message}", exception);

	/// <summary>
	/// A person is waiting on the signup or invitation page, so give up after 10 s, not the default 30 s.
	/// Replaces the handler AddServiceDefaults puts on every client. The retries' delays and the attempt
	/// timeout must stay below the total, or the options fail validation.
	/// </summary>
	private static IHttpClientBuilder WithKeycloakResilience(this IHttpClientBuilder builder)
	{
#pragma warning disable EXTEXP0001 // Experimental, but the only way to replace a default handler for one client.
		builder.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

		builder.AddStandardResilienceHandler(options =>
		{
			options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
			options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(8);
			options.Retry.MaxRetryAttempts = 2;
		});
		return builder;
	}
}
