using System.ComponentModel.DataAnnotations;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Email;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// The caller's own login (Min konto) and an admin's reset of someone's two-factor login. Passwords and
/// two-factor secrets never pass through here: the user changes them on Keycloak's own pages
/// (application-initiated actions), and this service only reads metadata, deletes credentials and ends
/// sessions through the admin API.
/// </summary>
public sealed class AccountService(
	AppDbContext db,
	ITenantContext tenant,
	KeycloakAdminService keycloak,
	IEmailSender email,
	ILogger<AccountService> logger)
{
	private const string Otp = "otp";
	private const string RecoveryCodes = "recovery-authn-codes";
	private static readonly string[] Passkeys = ["webauthn", "webauthn-passwordless"];

	/// <summary>Every credential type that counts as a second factor, and the codes that back it up.</summary>
	private static readonly string[] TwoFactorTypes = [Otp, RecoveryCodes, .. Passkeys];

	public record AccountCredentialDto(string Id, DateTimeOffset? CreatedAt, string? Label);

	/// <param name="Remaining">Unused codes, when Keycloak reports it.</param>
	public record RecoveryCodesDto(string Id, DateTimeOffset? CreatedAt, int? Remaining);

	/// <param name="Otp">The newest authenticator app, or null when two-factor is off.</param>
	/// <param name="AuthenticatorApps">Every authenticator app, newest first. Keycloak lets a user add a second
	/// app, and two-factor stays on until every one is removed.</param>
	/// <param name="ExternalLogin">The identity provider (e.g. "microsoft") when the person logs in through one.</param>
	public record AccountSecurityDto(
		string? Email,
		bool HasPassword,
		AccountCredentialDto? Otp,
		IReadOnlyList<AccountCredentialDto> AuthenticatorApps,
		RecoveryCodesDto? RecoveryCodes,
		IReadOnlyList<AccountCredentialDto> Passkeys,
		string? ExternalLogin);

	public record ResetTwoFactorRequest([Required, EmailAddress, StringLength(500)] string Email);

	public enum TwoFactorResetResult
	{
		Reset,
		NotFound,

		/// <summary>The person has no login yet (the invitation is not accepted).</summary>
		NoLogin,

		/// <summary>The login is also used at another school, or is the operator's: only the superadmin may reset it.</summary>
		SharedLogin,
		Self,
		NoTwoFactor,
	}

	public enum PersonKind
	{
		Staff,
		Parent,
		BoardMember,
	}

	public async Task<AccountSecurityDto> GetSecurityAsync(string subject, CancellationToken cancellationToken)
	{
		var credentials = await keycloak.GetCredentialsAsync(subject, cancellationToken);
		var apps = credentials.Where(c => c.Type == Otp).OrderByDescending(c => c.CreatedDate).Select(ToDto).ToList();
		var codes = credentials.FirstOrDefault(c => c.Type == RecoveryCodes);

		return new AccountSecurityDto(
			Email: await keycloak.GetEmailAsync(subject, cancellationToken),
			HasPassword: credentials.Any(c => c.Type == "password"),
			Otp: apps.FirstOrDefault(),
			AuthenticatorApps: apps,
			RecoveryCodes: codes is null ? null : new RecoveryCodesDto(codes.Id ?? "", CreatedAt(codes), RemainingCodes(codes)),
			Passkeys: [.. credentials.Where(c => Passkeys.Contains(c.Type)).Select(ToDto)],
			ExternalLogin: await keycloak.GetExternalLoginAsync(subject, cancellationToken));
	}

	public Task LogoutEverywhereAsync(string subject, CancellationToken cancellationToken) =>
		keycloak.LogoutEverywhereAsync(subject, cancellationToken);

	/// <summary>
	/// Deletes leftover recovery codes once no authenticator app or passkey is left, so the login page
	/// stops offering a recovery code. Keycloak deletes only the credential the user removed.
	/// </summary>
	public async Task CleanupRecoveryCodesAsync(string subject, CancellationToken cancellationToken)
	{
		var credentials = await keycloak.GetCredentialsAsync(subject, cancellationToken);
		if (credentials.Any(c => c.Type == Otp || Passkeys.Contains(c.Type)))
		{
			return;
		}

		foreach (var codes in credentials.Where(c => c.Type == RecoveryCodes && c.Id is not null))
		{
			await keycloak.DeleteCredentialAsync(subject, codes.Id!, cancellationToken);
		}
	}

	/// <summary>
	/// A school admin turns off two-factor for someone at their school who lost their phone and their
	/// recovery codes. Only for a login no other school uses: otherwise an admin at one school could weaken
	/// a login another school relies on.
	/// </summary>
	public async Task<TwoFactorResetResult> ResetTwoFactorAsync(
		PersonKind kind, Guid id, string adminSubject, CancellationToken cancellationToken)
	{
		var person = kind switch
		{
			PersonKind.Staff => await db.Staff.Where(s => s.Id == id)
				.Select(s => new { s.KeycloakSubject }).FirstOrDefaultAsync(cancellationToken),
			PersonKind.Parent => await db.Parents.Where(p => p.Id == id)
				.Select(p => new { p.KeycloakSubject }).FirstOrDefaultAsync(cancellationToken),
			PersonKind.BoardMember => await db.BoardMembers.Where(m => m.Id == id)
				.Select(m => new { m.KeycloakSubject }).FirstOrDefaultAsync(cancellationToken),
			_ => throw new ArgumentOutOfRangeException(nameof(kind)),
		};

		if (person is null)
		{
			return TwoFactorResetResult.NotFound;
		}

		if (string.IsNullOrEmpty(person.KeycloakSubject))
		{
			return TwoFactorResetResult.NoLogin;
		}

		var subject = person.KeycloakSubject;
		if (subject == adminSubject)
		{
			return TwoFactorResetResult.Self;
		}

		if (await UsedByAnotherSchoolAsync(subject, cancellationToken) || await keycloak.IsSuperAdminAsync(subject, cancellationToken))
		{
			return TwoFactorResetResult.SharedLogin;
		}

		var adminName = await db.Staff.Where(s => s.KeycloakSubject == adminSubject)
			.Select(s => s.Name).FirstOrDefaultAsync(cancellationToken) ?? "en administrator";
		// Schools has no TenantId column (its Id is the tenant), so the explicit Id keeps this to the caller's school.
		var school = await db.Schools.IgnoreQueryFilters()
			.Where(s => s.Id == tenant.TenantId).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken) ?? "Skoleoverblikket";

		return await ResetAsync(subject, $"{adminName} på {school}", cancellationToken);
	}

	/// <summary>The operator resets any login by its email, also one shared between schools.</summary>
	public async Task<TwoFactorResetResult> ResetTwoFactorByEmailAsync(
		string loginEmail, string superAdminSubject, CancellationToken cancellationToken)
	{
		var subject = await keycloak.FindUserIdByEmailAsync(loginEmail.Trim(), cancellationToken);
		if (subject is null)
		{
			return TwoFactorResetResult.NotFound;
		}

		if (subject == superAdminSubject)
		{
			return TwoFactorResetResult.Self;
		}

		return await ResetAsync(subject, "Skoleoverblikkets support", cancellationToken);
	}

	private async Task<TwoFactorResetResult> ResetAsync(string subject, string resetBy, CancellationToken cancellationToken)
	{
		// Recovery codes alone count too: login keeps asking for one until they are gone, e.g. when the
		// person removed the app and Min konto's cleanup never ran.
		var secondFactors = (await keycloak.GetCredentialsAsync(subject, cancellationToken))
			.Where(c => TwoFactorTypes.Contains(c.Type) && c.Id is not null)
			.ToList();
		if (secondFactors.Count == 0)
		{
			return TwoFactorResetResult.NoTwoFactor;
		}

		// Log out before deleting: if a step fails, factors are still left, so a retry finishes the reset
		// instead of finding no two-factor and refusing.
		await keycloak.LogoutEverywhereAsync(subject, cancellationToken);

		foreach (var credential in secondFactors)
		{
			await keycloak.DeleteCredentialAsync(subject, credential.Id!, cancellationToken);
		}

		// The login's own email, not the school's row: a school admin can edit the row's email.
		var to = await keycloak.GetEmailAsync(subject, cancellationToken);
		if (string.IsNullOrWhiteSpace(to))
		{
			logger.LogWarning("Two-factor reset for Keycloak user {Subject} without an email to notify", subject);
			return TwoFactorResetResult.Reset;
		}

		try
		{
			await SendResetNoticeAsync(to, resetBy, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// The reset has happened, so report success: a retry would find no two-factor and refuse.
			logger.LogError(ex, "Two-factor reset for Keycloak user {Subject}, but the notice email failed", subject);
		}

		return TwoFactorResetResult.Reset;
	}

	private Task SendResetNoticeAsync(string to, string resetBy, CancellationToken cancellationToken) =>
		email.SendAsync(new EmailMessage(
			To: to,
			Subject: "Tofaktor er slået fra på din konto",
			HtmlBody: EmailTemplate.Wrap("Tofaktor er slået fra", $"""
				<h1>Tofaktor er slået fra på din konto</h1>
				<p>{HtmlEncoder.Default.Encode(resetBy)} har slået tofaktor fra på din konto på Skoleoverblikket.
				Du logger nu ind med kun din adgangskode, og du er blevet logget ud på alle enheder.</p>
				<p>Du kan slå tofaktor til igen under <strong>Min konto</strong>.</p>
				<div class="notice"><p style="margin-bottom:0;">Hvis du ikke har bedt om det, så kontakt skolens kontor.</p></div>
				"""),
			PlainTextBody: $"{resetBy} har slået tofaktor fra på din konto på Skoleoverblikket. Du logger nu ind med kun din adgangskode, og du er blevet logget ud på alle enheder.\n\nDu kan slå tofaktor til igen under Min konto.\n\nHvis du ikke har bedt om det, så kontakt skolens kontor.\n"),
			cancellationToken);

	private async Task<bool> UsedByAnotherSchoolAsync(string subject, CancellationToken cancellationToken)
	{
		// IgnoreQueryFilters: a row at any other school makes the login shared. Only a yes/no leaves this
		// method, so no other school's data is exposed.
		var tenantId = tenant.TenantId;
		return await db.Staff.IgnoreQueryFilters().AnyAsync(s => s.KeycloakSubject == subject && s.TenantId != tenantId, cancellationToken)
			|| await db.BoardMembers.IgnoreQueryFilters().AnyAsync(m => m.KeycloakSubject == subject && m.TenantId != tenantId, cancellationToken)
			|| await db.Parents.IgnoreQueryFilters().AnyAsync(p => p.KeycloakSubject == subject && p.TenantId != tenantId, cancellationToken);
	}

	private static AccountCredentialDto ToDto(CredentialSummary c) => new(c.Id ?? "", CreatedAt(c), c.UserLabel);

	private static DateTimeOffset? CreatedAt(CredentialSummary c) =>
		c.CreatedDate is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;

	private static int? RemainingCodes(CredentialSummary c)
	{
		if (string.IsNullOrEmpty(c.CredentialData))
		{
			return null;
		}

		try
		{
			using var doc = JsonDocument.Parse(c.CredentialData);
			return doc.RootElement.TryGetProperty("remainingCodes", out var remaining) && remaining.TryGetInt32(out var n) ? n : null;
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
