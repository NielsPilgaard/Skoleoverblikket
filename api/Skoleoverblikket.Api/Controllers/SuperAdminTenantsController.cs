using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/admin/tenants")]
[Authorize(Roles = Roles.SuperAdmin)]
public sealed class SuperAdminTenantsController(AppDbContext db) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<TenantListItemDto>>> GetTenants(CancellationToken cancellationToken)
	{
		var tenants = await db.Schools
			.IgnoreQueryFilters()
			.OrderBy(s => s.Name)
			.Join(db.Subscriptions,
				s => s.Id,
				sub => sub.SchoolId,
				(s, sub) => new TenantListItemDto(
					s.Id,
					s.Name,
					s.ContactEmail,
					s.CreatedAt,
					sub.Status,
					sub.TrialEnd,
					sub.CurrentPeriodEnd,
					sub.ActiveModules.Count,
					sub.FreeAccessGrantedAt,
					sub.FreeAccessLastDay))
			.ToListAsync(cancellationToken);

		return Ok(tenants);
	}

	[HttpGet("{schoolId:guid}")]
	public async Task<ActionResult<TenantDetailDto>> GetTenant(Guid schoolId, CancellationToken cancellationToken)
	{
		var school = await db.Schools
			.IgnoreQueryFilters()
			.Where(s => s.Id == schoolId)
			.Join(db.Subscriptions,
				s => s.Id,
				sub => sub.SchoolId,
				(s, sub) => new TenantDetailDto(
					s.Id,
					s.Name,
					s.ContactEmail,
					s.ContactPhone,
					s.CreatedAt,
					sub.Status,
					sub.StripeCustomerId,
					sub.StripeSubscriptionId,
					sub.TrialEnd,
					sub.CurrentPeriodEnd,
					sub.ActiveModules
						.Select(m => new ModuleItemDto(m.Module, m.IsAdminOverride, m.StripeSubscriptionItemId))
						.ToList(),
					sub.FreeAccessGrantedAt,
					sub.FreeAccessLastDay))
			.FirstOrDefaultAsync(cancellationToken);

		if (school is null)
		{
			return NotFound();
		}

		return Ok(school);
	}
}

public record TenantListItemDto(
	Guid Id,
	string Name,
	string? ContactEmail,
	DateTimeOffset CreatedAt,
	SubscriptionStatus SubscriptionStatus,
	DateTimeOffset TrialEnd,
	DateTimeOffset? CurrentPeriodEnd,
	int ActiveModuleCount,
	DateTimeOffset? FreeAccessGrantedAt,
	DateOnly? FreeAccessLastDay);

public record TenantDetailDto(
	Guid Id,
	string Name,
	string? ContactEmail,
	string? ContactPhone,
	DateTimeOffset CreatedAt,
	SubscriptionStatus SubscriptionStatus,
	string? StripeCustomerId,
	string? StripeSubscriptionId,
	DateTimeOffset TrialEnd,
	DateTimeOffset? CurrentPeriodEnd,
	IReadOnlyList<ModuleItemDto> Modules,
	DateTimeOffset? FreeAccessGrantedAt,
	DateOnly? FreeAccessLastDay);

public record ModuleItemDto(
	SubscriptionModule Module,
	bool IsAdminOverride,
	string? StripeSubscriptionItemId);
