using Microsoft.EntityFrameworkCore;
using Npgsql;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Services;

public record ClassPermissionDto(Guid StaffId, string StaffName, DateTimeOffset GrantedAt);

public record GrantPermissionRequest(Guid StaffId);

public enum GrantPermissionResult
{
	Granted,
	ClassNotFound,
	StaffNotFound,
	AlreadyGranted,
}

/// <summary>
/// The one writer of <see cref="ClassPermission"/> rows. The first row for a class switches it to restricted
/// editing, and removing the last row opens it to all staff again (docs/AUTHORIZATION.md). The EditClass
/// policy reads the rows; this service only maintains them.
/// </summary>
public sealed class ClassPermissionService(AppDbContext db, ITenantContext tenant)
{
	/// <summary>Null when the class doesn't exist.</summary>
	public async Task<List<ClassPermissionDto>?> GetForClassAsync(Guid classId, CancellationToken cancellationToken)
	{
		if (!await db.Classes.AnyAsync(c => c.Id == classId, cancellationToken))
		{
			return null;
		}

		return await db.ClassPermissions
			.AsNoTracking()
			.Where(p => p.ClassId == classId)
			.OrderBy(p => p.Staff.Name)
			.Select(p => new ClassPermissionDto(p.StaffId, p.Staff.Name, p.GrantedAt))
			.ToListAsync(cancellationToken);
	}

	public async Task<(GrantPermissionResult Result, ClassPermissionDto? Permission)> GrantAsync(
		Guid classId, Guid staffId, CancellationToken cancellationToken)
	{
		// One round trip: no row means no class; a null name means no staff member.
		var lookup = await db.Classes
			.Where(c => c.Id == classId)
			.Select(c => new
			{
				StaffName = db.Staff.Where(s => s.Id == staffId).Select(s => s.Name).FirstOrDefault(),
				AlreadyGranted = db.ClassPermissions.Any(p => p.ClassId == classId && p.StaffId == staffId),
			})
			.FirstOrDefaultAsync(cancellationToken);
		if (lookup is null)
		{
			return (GrantPermissionResult.ClassNotFound, null);
		}

		if (lookup.StaffName is null)
		{
			return (GrantPermissionResult.StaffNotFound, null);
		}

		if (lookup.AlreadyGranted)
		{
			return (GrantPermissionResult.AlreadyGranted, null);
		}

		var permission = new ClassPermission
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			ClassId = classId,
			StaffId = staffId,
			GrantedAt = DateTimeOffset.UtcNow,
		};
		db.ClassPermissions.Add(permission);
		try
		{
			await db.SaveChangesAsync(cancellationToken);
		}
		catch (DbUpdateException ex) when (ex.InnerException is PostgresException
		{
			SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_ClassPermissions_TenantId_ClassId_StaffId",
		})
		{
			// A concurrent request (a double click) granted the same access first.
			db.Entry(permission).State = EntityState.Detached;
			return (GrantPermissionResult.AlreadyGranted, null);
		}

		return (GrantPermissionResult.Granted, new ClassPermissionDto(permission.StaffId, lookup.StaffName, permission.GrantedAt));
	}

	/// <summary>False when the staff member has no permission row for the class.</summary>
	public async Task<bool> RevokeAsync(Guid classId, Guid staffId, CancellationToken cancellationToken)
	{
		var permission = await db.ClassPermissions
			.FirstOrDefaultAsync(p => p.ClassId == classId && p.StaffId == staffId, cancellationToken);
		if (permission is null)
		{
			return false;
		}

		db.ClassPermissions.Remove(permission);
		await db.SaveChangesAsync(cancellationToken);
		return true;
	}
}
