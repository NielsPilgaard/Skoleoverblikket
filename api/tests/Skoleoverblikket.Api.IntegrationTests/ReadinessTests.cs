using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Observability;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// The public <c>/api/health</c> the CD watch job and the uptime monitor poll (task 44): 200 with the
/// deployed version while the database and Keycloak answer, 503 when Keycloak doesn't. Keycloak's
/// discovery request goes to a stub handler, so the result doesn't depend on a local Keycloak.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class ReadinessTests(ApiFactory factory)
{
	private const string Version = "0123456789abcdef";

	/// <param name="keycloakStatus">Discovery response status; null makes Keycloak unreachable.</param>
	private WebApplicationFactory<Program> WithKeycloak(HttpStatusCode? keycloakStatus) =>
		factory.WithWebHostBuilder(builder =>
		{
			builder.UseSetting("App:Version", Version);
			builder.ConfigureServices(services => services
				.AddHttpClient(ReadinessExtensions.KeycloakCheckName)
				.ConfigurePrimaryHttpMessageHandler(() => new StubHandler(keycloakStatus)));
		});

	[Test]
	public async Task Health_DatabaseAndKeycloakUp_ReturnsHealthyWithVersion()
	{
		await using var app = WithKeycloak(HttpStatusCode.OK);

		// No test auth headers: the endpoint is anonymous.
		var response = await app.CreateClient().GetAsync(ReadinessExtensions.Path);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("Healthy");
		await Assert.That(response.Headers.GetValues(ReadinessExtensions.VersionHeader).Single()).IsEqualTo(Version);
	}

	[Test]
	public async Task Health_KeycloakUnreachable_ReturnsServiceUnavailable()
	{
		await using var app = WithKeycloak(null);

		var response = await app.CreateClient().GetAsync(ReadinessExtensions.Path);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
		await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("Unhealthy");
	}

	private sealed class StubHandler(HttpStatusCode? status) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			status is { } code
				? Task.FromResult(new HttpResponseMessage(code))
				: throw new HttpRequestException("Connection refused");
	}
}
