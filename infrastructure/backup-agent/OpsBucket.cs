using System.Net;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Skoleoverblikket.BackupAgent;

/// <summary>A run, action or event, appended to history/YYYY-MM.json. PII-free: no row content, ever.</summary>
public sealed record HistoryEntry(
	DateTimeOffset At,
	string Kind,
	bool? Ok,
	string Summary,
	double? DurationSeconds = null,
	JsonElement? Details = null);

/// <summary>
/// The ops bucket (task 60 D3): status.json, history/, ledger.json and migrations.json. It survives a
/// broken database and a restore that rewinds it. Every file is written to the agent's volume first,
/// so the console works with S3 down; the bucket is the copy that survives losing the box.
/// </summary>
public sealed class OpsBucket
{
	public const string StatusKey = "status.json";
	public const string LedgerKey = "ledger.json";
	public const string MigrationsKey = "migrations.json";

	private readonly OpsBucketOptions _options;
	private readonly string _localDirectory;
	private readonly IAmazonS3? _s3;
	private readonly ILogger<OpsBucket> _logger;
	private readonly SemaphoreSlim _historyGate = new(1, 1);

	public OpsBucket(IOptions<OpsBucketOptions> options, IOptions<AgentOptions> agent, ILogger<OpsBucket> logger)
	{
		_options = options.Value;
		_logger = logger;
		_localDirectory = Path.Combine(agent.Value.StateDirectory, "ops");
		if (_options.IsConfigured)
		{
			_s3 = new AmazonS3Client(
				new BasicAWSCredentials(_options.AccessKey, _options.SecretKey),
				new AmazonS3Config { ServiceURL = _options.ServiceUrl, ForcePathStyle = true });
		}
	}

	public bool IsConfigured => _s3 is not null;
	public string? LastError { get; private set; }
	public DateTimeOffset? LastWriteOkAt { get; private set; }

	public async Task WriteAsync<T>(string key, T value, CancellationToken cancellationToken)
	{
		var json = JsonSerializer.Serialize(value, AgentJson.Options);
		var path = LocalPath(key);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		await File.WriteAllTextAsync(path + ".tmp", json, cancellationToken);
		File.Move(path + ".tmp", path, overwrite: true);

		if (_s3 is null)
		{
			return;
		}

		try
		{
			await _s3.PutObjectAsync(new PutObjectRequest
			{
				BucketName = _options.BucketName,
				Key = key,
				ContentBody = json,
				ContentType = "application/json",
			}, cancellationToken);
			LastWriteOkAt = DateTimeOffset.UtcNow;
			LastError = null;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			LastError = $"{key}: {ex.Message}";
			_logger.LogWarning(ex, "Could not write {Key} to the ops bucket", key);
		}
	}

	/// <summary>Reads the bucket's copy when <paramref name="preferRemote"/> (it survives a restore), else the local one.</summary>
	public async Task<T?> ReadAsync<T>(string key, bool preferRemote, CancellationToken cancellationToken)
	{
		if (preferRemote && _s3 is not null)
		{
			try
			{
				using var response = await _s3.GetObjectAsync(_options.BucketName, key, cancellationToken);
				using var reader = new StreamReader(response.ResponseStream);
				var json = await reader.ReadToEndAsync(cancellationToken);
				var path = LocalPath(key);
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
				await File.WriteAllTextAsync(path, json, cancellationToken);
				return JsonSerializer.Deserialize<T>(json, AgentJson.Options);
			}
			catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
			{
				// Fall through to the local copy, which may be newer than a bucket that was never written.
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				LastError = $"{key}: {ex.Message}";
				_logger.LogWarning(ex, "Could not read {Key} from the ops bucket; using the local copy", key);
			}
		}

		var local = LocalPath(key);
		return File.Exists(local)
			? JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(local, cancellationToken), AgentJson.Options)
			: default;
	}

	public async Task AppendHistoryAsync(HistoryEntry entry, CancellationToken cancellationToken)
	{
		var key = HistoryKey(entry.At);
		await _historyGate.WaitAsync(cancellationToken);
		try
		{
			var entries = await ReadAsync<List<HistoryEntry>>(key, preferRemote: !File.Exists(LocalPath(key)), cancellationToken) ?? [];
			entries.Add(entry);
			await WriteAsync(key, entries, cancellationToken);
		}
		finally
		{
			_historyGate.Release();
		}
	}

	/// <summary>The local history of the last few months, newest first.</summary>
	public IReadOnlyList<HistoryEntry> ReadHistory(int months = 3)
	{
		var now = DateTimeOffset.UtcNow;
		var entries = new List<HistoryEntry>();
		for (var i = 0; i < months; i++)
		{
			var path = LocalPath(HistoryKey(now.AddMonths(-i)));
			if (File.Exists(path))
			{
				entries.AddRange(JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(path), AgentJson.Options) ?? []);
			}
		}

		return [.. entries.OrderByDescending(e => e.At)];
	}

	/// <summary>On a fresh box (VPS gone), pull history and ledger down so the console has them.</summary>
	public async Task SyncDownAsync(CancellationToken cancellationToken)
	{
		var now = DateTimeOffset.UtcNow;
		string[] keys = [LedgerKey, MigrationsKey, .. Enumerable.Range(0, 3).Select(i => HistoryKey(now.AddMonths(-i)))];
		foreach (var key in keys.Where(k => !File.Exists(LocalPath(k))))
		{
			await ReadAsync<JsonElement>(key, preferRemote: true, cancellationToken);
		}
	}

	private static string HistoryKey(DateTimeOffset at) => $"history/{at.UtcDateTime:yyyy-MM}.json";

	private string LocalPath(string key) => Path.Combine(_localDirectory, key.Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>elmah.io heartbeats (task 53 D3): elmah.io alerts on Unhealthy and on a missing heartbeat.</summary>
public sealed class Heartbeats(IHttpClientFactory httpClientFactory, IOptions<HeartbeatOptions> options, ILogger<Heartbeats> logger)
{
	public enum Kind
	{
		Wal,
		Backup,
		Drill,
		Verify,
	}

	private readonly System.Collections.Concurrent.ConcurrentDictionary<Kind, (DateTimeOffset At, Health Result)> _lastSent = new();

	private string Id(Kind kind) => kind switch
	{
		Kind.Wal => options.Value.WalId,
		Kind.Backup => options.Value.BackupId,
		Kind.Drill => options.Value.DrillId,
		_ => options.Value.VerifyId,
	};

	public bool IsConfigured(Kind kind) =>
		!string.IsNullOrWhiteSpace(options.Value.ApiKey) && !string.IsNullOrWhiteSpace(options.Value.LogId) && !string.IsNullOrWhiteSpace(Id(kind));

	public (DateTimeOffset At, Health Result)? LastSent(Kind kind) => _lastSent.TryGetValue(kind, out var last) ? last : null;

	public async Task SendAsync(Kind kind, Health result, string? reason, TimeSpan? took, CancellationToken cancellationToken)
	{
		var opts = options.Value;
		var id = Id(kind);
		if (!IsConfigured(kind))
		{
			return;
		}

		_lastSent[kind] = (DateTimeOffset.UtcNow, result);
		try
		{
			var client = httpClientFactory.CreateClient();
			using var response = await client.PostAsJsonAsync(
				$"{opts.BaseUrl.TrimEnd('/')}/v3/heartbeats/{opts.LogId}/{id}?api_key={Uri.EscapeDataString(opts.ApiKey)}",
				new { result = result.ToString(), reason, took = took is { } t ? (long?)t.TotalMilliseconds : null, application = "backup-agent" },
				cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				logger.LogWarning("elmah.io heartbeat {Kind} returned {Status}", kind, (int)response.StatusCode);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogWarning(ex, "Could not send elmah.io heartbeat {Kind}", kind);
		}
	}
}
