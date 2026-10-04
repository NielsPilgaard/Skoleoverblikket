using Elmah.Io.Client;
using Skoleoverblikket.Api.Observability;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// elmah.io stores error logs in the US, so <see cref="ElmahIoScrubber"/> must strip personal data
/// before a message is sent: request bodies, cookies, query values, identifying headers, and
/// emails, phone numbers, CPR numbers, tokens and PostgreSQL key values in free text. Ids stay.
/// </summary>
public sealed class ElmahIoScrubberTests
{
	[Test]
	public async Task RequestData_IsDropped_SafeHeadersKept()
	{
		var message = new CreateMessage
		{
			Form = [new Item("Email", "hanne@skole.dk"), new Item("Password", "hemmelig")],
			Cookies = [new Item("session", "abc")],
			QueryString = [new Item("email", "hanne@skole.dk")],
			ServerVariables =
			[
				new Item("Authorization", "Bearer eyJhbGciOi.eyJzdWIi.c2lnbmF0dXJl"),
				new Item("Cookie", "session=abc"),
				new Item("REMOTE_ADDR", "203.0.113.7"),
				new Item("Referer", "https://skoleoverblikket.dk/elever?navn=Mikkel"),
				new Item("User-Agent", "Mozilla/5.0"),
			],
		};

		ElmahIoScrubber.Scrub(message);

		await Assert.That(message.Form.Count).IsEqualTo(0);
		await Assert.That(message.Cookies.Count).IsEqualTo(0);
		await Assert.That(message.QueryString.Single().Key).IsEqualTo("email");
		await Assert.That(message.QueryString.Single().Value).DoesNotContain("hanne");
		await Assert.That(message.ServerVariables.Select(i => i.Key)).IsEquivalentTo(["User-Agent"]);
	}

	[Test]
	public async Task FreeText_PersonalDataMasked_IdsKept()
	{
		var schoolId = Guid.NewGuid();
		var message = new CreateMessage
		{
			Title = $"Failed for hanne@skole.dk at school {schoolId}",
			Detail = """
				Npgsql.PostgresException: 23505: duplicate key value violates unique constraint "IX_Parents_Email"
				DETAIL: Key ("TenantId", "Email")=(3f2504e0-4f89-11d3-9a0c-0305e82c3301, mor@example.com) already exists.
				Ring på +45 12 34 56 78 eller 87654321. CPR 010203-1234.
				Authorization: Bearer abc.def-ghi
				""",
			User = "far@example.dk",
			Data = [new Item("Email", "far@example.dk"), new Item("StaffId", schoolId.ToString())],
		};

		ElmahIoScrubber.Scrub(message);

		var all = string.Join("\n", message.Title, message.Detail, message.User, string.Join(";", message.Data.Select(d => d.Value)));
		foreach (var leak in new[] { "hanne@skole.dk", "mor@example.com", "far@example.dk", "12 34 56 78", "87654321", "010203-1234", "abc.def-ghi" })
		{
			await Assert.That(all).DoesNotContain(leak);
		}

		await Assert.That(message.Title).Contains(schoolId.ToString());
		await Assert.That(message.Detail).Contains("IX_Parents_Email").And.Contains("Key (\"TenantId\", \"Email\")=");
		await Assert.That(message.Data.Single(d => d.Key == "StaffId").Value).IsEqualTo(schoolId.ToString());
	}
}
