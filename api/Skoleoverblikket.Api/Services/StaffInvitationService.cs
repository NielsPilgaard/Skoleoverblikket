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
	KeycloakAdminService keycloakAdmin,
	ILogger<StaffInvitationService> logger)
{
	private static readonly TimeSpan InvitationValidity = TimeSpan.FromDays(14);

	/// <summary>Null when the email belongs to a login at another school; nothing is saved then.</summary>
	public async Task<StaffInvitation?> CreateAndSendAsync(Staff staff, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(staff.Email))
		{
			throw new InvalidOperationException("Staff member has no email address.");
		}

		// Find or create the login first, so an email used at another school fails before anything is saved.
		// A new login gets a temporary password with the UPDATE_PASSWORD required action, forcing the
		// user to choose a new password on first login. An existing login at this school is linked.
		string? temporaryPassword = null;
		var existingAccount = false;
		if (string.IsNullOrWhiteSpace(staff.KeycloakSubject))
		{
			try
			{
				var password = GenerateTemporaryPassword();
				var nameParts = staff.Name.Split(' ', 2);
				var login = await keycloakAdmin.CreateOrFindInviteeAsync(
					staff.Email, nameParts[0], nameParts.Length > 1 ? nameParts[1] : string.Empty, password, tenant.TenantId, cancellationToken);
				staff.KeycloakSubject = login.Subject;
				existingAccount = login.Existing;
				temporaryPassword = login.Existing ? null : password;
			}
			catch (KeycloakUserInOtherSchoolException)
			{
				return null;
			}
			catch (KeycloakException ex)
			{
				logger.LogWarning(ex, "Could not pre-create Keycloak account for invited staff {Email}; invitation email will still be sent", staff.Email);
			}
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
								  HtmlBody: StaffInvitationEmail.BuildHtml(staff.Name, school, link, temporaryPassword, existingAccount),
								  PlainTextBody: StaffInvitationEmail.BuildPlainText(staff.Name, school, link, temporaryPassword, existingAccount)
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

	// Generates a temporary password that satisfies common Keycloak password policies:
	// at least one uppercase, one lowercase, one digit, one special char, 12 chars total.
	private static string GenerateTemporaryPassword()
	{
		const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
		const string lower = "abcdefghjkmnpqrstuvwxyz";
		const string digits = "23456789";
		const string special = "!@#$%&*";
		const string all = upper + lower + digits + special;

		var rng = RandomNumberGenerator.GetBytes(12);
		var chars = new char[12];
		chars[0] = upper[rng[0] % upper.Length];
		chars[1] = lower[rng[1] % lower.Length];
		chars[2] = digits[rng[2] % digits.Length];
		chars[3] = special[rng[3] % special.Length];
		for (var i = 4; i < 12; i++)
		{
			chars[i] = all[rng[i] % all.Length];
		}

		RandomNumberGenerator.Shuffle(chars.AsSpan());
		return new string(chars);
	}
}
