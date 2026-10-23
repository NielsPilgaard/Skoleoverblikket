namespace Skoleoverblikket.Api.Email;

/// <summary>
/// The account line in an invitation email. Nobody gets a password by email: a new invitee chooses one
/// on the invitation page, and someone who already has a login at the school, for example a teacher
/// invited as a parent, uses that.
/// </summary>
internal static class InvitationAccountNotice
{
	private const string ExistingText = "Log ind med din eksisterende konto. Du skal ikke oprette en ny.";
	private const string NewText = "Du vælger selv din adgangskode, når du klikker på knappen.";

	internal static string Html(bool existingAccount) => $"""
		<div style="margin:0 0 24px;padding:16px;background:#f3f4f6;border-radius:8px;border:1px solid #e5e7eb;">
		  <p style="margin:0;font-size:14px;color:#111827;">{(existingAccount ? ExistingText : NewText)}</p>
		</div>
		""";

	internal static string PlainText(bool existingAccount) => $"\n{(existingAccount ? ExistingText : NewText)}\n";
}
