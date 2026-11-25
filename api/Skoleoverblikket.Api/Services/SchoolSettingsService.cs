using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Storage;
using Skoleoverblikket.Api.Tenancy;
using ZiggyCreatures.Caching.Fusion;
using static Skoleoverblikket.Api.Controllers.SchoolsController;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// The school's own settings: name, contact details and logo.
/// </summary>
public sealed class SchoolSettingsService(
	AppDbContext db,
	ITenantContext tenant,
	IObjectStorage storage,
	IFusionCache cache,
	ILogger<SchoolSettingsService> logger)
{
	public const long MaxLogoBytes = 2 * 1024 * 1024;

	public enum LogoResult
	{
		Done,
		NotFound,
		TooLarge,
		UnsupportedType,
	}

	public async Task<SchoolSettingsDto?> GetAsync(CancellationToken cancellationToken) =>
		await CurrentSchool()
			.AsNoTracking()
			.Select(s => new SchoolSettingsDto(s.Name, s.ContactEmail, s.ContactPhone, s.LogoUrl))
			.FirstOrDefaultAsync(cancellationToken);

	public async Task<SchoolSettingsDto?> UpdateAsync(UpdateSchoolSettingsRequest req, CancellationToken cancellationToken)
	{
		var school = await CurrentSchool().FirstOrDefaultAsync(cancellationToken);
		if (school is null)
		{
			return null;
		}

		school.Name = req.Name;
		school.ContactEmail = req.ContactEmail;
		school.ContactPhone = req.ContactPhone;

		await db.SaveChangesAsync(cancellationToken);
		return ToDto(school);
	}

	/// <summary>Replaces the logo with a PNG, JPG or WebP of at most <see cref="MaxLogoBytes"/>.</summary>
	public async Task<(LogoResult Result, SchoolSettingsDto? Settings)> UploadLogoAsync(
		string fileName, long sizeBytes, Stream content, CancellationToken cancellationToken)
	{
		if (sizeBytes > MaxLogoBytes)
		{
			return (LogoResult.TooLarge, null);
		}

		var ext = Path.GetExtension(fileName).ToLowerInvariant();

		// The MIME type comes from the extension, never from the client, so only known image types are stored.
		var mimeType = ext switch
		{
			".png" => "image/png",
			".jpg" or ".jpeg" => "image/jpeg",
			".webp" => "image/webp",
			_ => null,
		};

		if (mimeType is null)
		{
			return (LogoResult.UnsupportedType, null);
		}

		var school = await CurrentSchool().FirstOrDefaultAsync(cancellationToken);
		if (school is null)
		{
			return (LogoResult.NotFound, null);
		}

		if (school.LogoUrl is not null)
		{
			var oldKey = storage.GetKeyFromPublicUrl(school.LogoUrl);
			if (oldKey is not null)
			{
				try
				{
					await storage.DeleteAsync(oldKey, cancellationToken);
				}
				catch (Exception ex)
				{
					// A missing or inaccessible old file must not block the new upload.
					logger.LogWarning(ex, "Could not delete old logo {Key} for school {TenantId}", oldKey, tenant.TenantId);
				}
			}
		}

		var key = $"logos/{tenant.TenantId}{ext}";
		school.LogoUrl = await storage.UploadPublicAsync(key, mimeType, content, cancellationToken);

		await db.SaveChangesAsync(cancellationToken);
		await cache.RemoveAsync(OnboardingCacheKey(tenant.TenantId), token: cancellationToken);

		return (LogoResult.Done, ToDto(school));
	}

	// School.TenantId is computed from Id and has no column, so the tenant filter can't run here. Matching on Id scopes it to this tenant.
	private IQueryable<School> CurrentSchool() =>
		db.Schools.IgnoreQueryFilters().Where(s => s.Id == tenant.TenantId);

	private static SchoolSettingsDto ToDto(School s) => new(s.Name, s.ContactEmail, s.ContactPhone, s.LogoUrl);
}
