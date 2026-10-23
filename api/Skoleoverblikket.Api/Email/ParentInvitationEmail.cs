using System.Text.Encodings.Web;

namespace Skoleoverblikket.Api.Email;

internal static class ParentInvitationEmail
{
	internal static string Subject(string schoolName) => $"Invitation til {schoolName} på Skoleoverblikket";

	internal static string BuildHtml(string name, string schoolName, string link, bool existingAccount)
	{
		var encodedName = HtmlEncoder.Default.Encode(name);
		var encodedSchoolName = HtmlEncoder.Default.Encode(schoolName);
		var encodedLink = HtmlEncoder.Default.Encode(link);

		var accountBlock = InvitationAccountNotice.Html(existingAccount);

		return EmailTemplate.Wrap($"Adgang til {encodedSchoolName}", $"""
			<h1>Adgang til {encodedSchoolName}</h1>
			<p>Hej {encodedName},<br><br>
			Du er inviteret til Skoleoverblikket, skolens fælles overblik, af <strong>{encodedSchoolName}</strong>.
			Her finder du dit barns skema og ugeplan, kan skrive med skolen i kontaktbogen, melde fravær og se skolens kontaktliste og beskeder.
			Klik på knappen herunder for at oprette din konto. Linket er gyldigt i 14 dage.</p>
			{accountBlock}
			<div class="btn-wrapper">
			  <a href="{encodedLink}" class="btn">Opret konto</a>
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

		return $"""
Hej {name},

Du er inviteret til Skoleoverblikket, skolens fælles overblik, af {schoolName}. Her finder du dit barns skema og ugeplan, kan skrive med skolen i kontaktbogen, melde fravær og se skolens kontaktliste og beskeder.{accountLine}
Opret din konto her:
{link}

Linket er gyldigt i 14 dage.
""";
	}
}
