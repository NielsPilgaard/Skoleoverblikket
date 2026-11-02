using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Cache;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Tenancy;
using ZiggyCreatures.Caching.Fusion;

namespace Skoleoverblikket.Api.Services;

public sealed class MessageRecipientService(TenantCache cache, ITenantContext tenant)
{
	private const int MaxResults = 50;

	public async Task<IReadOnlyList<MessagesController.RecipientDto>> SearchAsync(
		Guid callerId,
		RecipientType callerType,
		string query,
		CancellationToken cancellationToken)
	{
		var all = await cache.GetOrSetAsync<(Guid CallerId, RecipientType CallerType), List<MessagesController.RecipientDto>>(
			$"recipients:{tenant.TenantId}:{callerId}",
			(callerId, callerType),
			LoadAllAsync,
			options => options.SetDuration(TimeSpan.FromSeconds(30)),
			cancellationToken);

		var filtered = string.IsNullOrEmpty(query)
			? all
			: all.Where(r => r.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

		return filtered.Take(MaxResults).ToList();
	}

	private static async Task<List<MessagesController.RecipientDto>> LoadAllAsync(
		AppDbContext db,
		(Guid CallerId, RecipientType CallerType) caller,
		CancellationToken cancellationToken)
	{
		var results = new List<MessagesController.RecipientDto>();

		if (caller.CallerType == RecipientType.Parent)
		{
			var staff = await db.Staff.AsNoTracking().ToListAsync(cancellationToken);
			results.AddRange(staff.Select(s => new MessagesController.RecipientDto(s.Id, s.Name, RecipientType.Staff, s.AvatarUrl)));

			var parents = await db.Parents.AsNoTracking()
				.Where(p => p.ShareContactInfo && p.Id != caller.CallerId)
				.ToListAsync(cancellationToken);
			results.AddRange(parents.Select(p => new MessagesController.RecipientDto(p.Id, p.Name, RecipientType.Parent, p.AvatarUrl)));
		}
		else
		{
			var parents = await db.Parents.AsNoTracking().ToListAsync(cancellationToken);
			results.AddRange(parents.Select(p => new MessagesController.RecipientDto(p.Id, p.Name, RecipientType.Parent, p.AvatarUrl)));

			var staff = await db.Staff.AsNoTracking().ToListAsync(cancellationToken);
			results.AddRange(staff.Select(s => new MessagesController.RecipientDto(s.Id, s.Name, RecipientType.Staff, s.AvatarUrl)));
		}

		return results;
	}
}
