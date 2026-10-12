namespace Skoleoverblikket.Api.OpenApi;

/// <summary>
/// True while dotnet-getdocument starts the app at build time to write openapi/ (see Directory.Build.targets).
/// No database or object storage is running then, so startup must not reach for them.
/// </summary>
public static class OpenApiGeneration
{
	public static bool IsRunning { get; } = string.Equals(
		Environment.GetEnvironmentVariable("OPENAPI_GENERATE"), "true",
		StringComparison.OrdinalIgnoreCase);
}
