using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Email;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;
using static Skoleoverblikket.Api.Controllers.BoardMembersController;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// The one writer of <see cref="BoardMember"/> rows, apart from the board member CSV import.
/// The response records stay nested in <see cref="Controllers.BoardMembersController"/> so their OpenAPI schema names,
/// and with them the generated web client, are unchanged.
/// </summary>
public sealed class BoardMemberInvitationService(
	AppDbContext db,
	ITenantContext tenant,
	IEmailSender email,
	IOptions<ApplicationOptions> appOptions,
	SchoolLoginService logins,
	SubscriptionService subscriptions,
	ILogger<BoardMemberInvitationService> logger)
{
	public enum DeleteResult
	{
		Deleted,
		NotFound,

		/// <summary>Keycloak refused to delete the login; the member is kept so the delete can be retried.</summary>
		LoginDeleteFailed,
	}

	private static readonly TimeSpan InvitationValidity = TimeSpan.FromDays(14);

	public sealed record InvitationPreview(string BoardMemberName, string Email, string SchoolName, DateTimeOffset ExpiresAt, bool NeedsPassword);

	public Task<List<BoardMemberDto>> GetAllAsync(CancellationToken cancellationToken) =>
		db.BoardMembers
			.AsNoTracking()
			.OrderBy(m => m.Name)
			.Select(m => new BoardMemberDto(m.Id, m.Name, m.Email, m.CanAccessTeacherData, m.KeycloakSubject != null, m.CreatedAt))
			.ToListAsync(cancellationToken);

	public Task<BoardMemberDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
		db.BoardMembers
			.AsNoTracking()
			.Where(m => m.Id == id)
			.Select(m => new BoardMemberDto(m.Id, m.Name, m.Email, m.CanAccessTeacherData, m.KeycloakSubject != null, m.CreatedAt))
			.FirstOrDefaultAsync(cancellationToken);

	/// <summary>The board member signed in with <paramref name="keycloakSubject"/>. Null when there is none.</summary>
	public Task<BoardMemberDto?> GetBySubjectAsync(string keycloakSubject, CancellationToken cancellationToken) =>
		db.BoardMembers
			.AsNoTracking()
			// A login belongs to one school (KeycloakUserInOtherSchoolException), so the subject alone identifies the member.
			.IgnoreQueryFilters()
			.Where(m => m.KeycloakSubject == keycloakSubject)
			.Select(m => new BoardMemberDto(m.Id, m.Name, m.Email, m.CanAccessTeacherData, m.KeycloakSubject != null, m.CreatedAt))
			.FirstOrDefaultAsync(cancellationToken);

	/// <summary>Lets the member see teacher data, or takes that away. Null when the member doesn't exist.</summary>
	public async Task<BoardMemberDto?> SetTeacherDataAccessAsync(Guid id, bool canAccessTeacherData, CancellationToken cancellationToken)
	{
		var member = await db.BoardMembers.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
		if (member is null)
		{
			return null;
		}

		member.CanAccessTeacherData = canAccessTeacherData;
		await db.SaveChangesAsync(cancellationToken);
		return new BoardMemberDto(member.Id, member.Name, member.Email, member.CanAccessTeacherData, member.KeycloakSubject is not null, member.CreatedAt);
	}

	/// <summary>
	/// Invites a board member, reusing the row when the email is already on the board, and emails the
	/// invitation. The login is resolved before a new member is saved, so an email used at another
	/// school leaves nothing behind. Null in that case.
	/// </summary>
	public async Task<BoardMember?> InviteAsync(string name, string email, CancellationToken cancellationToken)
	{
		var normalizedEmail = email.Trim().ToLowerInvariant();
		var member = await db.BoardMembers.FirstOrDefaultAsync(m => m.Email == normalizedEmail, cancellationToken);

		// A new login has no password; the invitee chooses one on the invitation page.
		var subject = member?.KeycloakSubject;
		var existingAccount = false;
		try
		{
			if (string.IsNullOrWhiteSpace(subject))
			{
				subject = await logins.FindOrCreateAsync(member?.Name ?? name.Trim(), normalizedEmail, tenant.TenantId, cancellationToken);
			}

			existingAccount = !await logins.NeedsPasswordAsync(subject, cancellationToken);
		}
		catch (KeycloakUserInOtherSchoolException)
		{
			return null;
		}
		catch (KeycloakException ex)
		{
			// The invitation page creates the login when the invitee chooses a password.
			logger.LogWarning(ex, "Could not pre-create Keycloak account for board member {Email}", normalizedEmail);
		}

		if (member is null)
		{
			member = new BoardMember
			{
				Id = Guid.NewGuid(),
				TenantId = tenant.TenantId,
				Name = name.Trim(),
				Email = normalizedEmail,
			};
			db.BoardMembers.Add(member);
			try
			{
				await db.SaveChangesAsync(cancellationToken);
			}
			catch (DbUpdateException)
			{
				// Race condition: another request created the same member
				db.ChangeTracker.Clear();
				member = await db.BoardMembers.FirstAsync(m => m.Email == normalizedEmail, cancellationToken);
			}
		}

		await SendAsync(member, subject, existingAccount, cancellationToken);
		return member;
	}

	private async Task SendAsync(BoardMember member, string? subject, bool existingAccount, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(subject))
		{
			member.KeycloakSubject = subject;
		}

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

		var school = await db.Schools
							 .IgnoreQueryFilters()
							 .Where(s => s.Id == tenant.TenantId)
							 .Select(s => s.Name)
							 .FirstOrDefaultAsync(cancellationToken) ?? "Skoleoverblikket";

		var link = $"{appOptions.Value.SanitizedBaseUrl}/board-invitation/{token}";

		await email.SendAsync(new EmailMessage(
			To: member.Email,
			Subject: $"Invitation til bestyrelsen på {school}",
			HtmlBody: BuildHtmlEmail(member.Name, school, link, existingAccount),
			PlainTextBody: BuildPlainEmail(member.Name, school, link, existingAccount)
		), cancellationToken);
	}

	/// <summary>Removes the member, and their login unless they are also staff or a parent.</summary>
	public async Task<DeleteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
	{
		var member = await db.BoardMembers.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
		if (member is null)
		{
			return DeleteResult.NotFound;
		}

		if (!string.IsNullOrEmpty(member.KeycloakSubject))
		{
			try
			{
				await logins.DeleteIfUnusedAsync(member.KeycloakSubject, member.Id, cancellationToken);
			}
			catch (KeycloakException ex)
			{
				logger.LogWarning(ex, "Could not delete Keycloak account for board member {BoardMemberId}", id);
				return DeleteResult.LoginDeleteFailed;
			}
		}

		db.BoardMembers.Remove(member);
		await db.SaveChangesAsync(cancellationToken);
		return DeleteResult.Deleted;
	}

	public async Task<BoardMemberInvitation?> FindValidAsync(string token, CancellationToken cancellationToken) =>
		await db.BoardMemberInvitations
				.IgnoreQueryFilters()
				.Include(i => i.BoardMember)
				.FirstOrDefaultAsync(
					i => i.Token == token && i.AcceptedAt == null && i.ExpiresAt > DateTimeOffset.UtcNow,
					cancellationToken);

	/// <summary>What the invitation page shows. Null when the token is invalid or expired.</summary>
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
			invitation.BoardMember.Name,
			invitation.Email,
			school ?? "Skoleoverblikket",
			invitation.ExpiresAt,
			await logins.NeedsPasswordAsync(invitation.BoardMember.KeycloakSubject, cancellationToken));
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

		var member = invitation.BoardMember;
		var login = await logins.SetInviteePasswordAsync(
			member.KeycloakSubject, member.Name, invitation.Email, invitation.TenantId, password, cancellationToken);
		if (login is null)
		{
			return (SetInvitationPasswordResult.PasswordAlreadySet, null);
		}

		member.KeycloakSubject = login.Value.Subject;
		await db.SaveChangesAsync(cancellationToken);
		return (SetInvitationPasswordResult.Set, new InvitationLoginDto(login.Value.Token.AccessToken, login.Value.Token.RefreshToken));
	}

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

	private static string BuildHtmlEmail(string name, string schoolName, string link, bool existingAccount)
	{
		var encodedName = HtmlEncoder.Default.Encode(name);
		var encodedSchoolName = HtmlEncoder.Default.Encode(schoolName);
		var encodedLink = HtmlEncoder.Default.Encode(link);

		var accountBlock = InvitationAccountNotice.Html(existingAccount);

		return EmailTemplate.Wrap($"Invitation til bestyrelsen på {encodedSchoolName}", $"""
            <h1>Du er inviteret til bestyrelsen på {encodedSchoolName}</h1>
            <p>Hej {encodedName},<br><br>
            Du er inviteret til at få adgang til Skoleoverblikket som bestyrelsesmedlem på <strong>{encodedSchoolName}</strong>.</p>
            {accountBlock}
            <div class="btn-wrapper">
              <a href="{encodedLink}" class="btn">Acceptér invitation</a>
            </div>
            <div class="notice">
              <p style="margin-bottom:0;">Har du problemer med knappen? Kopier dette link direkte i din browser:<br>
              <a href="{encodedLink}" style="color:#1f6321;word-break:break-all;">{encodedLink}</a></p>
            </div>
            """);
	}

	private static string BuildPlainEmail(string name, string schoolName, string link, bool existingAccount)
	{
		var accountLine = InvitationAccountNotice.PlainText(existingAccount);

		return $"Hej {name},\n\nDu er inviteret til bestyrelsen på {schoolName} på Skoleoverblikket.{accountLine}\nAcceptér din invitation her:\n{link}\n\nLinket er gyldigt i 14 dage.\n";
	}
}
