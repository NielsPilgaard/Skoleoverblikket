using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Databehandleraftale: admins see whether the school accepted the current version and accept it
/// on the school's behalf; acceptance is per school; the superadmin sub-processor notice reaches
/// every school's admins in Bcc and refuses less than 30 days' notice.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel(nameof(DataProcessingAgreementTests))] // The notice emails every school, including other tests' schools.
public sealed class DataProcessingAgreementTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

	private async Task<(Guid SchoolId, HttpClient Admin)> SeedSchoolAsync(string? adminEmail = null)
	{
		var schoolId = Guid.NewGuid();
		var subject = $"dpa-admin-{schoolId:N}";
		await TestDataBuilder.CreateSchoolAsync(factory.Services, schoolId);
		var admin = await TestDataBuilder.CreateStaffAsync(factory.Services, schoolId, "Hanne Kontor", isAdmin: true, keycloakSubject: subject);
		if (adminEmail is not null)
		{
			using var scope = factory.Services.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
			db.Staff.Attach(admin).Entity.Email = adminEmail;
			await db.SaveChangesAsync();
		}

		return (schoolId, Client(schoolId, "admin", subject));
	}

	private HttpClient Client(Guid schoolId, string roles, string subject)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", schoolId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", roles);
		client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		return client;
	}

	private static async Task<DataProcessingAgreementStatusDto> StatusAsync(HttpClient client) =>
		(await client.GetFromJsonAsync<DataProcessingAgreementStatusDto>("/api/v1/data-processing-agreement", JsonOpts))!;

	private static Task<HttpResponseMessage> AcceptAsync(HttpClient client, string version) =>
		client.PostAsJsonAsync("/api/v1/data-processing-agreement/acceptance", new AcceptDataProcessingAgreementRequest(version), JsonOpts);

	[Test]
	public async Task NewSchool_HasNotAccepted()
	{
		var (_, admin) = await SeedSchoolAsync();

		var status = await StatusAsync(admin);

		await Assert.That(status.CurrentVersion).IsEqualTo(DataProcessingAgreementService.CurrentVersion);
		await Assert.That(status.AcceptedCurrentVersion).IsFalse();
		await Assert.That(status.AcceptedAt).IsNull();
	}

	[Test]
	public async Task Admin_Accepts_RecordsWhoAndWhen_OnlyForThatSchool()
	{
		var (_, admin) = await SeedSchoolAsync();
		var (_, otherAdmin) = await SeedSchoolAsync();

		var response = await AcceptAsync(admin, DataProcessingAgreementService.CurrentVersion);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		// A second click is harmless.
		response = await AcceptAsync(admin, DataProcessingAgreementService.CurrentVersion);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var status = await StatusAsync(admin);
		await Assert.That(status.AcceptedCurrentVersion).IsTrue();
		await Assert.That(status.AcceptedVersion).IsEqualTo(DataProcessingAgreementService.CurrentVersion);
		await Assert.That(status.AcceptedByName).IsEqualTo("Hanne Kontor");
		await Assert.That(status.AcceptedAt).IsNotNull();

		await Assert.That((await StatusAsync(otherAdmin)).AcceptedCurrentVersion).IsFalse();
	}

	[Test]
	public async Task OutdatedVersion_Returns409()
	{
		var (_, admin) = await SeedSchoolAsync();

		var response = await AcceptAsync(admin, "0.9");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
		await Assert.That((await StatusAsync(admin)).AcceptedCurrentVersion).IsFalse();
	}

	[Test]
	public async Task NonAdmin_Returns403()
	{
		var (schoolId, _) = await SeedSchoolAsync();
		var teacher = Client(schoolId, "user", $"dpa-teacher-{schoolId:N}");

		await Assert.That((await teacher.GetAsync("/api/v1/data-processing-agreement")).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That((await AcceptAsync(teacher, DataProcessingAgreementService.CurrentVersion)).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task AdminWithoutStaffRow_CannotAccept()
	{
		// A superadmin viewing the school as admin is not one of its staff.
		var (schoolId, _) = await SeedSchoolAsync();
		var outsider = Client(schoolId, "admin", $"outsider-{schoolId:N}");

		var response = await AcceptAsync(outsider, DataProcessingAgreementService.CurrentVersion);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task SubProcessorNotice_ReachesAdminsOfEverySchool_InBcc()
	{
		var emailA = $"a-{Guid.NewGuid():N}@skole.dk";
		var emailB = $"b-{Guid.NewGuid():N}@skole.dk";
		await SeedSchoolAsync(emailA);
		await SeedSchoolAsync(emailB);
		var superAdmin = Client(Guid.NewGuid(), "superadmin", "superadmin-subject");

		var response = await superAdmin.PostAsJsonAsync("/api/v1/admin/sub-processor-notice",
			new SubProcessorNoticeRequest("Vi tilføjer Alexandra Instituttet (AI-forslag til skemaer, Danmark).",
				DateOnly.FromDateTime(DateTime.UtcNow.AddDays(31))), JsonOpts);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		foreach (var address in new[] { emailA, emailB })
		{
			var received = factory.Emails.To(address);
			await Assert.That(received.Count).IsEqualTo(1);
			await Assert.That(received[0].To).IsNotEqualTo(address); // Bcc only: schools never see each other.
			await Assert.That(received[0].HtmlBody).Contains("Alexandra Instituttet");
		}
	}

	[Test]
	public async Task SubProcessorNotice_LessThan30Days_IsRefused()
	{
		var email = $"c-{Guid.NewGuid():N}@skole.dk";
		await SeedSchoolAsync(email);
		var superAdmin = Client(Guid.NewGuid(), "superadmin", "superadmin-subject");

		var response = await superAdmin.PostAsJsonAsync("/api/v1/admin/sub-processor-notice",
			new SubProcessorNoticeRequest("Ny leverandør", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10))), JsonOpts);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That(factory.Emails.To(email).Count).IsEqualTo(0);
	}

	[Test]
	public async Task SubProcessorNotice_RequiresSuperAdmin()
	{
		var (_, admin) = await SeedSchoolAsync();

		var response = await admin.PostAsJsonAsync("/api/v1/admin/sub-processor-notice",
			new SubProcessorNoticeRequest("Ny leverandør", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(40))), JsonOpts);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}
}
