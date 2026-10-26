using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// Superadmin's read-only view of every school and its subscription. Superadmin acts across
/// tenants, so schools are read with <c>IgnoreQueryFilters()</c>.
/// </summary>
public sealed class SuperAdminTenantService(AppDbContext db)
{
	/// <summary>
	/// Every school by name. Internal schools (<see cref="School.IsInternal"/>) only with
	/// <paramref name="includeInternal"/>, so the smoke test school doesn't pass for a customer.
	/// </summary>
	public async Task<IReadOnlyList<TenantListItemDto>> ListAsync(bool includeInternal, CancellationToken cancellationToken) =>
		await db.Schools
			.IgnoreQueryFilters()
			.Where(s => includeInternal || !s.IsInternal)
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
					sub.FreeAccessLastDay,
					s.IsInternal))
			.ToListAsync(cancellationToken);

	public async Task<TenantDetailDto?> GetAsync(Guid schoolId, CancellationToken cancellationToken) =>
		await db.Schools
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
					sub.FreeAccessLastDay,
					s.IsInternal))
			.FirstOrDefaultAsync(cancellationToken);
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
	DateOnly? FreeAccessLastDay,
	bool IsInternal);

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
	DateOnly? FreeAccessLastDay,
	bool IsInternal);

public record ModuleItemDto(
	SubscriptionModule Module,
	bool IsAdminOverride,
	string? StripeSubscriptionItemId);
