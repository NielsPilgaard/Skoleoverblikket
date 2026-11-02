using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.IntegrationTests;

[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class SchoolSignupTests(ApiFactory factory)
{
	[Test]
	public async Task KeycloakUnreachable_ShowsDanishError_NotAGeneric500()
	{
		// No Keycloak runs in tests, and this client uses the real Keycloak clients, so the call fails to connect.
		var email = $"keycloak-nede-{Guid.NewGuid():N}@skole.dk";
		var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/tenants", new CreateTenantRequest(
			Name: "Nedbrud Friskole",
			AdminEmail: email,
			AdminFirstName: "Hanne",
			AdminLastName: "Kontor",
			AdminPassword: "hemmeligt-123",
			AcceptDataProcessingAgreement: true));
		var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadGateway);
		await Assert.That(problem!.Title).IsEqualTo("Kunne ikke oprette brugerkonto");
	}
}
