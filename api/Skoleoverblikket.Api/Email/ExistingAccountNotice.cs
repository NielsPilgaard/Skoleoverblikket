namespace Skoleoverblikket.Api.Email;

/// <summary>
/// Shown in an invitation email instead of a temporary password when the invitee already has a login
/// at the school, for example a teacher invited as a parent.
/// </summary>
internal static class ExistingAccountNotice
{
	internal const string Html = """
		<div style="margin:0 0 24px;padding:16px;background:#f3f4f6;border-radius:8px;border:1px solid #e5e7eb;">
		  <p style="margin:0;font-size:14px;color:#111827;">Log ind med din eksisterende konto. Du skal ikke oprette en ny.</p>
		</div>
		""";

	internal const string PlainText = "\nLog ind med din eksisterende konto. Du skal ikke oprette en ny.\n";
}
