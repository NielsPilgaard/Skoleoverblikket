using System.Text.Encodings.Web;

namespace Skoleoverblikket.Api.Email;

internal static class StaffInvitationEmail
{
	internal static string Subject(string schoolName) => $"Invitation til {schoolName} på Skoleoverblikket";

	internal static string BuildHtml(string name, string schoolName, string link, bool existingAccount)
	{
		var encodedName = HtmlEncoder.Default.Encode(name);
		var encodedSchoolName = HtmlEncoder.Default.Encode(schoolName);
		var encodedLink = HtmlEncoder.Default.Encode(link);

		var accountBlock = InvitationAccountNotice.Html(existingAccount);

		return EmailTemplate.Wrap($"Invitation til {encodedSchoolName}", $"""
			<h1>Du er inviteret til {encodedSchoolName}</h1>
			<p>Hej {encodedName},<br><br>
			Du er inviteret til at oprette din konto på Skoleoverblikket som medarbejder på <strong>{encodedSchoolName}</strong>.
			Klik på knappen herunder for at komme i gang. Linket er gyldigt i 14 dage.</p>
			{accountBlock}
			<div class="btn-wrapper">
			  <a href="{encodedLink}" class="btn">Opret konto og acceptér</a>
			</div>
			<div class="notice">
			  <p style="margin-bottom:0;">Har du problemer med knappen? Kopier dette link direkte i din browser:<br>
			  <a href="{encodedLink}" style="color:#1f6321;word-break:break-all;">{encodedLink}</a></p>
			</div>
			""");
	}

	internal static string BuildPlainText(string name, string schoolName, string link, bool existingAccount)
	{
		var accountLine = InvitationAccountNotice.PlainText(existingAccount);

		return $"Hej {name},\n\nDu er inviteret til {schoolName} på Skoleoverblikket.{accountLine}\nOpret din konto her:\n{link}\n\nLinket er gyldigt i 14 dage.\n";
	}
}
