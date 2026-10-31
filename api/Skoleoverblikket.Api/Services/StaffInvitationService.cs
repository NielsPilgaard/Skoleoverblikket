using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Email;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Services;

public sealed class StaffInvitationService(
	AppDbContext db,
	ITenantContext tenant,
	IEmailSender email,
	IOptions<ApplicationOptions> appOptions,
	SchoolLoginService logins,
	ILogger<StaffInvitationService> logger)
{
	private static readonly TimeSpan InvitationValidity = TimeSpan.FromDays(14);

	public sealed record InvitationPreview(string StaffName, string Email, string SchoolName, DateTimeOffset ExpiresAt, bool NeedsPassword);

	/// <summary>Null when the email belongs to a login at another school; nothing is saved then.</summary>
	public async Task<StaffInvitation?> CreateAndSendAsync(Staff staff, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(staff.Email))
		{
			throw new InvalidOperationException("Staff member has no email address.");
		}

		// Find or create the login first, so an email used at another school fails before anything is saved.
		// A new login has no password; the invitee chooses one on the invitation page.
		var existingAccount = false;
		try
		{
			if (string.IsNullOrWhiteSpace(staff.KeycloakSubject))
			{
				staff.KeycloakSubject = await logins.FindOrCreateAsync(staff.Name, staff.Email, tenant.TenantId, cancellationToken);
			}

			existingAccount = !await logins.NeedsPasswordAsync(staff.KeycloakSubject, cancellationToken);
		}
		catch (KeycloakUserInOtherSchoolException)
		{
			return null;
		}
		catch (KeycloakException ex)
		{
			// The invitation page creates the login when the invitee chooses a password.
			logger.LogWarning(ex, "Could not pre-create Keycloak account for invited staff {StaffId}; invitation email will still be sent", staff.Id);
		}

		// Expire any existing pending invitations for this staff member
		var existing = await db.StaffInvitations
							   .Where(i => i.StaffId == staff.Id && i.AcceptedAt == null)
							   .ToListAsync(cancellationToken);

		db.StaffInvitations.RemoveRange(existing);

		var token = GenerateToken();
		var invitation = new StaffInvitation
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			StaffId = staff.Id,
			Email = staff.Email,
			Token = token,
			ExpiresAt = DateTimeOffset.UtcNow.Add(InvitationValidity),
		};

		db.StaffInvitations.Add(invitation);
		await db.SaveChangesAsync(cancellationToken);

		var school = await db.Schools
							 .IgnoreQueryFilters()
							 .Where(s => s.Id == tenant.TenantId)
							 .Select(s => s.Name)
							 .FirstOrDefaultAsync(cancellationToken) ??
					 "Skoleoverblikket";

		var link = $"{appOptions.Value.SanitizedBaseUrl}/invitation/{token}";

		await email.SendAsync(new EmailMessage(
								  To: staff.Email,
								  Subject: StaffInvitationEmail.Subject(school),
								  HtmlBody: StaffInvitationEmail.BuildHtml(staff.Name, school, link, existingAccount),
								  PlainTextBody: StaffInvitationEmail.BuildPlainText(staff.Name, school, link, existingAccount)
							  ),
							  cancellationToken);

		return invitation;
	}

	public async Task<StaffInvitation?> FindValidAsync(string token, CancellationToken cancellationToken) =>
		await db.StaffInvitations
				.IgnoreQueryFilters()
				.Include(i => i.Staff)
				.FirstOrDefaultAsync(
					i => i.Token == token && i.AcceptedAt == null && i.ExpiresAt > DateTimeOffset.UtcNow,
					cancellationToken);

	/// <summary>What the invitation page shows before login. Null when the token is invalid or expired.</summary>
	public async Task<InvitationPreview?> PreviewAsync(string token, CancellationToken cancellationToken)
	{
		var invitation = await FindValidAsync(token, cancellationToken);
		if (invitation is null)
		{
			return null;
		}

		var school = await db.Schools
							 .IgnoreQueryFilters()
							 .Where(s => s.Id == invitation.TenantId)
							 .Select(s => s.Name)
							 .FirstOrDefaultAsync(cancellationToken);

		return new InvitationPreview(
			invitation.Staff.Name,
			invitation.Email,
			school ?? "Skoleoverblikket",
			invitation.ExpiresAt,
			await logins.NeedsPasswordAsync(invitation.Staff.KeycloakSubject, cancellationToken));
	}

	/// <summary>Sets the password a new invitee chose and logs them in. The invitation is accepted afterwards.</summary>
	public async Task<(SetInvitationPasswordResult Result, InvitationLoginDto? Login)> SetPasswordAsync(
		string token, string password, CancellationToken cancellationToken)
	{
		var invitation = await FindValidAsync(token, cancellationToken);
		if (invitation is null)
		{
			return (SetInvitationPasswordResult.Invalid, null);
		}

		var staff = invitation.Staff;
		var login = await logins.SetInviteePasswordAsync(
			staff.KeycloakSubject, staff.Name, invitation.Email, invitation.TenantId, password, cancellationToken);
		if (login is null)
		{
			return (SetInvitationPasswordResult.PasswordAlreadySet, null);
		}

		staff.KeycloakSubject = login.Value.Subject;
		await db.SaveChangesAsync(cancellationToken);
		return (SetInvitationPasswordResult.Set, new InvitationLoginDto(login.Value.Token.AccessToken, login.Value.Token.RefreshToken));
	}

	public async Task MarkAcceptedAsync(StaffInvitation invitation, string keycloakSubject, CancellationToken cancellationToken)
	{
		try
		{
			invitation.AcceptedAt = DateTimeOffset.UtcNow;
			var staff = await db.Staff
								.IgnoreQueryFilters()
								.FirstAsync(s => s.Id == invitation.StaffId, cancellationToken);

			staff.KeycloakSubject = keycloakSubject;
			await db.SaveChangesAsync(cancellationToken);
		}
		catch (DbUpdateConcurrencyException)
		{
			// Another request has already accepted this invitation
			// Refresh the entity and check if it's already accepted
			await db.Entry(invitation).ReloadAsync(cancellationToken);
			if (invitation.AcceptedAt != null)
			{
				// Invitation was already accepted, which is fine
				return;
			}

			// If AcceptedAt is still null after reload, this is an unexpected error
			throw new InvalidOperationException(
				"Failed to accept invitation due to concurrent modification. Please try again.");
		}
	}

	private static string GenerateToken()
	{
		var bytes = RandomNumberGenerator.GetBytes(32);
		return Convert.ToBase64String(bytes)
					  .Replace('+', '-')
					  .Replace('/', '_')
					  .TrimEnd('=');
	}
}
