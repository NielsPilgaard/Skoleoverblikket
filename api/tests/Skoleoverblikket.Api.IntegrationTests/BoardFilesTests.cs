using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// The bestyrelse file area (/api/v1/board-files): folders, upload (presign, storage PUT, confirm),
/// listing and search, and the rule that only admins delete. Module gating is in <see cref="ModuleAccessTests"/>.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class BoardFilesTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		Converters = { new JsonStringEnumConverter() },
		PropertyNameCaseInsensitive = true,
	};

	private readonly Guid _tenantId = Guid.NewGuid();
	private HttpClient _admin = null!;
	private HttpClient _board = null!;

	[Before(Test)]
	public async Task SetUp()
	{
		await TestDataBuilder.CreateSchoolAsync(factory.Services, _tenantId);
		await TestDataBuilder.CreateTrialSubscriptionAsync(factory.Services, _tenantId);
		await TestDataBuilder.CreateBoardMemberAsync(factory.Services, _tenantId, "board-files-member");

		_admin = Client(_tenantId);
		_board = Client(_tenantId, "board-files-member");
	}

	[Test]
	public async Task Folders_AreListedUnderTheirParent_AndCanBeRenamed()
	{
		var referater = await CreateFolderAsync(_admin, "Referater");
		var y2026 = await CreateFolderAsync(_admin, "  2026  ", referater.Id);
		await Assert.That(y2026.Name).IsEqualTo("2026");

		var root = await ListAsync(_admin);
		await Assert.That(root.Folders.Select(f => f.Id)).Contains(referater.Id);
		await Assert.That(root.Folders.Select(f => f.Id)).DoesNotContain(y2026.Id);

		var inside = await ListAsync(_admin, $"?folderId={referater.Id}");
		await Assert.That(inside.Folders.Select(f => f.Id)).IsEquivalentTo([y2026.Id]);

		var rename = await _board.PatchAsJsonAsync(
			$"/api/v1/board-files/folders/{referater.Id}",
			new BoardFilesController.RenameFolderRequest("Mødereferater"));
		await Assert.That(rename.StatusCode).IsEqualTo(HttpStatusCode.OK);

		root = await ListAsync(_admin);
		await Assert.That(root.Folders.Single(f => f.Id == referater.Id).Name).IsEqualTo("Mødereferater");
	}

	[Test]
	public async Task CreateFolder_WithUnknownParent_Returns400()
	{
		var response = await _admin.PostAsJsonAsync("/api/v1/board-files/folders",
			new BoardFilesController.CreateFolderRequest("Bilag", Guid.NewGuid()));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task BoardMember_UploadsFileIntoFolder_AndConfirmIsIdempotent()
	{
		var folder = await CreateFolderAsync(_board, "Budget");

		var presign = await PresignAndUploadAsync(_board, "budget-2027.pdf", folder.Id);
		var confirm = await _board.PostAsJsonAsync("/api/v1/board-files/confirm",
			new BoardFilesController.ConfirmRequest(presign.ConfirmToken));
		await Assert.That(confirm.StatusCode).IsEqualTo(HttpStatusCode.Created);
		var file = (await confirm.Content.ReadFromJsonAsync<BoardFilesController.BoardFileDto>(JsonOpts))!;
		await Assert.That(file.Id).IsEqualTo(presign.FileId);
		await Assert.That(file.FileName).IsEqualTo("budget-2027.pdf");
		await Assert.That(file.FolderId).IsEqualTo(folder.Id);

		// A retried confirm (double click, flaky network) must not create a second row.
		var again = await _board.PostAsJsonAsync("/api/v1/board-files/confirm",
			new BoardFilesController.ConfirmRequest(presign.ConfirmToken));
		await Assert.That(again.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var inFolder = await ListAsync(_board, $"?folderId={folder.Id}");
		await Assert.That(inFolder.Files.Select(f => f.Id)).IsEquivalentTo([file.Id]);

		var atRoot = await ListAsync(_board);
		await Assert.That(atRoot.Files.Select(f => f.Id)).DoesNotContain(file.Id);
	}

	[Test]
	public async Task Confirm_WithTamperedToken_Returns400()
	{
		var presign = await PresignAndUploadAsync(_admin, "vedtaegter.pdf", null);

		var response = await _admin.PostAsJsonAsync("/api/v1/board-files/confirm",
			new BoardFilesController.ConfirmRequest(presign.ConfirmToken + "x"));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That((await ListAsync(_admin)).Files).IsEmpty();
	}

	[Test]
	public async Task Confirm_WithTokenFromAnotherSchool_IsForbidden()
	{
		var otherTenant = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(factory.Services, otherTenant, "Anden Skole");
		await TestDataBuilder.CreateTrialSubscriptionAsync(factory.Services, otherTenant);
		var presign = await PresignAndUploadAsync(_admin, "regnskab.pdf", null);

		var response = await Client(otherTenant).PostAsJsonAsync("/api/v1/board-files/confirm",
			new BoardFilesController.ConfirmRequest(presign.ConfirmToken));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task Search_FindsFilesAndFoldersInAnyFolder_IgnoringCase()
	{
		var folder = await CreateFolderAsync(_admin, "Generalforsamling");
		var nested = await UploadAsync(_admin, "Dagsorden-april.pdf", folder.Id);
		await UploadAsync(_admin, "regnskab.pdf", null);

		var byFile = await ListAsync(_admin, "?search=DAGSORDEN");
		await Assert.That(byFile.Files.Select(f => f.Id)).IsEquivalentTo([nested.Id]);
		await Assert.That(byFile.Folders).IsEmpty();

		var byFolder = await ListAsync(_admin, "?search=generalfor");
		await Assert.That(byFolder.Folders.Select(f => f.Id)).IsEquivalentTo([folder.Id]);
		await Assert.That(byFolder.Files).IsEmpty();
	}

	[Test]
	public async Task BoardMember_CannotDeleteFilesOrFolders()
	{
		var folder = await CreateFolderAsync(_admin, "Fortroligt");
		var file = await UploadAsync(_admin, "personsag.pdf", folder.Id);

		await Assert.That((await _board.DeleteAsync($"/api/v1/board-files/{file.Id}")).StatusCode)
			.IsEqualTo(HttpStatusCode.Forbidden);
		await Assert.That((await _board.DeleteAsync($"/api/v1/board-files/folders/{folder.Id}")).StatusCode)
			.IsEqualTo(HttpStatusCode.Forbidden);

		var inFolder = await ListAsync(_admin, $"?folderId={folder.Id}");
		await Assert.That(inFolder.Files.Select(f => f.Id)).IsEquivalentTo([file.Id]);
	}

	[Test]
	public async Task Admin_DeletesFile()
	{
		var file = await UploadAsync(_admin, "gammel.pdf", null);

		await Assert.That((await _admin.DeleteAsync($"/api/v1/board-files/{file.Id}")).StatusCode)
			.IsEqualTo(HttpStatusCode.NoContent);
		await Assert.That((await ListAsync(_admin)).Files).IsEmpty();
		await Assert.That((await _admin.DeleteAsync($"/api/v1/board-files/{file.Id}")).StatusCode)
			.IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task Admin_DeletingFolder_RemovesSubfoldersAndTheirFiles()
	{
		var parent = await CreateFolderAsync(_admin, "2025");
		var child = await CreateFolderAsync(_admin, "Bilag", parent.Id);
		await UploadAsync(_admin, "top.pdf", parent.Id);
		await UploadAsync(_admin, "nested.pdf", child.Id);
		var kept = await UploadAsync(_admin, "rod.pdf", null);

		var response = await _admin.DeleteAsync($"/api/v1/board-files/folders/{parent.Id}");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var body = (await response.Content.ReadFromJsonAsync<BoardFilesController.DeleteFolderResponse>(JsonOpts))!;
		await Assert.That(body.Warnings).IsEmpty();

		var root = await ListAsync(_admin);
		await Assert.That(root.Folders).IsEmpty();
		await Assert.That(root.Files.Select(f => f.Id)).IsEquivalentTo([kept.Id]);
		await Assert.That((await ListAsync(_admin, "?search=pdf")).Files.Select(f => f.Id)).IsEquivalentTo([kept.Id]);
	}

	[Test]
	public async Task FoldersFromAnotherSchool_CannotBeRenamedOrDeleted()
	{
		var otherTenant = Guid.NewGuid();
		await TestDataBuilder.CreateSchoolAsync(factory.Services, otherTenant, "Anden Skole");
		await TestDataBuilder.CreateTrialSubscriptionAsync(factory.Services, otherTenant);
		var other = Client(otherTenant);
		var folder = await CreateFolderAsync(_admin, "Kun vores");

		var rename = await other.PatchAsJsonAsync($"/api/v1/board-files/folders/{folder.Id}",
			new BoardFilesController.RenameFolderRequest("Overtaget"));
		await Assert.That(rename.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
		await Assert.That((await other.DeleteAsync($"/api/v1/board-files/folders/{folder.Id}")).StatusCode)
			.IsEqualTo(HttpStatusCode.NotFound);
		await Assert.That((await ListAsync(other)).Folders).IsEmpty();

		var root = await ListAsync(_admin);
		await Assert.That(root.Folders.Single(f => f.Id == folder.Id).Name).IsEqualTo("Kun vores");
	}

	private HttpClient Client(Guid tenantId, string? subject = null)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", tenantId.ToString());
		if (subject is not null)
		{
			client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
		}

		return client;
	}

	private static async Task<BoardFilesController.BoardFilesResponseDto> ListAsync(HttpClient client, string query = "")
	{
		var response = await client.GetAsync($"/api/v1/board-files{query}");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return (await response.Content.ReadFromJsonAsync<BoardFilesController.BoardFilesResponseDto>(JsonOpts))!;
	}

	private static async Task<BoardFilesController.BoardFolderDto> CreateFolderAsync(
		HttpClient client, string name, Guid? parentId = null)
	{
		var response = await client.PostAsJsonAsync("/api/v1/board-files/folders",
			new BoardFilesController.CreateFolderRequest(name, parentId));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
		return (await response.Content.ReadFromJsonAsync<BoardFilesController.BoardFolderDto>(JsonOpts))!;
	}

	private static async Task<BoardFilesController.PresignResponse> PresignAndUploadAsync(
		HttpClient client, string fileName, Guid? folderId)
	{
		const int size = 16;
		var response = await client.PostAsJsonAsync("/api/v1/board-files/presign",
			new BoardFilesController.PresignRequest(fileName, size, folderId));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var presign = (await response.Content.ReadFromJsonAsync<BoardFilesController.PresignResponse>(JsonOpts))!;

		using var storage = new HttpClient();
		var content = new ByteArrayContent(new byte[size]);
		content.Headers.ContentType = new MediaTypeHeaderValue(presign.ContentType);
		await Assert.That((await storage.PutAsync(presign.UploadUrl, content)).StatusCode).IsEqualTo(HttpStatusCode.OK);
		return presign;
	}

	private static async Task<BoardFilesController.BoardFileDto> UploadAsync(
		HttpClient client, string fileName, Guid? folderId)
	{
		var presign = await PresignAndUploadAsync(client, fileName, folderId);
		var confirm = await client.PostAsJsonAsync("/api/v1/board-files/confirm",
			new BoardFilesController.ConfirmRequest(presign.ConfirmToken));
		await Assert.That(confirm.StatusCode).IsEqualTo(HttpStatusCode.Created);
		return (await confirm.Content.ReadFromJsonAsync<BoardFilesController.BoardFileDto>(JsonOpts))!;
	}
}
