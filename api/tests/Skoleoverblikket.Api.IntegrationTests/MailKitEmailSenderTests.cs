using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skoleoverblikket.Api.Email;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// One dead Bcc address must not drop a whole parent-communication batch. Mailpit accepts every recipient,
/// so this runs the real sender against a tiny SMTP server that rejects one.
/// </summary>
public sealed class MailKitEmailSenderTests
{
	[Test]
	public async Task RejectedBccRecipient_IsSkipped_RestOfBatchIsDelivered()
	{
		using var server = new RejectingSmtpServer(rejected: "dead@skole.dk");
		var serverTask = server.RunAsync();

		var sender = new MailKitEmailSender(
			new FixedOptions(new SmtpOptions { Host = "127.0.0.1", Port = server.Port }),
			NullLogger<MailKitEmailSender>.Instance);

		await sender.SendAsync(new EmailMessage(
			To: "kontakt@skoleoverblikket.dk",
			Subject: "Ugebrev",
			HtmlBody: "<p>Hej</p>",
			Bcc: ["hanne@skole.dk", "dead@skole.dk", "birgitte@skole.dk"]));

		await serverTask.WaitAsync(TimeSpan.FromSeconds(10));
		await Assert.That(server.Delivered).IsTrue();
		await Assert.That(server.AcceptedRecipients)
			.IsEquivalentTo(["kontakt@skoleoverblikket.dk", "hanne@skole.dk", "birgitte@skole.dk"]);
	}

	private sealed class FixedOptions(SmtpOptions value) : IOptionsMonitor<SmtpOptions>
	{
		public SmtpOptions CurrentValue => value;
		public SmtpOptions Get(string? name) => value;
		public IDisposable? OnChange(Action<SmtpOptions, string?> listener) => null;
	}

	/// <summary>Serves one SMTP session without TLS or auth and answers 550 to RCPT TO for <c>rejected</c>.</summary>
	private sealed class RejectingSmtpServer(string rejected) : IDisposable
	{
		private readonly TcpListener _listener = Start();

		public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
		public List<string> AcceptedRecipients { get; } = [];
		public bool Delivered { get; private set; }

		private static TcpListener Start()
		{
			var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			return listener;
		}

		public async Task RunAsync()
		{
			using var client = await _listener.AcceptTcpClientAsync();
			await using var stream = client.GetStream();
			using var reader = new StreamReader(stream, Encoding.ASCII);
			await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

			await writer.WriteLineAsync("220 fake ESMTP");
			while (await reader.ReadLineAsync() is { } line)
			{
				var command = line.Split(' ', 2)[0].ToUpperInvariant();
				switch (command)
				{
					case "EHLO" or "HELO":
						await writer.WriteLineAsync("250 fake");
						break;
					case "RCPT":
						var address = line[(line.IndexOf('<') + 1)..line.IndexOf('>')];
						if (address == rejected)
						{
							await writer.WriteLineAsync("550 5.1.1 No such user");
						}
						else
						{
							AcceptedRecipients.Add(address);
							await writer.WriteLineAsync("250 OK");
						}

						break;
					case "DATA":
						await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
						while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
						{
						}

						Delivered = true;
						await writer.WriteLineAsync("250 Queued");
						break;
					case "QUIT":
						await writer.WriteLineAsync("221 Bye");
						return;
					default:
						await writer.WriteLineAsync("250 OK");
						break;
				}
			}
		}

		public void Dispose() => _listener.Stop();
	}
}
