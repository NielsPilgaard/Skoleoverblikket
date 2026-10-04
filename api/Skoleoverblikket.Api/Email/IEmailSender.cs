namespace Skoleoverblikket.Api.Email;

public interface IEmailSender
{
	Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record EmailMessage(
	string To,
	string Subject,
	string HtmlBody,
	string? PlainTextBody = null,
	IReadOnlyList<string>? Bcc = null,
	string? FromName = null,
	string? ReplyTo = null);
