using System.Text.Encodings.Web;

namespace Skoleoverblikket.Api.Email;

/// <summary>
/// The personal welcome email Niels sends every new school a day after signup. It should read like an
/// email typed in a mail client: no <see cref="EmailTemplate"/>, logo, button or images.
/// Sender and reply-to are constants, not config: there is one founder and it is not tenant-specific.
/// </summary>
internal static class WelcomeEmail
{
	internal const string FromName = "Niels Pilgaard";

	internal const string ReplyTo = "niels@skoleoverblikket.dk";

	internal static EmailMessage Build(string to, string firstName, string schoolName)
	{
		// One list of paragraphs feeds both bodies, so they never drift apart.
		string[] paragraphs =
		[
			$"Hej {firstName},",
			"Jeg hedder Niels, og det er mig, der har bygget Skoleoverblikket.",
			"For at være ærlig: Den her mail bliver sendt automatisk dagen efter, at en skole er blevet oprettet. Men jeg har skrevet den selv, og hvis du svarer, lander dit svar direkte i min indbakke. Jeg læser og svarer på alle mails personligt, oftest samme dag.",
			$"Tak, fordi I har oprettet {schoolName}. Jeg har lavet Skoleoverblikket, fordi skoler bruger alt for meget tid og alt for mange penge på administration. Den tid skulle hellere bruges på eleverne.",
			"Hvad skal der til, for at Skoleoverblikket bliver en rigtig god hjælp hos jer?",
			"Hvis noget er svært at finde ud af, eller hvis I mangler noget, så skriv bare tilbage. Ingen spørgsmål er for små.",
			"Venlig hilsen\nNiels\nSkoleoverblikket",
		];

		var text = string.Join("\n\n", paragraphs) + "\n";
		var html = string.Concat(paragraphs.Select(p =>
			$"<p>{string.Join("<br>", p.Split('\n').Select(HtmlEncoder.Default.Encode))}</p>\n"));

		return new EmailMessage(
			To: to,
			Subject: $"Velkommen til Skoleoverblikket, {firstName}",
			HtmlBody: html,
			PlainTextBody: text,
			FromName: FromName,
			ReplyTo: ReplyTo);
	}
}
