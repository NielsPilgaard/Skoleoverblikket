using System.ComponentModel.DataAnnotations;

namespace Skoleoverblikket.Api.Email;

public sealed class SmtpOptions
{
	public const string SectionName = "Smtp";

	public string Host { get; init; } = "smtp.tem.scaleway.com";
	public int Port { get; init; } = 587;

	public string Username { get; init; } = string.Empty;
	public string Password { get; init; } = string.Empty;

	[Required(AllowEmptyStrings = false)]
	public string FromAddress { get; init; } = "kontakt@skoleoverblikket.dk";

	public string FromName { get; init; } = "Skoleoverblikket";

	/// <summary>
	/// Recipients (To + Bcc) the provider accepts in one message. Scaleway TEM's default is 10; Support can raise it.
	/// </summary>
	[Range(2, 1000)]
	public int MaxRecipientsPerMessage { get; init; } = 10;

	/// <summary>Bcc addresses per message when To is <see cref="FromAddress"/>, which takes one recipient slot.</summary>
	public int BccBatchSize => MaxRecipientsPerMessage - 1;
}
