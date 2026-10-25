using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Email;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Services;

public sealed class ParentInvitationService(
	AppDbContext db,
	ITenantContext tenant,
	IEmailSender email,
	IOptions<ApplicationOptions> appOptions,
	SchoolLoginService logins,
	SubscriptionService subscriptions,
	ILogger<ParentInvitationService> logger)
{
	private static readonly TimeSpan InvitationValidity = TimeSpan.FromDays(14);
	private readonly string BaseUrl = appOptions.Value.SanitizedBaseUrl;

	public sealed record InvitationPreview(string ParentName, string Email, string SchoolName, DateTimeOffset ExpiresAt, bool NeedsPassword);

	public enum InviteResult
	{
		Invited,
		StudentsNotFound,

		/// <summary>The email belongs to a login at another school.</summary>
		EmailInOtherSchool,
	}

	/// <summary>
	/// Creates a parent linked to <paramref name="studentIds"/>, finds or creates their login and emails
	/// the invitation. The login is resolved before the parent is saved, so an email used at another
	/// school leaves nothing behind. Keycloak and email failures throw.
	/// </summary>
	public async Task<(InviteResult Result, Guid? ParentId)> InviteAsync(
		string name, string email, IReadOnlyList<Guid> studentIds, CancellationToken cancellationToken)
	{
		var students = await db.Students
			.Where(s => studentIds.Contains(s.Id))
			.ToListAsync(cancellationToken);
		if (students.Count != studentIds.Count)
		{
			return (InviteResult.StudentsNotFound, null);
		}

		if (string.IsNullOrWhiteSpace(name))
		{
			throw new InvalidOperationException("Cannot create Keycloak account for a parent with an empty name.");
		}

		string subject;
		try
		{
			subject = await logins.FindOrCreateAsync(name, email, tenant.TenantId, cancellationToken);
		}
		catch (KeycloakUserInOtherSchoolException)
		{
			return (InviteResult.EmailInOtherSchool, null);
		}

		var parent = new Parent
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			Name = name,
			Email = email,
			KeycloakSubject = subject,
		};
		foreach (var student in students)
		{
			parent.Students.Add(student);
		}

		db.Parents.Add(parent);
		await db.SaveChangesAsync(cancellationToken);

		await SendAsync(parent, cancellationToken);
		return (InviteResult.Invited, parent.Id);
	}

	/// <summary>
	/// Sends a new invitation to an existing parent, creating their login if they have none.
	/// False when the email belongs to a login at another school.
	/// </summary>
	public async Task<bool> ResendAsync(Parent parent, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(parent.KeycloakSubject))
		{
			if (string.IsNullOrWhiteSpace(parent.Name))
			{
				throw new InvalidOperationException($"Cannot create Keycloak account for parent {parent.Id}: name is empty.");
			}

			try
			{
				parent.KeycloakSubject = await logins.FindOrCreateAsync(parent.Name, parent.Email, tenant.TenantId, cancellationToken);
			}
			catch (KeycloakUserInOtherSchoolException)
			{
				return false;
			}

			await db.SaveChangesAsync(cancellationToken);
		}

		await SendAsync(parent, cancellationToken);
		return true;
	}

	/// <summary>
	/// Emails a new invitation link, replacing pending ones. Nobody gets a password by email: the
	/// invitation page asks a new invitee to choose one, so a lost or failed email can simply be resent.
	/// </summary>
	private async Task SendAsync(Parent parent, CancellationToken cancellationToken)
	{
		var existingAccount = !await logins.NeedsPasswordAsync(parent.KeycloakSubject, cancellationToken);

		var pending = await db.ParentInvitations
			.Where(i => i.ParentId == parent.Id && i.AcceptedAt == null)
			.ToListAsync(cancellationToken);
		db.ParentInvitations.RemoveRange(pending);

		var token = GenerateToken();
		var invitation = new ParentInvitation
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			ParentId = parent.Id,
			Email = parent.Email,
			Token = token,
			ExpiresAt = DateTimeOffset.UtcNow.Add(InvitationValidity),
		};

		db.ParentInvitations.Add(invitation);
		await db.SaveChangesAsync(cancellationToken);

		var school = await db.Schools
			.IgnoreQueryFilters()
			.Where(s => s.Id == tenant.TenantId)
			.Select(s => s.Name)
			.FirstOrDefaultAsync(cancellationToken) ?? "Skoleoverblikket";

		var link = $"{BaseUrl}/parent-invitation/{token}";

		await email.SendAsync(new EmailMessage(
			To: parent.Email,
			Subject: ParentInvitationEmail.Subject(school),
			HtmlBody: ParentInvitationEmail.BuildHtml(parent.Name, school, link, existingAccount),
			PlainTextBody: ParentInvitationEmail.BuildPlainText(parent.Name, school, link, existingAccount)
		), cancellationToken);
	}

	/// <summary>
	/// Removes the parent, and their login unless they are also staff or a board member. False when
	/// not found. A Keycloak failure is logged; the parent is removed anyway.
	/// </summary>
	public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
	{
		var parent = await db.Parents.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
		if (parent is null)
		{
			return false;
		}

		if (!string.IsNullOrWhiteSpace(parent.KeycloakSubject))
		{
			try
			{
				await logins.DeleteIfUnusedAsync(parent.KeycloakSubject, parent.Id, cancellationToken);
			}
			catch (KeycloakException ex)
			{
				logger.LogError(ex, "Failed to delete Keycloak account for parent {ParentId}", id);
			}
		}

		db.Parents.Remove(parent);
		await db.SaveChangesAsync(cancellationToken);
		return true;
	}

	public async Task<ParentInvitation?> FindValidAsync(string token, CancellationToken cancellationToken) =>
		await db.ParentInvitations
				.IgnoreQueryFilters()
				.Include(i => i.Parent)
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
			invitation.Parent.Name,
			invitation.Email,
			school ?? "Skoleoverblikket",
			invitation.ExpiresAt,
			await logins.NeedsPasswordAsync(invitation.Parent.KeycloakSubject, cancellationToken));
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

		var parent = invitation.Parent;
		var login = await logins.SetInviteePasswordAsync(
			parent.KeycloakSubject, parent.Name, invitation.Email, invitation.TenantId, password, cancellationToken);
		if (login is null)
		{
			return (SetInvitationPasswordResult.PasswordAlreadySet, null);
		}

		parent.KeycloakSubject = login.Value.Subject;
		await db.SaveChangesAsync(cancellationToken);
		return (SetInvitationPasswordResult.Set, new InvitationLoginDto(login.Value.Token.AccessToken, login.Value.Token.RefreshToken));
	}

	public async Task<InvitationAcceptResult> AcceptAsync(string token, string keycloakSubject, CancellationToken cancellationToken)
	{
		var invitation = await FindValidAsync(token, cancellationToken);
		if (invitation is null)
		{
			return InvitationAcceptResult.Invalid;
		}

		// Checked here, not only in ModuleAccessFilter: the accepting user may have no tenant claim yet.
		if (!await subscriptions.IsModuleActiveAsync(invitation.TenantId, SubscriptionModule.ParentModule, cancellationToken))
		{
			return InvitationAcceptResult.ModuleInactive;
		}

		await MarkAcceptedAsync(invitation, keycloakSubject, cancellationToken);
		return InvitationAcceptResult.Accepted;
	}

	private async Task MarkAcceptedAsync(ParentInvitation invitation, string keycloakSubject, CancellationToken cancellationToken)
	{
		try
		{
			invitation.AcceptedAt = DateTimeOffset.UtcNow;
			var parent = await db.Parents
								 .IgnoreQueryFilters()
								 .FirstAsync(p => p.Id == invitation.ParentId, cancellationToken);
			parent.KeycloakSubject = keycloakSubject;
			await db.SaveChangesAsync(cancellationToken);
		}
		catch (DbUpdateConcurrencyException)
		{
			await db.Entry(invitation).ReloadAsync(cancellationToken);
			if (invitation.AcceptedAt != null)
			{
				return;
			}

			throw new InvalidOperationException("Failed to accept invitation due to concurrent modification.");
		}
	}

	private static string GenerateToken()
	{
		var bytes = RandomNumberGenerator.GetBytes(32);
		return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
	}

}
