using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;
using static Skoleoverblikket.Api.Controllers.ClassPermissionsController;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// The one writer of <see cref="ClassPermission"/> rows. The first row for a class switches it to restricted
/// editing (docs/AUTHORIZATION.md); the EditClass policy reads the rows, this service only maintains them.
/// The request and response records stay nested in <see cref="ClassPermissionsController"/> so their
/// OpenAPI schema names, and with them the generated web client, are unchanged.
/// </summary>
public sealed class ClassPermissionService(AppDbContext db, ITenantContext tenant)
{
	public enum GrantResult
	{
		Granted,
		NotFound,
		AlreadyGranted,
	}

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

	/// <summary>NotFound when the class or the staff member doesn't exist in this school.</summary>
	public async Task<(GrantResult Result, ClassPermissionDto? Permission)> GrantAsync(
		Guid classId, Guid staffId, CancellationToken cancellationToken)
	{
		if (!await db.Classes.AnyAsync(c => c.Id == classId, cancellationToken))
		{
			return (GrantResult.NotFound, null);
		}

		var staffName = await db.Staff
			.Where(s => s.Id == staffId)
			.Select(s => s.Name)
			.FirstOrDefaultAsync(cancellationToken);
		if (staffName is null)
		{
			return (GrantResult.NotFound, null);
		}

		if (await db.ClassPermissions.AnyAsync(p => p.ClassId == classId && p.StaffId == staffId, cancellationToken))
		{
			return (GrantResult.AlreadyGranted, null);
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
		await db.SaveChangesAsync(cancellationToken);

		return (GrantResult.Granted, new ClassPermissionDto(permission.StaffId, staffName, permission.GrantedAt));
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
