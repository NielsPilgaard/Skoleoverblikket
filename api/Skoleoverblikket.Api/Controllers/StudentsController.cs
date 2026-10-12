using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Storage;
using Skoleoverblikket.Api.Tenancy;
using System.ComponentModel.DataAnnotations;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/students")]
[Authorize(Roles = Roles.Admin)]
[RequiresModule(SubscriptionModule.ParentModule)]
public sealed class StudentsController(AppDbContext db, ITenantContext tenant, IObjectStorage storage) : ControllerBase
{
	public record StudentDto(Guid Id, string Name, Guid ClassId, string ClassName, bool IsEnrolledInSfo, DateTimeOffset CreatedAt);
	public record UpsertStudentRequest(
		[Required, StringLength(200, MinimumLength = 1)] string Name,
		Guid ClassId,
		bool IsEnrolledInSfo);

	[HttpGet]
	public async Task<ActionResult<List<StudentDto>>> GetAll([FromQuery] Guid? classId, CancellationToken cancellationToken)
	{
		var query = db.Students.AsNoTracking().Include(s => s.Class).AsQueryable();

		if (classId.HasValue)
		{
			query = query.Where(s => s.ClassId == classId.Value);
		}

		var students = await query
			.OrderBy(s => s.Class.Name)
			.ThenBy(s => s.Name)
			.Select(s => new StudentDto(s.Id, s.Name, s.ClassId, s.Class.Name, s.IsEnrolledInSfo, s.CreatedAt))
			.ToListAsync(cancellationToken);

		return Ok(students);
	}

	[HttpPost]
	public async Task<ActionResult<StudentDto>> Create([FromBody] UpsertStudentRequest req, CancellationToken cancellationToken)
	{
		var classExists = await db.Classes.AnyAsync(c => c.Id == req.ClassId, cancellationToken);
		if (!classExists)
		{
			return ValidationProblem("ClassId does not reference a valid class.");
		}

		var student = new Student
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			Name = req.Name,
			ClassId = req.ClassId,
			IsEnrolledInSfo = req.IsEnrolledInSfo,
		};

		db.Students.Add(student);
		await db.SaveChangesAsync(cancellationToken);

		var className = await db.Classes.Where(c => c.Id == req.ClassId).Select(c => c.Name).FirstAsync(cancellationToken);
		return CreatedAtAction(nameof(GetAll), new StudentDto(student.Id, student.Name, student.ClassId, className, student.IsEnrolledInSfo, student.CreatedAt));
	}

	[HttpPut("{id:guid}")]
	public async Task<ActionResult<StudentDto>> Update(Guid id, [FromBody] UpsertStudentRequest req, CancellationToken cancellationToken)
	{
		var student = await db.Students.Include(s => s.Class).FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
		if (student is null)
		{
			return NotFound();
		}

		var classExists = await db.Classes.AnyAsync(c => c.Id == req.ClassId, cancellationToken);
		if (!classExists)
		{
			return ValidationProblem("ClassId does not reference a valid class.");
		}

		student.Name = req.Name;
		student.ClassId = req.ClassId;
		student.IsEnrolledInSfo = req.IsEnrolledInSfo;
		await db.SaveChangesAsync(cancellationToken);

		var className = await db.Classes.Where(c => c.Id == req.ClassId).Select(c => c.Name).FirstAsync(cancellationToken);
		return Ok(new StudentDto(student.Id, student.Name, student.ClassId, className, student.IsEnrolledInSfo, student.CreatedAt));
	}

	[HttpDelete("{id:guid}")]
	public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
	{
		var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
		if (student is null)
		{
			return NotFound();
		}

		db.Students.Remove(student);
		await db.SaveChangesAsync(cancellationToken);
		return NoContent();
	}

	public record AvatarPresignRequest(string ContentType, long FileSizeBytes);
	public record AvatarPresignResponse(string UploadUrl, string ObjectKey);

	[HttpPost("{id:guid}/avatar/presign")]
	public async Task<ActionResult<AvatarPresignResponse>> PresignAvatar(
		Guid id, [FromBody] AvatarPresignRequest req, CancellationToken cancellationToken)
	{
		if (!IsAllowedImageContentType(req.ContentType))
		{
			return BadRequest(new { detail = "ContentType must be image/jpeg, image/png, or image/webp." });
		}

		if (req.FileSizeBytes is <= 0 or > 5 * 1024 * 1024)
		{
			return BadRequest(new { detail = "File must be between 1 byte and 5 MB." });
		}

		var student = await db.Students.AsNoTracking()
			.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

		if (student is null)
		{
			return NotFound();
		}

		var ext = ContentTypeToExtension(req.ContentType);
		var key = $"avatars/{student.TenantId}/students/{student.Id}{ext}";
		var expiry = TimeSpan.FromMinutes(15);

		var (uploadUrl, _) = await storage.GeneratePresignedUploadUrlAsync(
			key, req.ContentType, req.FileSizeBytes, expiry, cancellationToken);

		return Ok(new AvatarPresignResponse(uploadUrl, key));
	}

	public record AvatarConfirmRequest(string ObjectKey);

	[HttpPost("{id:guid}/avatar/confirm")]
	public async Task<IActionResult> ConfirmAvatar(
		Guid id, [FromBody] AvatarConfirmRequest req, CancellationToken cancellationToken)
	{
		var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

		if (student is null)
		{
			return NotFound();
		}

		var expectedPrefix = $"avatars/{student.TenantId}/students/{student.Id}";
		if (!req.ObjectKey.StartsWith(expectedPrefix, StringComparison.Ordinal))
		{
			return BadRequest(new { detail = "Invalid object key." });
		}

		var (_, publicUrl) = await storage.GeneratePresignedUploadUrlAsync(
			req.ObjectKey, "image/jpeg", 1, TimeSpan.FromSeconds(1), cancellationToken);

		student.AvatarUrl = publicUrl;
		await db.SaveChangesAsync(cancellationToken);

		return NoContent();
	}

	private static bool IsAllowedImageContentType(string contentType) =>
		contentType is "image/jpeg" or "image/png" or "image/webp";

	private static string ContentTypeToExtension(string contentType) => contentType switch
	{
		"image/jpeg" => ".jpg",
		"image/png" => ".png",
		"image/webp" => ".webp",
		_ => ".bin",
	};
}
