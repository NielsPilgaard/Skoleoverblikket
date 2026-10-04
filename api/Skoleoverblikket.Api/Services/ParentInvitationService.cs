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
	KeycloakAdminService keycloakAdmin)
{
	private static readonly TimeSpan InvitationValidity = TimeSpan.FromDays(14);
	private readonly string BaseUrl = appOptions.Value.SanitizedBaseUrl;

	public async Task<ParentInvitation> CreateAndSendAsync(Parent parent, CancellationToken cancellationToken)
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

		var temporaryPassword = await EnsureKeycloakAccountAsync(parent, cancellationToken);

		var school = await db.Schools
			.IgnoreQueryFilters()
			.Where(s => s.Id == tenant.TenantId)
			.Select(s => s.Name)
			.FirstOrDefaultAsync(cancellationToken) ?? "Skoleoverblikket";

		var link = $"{BaseUrl}/parent-invitation/{token}";

		await email.SendAsync(new EmailMessage(
			To: parent.Email,
			Subject: ParentInvitationEmail.Subject(school),
			HtmlBody: ParentInvitationEmail.BuildHtml(parent.Name, school, link, temporaryPassword),
			PlainTextBody: ParentInvitationEmail.BuildPlainText(parent.Name, school, link, temporaryPassword)
		), cancellationToken);

		return invitation;
	}

	private async Task<string?> EnsureKeycloakAccountAsync(Parent parent, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(parent.KeycloakSubject))
		{

			return null;
		}

		if (string.IsNullOrWhiteSpace(parent.Name))
		{

			throw new InvalidOperationException($"Cannot create Keycloak account for parent {parent.Id}: name is empty.");
		}

		var temporaryPassword = GenerateTemporaryPassword();
		var nameParts = parent.Name.Trim().Split(' ', 2);
		parent.KeycloakSubject = await keycloakAdmin.CreateUserAsync(
			parent.Email,
			nameParts[0],
			nameParts.Length > 1 ? nameParts[1] : string.Empty,
			temporaryPassword,
			tenant.TenantId,
			realmRole: Roles.Parent,
			forcePasswordReset: true,
			cancellationToken);
		await db.SaveChangesAsync(cancellationToken);
		return temporaryPassword;
	}

	public async Task<ParentInvitation?> FindValidAsync(string token, CancellationToken cancellationToken) =>
		await db.ParentInvitations
				.IgnoreQueryFilters()
				.Include(i => i.Parent)
				.FirstOrDefaultAsync(
					i => i.Token == token && i.AcceptedAt == null && i.ExpiresAt > DateTimeOffset.UtcNow,
					cancellationToken);

	public async Task MarkAcceptedAsync(ParentInvitation invitation, string keycloakSubject, CancellationToken cancellationToken)
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
