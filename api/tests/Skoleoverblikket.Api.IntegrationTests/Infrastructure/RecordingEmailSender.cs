using System.Collections.Concurrent;
using Skoleoverblikket.Api.Email;

namespace Skoleoverblikket.Api.IntegrationTests.Infrastructure;

/// <summary>Collects outgoing email instead of sending it, so tests can assert who got what.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
	private readonly ConcurrentQueue<EmailMessage> _sent = new();

	public IReadOnlyList<EmailMessage> Sent => [.. _sent];

	public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
	{
		_sent.Enqueue(message);
		return Task.CompletedTask;
	}

	/// <summary>Messages addressed to <paramref name="address"/>, directly or in Bcc.</summary>
	public IReadOnlyList<EmailMessage> To(string address) =>
		[.. _sent.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase)
			|| (m.Bcc?.Contains(address, StringComparer.OrdinalIgnoreCase) ?? false))];
}
