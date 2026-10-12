using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Email;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Services;

public sealed class BoardMemberInvitationService(
	AppDbContext db,
	ITenantContext tenant,
	IEmailSender email,
	IOptions<ApplicationOptions> appOptions,
	KeycloakAdminService keycloakAdmin,
	SubscriptionService subscriptions,
	ILogger<BoardMemberInvitationService> logger)
{
	private static readonly TimeSpan InvitationValidity = TimeSpan.FromDays(14);

	public async Task<BoardMemberInvitation> CreateAndSendAsync(BoardMember member, CancellationToken cancellationToken)
	{
		var existing = await db.BoardMemberInvitations
							   .Where(i => i.BoardMemberId == member.Id && i.AcceptedAt == null)
							   .ToListAsync(cancellationToken);
		db.BoardMemberInvitations.RemoveRange(existing);

		var token = GenerateToken();
		var invitation = new BoardMemberInvitation
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			BoardMemberId = member.Id,
			Email = member.Email,
			Token = token,
			ExpiresAt = DateTimeOffset.UtcNow.Add(InvitationValidity),
		};

		db.BoardMemberInvitations.Add(invitation);
		await db.SaveChangesAsync(cancellationToken);

		string? temporaryPassword = null;
		string? pendingKeycloakSubject = null;
		if (string.IsNullOrWhiteSpace(member.KeycloakSubject))
		{
			try
			{
				temporaryPassword = GenerateTemporaryPassword();
				var nameParts = member.Name.Split(' ', 2);
				pendingKeycloakSubject = await keycloakAdmin.CreateUserAsync(
					member.Email,
					nameParts[0],
					nameParts.Length > 1 ? nameParts[1] : string.Empty,
					temporaryPassword,
					tenant.TenantId,
					realmRole: Roles.Board,
					forcePasswordReset: true,
					cancellationToken);
			}
			catch (KeycloakException ex)
			{
				logger.LogWarning(ex, "Could not pre-create Keycloak account for board member {Email}", member.Email);
				temporaryPassword = null;
			}
		}

		var school = await db.Schools
							 .IgnoreQueryFilters()
							 .Where(s => s.Id == tenant.TenantId)
							 .Select(s => s.Name)
							 .FirstOrDefaultAsync(cancellationToken) ?? "Skoleoverblikket";

		var link = $"{appOptions.Value.SanitizedBaseUrl}/board-invitation/{token}";

		await email.SendAsync(new EmailMessage(
			To: member.Email,
			Subject: $"Invitation til bestyrelsen på {school}",
			HtmlBody: BuildHtmlEmail(member.Name, school, link, temporaryPassword),
			PlainTextBody: BuildPlainEmail(member.Name, school, link, temporaryPassword)
		), cancellationToken);

		// Only persist KeycloakSubject after email is successfully sent so retries can regenerate the password
		if (pendingKeycloakSubject is not null)
		{
			member.KeycloakSubject = pendingKeycloakSubject;
			await db.SaveChangesAsync(cancellationToken);
		}

		return invitation;
	}

	public async Task<BoardMemberInvitation?> FindValidAsync(string token, CancellationToken cancellationToken) =>
		await db.BoardMemberInvitations
				.IgnoreQueryFilters()
				.Include(i => i.BoardMember)
				.FirstOrDefaultAsync(
					i => i.Token == token && i.AcceptedAt == null && i.ExpiresAt > DateTimeOffset.UtcNow,
					cancellationToken);

	public async Task<InvitationAcceptResult> AcceptAsync(string token, CancellationToken cancellationToken)
	{
		var invitation = await FindValidAsync(token, cancellationToken);
		if (invitation is null)
		{
			return InvitationAcceptResult.Invalid;
		}

		// The accepting user has no tenant claim yet, so ModuleAccessFilter can't check the school.
		if (!await subscriptions.IsModuleActiveAsync(invitation.TenantId, SubscriptionModule.BoardModule, cancellationToken))
		{
			return InvitationAcceptResult.ModuleInactive;
		}

		invitation.AcceptedAt = DateTimeOffset.UtcNow;
		await db.SaveChangesAsync(cancellationToken);
		return InvitationAcceptResult.Accepted;
	}

	private static string GenerateToken()
	{
		var bytes = RandomNumberGenerator.GetBytes(32);
		return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
	}

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

	private static string BuildHtmlEmail(string name, string schoolName, string link, string? temporaryPassword)
	{
		var encodedName = HtmlEncoder.Default.Encode(name);
		var encodedSchoolName = HtmlEncoder.Default.Encode(schoolName);
		var encodedLink = HtmlEncoder.Default.Encode(link);

		var passwordBlock = temporaryPassword is not null
			? $"""
              <div style="margin:0 0 24px;padding:16px;background:#f3f4f6;border-radius:8px;border:1px solid #e5e7eb;">
                <p style="margin:0 0 8px;font-size:13px;color:#6b7280;">Din midlertidige adgangskode (du skal ændre den ved første login):</p>
                <span style="font-family:monospace;font-size:18px;font-weight:700;letter-spacing:0.05em;color:#111827;user-select:all;">{HtmlEncoder.Default.Encode(temporaryPassword)}</span>
              </div>
              """
			: string.Empty;

		return EmailTemplate.Wrap($"Invitation til bestyrelsen på {encodedSchoolName}", $"""
            <h1>Du er inviteret til bestyrelsen på {encodedSchoolName}</h1>
            <p>Hej {encodedName},<br><br>
            Du er inviteret til at få adgang til Skoleoverblikket som bestyrelsesmedlem på <strong>{encodedSchoolName}</strong>.</p>
            {passwordBlock}
            <div class="btn-wrapper">
              <a href="{encodedLink}" class="btn">Acceptér invitation</a>
            </div>
            <div class="notice">
              <p style="margin-bottom:0;">Har du problemer med knappen? Kopier dette link direkte i din browser:<br>
              <a href="{encodedLink}" style="color:#1f6321;word-break:break-all;">{encodedLink}</a></p>
            </div>
            """);
	}

	private static string BuildPlainEmail(string name, string schoolName, string link, string? temporaryPassword)
	{
		var passwordLine = temporaryPassword is not null
			? $"\nDin midlertidige adgangskode: {temporaryPassword}\n(Du skal ændre den ved første login.)\n"
			: string.Empty;

		return $"Hej {name},\n\nDu er inviteret til bestyrelsen på {schoolName} på Skoleoverblikket.{passwordLine}\nAcceptér din invitation her:\n{link}\n\nLinket er gyldigt i 14 dage.\n";
	}
}
