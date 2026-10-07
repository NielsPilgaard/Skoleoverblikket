using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// Read-only access to the S3 ops bucket the backup agent writes to (task 60). Its own key, which
/// can only read that bucket, so the API never holds backup credentials. Empty keys mean "not set
/// up" (local dev, CI staging) and the backoffice card says so.
/// </summary>
public sealed class BackupStatusOptions
{
	public const string SectionName = "BackupStatus";

	public string ServiceUrl { get; init; } = "";
	public string AccessKey { get; init; } = "";
	public string SecretKey { get; init; } = "";
	public string BucketName { get; init; } = "skoleoverblikket-ops";

	public bool IsConfigured =>
		!string.IsNullOrWhiteSpace(ServiceUrl) && !string.IsNullOrWhiteSpace(AccessKey) && !string.IsNullOrWhiteSpace(SecretKey);
}

public enum BackupHealth
{
	Healthy,
	Degraded,
	Unhealthy,
}

/// <summary>What the backoffice card shows. A subset of the agent's PII-free status.json.</summary>
public sealed record BackupStatusDto(
	DateTimeOffset GeneratedAt,
	BackupHealth Health,
	IReadOnlyList<string> Issues,
	DateTimeOffset? DataSecuredAt,
	DateTimeOffset? LastFullBackupAt,
	DateTimeOffset? OldestRestorableAt,
	int RetentionDays,
	bool RetentionOk,
	DateTimeOffset? LastDrillAt,
	bool? LastDrillOk,
	double? LastDrillMinutes,
	string? SshTunnelCommand);

public sealed class BackupStatusService(
	IOptions<BackupStatusOptions> options,
	[FromKeyedServices(BackupStatusService.S3Key)] IAmazonS3 s3)
{
	public const string S3Key = "backup-status";
	public const string StatusObjectKey = "status.json";

	public enum Outcome
	{
		NotConfigured,
		NotWritten,
		Found,
	}

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
	{
		Converters = { new JsonStringEnumConverter() },
	};

	public async Task<(Outcome Outcome, BackupStatusDto? Status)> GetAsync(CancellationToken cancellationToken)
	{
		var opts = options.Value;
		if (!opts.IsConfigured)
		{
			return (Outcome.NotConfigured, null);
		}

		AgentStatusFile? file;
		try
		{
			using var response = await s3.GetObjectAsync(opts.BucketName, StatusObjectKey, cancellationToken);
			file = await JsonSerializer.DeserializeAsync<AgentStatusFile>(response.ResponseStream, Json, cancellationToken);
		}
		catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
		{
			return (Outcome.NotWritten, null);
		}

		if (file is null)
		{
			return (Outcome.NotWritten, null);
		}

		return (Outcome.Found, new BackupStatusDto(
			file.GeneratedAt,
			file.Health,
			file.Issues ?? [],
			file.DataSecuredAt,
			file.Backups?.LastFullAt,
			file.Backups?.OldestRestorableAt,
			file.Backups?.RetentionDays ?? 14,
			file.Backups?.RetentionOk ?? false,
			file.Drill?.LastAt,
			file.Drill?.LastOk,
			file.Drill?.LastMinutes,
			file.SshTunnelCommand));
	}

	// The parts of the agent's status.json (infrastructure/backup-agent/Status.cs) the card needs.
	// Unknown fields are ignored, so the agent can add fields without an API deploy.
	private sealed record AgentStatusFile(
		DateTimeOffset GeneratedAt,
		BackupHealth Health,
		List<string>? Issues,
		DateTimeOffset? DataSecuredAt,
		AgentBackups? Backups,
		AgentDrill? Drill,
		string? SshTunnelCommand);

	private sealed record AgentBackups(DateTimeOffset? LastFullAt, DateTimeOffset? OldestRestorableAt, int RetentionDays, bool RetentionOk);

	private sealed record AgentDrill(DateTimeOffset? LastAt, bool? LastOk, double? LastMinutes);
}

public static class BackupStatusExtensions
{
	public static IServiceCollection AddBackupStatus(this IServiceCollection services)
	{
		services.AddOptions<BackupStatusOptions>().BindConfiguration(BackupStatusOptions.SectionName);
		services.AddKeyedSingleton<IAmazonS3>(BackupStatusService.S3Key, (sp, _) =>
		{
			var opts = sp.GetRequiredService<IOptions<BackupStatusOptions>>().Value;
			var config = new AmazonS3Config { ServiceURL = opts.IsConfigured ? opts.ServiceUrl : "http://localhost", ForcePathStyle = true };
			return new AmazonS3Client(new BasicAWSCredentials(opts.AccessKey, opts.SecretKey), config);
		});
		services.AddScoped<BackupStatusService>();
		return services;
	}
}
