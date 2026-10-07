namespace Skoleoverblikket.BackupAgent;

/// <summary>
/// Everything the agent needs to find Postgres, its volumes and its schedule. Bound from the
/// "Agent" section, so compose sets e.g. <c>Agent__LiveVolumeName</c>. pgBackRest's own settings
/// (repo, S3 keys, cipher pass) come from PGBACKREST_* env vars, which child processes inherit.
/// </summary>
public sealed class AgentOptions
{
	public const string SectionName = "Agent";

	public string Stanza { get; init; } = "main";

	/// <summary>The Postgres socket directory, shared with the postgres container through a volume.</summary>
	public string SocketDirectory { get; init; } = "/var/run/postgresql";

	/// <summary>
	/// The live data volume, mounted read-only at the same path Postgres uses, because pgBackRest
	/// refuses a pg1-path that differs from the server's data_directory.
	/// </summary>
	public string LiveDataDirectory { get; init; } = "/var/lib/postgresql/data";

	/// <summary>The spare data volume, read-write. Restores go here, never over the live one.</summary>
	public string SpareDataDirectory { get; init; } = "/pgdata/spare";

	/// <summary>Compose volume name of the live data volume (<c>PG_VOLUME</c>). Shown in the console and typed to confirm.</summary>
	public string LiveVolumeName { get; init; } = "pgdata-a";

	/// <summary>Compose volume name of the spare data volume (<c>PG_SPARE_VOLUME</c>).</summary>
	public string SpareVolumeName { get; init; } = "pgdata-b";

	/// <summary>Persistent agent volume: state.json, the console key and local copies of the ops bucket files.</summary>
	public string StateDirectory { get; init; } = "/var/lib/backup-agent";

	/// <summary>tmpfs for the weekly drill. Nothing restored there survives the drill.</summary>
	public string DrillDirectory { get; init; } = "/drill";

	public string AppDatabase { get; init; } = "skoleoverblikket";
	public string KeycloakDatabase { get; init; } = "keycloak";
	public string KeycloakRealm { get; init; } = "Skoleoverblikket";

	public string DatabaseUser { get; init; } = "backup_agent";
	public string SlotName { get; init; } = "agent";

	/// <summary>
	/// pg_receivewal's directory, on its own named volume (task 60 D2a). Once a segment is flushed
	/// here the slot moves on and Postgres drops its copy, so this is the only copy until
	/// archive-push succeeds. Segments are deleted only after that.
	/// </summary>
	public string WalReceiveDirectory { get; init; } = "/wal-receive";

	/// <summary>
	/// Unpushed WAL in <see cref="WalReceiveDirectory"/> before the agent pauses pg_receivewal (D2a).
	/// Postgres then holds WAL for the slot, up to max_slot_wal_keep_size. Both caps bound the disk use.
	/// </summary>
	public long WalSpoolMaxBytes { get; init; } = 4L * 1024 * 1024 * 1024;

	/// <summary>Retained WAL in Postgres that raises a warning before the slot is lost.</summary>
	public long SlotWarnBytes { get; init; } = 2L * 1024 * 1024 * 1024;

	/// <summary>The DPA promise (BACKUP_RETENTION_DAYS in dataProcessing.ts).</summary>
	public int RetentionDays { get; init; } = 14;

	public string TimeZone { get; init; } = "Europe/Copenhagen";
	public int FullBackupHour { get; init; } = 2;
	public DayOfWeek VerifyDay { get; init; } = DayOfWeek.Wednesday;
	public int VerifyHour { get; init; } = 3;
	public DayOfWeek DrillDay { get; init; } = DayOfWeek.Sunday;
	public int DrillHour { get; init; } = 4;

	/// <summary>How often the agent commits a heartbeat row and switches WAL, so the 15-minute RPO holds on quiet nights.</summary>
	public TimeSpan WalSwitchInterval { get; init; } = TimeSpan.FromMinutes(5);

	public TimeSpan StatusInterval { get; init; } = TimeSpan.FromMinutes(5);

	/// <summary>The RPO. Data secured longer ago than this makes the agent unhealthy.</summary>
	public TimeSpan MaxDataLoss { get; init; } = TimeSpan.FromMinutes(15);

	/// <summary>Shown in the backoffice card and the console, e.g. <c>ssh -L 9090:127.0.0.1:9090 ubuntu@vps</c>.</summary>
	public string? SshTunnelCommand { get; init; }

	public string WalSpoolDirectory => WalReceiveDirectory;

	/// <summary>Finished segments set aside when streaming restarts from a new slot. pg_receivewal ignores subdirectories.</summary>
	public string WalOutboxDirectory => Path.Combine(WalReceiveDirectory, "outbox");
}

/// <summary>The S3 ops bucket (task 60 D3). Not the backup repo and not the files bucket.</summary>
public sealed class OpsBucketOptions
{
	public const string SectionName = "OpsBucket";

	public string ServiceUrl { get; init; } = "";
	public string AccessKey { get; init; } = "";
	public string SecretKey { get; init; } = "";
	public string BucketName { get; init; } = "skoleoverblikket-ops";

	public bool IsConfigured =>
		!string.IsNullOrWhiteSpace(ServiceUrl) && !string.IsNullOrWhiteSpace(AccessKey) && !string.IsNullOrWhiteSpace(SecretKey);
}

/// <summary>elmah.io heartbeats (task 53 D3). A heartbeat without an id is skipped.</summary>
public sealed class HeartbeatOptions
{
	public const string SectionName = "Heartbeats";

	/// <summary>Overridable so a local stack can point at a mock.</summary>
	public string BaseUrl { get; init; } = "https://api.elmah.io";

	public string ApiKey { get; init; } = "";
	public string LogId { get; init; } = "";
	public string WalId { get; init; } = "";
	public string BackupId { get; init; } = "";
	public string DrillId { get; init; } = "";
	public string VerifyId { get; init; } = "";
}
