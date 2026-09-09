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
[Route("api/v1/files")]
[Authorize]
public sealed class FilesController(
	AppDbContext db,
	ITenantContext tenant,
	FileUploadService uploads) : ControllerBase
{
	public record FileDto(
		Guid Id,
		string FileName,
		string ContentType,
		long SizeBytes,
		string Url,
		Guid? CourseId,
		string? CourseName,
		Guid? FolderId,
		string UploadedBy,
		DateTimeOffset UploadedAt);

	public record FolderDto(
		Guid Id,
		string Name,
		Guid? ParentId,
		Guid? CourseId,
		string? CourseName,
		DateTimeOffset CreatedAt);

	[HttpGet]
	public async Task<ActionResult<FilesResponseDto>> GetAll(
		[FromQuery] Guid? courseId,
		[FromQuery] Guid? folderId,
		[FromQuery] string? search,
		CancellationToken cancellationToken)
	{
		var isSearching = !string.IsNullOrWhiteSpace(search);

		var fileQuery = db.SchoolFiles
			.AsNoTracking()
			.Include(f => f.Course)
			.AsQueryable();

		if (courseId.HasValue)
		{
			fileQuery = fileQuery.Where(f => f.CourseId == courseId.Value);
		}

		if (isSearching)
		{
			var term = search!.Trim().ToLower();
			fileQuery = fileQuery.Where(f =>
				f.FileName.ToLower().Contains(term) ||
				(f.Course != null && f.Course.Name.ToLower().Contains(term)));
		}
		else if (folderId.HasValue)
		{
			fileQuery = fileQuery.Where(f => f.FolderId == folderId.Value);
		}
		else if (!courseId.HasValue)
		{
			fileQuery = fileQuery.Where(f => f.FolderId == null);
		}

		var files = await fileQuery
			.OrderByDescending(f => f.UploadedAt)
			.Select(f => new FileDto(
				f.Id,
				f.FileName,
				f.ContentType,
				f.SizeBytes,
				f.Url,
				f.CourseId,
				f.Course != null ? f.Course.Name : null,
				f.FolderId,
				f.UploadedBy,
				f.UploadedAt))
			.ToListAsync(cancellationToken);

		var folderQuery = db.SchoolFileFolders
			.AsNoTracking()
			.Include(f => f.Course)
			.AsQueryable();

		if (courseId.HasValue)
		{
			folderQuery = folderQuery.Where(f => f.CourseId == courseId.Value);
		}

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
			.Select(f => new FolderDto(f.Id, f.Name, f.ParentId, f.CourseId, f.Course != null ? f.Course.Name : null, f.CreatedAt))
			.ToListAsync(cancellationToken);

		return Ok(new FilesResponseDto(files, folders));
	}

	public record FilesResponseDto(List<FileDto> Files, List<FolderDto> Folders);

	public record PresignRequest(
		[Required] string FileName,
		[Required, Range(1, 524288000)] long FileSizeBytes,
		Guid? CourseId,
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

		var usedBytes = await uploads.GetUsedBytesAsync<SchoolFile>(cancellationToken);
		if (FileUploadService.WouldExceedQuota(usedBytes, req.FileSizeBytes))
		{
			return ValidationProblem(new ValidationProblemDetails
			{
				Errors = { ["file"] = ["Lagerkvoten er nået (100 GB). Slet filer for at frigøre plads."] }
			});
		}

		if (req.CourseId.HasValue)
		{
			var courseExists = await db.Courses.AnyAsync(c => c.Id == req.CourseId.Value, cancellationToken);
			if (!courseExists)
			{
				return ValidationProblem(new ValidationProblemDetails
				{
					Errors = { ["courseId"] = ["Faget findes ikke."] }
				});
			}
		}

		if (req.FolderId.HasValue)
		{
			var folderExists = await db.SchoolFileFolders.AnyAsync(f => f.Id == req.FolderId.Value, cancellationToken);
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
		var key = $"files/{tenant.TenantId}/{fileId}{ext}";

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
			req.CourseId,
			req.FolderId,
			uploads.ResolveUploaderName(),
			FileUploadService.PresignExpiresAt());

		var confirmToken = uploads.SignToken(tokenPayload);

		return Ok(new PresignResponse(fileId, uploadUrl, confirmToken, contentType));
	}

	public record ConfirmRequest([Required] string ConfirmToken);

	[HttpPost("confirm")]
	public async Task<ActionResult<FileDto>> Confirm(
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

		var alreadyExists = await db.SchoolFiles.AnyAsync(f => f.Id == payload.FileId, cancellationToken);
		if (alreadyExists)
		{
			var existing = await db.SchoolFiles
				.Include(f => f.Course)
				.FirstAsync(f => f.Id == payload.FileId, cancellationToken);

			return Ok(ToDto(existing));
		}

		var schoolFile = new SchoolFile
		{
			Id = payload.FileId,
			TenantId = payload.TenantId,
			FileName = payload.FileName,
			ContentType = payload.ContentType,
			SizeBytes = payload.SizeBytes,
			StorageKey = payload.StorageKey,
			Url = payload.PublicUrl,
			CourseId = payload.CourseId,
			FolderId = payload.FolderId,
			UploadedBy = payload.UploadedBy,
		};

		db.SchoolFiles.Add(schoolFile);
		await db.SaveChangesAsync(cancellationToken);

		string? courseName = null;
		if (payload.CourseId.HasValue)
		{
			courseName = await db.Courses.AsNoTracking()
				.Where(c => c.Id == payload.CourseId.Value)
				.Select(c => c.Name)
				.FirstOrDefaultAsync(cancellationToken);
		}

		return CreatedAtAction(nameof(GetAll), new FileDto(
			schoolFile.Id,
			schoolFile.FileName,
			schoolFile.ContentType,
			schoolFile.SizeBytes,
			schoolFile.Url,
			schoolFile.CourseId,
			courseName,
			schoolFile.FolderId,
			schoolFile.UploadedBy,
			schoolFile.UploadedAt));
	}

	[HttpDelete("{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
	{
		var file = await db.SchoolFiles.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
		if (file is null)
		{
			return NotFound();
		}

		db.SchoolFiles.Remove(file);
		await db.SaveChangesAsync(cancellationToken);

		await uploads.DeleteFromStorageAsync(file.StorageKey, cancellationToken);
		return NoContent();
	}

	public record CreateFolderRequest(
		[Required, StringLength(200, MinimumLength = 1)] string Name,
		Guid? ParentId,
		Guid? CourseId);

	[HttpPost("folders")]
	public async Task<ActionResult<FolderDto>> CreateFolder(
		[FromBody] CreateFolderRequest req,
		CancellationToken cancellationToken)
	{
		if (req.ParentId.HasValue)
		{
			var parentExists = await db.SchoolFileFolders.AnyAsync(f => f.Id == req.ParentId.Value, cancellationToken);
			if (!parentExists)
			{
				return ValidationProblem(new ValidationProblemDetails
				{
					Errors = { ["parentId"] = ["Overordnet mappe findes ikke."] }
				});
			}
		}

		if (req.CourseId.HasValue)
		{
			var courseExists = await db.Courses.AnyAsync(c => c.Id == req.CourseId.Value, cancellationToken);
			if (!courseExists)
			{
				return ValidationProblem(new ValidationProblemDetails
				{
					Errors = { ["courseId"] = ["Faget findes ikke."] }
				});
			}
		}

		var folder = new SchoolFileFolder
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			Name = req.Name.Trim(),
			ParentId = req.ParentId,
			CourseId = req.CourseId,
		};

		db.SchoolFileFolders.Add(folder);
		await db.SaveChangesAsync(cancellationToken);

		string? courseName = null;
		if (req.CourseId.HasValue)
		{
			courseName = await db.Courses.AsNoTracking()
				.Where(c => c.Id == req.CourseId.Value)
				.Select(c => c.Name)
				.FirstOrDefaultAsync(cancellationToken);
		}

		return CreatedAtAction(nameof(GetAll),
			new FolderDto(folder.Id, folder.Name, folder.ParentId, folder.CourseId, courseName, folder.CreatedAt));
	}

	[HttpPatch("folders/{id:guid}")]
	public async Task<ActionResult<FolderDto>> RenameFolder(
		Guid id,
		[FromBody] RenameFolderRequest req,
		CancellationToken cancellationToken)
	{
		var folder = await db.SchoolFileFolders
			.Include(f => f.Course)
			.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
		if (folder is null)
		{
			return NotFound();
		}

		folder.Name = req.Name.Trim();
		await db.SaveChangesAsync(cancellationToken);

		return Ok(new FolderDto(folder.Id, folder.Name, folder.ParentId, folder.CourseId, folder.Course?.Name, folder.CreatedAt));
	}

	public record RenameFolderRequest([Required, StringLength(200, MinimumLength = 1)] string Name);

	public record DeleteFolderResponse(List<string> Warnings);

	[HttpDelete("folders/{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<DeleteFolderResponse>> DeleteFolder(Guid id, CancellationToken cancellationToken)
	{
		var folder = await db.SchoolFileFolders.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
		if (folder is null)
		{
			return NotFound();
		}

		var allFolderIds = await uploads.CollectFolderAndDescendantIdsAsync<SchoolFileFolder>(id, cancellationToken);

		var files = await db.SchoolFiles
			.Where(f => f.FolderId != null && allFolderIds.Contains(f.FolderId.Value))
			.ToListAsync(cancellationToken);

		var warnings = await uploads.DeleteFilesFromStorageAsync(files, cancellationToken);

		db.SchoolFiles.RemoveRange(files);
		db.SchoolFileFolders.Remove(folder);
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
		Guid? CourseId,
		Guid? FolderId,
		string UploadedBy,
		long ExpiresAt);

	private static FileDto ToDto(SchoolFile f) =>
		new(f.Id, f.FileName, f.ContentType, f.SizeBytes, f.Url,
			f.CourseId, f.Course?.Name, f.FolderId, f.UploadedBy, f.UploadedAt);
}
