using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// The admin's school settings page: name, contact details and logo.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class SchoolSettingsTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

	private readonly Guid _tenantId = Guid.NewGuid();
	private HttpClient _client = null!;

	[Before(Test)]
	public async Task SetUp()
	{
		await TestDataBuilder.CreateSchoolAsync(factory.Services, _tenantId, name: "Solskolen");
		_client = factory.CreateClient();
		_client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
	}

	[Test]
	public async Task UpdateSettings_IsReturnedByGet()
	{
		var update = await _client.PutAsJsonAsync("/api/v1/schools/settings",
			new { name = "Solskolen Friskole", contactEmail = "kontor@solskolen.dk", contactPhone = "12345678" });
		await Assert.That(update.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var settings = await _client.GetFromJsonAsync<SchoolsController.SchoolSettingsDto>("/api/v1/schools/settings", JsonOpts);
		await Assert.That(settings!.Name).IsEqualTo("Solskolen Friskole");
		await Assert.That(settings.ContactEmail).IsEqualTo("kontor@solskolen.dk");
		await Assert.That(settings.ContactPhone).IsEqualTo("12345678");
	}

	[Test]
	public async Task UpdateSettings_DoesNotTouchOtherSchool()
	{
		var otherTenantId = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(factory.Services, otherTenantId, name: "Anden Skole");

		await _client.PutAsJsonAsync("/api/v1/schools/settings", new { name = "Nyt Navn" });

		var otherClient = factory.CreateClient();
		otherClient.DefaultRequestHeaders.Add("X-Test-TenantId", otherTenantId.ToString());
		var other = await otherClient.GetFromJsonAsync<SchoolsController.SchoolSettingsDto>("/api/v1/schools/settings", JsonOpts);
		await Assert.That(other!.Name).IsEqualTo("Anden Skole");
	}

	[Test]
	public async Task UploadLogo_SetsLogoAndCompletesOnboardingStep()
	{
		// Read the status first so the cached value has to be invalidated by the upload.
		var before = await _client.GetFromJsonAsync<SchoolsController.OnboardingStatusDto>("/api/v1/schools/onboarding-status", JsonOpts);
		await Assert.That(before!.HasLogo).IsFalse();

		var response = await UploadLogoAsync("logo.PNG", 1024);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var settings = await response.Content.ReadFromJsonAsync<SchoolsController.SchoolSettingsDto>(JsonOpts);
		await Assert.That(settings!.LogoUrl).EndsWith($"logos/{_tenantId}.png");

		var after = await _client.GetFromJsonAsync<SchoolsController.OnboardingStatusDto>("/api/v1/schools/onboarding-status", JsonOpts);
		await Assert.That(after!.HasLogo).IsTrue();
	}

	[Test]
	public async Task UploadLogo_ReplacesEarlierLogo()
	{
		await UploadLogoAsync("logo.png", 1024);

		var response = await UploadLogoAsync("logo.jpg", 1024);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var settings = await _client.GetFromJsonAsync<SchoolsController.SchoolSettingsDto>("/api/v1/schools/settings", JsonOpts);
		await Assert.That(settings!.LogoUrl).EndsWith($"logos/{_tenantId}.jpg");
	}

	[Test]
	public async Task UploadLogo_UnsupportedType_ReturnsValidationProblem()
	{
		var response = await UploadLogoAsync("logo.gif", 1024);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That(await FileErrorAsync(response)).IsEqualTo("Kun PNG, JPG og WebP er tilladt.");
	}

	[Test]
	public async Task UploadLogo_TooLarge_ReturnsValidationProblem()
	{
		var response = await UploadLogoAsync("logo.png", 2 * 1024 * 1024 + 1);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That(await FileErrorAsync(response)).IsEqualTo("Logo må maksimalt være 2 MB.");

		var settings = await _client.GetFromJsonAsync<SchoolsController.SchoolSettingsDto>("/api/v1/schools/settings", JsonOpts);
		await Assert.That(settings!.LogoUrl).IsNull();
	}

	private static async Task<string?> FileErrorAsync(HttpResponseMessage response)
	{
		var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(JsonOpts);
		return problem!.Errors["file"].Single();
	}

	private async Task<HttpResponseMessage> UploadLogoAsync(string fileName, int sizeBytes)
	{
		var file = new ByteArrayContent(new byte[sizeBytes]);
		file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
		using var form = new MultipartFormDataContent { { file, "file", fileName } };
		return await _client.PostAsync("/api/v1/schools/logo", form);
	}
}
