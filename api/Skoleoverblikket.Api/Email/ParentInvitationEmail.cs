using System.Text.Encodings.Web;

namespace Skoleoverblikket.Api.Email;

internal static class ParentInvitationEmail
{
	internal static string Subject(string schoolName) => $"Invitation til {schoolName} på Skoleoverblikket";

	internal static string BuildHtml(string name, string schoolName, string link, string? temporaryPassword, bool existingAccount = false)
	{
		var encodedName = HtmlEncoder.Default.Encode(name);
		var encodedSchoolName = HtmlEncoder.Default.Encode(schoolName);
		var encodedLink = HtmlEncoder.Default.Encode(link);

		var passwordBlock = existingAccount ? ExistingAccountNotice.Html
			: temporaryPassword is not null
			? $"""
			  <div style="margin:0 0 24px;padding:16px;background:#f3f4f6;border-radius:8px;border:1px solid #e5e7eb;">
			    <p style="margin:0 0 8px;font-size:13px;color:#6b7280;">Din midlertidige adgangskode (skal ændres ved første login):</p>
			    <span style="font-family:monospace;font-size:18px;font-weight:700;letter-spacing:0.05em;color:#111827;">{HtmlEncoder.Default.Encode(temporaryPassword)}</span>
			  </div>
			  """
			: string.Empty;

		return EmailTemplate.Wrap($"Adgang til {encodedSchoolName}", $"""
			<h1>Adgang til {encodedSchoolName}</h1>
			<p>Hej {encodedName},<br><br>
			Du er inviteret til Skoleoverblikket, skolens fælles overblik, af <strong>{encodedSchoolName}</strong>.
			Her finder du dit barns skema og ugeplan, kan skrive med skolen i kontaktbogen, melde fravær og se skolens kontaktliste og beskeder.
			Klik på knappen herunder for at oprette din konto. Linket er gyldigt i 14 dage.</p>
			{passwordBlock}
			<div class="btn-wrapper">
			  <a href="{encodedLink}" class="btn">Opret konto</a>
			</div>
			<div class="notice">
			  <p style="margin-bottom:0;">Har du problemer med knappen? Kopier dette link direkte i din browser:<br>
			  <a href="{encodedLink}" style="color:#1f6321;word-break:break-all;">{encodedLink}</a></p>
			</div>
			""");
	}

	internal static string BuildPlainText(string name, string schoolName, string link, string? temporaryPassword, bool existingAccount = false)
	{
		var passwordLine = existingAccount ? ExistingAccountNotice.PlainText
			: temporaryPassword is not null
			? $"\nDin midlertidige adgangskode: {temporaryPassword}\n(Skal ændres ved første login.)\n"
			: string.Empty;

		return $"""
Hej {name},

Du er inviteret til Skoleoverblikket, skolens fælles overblik, af {schoolName}. Her finder du dit barns skema og ugeplan, kan skrive med skolen i kontaktbogen, melde fravær og se skolens kontaktliste og beskeder.{passwordLine}
Opret din konto her:
{link}

Linket er gyldigt i 14 dage.
""";
	}
}
