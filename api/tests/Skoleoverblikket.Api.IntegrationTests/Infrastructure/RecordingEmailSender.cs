using System.Collections.Concurrent;
using System.Text.RegularExpressions;
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

	/// <summary>The token in the newest invitation link to <paramref name="address"/>, such as /<paramref name="linkPath"/>/{token}.</summary>
	public string LastInvitationToken(string address, string linkPath) =>
		Regex.Match(To(address).Last().PlainTextBody!, $"/{linkPath}/([A-Za-z0-9_-]+)").Groups[1].Value;
}
