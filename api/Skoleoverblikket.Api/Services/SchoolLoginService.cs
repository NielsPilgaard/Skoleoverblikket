using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// One Keycloak login can be staff, parent and board member at the same time (task 61), so removing
/// one of those rows must not delete a login another row still uses.
/// </summary>
public sealed class SchoolLoginService(AppDbContext db, KeycloakAdminService keycloak)
{
	/// <summary>
	/// Deletes the login unless a Staff, BoardMember or Parent row other than <paramref name="removedRowId"/>
	/// still uses it. Throws <see cref="KeycloakException"/> when Keycloak refuses.
	/// </summary>
	public async Task DeleteIfUnusedAsync(string subject, Guid removedRowId, CancellationToken cancellationToken)
	{
		// IgnoreQueryFilters: a row in any school keeps the login alive, not only rows in this tenant.
		// Only a yes/no leaves this method, so no other school's data is exposed.
		var inUse = await db.Staff.IgnoreQueryFilters().AnyAsync(s => s.KeycloakSubject == subject && s.Id != removedRowId, cancellationToken)
			|| await db.BoardMembers.IgnoreQueryFilters().AnyAsync(m => m.KeycloakSubject == subject && m.Id != removedRowId, cancellationToken)
			|| await db.Parents.IgnoreQueryFilters().AnyAsync(p => p.KeycloakSubject == subject && p.Id != removedRowId, cancellationToken);

		if (!inUse)
		{
			await keycloak.DeleteStaffUserAsync(subject, cancellationToken);
		}
	}
}
