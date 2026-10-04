using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// The founder's personal welcome email, sent by <see cref="WelcomeEmailJob"/> a day after self-serve
/// signup. Covers: nothing at signup or before the day is up, then one plain email to the new admin
/// with Niels's reply-to and the automated-sending disclosure, never twice, and no email when signup
/// fails because the email already has a login. Keycloak is replaced by <see cref="FakeKeycloak"/>.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
// The job sends every due school's email, so two tests running it at once could both send one.
[NotInParallel(nameof(SchoolSignupWelcomeEmailTests))]
public sealed class SchoolSignupWelcomeEmailTests(ApiFactory factory)
{
	private readonly FakeKeycloak _keycloak = new();

	private WebApplicationFactory<Program> SignupFactory() =>
		factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
		{
			services.RemoveAll<IKeycloakAdminApi>();
			services.RemoveAll<IKeycloakTokenApi>();
			services.AddSingleton<IKeycloakAdminApi>(_keycloak);
			services.AddSingleton<IKeycloakTokenApi>(_keycloak);
		}));

	private static CreateTenantRequest Signup(string email) => new(
		Name: "Velkomst Friskole",
		AdminEmail: email,
		AdminFirstName: "Hanne",
		AdminLastName: "Kontor",
		AdminPassword: "hemmeligt-123",
		AcceptDataProcessingAgreement: true);

	private Task RunJobAsync(TimeSpan afterSignup) =>
		WelcomeEmailJob.RunAsync(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance,
			DateTimeOffset.UtcNow + afterSignup, CancellationToken.None);

	[Test]
	public async Task Signup_SendsOnePersonalWelcomeEmailToAdminADayLater()
	{
		var email = $"welcome-{Guid.NewGuid():N}@skole.dk";
		await using var signupFactory = SignupFactory();

		var response = await signupFactory.CreateClient().PostAsJsonAsync("/api/v1/tenants", Signup(email));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(factory.Emails.To(email).Count).IsEqualTo(0);

		await RunJobAsync(TimeSpan.FromHours(23));
		await Assert.That(factory.Emails.To(email).Count).IsEqualTo(0);

		await RunJobAsync(TimeSpan.FromHours(25));
		await RunJobAsync(TimeSpan.FromHours(26));
		var sent = factory.Emails.To(email);
		await Assert.That(sent.Count).IsEqualTo(1);
		var welcome = sent[0];
		await Assert.That(welcome.ReplyTo).IsEqualTo("niels@skoleoverblikket.dk");
		await Assert.That(welcome.FromName).IsEqualTo("Niels Pilgaard");
		await Assert.That(welcome.Subject).Contains("Hanne");
		await Assert.That(welcome.PlainTextBody!).Contains("Hej Hanne,");
		await Assert.That(welcome.PlainTextBody!).Contains("Velkomst Friskole");
		await Assert.That(welcome.PlainTextBody!).Contains("sendt automatisk");

		// Looks typed in a mail client: none of EmailTemplate's branded markup.
		await Assert.That(welcome.HtmlBody).DoesNotContain("<style");
		await Assert.That(welcome.HtmlBody).DoesNotContain("<img");
		await Assert.That(welcome.HtmlBody).DoesNotContain("class=\"btn");
	}

	[Test]
	public async Task Signup_WithEmailThatAlreadyHasLogin_SendsNoWelcomeEmail()
	{
		var email = $"taken-{Guid.NewGuid():N}@skole.dk";
		_keycloak.AddExistingUser(email);
		await using var signupFactory = SignupFactory();

		var response = await signupFactory.CreateClient().PostAsJsonAsync("/api/v1/tenants", Signup(email));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
		await RunJobAsync(TimeSpan.FromHours(25));
		await Assert.That(factory.Emails.To(email).Count).IsEqualTo(0);
	}
}
