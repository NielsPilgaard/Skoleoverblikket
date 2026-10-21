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
	KeycloakAdminService keycloakAdmin,
	SchoolLoginService logins,
	SubscriptionService subscriptions,
	ILogger<ParentInvitationService> logger)
{
	private static readonly TimeSpan InvitationValidity = TimeSpan.FromDays(14);
	private readonly string BaseUrl = appOptions.Value.SanitizedBaseUrl;

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

		var temporaryPassword = GenerateTemporaryPassword();
		KeycloakLogin login;
		try
		{
			login = await FindOrCreateLoginAsync(name, email, temporaryPassword, cancellationToken);
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
			KeycloakSubject = login.Subject,
		};
		foreach (var student in students)
		{
			parent.Students.Add(student);
		}

		db.Parents.Add(parent);
		await db.SaveChangesAsync(cancellationToken);

		await SendAsync(parent, login.Existing ? null : temporaryPassword, login.Existing, cancellationToken);
		return (InviteResult.Invited, parent.Id);
	}

	/// <summary>
	/// Sends a new invitation to an existing parent, creating their login if they have none.
	/// False when the email belongs to a login at another school.
	/// </summary>
	public async Task<bool> ResendAsync(Parent parent, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(parent.KeycloakSubject))
		{
			await SendAsync(parent, temporaryPassword: null, existingAccount: false, cancellationToken);
			return true;
		}

		if (string.IsNullOrWhiteSpace(parent.Name))
		{
			throw new InvalidOperationException($"Cannot create Keycloak account for parent {parent.Id}: name is empty.");
		}

		var temporaryPassword = GenerateTemporaryPassword();
		try
		{
			var login = await FindOrCreateLoginAsync(parent.Name, parent.Email, temporaryPassword, cancellationToken);
			parent.KeycloakSubject = login.Subject;
			await db.SaveChangesAsync(cancellationToken);
			await SendAsync(parent, login.Existing ? null : temporaryPassword, login.Existing, cancellationToken);
			return true;
		}
		catch (KeycloakUserInOtherSchoolException)
		{
			return false;
		}
	}

	private Task<KeycloakLogin> FindOrCreateLoginAsync(string name, string email, string temporaryPassword, CancellationToken cancellationToken)
	{
		var nameParts = name.Trim().Split(' ', 2);
		return keycloakAdmin.CreateOrFindInviteeAsync(
			email,
			nameParts[0],
			nameParts.Length > 1 ? nameParts[1] : string.Empty,
			temporaryPassword,
			tenant.TenantId,
			cancellationToken);
	}

	private async Task SendAsync(Parent parent, string? temporaryPassword, bool existingAccount, CancellationToken cancellationToken)
	{
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
			HtmlBody: ParentInvitationEmail.BuildHtml(parent.Name, school, link, temporaryPassword, existingAccount),
			PlainTextBody: ParentInvitationEmail.BuildPlainText(parent.Name, school, link, temporaryPassword, existingAccount)
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
