using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/board-files")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Board}")]
public sealed class BoardFilesController(
	AppDbContext db,
	ITenantContext tenant,
	FileUploadService uploads) : ControllerBase
{
	public record BoardFileDto(
		Guid Id,
		string FileName,
		string ContentType,
		long SizeBytes,
		string Url,
		Guid? FolderId,
		string UploadedBy,
		DateTimeOffset UploadedAt);

	public record BoardFolderDto(
		Guid Id,
		string Name,
		Guid? ParentId,
		DateTimeOffset CreatedAt);

	[HttpGet]
	public async Task<ActionResult<BoardFilesResponseDto>> GetAll(
		[FromQuery] Guid? folderId,
		[FromQuery] string? search,
		CancellationToken cancellationToken)
	{
		var isSearching = !string.IsNullOrWhiteSpace(search);

		var fileQuery = db.BoardFiles
			.AsNoTracking()
			.AsQueryable();

		if (isSearching)
		{
			var term = search!.Trim().ToLower();
			fileQuery = fileQuery.Where(f => f.FileName.ToLower().Contains(term));
		}
		else if (folderId.HasValue)
		{
			fileQuery = fileQuery.Where(f => f.FolderId == folderId.Value);
		}
		else
		{
			fileQuery = fileQuery.Where(f => f.FolderId == null);
		}

		var files = await fileQuery
			.OrderByDescending(f => f.UploadedAt)
			.Select(f => new BoardFileDto(
				f.Id,
				f.FileName,
				f.ContentType,
				f.SizeBytes,
				f.Url,
				f.FolderId,
				f.UploadedBy,
				f.UploadedAt))
			.ToListAsync(cancellationToken);

		var folderQuery = db.BoardFileFolders
			.AsNoTracking()
			.AsQueryable();

		if (isSearching)
		{
			var term = search!.Trim().ToLower();
			folderQuery = folderQuery.Where(f => f.Name.ToLower().Contains(term));
		}
		else
		{
			folderQuery = folderQuery.Where(f => f.ParentId == folderId);
		}

		var folders = await folderQuery
			.OrderBy(f => f.Name)
			.Select(f => new BoardFolderDto(f.Id, f.Name, f.ParentId, f.CreatedAt))
			.ToListAsync(cancellationToken);

		return Ok(new BoardFilesResponseDto(files, folders));
	}

	public record BoardFilesResponseDto(List<BoardFileDto> Files, List<BoardFolderDto> Folders);

	public record PresignRequest(
		[Required] string FileName,
		[Required, Range(1, 524288000)] long FileSizeBytes,
		Guid? FolderId);

	public record PresignResponse(
		Guid FileId,
		string UploadUrl,
		string ConfirmToken,
		string ContentType);

	[HttpPost("presign")]
	public async Task<ActionResult<PresignResponse>> Presign(
		[FromBody] PresignRequest req,
		CancellationToken cancellationToken)
	{
		if (req.FileSizeBytes > FileUploadService.MaxFileSizeBytes)
		{
			return ValidationProblem(new ValidationProblemDetails
			{
				Errors = { ["file"] = [$"Filen må maksimalt være 500 MB."] }
			});
		}

		var usedBytes = await uploads.GetUsedBytesAsync<BoardFile>(cancellationToken);
		if (FileUploadService.WouldExceedQuota(usedBytes, req.FileSizeBytes))
		{
			return ValidationProblem(new ValidationProblemDetails
			{
				Errors = { ["file"] = ["Lagerkvoten er nået (100 GB). Slet filer for at frigøre plads."] }
			});
		}

		if (req.FolderId.HasValue)
		{
			var folderExists = await db.BoardFileFolders.AnyAsync(f => f.Id == req.FolderId.Value, cancellationToken);
			if (!folderExists)
			{
				return ValidationProblem(new ValidationProblemDetails
				{
					Errors = { ["folderId"] = ["Mappen findes ikke."] }
				});
			}
		}

		var fileId = Guid.NewGuid();
		var ext = FileUploadService.ExtensionOf(req.FileName);
		var contentType = FileUploadService.ResolveContentType(req.FileName);
		var key = $"board-files/{tenant.TenantId}/{fileId}{ext}";

		var (uploadUrl, publicUrl) = await uploads.GeneratePresignedUploadAsync(
			key, contentType, req.FileSizeBytes, cancellationToken);

		var tokenPayload = new ConfirmTokenPayload(
			fileId,
			tenant.TenantId,
			Path.GetFileName(req.FileName),
			contentType,
			req.FileSizeBytes,
			key,
			publicUrl,
			req.FolderId,
			uploads.ResolveUploaderName(),
			FileUploadService.PresignExpiresAt());

		var confirmToken = uploads.SignToken(tokenPayload);

		return Ok(new PresignResponse(fileId, uploadUrl, confirmToken, contentType));
	}

	public record ConfirmRequest([Required] string ConfirmToken);

	[HttpPost("confirm")]
	public async Task<ActionResult<BoardFileDto>> Confirm(
		[FromBody] ConfirmRequest req,
		CancellationToken cancellationToken)
	{
		if (!uploads.TryVerifyToken<ConfirmTokenPayload>(req.ConfirmToken, out var payload) || payload is null)
		{
			return BadRequest(new ProblemDetails { Title = "Ugyldigt bekræftelsestoken." });
		}

		if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > payload.ExpiresAt)
		{
			return BadRequest(new ProblemDetails { Title = "Bekræftelsestokenet er udløbet." });
		}

		if (payload.TenantId != tenant.TenantId)
		{
			return Forbid();
		}

		var alreadyExists = await db.BoardFiles.AnyAsync(f => f.Id == payload.FileId, cancellationToken);
		if (alreadyExists)
		{
			var existing = await db.BoardFiles
				.FirstAsync(f => f.Id == payload.FileId, cancellationToken);

			return Ok(ToDto(existing));
		}

		var boardFile = new BoardFile
		{
			Id = payload.FileId,
			TenantId = payload.TenantId,
			FileName = payload.FileName,
			ContentType = payload.ContentType,
			SizeBytes = payload.SizeBytes,
			StorageKey = payload.StorageKey,
			Url = payload.PublicUrl,
			FolderId = payload.FolderId,
			UploadedBy = payload.UploadedBy,
		};

		db.BoardFiles.Add(boardFile);
		await db.SaveChangesAsync(cancellationToken);

		return CreatedAtAction(nameof(GetAll), ToDto(boardFile));
	}

	[HttpDelete("{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
	{
		var file = await db.BoardFiles.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
		if (file is null)
		{
			return NotFound();
		}

		db.BoardFiles.Remove(file);
		await db.SaveChangesAsync(cancellationToken);

		await uploads.DeleteFromStorageAsync(file.StorageKey, cancellationToken);
		return NoContent();
	}

	public record CreateFolderRequest(
		[Required, StringLength(200, MinimumLength = 1)] string Name,
		Guid? ParentId);

	[HttpPost("folders")]
	public async Task<ActionResult<BoardFolderDto>> CreateFolder(
		[FromBody] CreateFolderRequest req,
		CancellationToken cancellationToken)
	{
		if (req.ParentId.HasValue)
		{
			var parentExists = await db.BoardFileFolders.AnyAsync(f => f.Id == req.ParentId.Value, cancellationToken);
			if (!parentExists)
			{
				return ValidationProblem(new ValidationProblemDetails
				{
					Errors = { ["parentId"] = ["Overordnet mappe findes ikke."] }
				});
			}
		}

		var folder = new BoardFileFolder
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			Name = req.Name.Trim(),
			ParentId = req.ParentId,
		};

		db.BoardFileFolders.Add(folder);
		await db.SaveChangesAsync(cancellationToken);

		return CreatedAtAction(nameof(GetAll),
			new BoardFolderDto(folder.Id, folder.Name, folder.ParentId, folder.CreatedAt));
	}

	[HttpPatch("folders/{id:guid}")]
	public async Task<ActionResult<BoardFolderDto>> RenameFolder(
		Guid id,
		[FromBody] RenameFolderRequest req,
		CancellationToken cancellationToken)
	{
		var folder = await db.BoardFileFolders
			.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
		if (folder is null)
		{
			return NotFound();
		}

		folder.Name = req.Name.Trim();
		await db.SaveChangesAsync(cancellationToken);

		return Ok(new BoardFolderDto(folder.Id, folder.Name, folder.ParentId, folder.CreatedAt));
	}

	public record RenameFolderRequest([Required, StringLength(200, MinimumLength = 1)] string Name);

	public record DeleteFolderResponse(List<string> Warnings);

	[HttpDelete("folders/{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<DeleteFolderResponse>> DeleteFolder(Guid id, CancellationToken cancellationToken)
	{
		var folder = await db.BoardFileFolders.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
		if (folder is null)
		{
			return NotFound();
		}

		// Collect all descendant folder IDs so we can delete their files from storage too.
		// DB cascade deletes the subfolders; SetNull on BoardFile.FolderId would orphan the files
		// in storage, so we must delete them explicitly first.
		var allFolderIds = await uploads.CollectFolderAndDescendantIdsAsync<BoardFileFolder>(id, cancellationToken);

		var files = await db.BoardFiles
			.Where(f => f.FolderId != null && allFolderIds.Contains(f.FolderId.Value))
			.ToListAsync(cancellationToken);

		var warnings = await uploads.DeleteFilesFromStorageAsync(files, cancellationToken);

		db.BoardFiles.RemoveRange(files);
		db.BoardFileFolders.Remove(folder);
		await db.SaveChangesAsync(cancellationToken);

		return Ok(new DeleteFolderResponse(warnings));
	}

	private record ConfirmTokenPayload(
		Guid FileId,
		Guid TenantId,
		string FileName,
		string ContentType,
		long SizeBytes,
		string StorageKey,
		string PublicUrl,
		Guid? FolderId,
		string UploadedBy,
		long ExpiresAt);

	private static BoardFileDto ToDto(BoardFile f) =>
		new(f.Id, f.FileName, f.ContentType, f.SizeBytes, f.Url, f.FolderId, f.UploadedBy, f.UploadedAt);
}
