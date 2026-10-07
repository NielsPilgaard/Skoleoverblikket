using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Skoleoverblikket.Api.Email;

public sealed class MailKitEmailSender(IOptionsMonitor<SmtpOptions> options) : IEmailSender
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
				mime.Bcc.Add(MailboxAddress.Parse(bcc));
			}
		}

		var bodyBuilder = new BodyBuilder
		{
			HtmlBody = message.HtmlBody,
			TextBody = message.PlainTextBody,
		};
		mime.Body = bodyBuilder.ToMessageBody();

		using var smtp = new SmtpClient();

		var tls = string.IsNullOrEmpty(_options.Username) ? SecureSocketOptions.None : SecureSocketOptions.Auto;

		await smtp.ConnectAsync(_options.Host, _options.Port, tls, cancellationToken);

		if (!string.IsNullOrEmpty(_options.Username) && smtp.Capabilities.HasFlag(SmtpCapabilities.Authentication))
		{
			await smtp.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
		}

		await smtp.SendAsync(mime, cancellationToken);
		await smtp.DisconnectAsync(quit: true, cancellationToken);
	}
}
