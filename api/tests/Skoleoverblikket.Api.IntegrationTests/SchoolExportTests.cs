using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Storage;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// "Download everything" ZIP on /eksporter (GET /api/v1/exports/school.zip). Covers: a CSV per table
/// including archived rows and message bodies, files under their folder and file names, other
/// storage objects under andre-filer, invitation tokens left out, no rows from another school,
/// admin-only access, and the single-use download link the browser downloads natively.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class SchoolExportTests(ApiFactory factory)
{
	private static readonly byte[] PdfBytes = Encoding.UTF8.GetBytes("%PDF-1.4 skoleplan");

	private HttpClient Client(Guid schoolId, string roles)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", schoolId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", roles);
		client.DefaultRequestHeaders.Add("X-Test-Subject", $"export-{schoolId:N}");
		return client;
	}

	/// <summary>A school with a class, an archived class, a student, a message, a pending invitation and a file in a folder.</summary>
	private async Task<Guid> SeedSchoolAsync(string studentName, string invitationToken)
	{
		var schoolId = Guid.NewGuid();
		var services = factory.Services;
		await TestDataBuilder.CreateSchoolAsync(services, schoolId, "Eksport Friskole");
		var admin = await TestDataBuilder.CreateStaffAsync(services, schoolId, "Hanne Kontor", isAdmin: true);
		var teacher = await TestDataBuilder.CreateStaffAsync(services, schoolId, "Thomas Lærer");
		var (klass, _) = await TestDataBuilder.CreateClassWithSchemaAsync(services, schoolId, className: "3.a");

		using var scope = services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		db.Classes.Add(new Class { Id = Guid.NewGuid(), TenantId = schoolId, Name = "Gammel 9.x", ArchivedAt = DateTimeOffset.UtcNow });
		db.Students.Add(new Student { Id = Guid.NewGuid(), TenantId = schoolId, Name = studentName, ClassId = klass.Id });
		db.Messages.Add(new Message
		{
			Id = Guid.NewGuid(),
			TenantId = schoolId,
			SenderId = admin.Id,
			SenderType = RecipientType.Staff,
			RecipientId = teacher.Id,
			RecipientType = RecipientType.Staff,
			Subject = "Tur",
			Body = "Husk madpakke; vi går kl. 9",
			SentAt = DateTimeOffset.UtcNow,
		});
		db.StaffInvitations.Add(new StaffInvitation
		{
			Id = Guid.NewGuid(),
			TenantId = schoolId,
			StaffId = teacher.Id,
			Email = $"thomas-{schoolId:N}@skole.dk",
			Token = invitationToken,
			ExpiresAt = DateTimeOffset.UtcNow.AddDays(14),
		});

		var folder = new SchoolFileFolder { Id = Guid.NewGuid(), TenantId = schoolId, Name = "Planer" };
		db.SchoolFileFolders.Add(folder);
		var fileId = Guid.NewGuid();
		var fileKey = $"files/{schoolId}/{fileId}.pdf";
		db.SchoolFiles.Add(new SchoolFile
		{
			Id = fileId,
			TenantId = schoolId,
			FileName = "Årsplan.pdf",
			ContentType = "application/pdf",
			SizeBytes = PdfBytes.Length,
			StorageKey = fileKey,
			Url = $"https://storage.example.com/{fileKey}",
			UploadedBy = "hanne@skole.dk",
			FolderId = folder.Id,
		});
		await db.SaveChangesAsync();

		var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
		await storage.UploadAsync(fileKey, "application/pdf", new MemoryStream(PdfBytes));
		await storage.UploadAsync($"avatars/{schoolId}/students/{Guid.NewGuid()}.png", "image/png", new MemoryStream([1, 2, 3]));
		return schoolId;
	}

	private static async Task<Dictionary<string, byte[]>> ReadZipAsync(HttpResponseMessage response)
	{
		await using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
		var entries = new Dictionary<string, byte[]>();
		foreach (var entry in zip.Entries)
		{
			await using var stream = await entry.OpenAsync();
			using var buffer = new MemoryStream();
			await stream.CopyToAsync(buffer);
			entries[entry.FullName] = buffer.ToArray();
		}

		return entries;
	}

	private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

	[Test]
	public async Task Admin_DownloadsZipWithEveryTableAndFile()
	{
		var schoolId = await SeedSchoolAsync("Mikkel Eksportsen", $"tok-{Guid.NewGuid():N}");
		await SeedSchoolAsync("Anna Andenskole", $"tok-{Guid.NewGuid():N}");

		var response = await Client(schoolId, "admin").GetAsync("/api/v1/exports/school.zip");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/zip");
		await Assert.That(response.Content.Headers.ContentDisposition!.FileNameStar!).StartsWith("Eksport Friskole-alle-data-");

		var entries = await ReadZipAsync(response);
		await Assert.That(entries.Keys).Contains("LÆS MIG.txt");

		// One CSV per table in the model, so a new table can't be left out silently.
		using (var scope = factory.Services.CreateScope())
		{
			var model = scope.ServiceProvider.GetRequiredService<AppDbContext>().Model;
			foreach (var table in SchoolExportService.ExportedTables(model))
			{
				await Assert.That(entries.Keys).Contains($"data/{table.GetTableName()}.csv");
			}
		}

		var students = Text(entries["data/Students.csv"]);
		await Assert.That(students).Contains("Mikkel Eksportsen");
		await Assert.That(students).DoesNotContain("Anna Andenskole");
		await Assert.That(Text(entries["data/Classes.csv"])).Contains("Gammel 9.x");
		await Assert.That(Text(entries["data/Messages.csv"])).Contains("\"Husk madpakke; vi går kl. 9\"");
		await Assert.That(Text(entries["data/Schools.csv"]).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length).IsEqualTo(2);

		await Assert.That(entries["filer/Planer/Årsplan.pdf"]).IsEquivalentTo(PdfBytes);
		await Assert.That(entries.Keys.Any(k => k.StartsWith("andre-filer/avatars/skole/students/", StringComparison.Ordinal))).IsTrue();
	}

	[Test]
	public async Task Export_LeavesOutInvitationTokens()
	{
		var token = $"tok-{Guid.NewGuid():N}";
		var schoolId = await SeedSchoolAsync("Mikkel Token", token);

		var response = await Client(schoolId, "admin").GetAsync("/api/v1/exports/school.zip");

		var entries = await ReadZipAsync(response);
		var invitations = Text(entries["data/StaffInvitations.csv"]);
		await Assert.That(invitations).Contains($"thomas-{schoolId:N}@skole.dk");
		await Assert.That(entries.Values.Any(bytes => Text(bytes).Contains(token, StringComparison.Ordinal))).IsFalse();
	}

	[Test]
	public async Task DownloadLink_WorksOnceForTheAdminsSchool()
	{
		var schoolId = await SeedSchoolAsync("Mikkel Linksen", $"tok-{Guid.NewGuid():N}");
		await SeedSchoolAsync("Anna Linkskole", $"tok-{Guid.NewGuid():N}");

		var linkResponse = await Client(schoolId, "admin").PostAsync("/api/v1/exports/school.zip/link", null);
		await Assert.That(linkResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var link = await linkResponse.Content.ReadFromJsonAsync<SchoolExportController.ExportLinkDto>();

		// A browser download sends no bearer token, only the link.
		var browser = factory.CreateClient();
		var response = await browser.GetAsync(link!.Url);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var students = Text((await ReadZipAsync(response))["data/Students.csv"]);
		await Assert.That(students).Contains("Mikkel Linksen");
		await Assert.That(students).DoesNotContain("Anna Linkskole");

		var again = await browser.GetAsync(link.Url);
		await Assert.That(again.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task DownloadLink_RedeemedConcurrently_WorksExactlyOnce()
	{
		var schoolId = await SeedSchoolAsync("Mikkel Samtidig", $"tok-{Guid.NewGuid():N}");
		var linkResponse = await Client(schoolId, "admin").PostAsync("/api/v1/exports/school.zip/link", null);
		var link = await linkResponse.Content.ReadFromJsonAsync<SchoolExportController.ExportLinkDto>();

		// All requests wait on the same gate, so they hit the handler together.
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var requests = Enumerable.Range(0, 8).Select(async _ =>
		{
			var browser = factory.CreateClient();
			await gate.Task;
			return await browser.GetAsync(link!.Url);
		}).ToList();
		gate.SetResult();
		var responses = await Task.WhenAll(requests);

		await Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.OK)).IsEqualTo(1);
		await Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized)).IsEqualTo(responses.Length - 1);
	}

	[Test]
	public async Task DownloadLink_WithForgedOrMissingToken_IsUnauthorized()
	{
		var browser = factory.CreateClient();

		var forged = await browser.GetAsync("/api/v1/exports/school.zip/download?token=CfDJ8forged");
		var missing = await browser.GetAsync("/api/v1/exports/school.zip/download");

		await Assert.That(forged.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
		await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task NonAdmin_CannotCreateDownloadLink()
	{
		var schoolId = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(factory.Services, schoolId);

		var response = await Client(schoolId, "staff").PostAsync("/api/v1/exports/school.zip/link", null);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task NonAdmin_IsForbidden()
	{
		var schoolId = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(factory.Services, schoolId);

		var response = await Client(schoolId, "staff").GetAsync("/api/v1/exports/school.zip");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}
}
