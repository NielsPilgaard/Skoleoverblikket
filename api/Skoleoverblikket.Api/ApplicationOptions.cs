using System.Buffers.Text;
using System.ComponentModel.DataAnnotations;

namespace Skoleoverblikket.Api;

public sealed class ApplicationOptions
{
	public const string SectionName = "App";

	[Required(AllowEmptyStrings = false)]
	public string BaseUrl { get; init; } = "http://localhost:5173";

	/// <summary>
	/// Commit sha the image was built from (<c>APP_VERSION</c> build arg). Sent as
	/// <c>X-App-Version</c> by <c>/api/health</c> and as the elmah.io message version.
	/// </summary>
	public string Version { get; init; } = "dev";

	public string SanitizedBaseUrl => BaseUrl.TrimEnd('/');
}
