using System.Threading.Channels;
using Skoleoverblikket.Api.Email;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// Sends notification e-mails after the request has returned. Each SMTP send opens its own connection, so even
/// the Bcc batches for every parent at a school add seconds to Hanne's request, and an SMTP error would turn an
/// already saved change into a 500. The queue lives in memory: e-mails still
/// queued when the process stops are lost, which is acceptable for notifications that are also shown in-app.
/// </summary>
public sealed class NotificationEmailQueue(IEmailSender email, ILogger<NotificationEmailQueue> logger) : BackgroundService
{
	private readonly Channel<EmailMessage> _channel = Channel.CreateUnbounded<EmailMessage>(
		new UnboundedChannelOptions { SingleReader = true });

	public void Enqueue(EmailMessage message) => _channel.Writer.TryWrite(message);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			await foreach (var message in _channel.Reader.ReadAllAsync(stoppingToken))
			{
				await SendAsync(message);
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}

		// Shutting down (usually a deploy): send what is already queued for as long as the host waits for us.
		while (_channel.Reader.TryRead(out var message))
		{
			await SendAsync(message);
		}
	}

	private async Task SendAsync(EmailMessage message)
	{
		try
		{
			await email.SendAsync(message, CancellationToken.None);
		}
		catch (Exception ex)
		{
			// One bad address or SMTP hiccup must not stop the rest. The recipient is left out of the log on purpose.
			logger.LogError(ex, "Notification e-mail could not be sent");
		}
	}
}
