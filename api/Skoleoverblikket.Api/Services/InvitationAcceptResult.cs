namespace Skoleoverblikket.Api.Services;

public enum InvitationAcceptResult
{
	Accepted,
	Invalid,

	/// <summary>The inviting school no longer has the module that board/parent users need.</summary>
	ModuleInactive,
}
