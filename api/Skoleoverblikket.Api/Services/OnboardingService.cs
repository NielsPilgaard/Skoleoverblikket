using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Cache;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Services;

public sealed class OnboardingService(TenantCache cache, ITenantContext tenant)
{
	public ValueTask<SchoolsController.OnboardingStatusDto?> GetStatusAsync(CancellationToken cancellationToken)
	{
		var tenantId = tenant.TenantId;

		return cache.GetOrSetAsync<Guid, SchoolsController.OnboardingStatusDto?>(
			SchoolsController.OnboardingCacheKey(tenantId),
			tenantId,
			LoadStatusAsync,
			cancellationToken: cancellationToken);
	}

	private static async Task<SchoolsController.OnboardingStatusDto?> LoadStatusAsync(
		AppDbContext db,
		Guid tenantId,
		CancellationToken cancellationToken)
	{
		// School.TenantId is computed from Id and has no column, so the tenant filter can't run here. Matching on Id scopes it to this tenant.
		var school = await db.Schools
							 .AsNoTracking()
							 .IgnoreQueryFilters()
							 .FirstOrDefaultAsync(s => s.Id == tenantId, cancellationToken);

		if (school is null)
		{
			return null;
		}

		var staffCount = await db.Staff.CountAsync(cancellationToken);
		var classCount = await db.Classes.CountAsync(cancellationToken);
		var courseCount = await db.Courses.CountAsync(cancellationToken);
		var roomCount = await db.Rooms.CountAsync(cancellationToken);
		var hasLogo = !string.IsNullOrEmpty(school.LogoUrl);

		var stepsCompleted =
			(hasLogo ? 1 : 0) +
			(staffCount > 0 ? 1 : 0) +
			(classCount > 0 ? 1 : 0) +
			(courseCount > 0 ? 1 : 0) +
			(roomCount > 0 ? 1 : 0);

		return new SchoolsController.OnboardingStatusDto(hasLogo, staffCount, classCount, courseCount, roomCount, stepsCompleted, StepsTotal: 5);
	}
}
