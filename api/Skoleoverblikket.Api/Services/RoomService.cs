using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;
using ZiggyCreatures.Caching.Fusion;
using static Skoleoverblikket.Api.Controllers.RoomsController;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// The one writer of <see cref="Room"/> (lokale) rows. The request and response records stay nested in
/// <see cref="RoomsController"/> so their OpenAPI schema names, and with them the generated web client, are unchanged.
/// </summary>
public sealed class RoomService(AppDbContext db, ITenantContext tenant, IFusionCache cache)
{
	public Task<List<RoomDto>> GetAllAsync(CancellationToken cancellationToken) =>
		db.Rooms
			.AsNoTracking()
			.OrderBy(r => r.Name)
			.Select(r => new RoomDto(r.Id, r.Name, r.Capacity, r.Description))
			.ToListAsync(cancellationToken);

	public Task<RoomDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
		db.Rooms
			.AsNoTracking()
			.Where(r => r.Id == id)
			.Select(r => new RoomDto(r.Id, r.Name, r.Capacity, r.Description))
			.FirstOrDefaultAsync(cancellationToken);

	/// <summary>Creates a lokale and clears the onboarding checklist cache, which counts rooms.</summary>
	public async Task<RoomDto> CreateRoomAsync(UpsertRoomRequest req, CancellationToken cancellationToken)
	{
		var room = new Room
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			Name = req.Name,
			Capacity = req.Capacity,
			Description = req.Description,
		};
		db.Rooms.Add(room);
		await db.SaveChangesAsync(cancellationToken);
		await cache.RemoveAsync(SchoolsController.OnboardingCacheKey(tenant.TenantId), token: cancellationToken);
		return new RoomDto(room.Id, room.Name, room.Capacity, room.Description);
	}

	public async Task<RoomDto?> UpdateRoomAsync(Guid id, UpsertRoomRequest req, CancellationToken cancellationToken)
	{
		var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
		if (room is null)
		{
			return null;
		}

		room.Name = req.Name;
		room.Capacity = req.Capacity;
		room.Description = req.Description;
		await db.SaveChangesAsync(cancellationToken);
		return new RoomDto(room.Id, room.Name, room.Capacity, room.Description);
	}

	/// <summary>Deletes a lokale and clears the onboarding checklist cache. Returns false when it doesn't exist.</summary>
	public async Task<bool> DeleteRoomAsync(Guid id, CancellationToken cancellationToken)
	{
		var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
		if (room is null)
		{
			return false;
		}

		db.Rooms.Remove(room);
		await db.SaveChangesAsync(cancellationToken);
		await cache.RemoveAsync(SchoolsController.OnboardingCacheKey(tenant.TenantId), token: cancellationToken);
		return true;
	}
}
