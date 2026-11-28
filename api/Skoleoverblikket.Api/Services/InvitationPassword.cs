using System.ComponentModel.DataAnnotations;

namespace Skoleoverblikket.Api.Services;

/// <summary>The password a new invitee chooses on the invitation page. The invitation token proves the email.</summary>
public sealed record SetInvitationPasswordRequest(
	[Required] string Token,
	[Required]
	[MinLength(PasswordRules.MinLength)]
	[MaxLength(PasswordRules.MaxLength)]
	string Password);

/// <summary>Tokens for the invitee's first session, so they need not log in right after choosing a password.</summary>
public sealed record InvitationLoginDto(string AccessToken, string? RefreshToken);

public enum SetInvitationPasswordResult
{
	Set,
	Invalid,

	/// <summary>The login already has a password the person chose; an invitation link must not replace it.</summary>
	PasswordAlreadySet,

	/// <summary>The password is the person's email, which the realm's password policy rejects.</summary>
	PasswordIsEmail,
}

/// <summary>
/// The realm's password policy (Skoleoverblikket-realm.json <c>passwordPolicy</c>), checked before Keycloak
/// sees the password. Keycloak's 400 would otherwise surface as "try again later". The username is the
/// email, so <c>notUsername</c> and <c>notEmail</c> are the same rule.
/// </summary>
public static class PasswordRules
{
	public const int MinLength = 8;
	public const int MaxLength = 128;
	public const string IsEmailMessage = "Adgangskoden må ikke være din e-mailadresse.";

	public static bool IsEmail(string password, string? email) =>
		!string.IsNullOrWhiteSpace(email) && string.Equals(password, email.Trim(), StringComparison.OrdinalIgnoreCase);
}
