using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// The Keycloak login behind Staff, BoardMember and Parent rows. One login can be all three at the same
/// time (task 61), so removing one of those rows must not delete a login another row still uses.
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
			await keycloak.DeleteUserIfExistsAsync(subject, cancellationToken);
		}
	}

	/// <summary>
	/// The invitee's login subject: a new login without a password, or their existing one at the same school.
	/// </summary>
	/// <exception cref="KeycloakUserInOtherSchoolException">The email belongs to a login at another school.</exception>
	public Task<string> FindOrCreateAsync(string name, string email, Guid tenantId, CancellationToken cancellationToken)
	{
		var nameParts = name.Trim().Split(' ', 2);
		return keycloak.CreateOrFindInviteeAsync(
			email, nameParts[0], nameParts.Length > 1 ? nameParts[1] : string.Empty, tenantId, cancellationToken);
	}

	/// <summary>True when the invitee must choose a password: there is no login yet, or nobody has set one.</summary>
	public async Task<bool> NeedsPasswordAsync(string? subject, CancellationToken cancellationToken) =>
		string.IsNullOrWhiteSpace(subject) || !await keycloak.HasOwnPasswordAsync(subject, cancellationToken);

	/// <summary>
	/// Sets the password an invitee chose and logs them in. Creates the login when the invitation was
	/// sent while Keycloak was down. Null when the login already has a password the person chose.
	/// </summary>
	public async Task<(string Subject, TokenResponse Token)?> SetInviteePasswordAsync(
		string? subject, string name, string email, Guid tenantId, string password, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(subject))
		{
			subject = await FindOrCreateAsync(name, email, tenantId, cancellationToken);
		}

		if (await keycloak.HasOwnPasswordAsync(subject, cancellationToken))
		{
			return null;
		}

		await keycloak.SetPasswordAsync(subject, password, cancellationToken);
		return (subject, await keycloak.GetTokenForUserAsync(email, password, cancellationToken));
	}
}
