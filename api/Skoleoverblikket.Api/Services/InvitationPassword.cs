using System.ComponentModel.DataAnnotations;

namespace Skoleoverblikket.Api.Services;

/// <summary>The password a new invitee chooses on the invitation page. The invitation token proves the email.</summary>
public sealed record SetInvitationPasswordRequest(
	[Required] string Token,
	[Required]
	[MinLength(8)]
	string Password);

/// <summary>Tokens for the invitee's first session, so they need not log in right after choosing a password.</summary>
public sealed record InvitationLoginDto(string AccessToken, string? RefreshToken);

public enum SetInvitationPasswordResult
{
	Set,
	Invalid,

	/// <summary>The login already has a password the person chose; an invitation link must not replace it.</summary>
	PasswordAlreadySet,
}
