using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;
using ZiggyCreatures.Caching.Fusion;
using static Skoleoverblikket.Api.Controllers.StaffController;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// Staff members: create, edit, admin access and delete. Admin is <see cref="Staff.IsAdmin"/>; the API
/// reads it on every request (SchoolRolesClaimsTransformer), so there's no Keycloak role to grant.
/// </summary>
public sealed class StaffService(
	AppDbContext db,
	ITenantContext tenant,
	KeycloakAdminService keycloak,
	SchoolLoginService logins,
	IFusionCache cache,
	ILogger<StaffService> logger)
{
	public enum ChangeResult
	{
		Done,
		NotFound,
		EmailTaken,

		/// <summary>The staff member hasn't accepted their invitation, so has no login to make admin.</summary>
		NotAccepted,
		CannotRemoveOwnAdmin,
		LastAdmin,

		/// <summary>Phase A: the Keycloak admin role couldn't be removed, so nothing changed.</summary>
		KeycloakFailed,
	}

	public enum DeleteResult
	{
		Deleted,
		NotFound,
		Self,
		HasLessons,
	}

	/// <summary>Null when another staff member already has the email.</summary>
	public async Task<StaffDto?> CreateAsync(UpsertStaffRequest req, CancellationToken cancellationToken)
	{
		if (await EmailTakenAsync(req.Email, exceptId: null, cancellationToken))
		{
			return null;
		}

		var staff = new Staff
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			Name = req.Name,
			Email = req.Email,
			Phone = req.Phone,
			Role = req.Role,
			IsAdmin = req.IsAdmin,
		};
		db.Staff.Add(staff);
		await db.SaveChangesAsync(cancellationToken);
		await cache.RemoveAsync(SchoolsController.OnboardingCacheKey(tenant.TenantId), token: cancellationToken);
		return ToDto(staff);
	}

	public async Task<(ChangeResult Result, StaffDto? Staff)> UpdateAsync(
		Guid id, UpsertStaffRequest req, string? currentSubject, CancellationToken cancellationToken)
	{
		var staff = await db.Staff.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
		if (staff is null)
		{
			return (ChangeResult.NotFound, null);
		}

		if (req.Email != staff.Email && await EmailTakenAsync(req.Email, exceptId: id, cancellationToken))
		{
			return (ChangeResult.EmailTaken, null);
		}

		// Admin rules only apply to someone with a login; before that IsAdmin is just a setting for the invite.
		if (staff.IsAdmin != req.IsAdmin && staff.KeycloakSubject is not null
			&& await CheckAdminChangeAsync(staff, req.IsAdmin, currentSubject, cancellationToken) is { } refused)
		{
			return (refused, null);
		}

		var wasAdmin = staff.IsAdmin;
		staff.Name = req.Name;
		staff.Email = req.Email;
		staff.Phone = req.Phone;
		staff.Role = req.Role;
		staff.IsAdmin = req.IsAdmin;
		await db.SaveChangesAsync(cancellationToken);

		if (wasAdmin && !staff.IsAdmin && !await TryRemoveKeycloakAdminAsync(staff, cancellationToken))
		{
			return (ChangeResult.KeycloakFailed, null);
		}

		return (ChangeResult.Done, ToDto(staff));
	}

	public async Task<(ChangeResult Result, StaffDto? Staff)> SetAdminAsync(
		Guid id, bool isAdmin, string? currentSubject, CancellationToken cancellationToken)
	{
		var staff = await db.Staff.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
		if (staff is null)
		{
			return (ChangeResult.NotFound, null);
		}

		if (staff.KeycloakSubject is null)
		{
			return (ChangeResult.NotAccepted, null);
		}

		if (staff.IsAdmin == isAdmin)
		{
			return (ChangeResult.Done, ToDto(staff));
		}

		if (await CheckAdminChangeAsync(staff, isAdmin, currentSubject, cancellationToken) is { } refused)
		{
			return (refused, null);
		}

		staff.IsAdmin = isAdmin;
		await db.SaveChangesAsync(cancellationToken);

		if (!isAdmin && !await TryRemoveKeycloakAdminAsync(staff, cancellationToken))
		{
			return (ChangeResult.KeycloakFailed, null);
		}

		return (ChangeResult.Done, ToDto(staff));
	}

	public async Task<DeleteResult> DeleteAsync(Guid id, string? currentSubject, CancellationToken cancellationToken)
	{
		var staff = await db.Staff.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
		if (staff is null)
		{
			return DeleteResult.NotFound;
		}

		if (!string.IsNullOrWhiteSpace(staff.KeycloakSubject) && staff.KeycloakSubject == currentSubject)
		{
			return DeleteResult.Self;
		}

		if (await db.SchemaSlots.AnyAsync(sl => sl.TeacherId == id || sl.AideId == id, cancellationToken))
		{
			return DeleteResult.HasLessons;
		}

		if (!string.IsNullOrWhiteSpace(staff.KeycloakSubject))
		{
			try
			{
				await logins.DeleteIfUnusedAsync(staff.KeycloakSubject, staff.Id, cancellationToken);
			}
			catch (KeycloakException ex)
			{
				logger.LogWarning(ex, "Could not delete Keycloak account for staff {StaffId}; DB record will still be removed", id);
			}
		}

		db.Staff.Remove(staff);
		await db.SaveChangesAsync(cancellationToken);
		await cache.RemoveAsync(SchoolsController.OnboardingCacheKey(tenant.TenantId), token: cancellationToken);
		return DeleteResult.Deleted;
	}

	/// <summary>
	/// Phase A backfill (<see cref="AdminRoleBackfillJob"/>): sets IsAdmin on every staff row whose login
	/// holds Keycloak's admin role. Returns how many rows changed.
	/// </summary>
	public async Task<int> BackfillAdminsAsync(IReadOnlyCollection<string> keycloakAdminSubjects, CancellationToken cancellationToken)
	{
		if (keycloakAdminSubjects.Count == 0)
		{
			return 0;
		}

		// IgnoreQueryFilters: a startup job with no tenant, fixing every school at once. It only sets a
		// flag on rows whose own login is a Keycloak admin, and reads nothing back.
		return await db.Staff
			.IgnoreQueryFilters()
			.Where(s => !s.IsAdmin && s.KeycloakSubject != null && keycloakAdminSubjects.Contains(s.KeycloakSubject))
			.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsAdmin, true), cancellationToken);
	}

	private async Task<ChangeResult?> CheckAdminChangeAsync(Staff staff, bool newIsAdmin, string? currentSubject, CancellationToken cancellationToken)
	{
		if (newIsAdmin)
		{
			return null;
		}

		if (staff.KeycloakSubject is not null && staff.KeycloakSubject == currentSubject)
		{
			return ChangeResult.CannotRemoveOwnAdmin;
		}

		return await db.Staff.CountAsync(s => s.IsAdmin, cancellationToken) <= 1
			? ChangeResult.LastAdmin
			: null;
	}

	/// <summary>
	/// Phase A of task 61 still reads Keycloak's admin role, so revoking admin must remove it too.
	/// On failure IsAdmin is restored, so the database never says "not admin" while Keycloak says admin.
	/// </summary>
	private async Task<bool> TryRemoveKeycloakAdminAsync(Staff staff, CancellationToken cancellationToken)
	{
		if (staff.KeycloakSubject is null)
		{
			return true;
		}

		try
		{
			await keycloak.RemoveAdminRoleAsync(staff.KeycloakSubject, cancellationToken);
			return true;
		}
		catch (KeycloakException ex)
		{
			logger.LogWarning(ex, "Could not remove Keycloak admin role for staff {StaffId}; restoring admin", staff.Id);
			staff.IsAdmin = true;
			await db.SaveChangesAsync(cancellationToken);
			return false;
		}
	}

	private async Task<bool> EmailTakenAsync(string? email, Guid? exceptId, CancellationToken cancellationToken) =>
		!string.IsNullOrWhiteSpace(email)
		&& await db.Staff.AnyAsync(s => s.Id != exceptId && s.Email == email, cancellationToken);

	private static StaffDto ToDto(Staff s) => new(s.Id, s.Name, s.Email, s.Phone, s.Role, s.IsAdmin, s.KeycloakSubject);
}
