using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Storage;
using Skoleoverblikket.Api.Tenancy;
using ZiggyCreatures.Caching.Fusion;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/schools")]
[Authorize(Roles = Roles.Admin)]
public sealed class SchoolsController(AppDbContext db, ITenantContext tenant, IObjectStorage storage, IFusionCache cache) : ControllerBase
{
	public record SchoolSettingsDto(string Name, string? ContactEmail, string? ContactPhone, string? LogoUrl);

	public record OnboardingStatusDto(
		bool HasLogo,
		int StaffCount,
		int ClassCount,
		int CourseCount,
		int RoomCount,
		int StepsCompleted,
		int StepsTotal);

	public record UpdateSchoolSettingsRequest(
		[Required]
		[StringLength(200, MinimumLength = 1)]
		string Name,
		[EmailAddress] string? ContactEmail,
		[Phone] string? ContactPhone);

	[HttpGet("settings")]
	public async Task<ActionResult<SchoolSettingsDto>> GetSettings(CancellationToken cancellationToken)
	{
		var school = await db.Schools
							 .AsNoTracking()
							 .IgnoreQueryFilters()
							 .Where(s => s.Id == tenant.TenantId)
							 .Select(s => new SchoolSettingsDto(s.Name, s.ContactEmail, s.ContactPhone, s.LogoUrl))
							 .FirstOrDefaultAsync(cancellationToken);

		return school is null
				   ? NotFound()
				   : Ok(school);
	}

	[HttpPut("settings")]
	public async Task<ActionResult<SchoolSettingsDto>> UpdateSettings(
		[FromBody] UpdateSchoolSettingsRequest req,
		CancellationToken cancellationToken)
	{
		var school = await db.Schools
							 .IgnoreQueryFilters()
							 .FirstOrDefaultAsync(s => s.Id == tenant.TenantId, cancellationToken);

		if (school is null)
		{
			return NotFound();
		}

		school.Name = req.Name;
		school.ContactEmail = req.ContactEmail;
		school.ContactPhone = req.ContactPhone;

		await db.SaveChangesAsync(cancellationToken);
		return Ok(new SchoolSettingsDto(school.Name, school.ContactEmail, school.ContactPhone, school.LogoUrl));
	}

	internal static string OnboardingCacheKey(Guid tenantId) => $"onboarding:{tenantId}";

	[HttpGet("onboarding-status")]
	public async Task<ActionResult<OnboardingStatusDto>> GetOnboardingStatus(
		[FromServices] OnboardingService onboarding,
		CancellationToken cancellationToken)
	{
		var result = await onboarding.GetStatusAsync(cancellationToken);

		return result is null ? NotFound() : Ok(result);
	}

	[HttpPost("logo")]
	[Consumes("multipart/form-data")]
	public async Task<ActionResult<SchoolSettingsDto>> UploadLogo(IFormFile file, CancellationToken cancellationToken)
	{
		const long maxBytes = 2 * 1024 * 1024; // 2 MB
		if (file.Length > maxBytes)
		{
			return ValidationProblem(new ValidationProblemDetails
			{
				Errors = { ["file"] = ["Logo må maksimalt være 2 MB."] }
			});
		}

		var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

		// Map extension to safe MIME type; reject unknown extensions
		var mimeType = ext switch
		{
			".png" => "image/png",
			".jpg" or ".jpeg" => "image/jpeg",
			".webp" => "image/webp",
			_ => null
		};

		if (mimeType is null)
		{
			return ValidationProblem(new ValidationProblemDetails
			{
				Errors = { ["file"] = ["Kun PNG, JPG og WebP er tilladt."] }
			});
		}

		var school = await db.Schools
							 .IgnoreQueryFilters()
							 .FirstOrDefaultAsync(s => s.Id == tenant.TenantId, cancellationToken);

		if (school is null)
		{
			return NotFound();
		}

		if (school.LogoUrl is not null)
		{
			var oldKey = storage.GetKeyFromPublicUrl(school.LogoUrl);
			if (oldKey is not null)
			{
				try { await storage.DeleteAsync(oldKey, cancellationToken); }
				catch { /* swallow — old file missing or inaccessible should not block upload */ }
			}
		}

		await using var stream = file.OpenReadStream();
		var key = $"logos/{tenant.TenantId}{ext}";
		var url = await storage.UploadPublicAsync(key, mimeType, stream, cancellationToken);

		school.LogoUrl = url;
		await db.SaveChangesAsync(cancellationToken);
		await cache.RemoveAsync(OnboardingCacheKey(tenant.TenantId), token: cancellationToken);

		return Ok(new SchoolSettingsDto(school.Name, school.ContactEmail, school.ContactPhone, school.LogoUrl));
	}
}
