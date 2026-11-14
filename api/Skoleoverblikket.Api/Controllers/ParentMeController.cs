using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Storage;
using System.ComponentModel.DataAnnotations;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/parents/me")]
[Authorize(Roles = Roles.Parent)]
public sealed class ParentMeController(AppDbContext db, IObjectStorage storage) : ControllerBase
{
	public record ParentMeDto(
		Guid Id,
		string Name,
		string? AvatarUrl,
		string? Phone,
		string? Address,
		string? PostalCode,
		string? City,
		bool ShareContactInfo,
		IReadOnlyList<ParentClassDto> Classes,
		IReadOnlyList<ParentStudentDto> Students);
	public record ParentClassDto(Guid ClassId, string ClassName);
	public record ParentStudentDto(Guid StudentId, string StudentName, Guid ClassId, bool IsEnrolledInSfo);

	[HttpGet]
	public async Task<ActionResult<ParentMeDto>> GetMe(CancellationToken cancellationToken)
	{
		var subject = User.GetKeycloakSubject();

		if (subject is null)
		{
			return Unauthorized();
		}

		var parent = await db.Parents
			.AsNoTracking()
			.Include(p => p.Students).ThenInclude(s => s.Class)
			.FirstOrDefaultAsync(p => p.KeycloakSubject == subject, cancellationToken);

		if (parent is null)
		{
			return NotFound();
		}

		var classes = parent.Students
			.Select(s => new ParentClassDto(s.ClassId, s.Class?.Name ?? string.Empty))
			.DistinctBy(c => c.ClassId)
			.ToList();

		var students = parent.Students
			.Select(s => new ParentStudentDto(s.Id, s.Name, s.ClassId, s.IsEnrolledInSfo))
			.ToList();

		return Ok(new ParentMeDto(
			parent.Id,
			parent.Name,
			parent.AvatarUrl,
			parent.Phone,
			parent.Address,
			parent.PostalCode,
			parent.City,
			parent.ShareContactInfo,
			classes,
			students));
	}

	public record AvatarPresignRequest(string ContentType, long FileSizeBytes);
	public record AvatarPresignResponse(string UploadUrl, string ObjectKey);

	[HttpPost("avatar/presign")]
	public async Task<ActionResult<AvatarPresignResponse>> PresignAvatar(
		[FromBody] AvatarPresignRequest req, CancellationToken cancellationToken)
	{
		if (!IsAllowedImageContentType(req.ContentType))
		{
			return BadRequest(new { detail = "ContentType must be image/jpeg, image/png, or image/webp." });
		}

		if (req.FileSizeBytes is <= 0 or > 5 * 1024 * 1024)
		{
			return BadRequest(new { detail = "File must be between 1 byte and 5 MB." });
		}

		var subject = User.GetKeycloakSubject();
		var parent = await db.Parents.AsNoTracking()
			.FirstOrDefaultAsync(p => p.KeycloakSubject == subject, cancellationToken);

		if (parent is null)
		{
			return NotFound();
		}

		var ext = ContentTypeToExtension(req.ContentType);
		var key = $"avatars/{parent.TenantId}/parents/{parent.Id}{ext}";
		var expiry = TimeSpan.FromMinutes(15);

		var (uploadUrl, _) = await storage.GeneratePresignedUploadUrlAsync(
			key, req.ContentType, req.FileSizeBytes, expiry, cancellationToken);

		return Ok(new AvatarPresignResponse(uploadUrl, key));
	}

	public record UpdateContactRequest(
		[Required, StringLength(200, MinimumLength = 1)] string Name,
		[StringLength(50)] string? Phone,
		[StringLength(500)] string? Address,
		[StringLength(10)] string? PostalCode,
		[StringLength(100)] string? City,
		bool ShareContactInfo);

	[HttpPatch("contact")]
	public async Task<IActionResult> UpdateContact(
		[FromBody] UpdateContactRequest req, CancellationToken cancellationToken)
	{
		var subject = User.GetKeycloakSubject();

		var parent = await db.Parents
			.FirstOrDefaultAsync(p => p.KeycloakSubject == subject, cancellationToken);

		if (parent is null)
		{
			return NotFound();
		}

		var trimmedName = req.Name.Trim();
		if (trimmedName.Length == 0)
		{
			return ValidationProblem(new ValidationProblemDetails { Errors = { ["name"] = ["Navn er påkrævet."] } });
		}

		if (!ContactValidation.TryNormalizePhone(req.Phone, out var normalizedPhone))
		{
			return ValidationProblem(new ValidationProblemDetails { Errors = { ["phone"] = ["Telefonnummer skal være 8 cifre, evt. med +45 foran."] } });
		}

		if (!ContactValidation.TryNormalizePostalCode(req.PostalCode, out var normalizedPostalCode))
		{
			return ValidationProblem(new ValidationProblemDetails { Errors = { ["postalCode"] = ["Postnummer skal være 4 cifre."] } });
		}

		parent.Name = trimmedName;
		parent.Phone = normalizedPhone;
		parent.Address = req.Address;
		parent.PostalCode = normalizedPostalCode;
		parent.City = req.City;
		parent.ShareContactInfo = req.ShareContactInfo;

		await db.SaveChangesAsync(cancellationToken);
		return NoContent();
	}

	public record AvatarConfirmRequest(string ObjectKey);

	[HttpPost("avatar/confirm")]
	public async Task<IActionResult> ConfirmAvatar(
		[FromBody] AvatarConfirmRequest req, CancellationToken cancellationToken)
	{
		var subject = User.GetKeycloakSubject();
		var parent = await db.Parents
			.FirstOrDefaultAsync(p => p.KeycloakSubject == subject, cancellationToken);

		if (parent is null)
		{
			return NotFound();
		}

		var expectedPrefix = $"avatars/{parent.TenantId}/parents/{parent.Id}";
		if (!req.ObjectKey.StartsWith(expectedPrefix, StringComparison.Ordinal))
		{
			return BadRequest(new { detail = "Invalid object key." });
		}

		var (_, publicUrl) = await storage.GeneratePresignedUploadUrlAsync(
			req.ObjectKey, "image/jpeg", 1, TimeSpan.FromHours(24 * 365), cancellationToken);

		parent.AvatarUrl = publicUrl;
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
