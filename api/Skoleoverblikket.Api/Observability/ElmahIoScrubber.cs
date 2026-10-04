using System.Text.RegularExpressions;
using Elmah.Io.Client;

namespace Skoleoverblikket.Api.Observability;

/// <summary>
/// elmah.io stores data in the US (Azure West/East US, no EU region), so personal data is removed
/// before a message leaves the API. Ids (user, tenant, entity GUIDs) are kept: they are needed to
/// debug and identify no one outside our own database. Our own log messages and exceptions name
/// people by id only; names cannot be matched by a pattern, so they must not be logged.
///
/// Dropped: request bodies (Form), cookies, query values, every header not on a short allowlist
/// (Authorization, Cookie, client IP, Referer), and the user unless it is an id. Masked in free
/// text: email addresses, phone numbers, CPR numbers, bearer tokens and the row values PostgreSQL
/// puts in constraint errors (which can hold names).
/// </summary>
public static partial class ElmahIoScrubber
{
	private const string Redacted = "[fjernet]";

	private static readonly HashSet<string> SafeServerVariables = new(StringComparer.OrdinalIgnoreCase)
	{
		"Content-Type",
		"Content-Length",
		"Host",
		"User-Agent",
		"Accept",
		"Accept-Language",
		"REQUEST_METHOD",
		"SERVER_PROTOCOL",
	};

	public static void Scrub(CreateMessage message)
	{
		message.Form = [];
		message.Cookies = [];
		message.QueryString = message.QueryString?.Select(i => new Item(i.Key, Redacted)).ToList();
		message.ServerVariables = message.ServerVariables?.Where(i => SafeServerVariables.Contains(i.Key)).ToList();

		message.Title = ScrubText(message.Title);
		message.TitleTemplate = ScrubText(message.TitleTemplate);
		message.Detail = ScrubText(message.Detail);
		// The JWT name claim is the Keycloak subject (a GUID); anything else could be a name.
		message.User = Guid.TryParse(message.User, out _) ? message.User : null;
		message.Data = message.Data?.Select(i => new Item(i.Key, ScrubText(i.Value))).ToList();
		message.Breadcrumbs = message.Breadcrumbs?.Select(b =>
		{
			b.Message = ScrubText(b.Message);
			return b;
		}).ToList();
	}

	public static string? ScrubText(string? text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return text;
		}

		text = PostgresKeyValues().Replace(text, "$1=(" + Redacted + ")");
		text = PostgresFailingRow().Replace(text, "$1(" + Redacted + ")");
		text = BearerToken().Replace(text, "Bearer " + Redacted);
		text = Jwt().Replace(text, Redacted);
		text = Email().Replace(text, Redacted);
		text = Cpr().Replace(text, Redacted);
		text = Phone().Replace(text, Redacted);
		return text;
	}

	// PostgreSQL unique/foreign key errors: Key ("Email")=(hanne@skole.dk) already exists.
	[GeneratedRegex(@"(Key \([^)]*\))=\([^)]*\)")]
	private static partial Regex PostgresKeyValues();

	// PostgreSQL not-null/check errors: Failing row contains (<id>, Mikkel Hansen, null, ...).
	[GeneratedRegex(@"(Failing row contains )\(.*\)")]
	private static partial Regex PostgresFailingRow();

	[GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
	private static partial Regex BearerToken();

	[GeneratedRegex(@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+")]
	private static partial Regex Jwt();

	[GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
	private static partial Regex Email();

	[GeneratedRegex(@"\b[0-3]\d[01]\d\d{2}-?\d{4}\b")]
	private static partial Regex Cpr();

	// Danish numbers: 8 digits, optionally +45/0045 and spaced in pairs or 4+4. Not part of a GUID or longer number.
	[GeneratedRegex(@"(?<![\w-])(?:\+45|0045)?\s?(?:\d{2}\s?){3}\d{2}(?![\w-])")]
	private static partial Regex Phone();
}
