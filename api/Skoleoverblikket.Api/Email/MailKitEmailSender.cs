using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Skoleoverblikket.Api.Email;

public sealed class MailKitEmailSender(IOptionsMonitor<SmtpOptions> options, ILogger<MailKitEmailSender> logger) : IEmailSender
{
	private readonly SmtpOptions _options = options.CurrentValue;

	public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
	{
		var mime = new MimeMessage();
		mime.From.Add(new MailboxAddress(message.FromName ?? _options.FromName, _options.FromAddress));
		mime.To.Add(MailboxAddress.Parse(message.To));
		if (message.ReplyTo is not null)
		{
			mime.ReplyTo.Add(MailboxAddress.Parse(message.ReplyTo));
		}

		mime.Subject = message.Subject;

		if (message.Bcc is { Count: > 0 })
		{
			foreach (var bcc in message.Bcc)
			{
				// One malformed address in a Bcc batch must not stop the message for everyone else in it.
				if (MailboxAddress.TryParse(bcc, out var address))
				{
					mime.Bcc.Add(address);
				}
				else
				{
					logger.LogWarning("Skipped a malformed Bcc address");
				}
			}
		}

		var bodyBuilder = new BodyBuilder
		{
			HtmlBody = message.HtmlBody,
			TextBody = message.PlainTextBody,
		};
		mime.Body = bodyBuilder.ToMessageBody();

		using var smtp = new TolerantSmtpClient(logger);

		var tls = string.IsNullOrEmpty(_options.Username) ? SecureSocketOptions.None : SecureSocketOptions.Auto;

		await smtp.ConnectAsync(_options.Host, _options.Port, tls, cancellationToken);

		if (!string.IsNullOrEmpty(_options.Username) && smtp.Capabilities.HasFlag(SmtpCapabilities.Authentication))
		{
			await smtp.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
		}

		await smtp.SendAsync(mime, cancellationToken);
		await smtp.DisconnectAsync(quit: true, cancellationToken);
	}

	/// <summary>
	/// MailKit throws when the server rejects any one recipient, which would drop a whole Bcc batch over a single
	/// dead address. This logs and goes on; MailKit still throws if no recipient at all is accepted.
	/// </summary>
	private sealed class TolerantSmtpClient(ILogger logger) : SmtpClient
	{
		protected override void OnRecipientNotAccepted(MimeMessage message, MailboxAddress mailbox, SmtpResponse response) =>
			logger.LogWarning("SMTP server rejected a recipient: {StatusCode} {Response}", response.StatusCode, response.Response);
	}
}
