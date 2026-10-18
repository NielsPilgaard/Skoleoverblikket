using System.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Skoleoverblikket.BackupAgent;

/// <summary>A school SchoolDeletionService deleted, as mirrored to ledger.json in the ops bucket.</summary>
public sealed record LedgerEntry(Guid SchoolId, string SchoolName, DateTimeOffset DeletedAt);

public enum RestoreMode
{
	Time,
	Migration,
	Backup,
}

public sealed record RestoreRequest(RestoreMode Mode, string? Time, string? MigrationId, string? BackupLabel, string? Confirmation, bool PrepareResurrected);

public sealed record RestoreTarget(string[] Arguments, string Description, DateTimeOffset? Target);

/// <summary>
/// Task 60 D5: restore into the spare volume, check it with a temporary Postgres inside the agent,
/// and let a human make it live: "Go live" in the console names the spare for Postgres' next start,
/// and a redeploy in Dokploy does the rest. The live volume is never written to; the old one stays
/// for forensics until "Delete old volume".
/// </summary>
public sealed class RestoreService(
	IOptions<AgentOptions> options,
	PgBackRest pgBackRest,
	RepoInfoCache repo,
	DrillChecks checks,
	StateStore state,
	OpsBucket ops,
	JobRunner jobs,
	DataVolumes volumes)
{
	// Mirrors SchoolDeletionService (API): deletion needs 90 days since cancellation and a warning 7 days old.
	private static readonly TimeSpan SchoolRetention = TimeSpan.FromDays(90);
	private static readonly TimeSpan WarningNotice = TimeSpan.FromDays(7);

	private readonly AgentOptions _options = options.Value;

	/// <summary>Why the spare volume must not be written to, or null when it's safe.</summary>
	public async Task<string?> SpareProblemAsync(CancellationToken cancellationToken)
	{
		if (volumes.UnknownLiveDirectory is { } unknown)
		{
			return $"Postgres runs on {unknown}, not on a /pgdata volume. Restores are off until that's fixed.";
		}

		if (volumes.GoLivePending)
		{
			return $"{volumes.SpareName} goes live on the next redeploy. Cancel that first.";
		}

		var spare = volumes.SpareDirectory;
		if (!Directory.Exists(spare))
		{
			return $"Spare volume not mounted at {spare}.";
		}

		var liveId = await Volumes.IdentityAsync(volumes.LiveDirectory, cancellationToken);
		var spareId = await Volumes.IdentityAsync(spare, cancellationToken);
		if (liveId is null || spareId is null)
		{
			return "Can't tell whether spare and live are different volumes (stat failed).";
		}

		return liveId == spareId
			? $"{volumes.LiveName} and {volumes.SpareName} are the same volume. Check the volume mounts in compose."
			: null;
	}

	public bool SpareHasPidFile => File.Exists(Path.Combine(volumes.SpareDirectory, "postmaster.pid"));

	public bool SpareIsEmpty => !Directory.Exists(volumes.SpareDirectory) || !Directory.EnumerateFileSystemEntries(volumes.SpareDirectory).Any();

	/// <summary>
	/// Names the spare for Postgres' next start. A restore with failed checks needs an extra tick:
	/// in a real incident live is often what's broken, so checks against it can fail for good reasons.
	/// Nothing changes until someone redeploys.
	/// </summary>
	public async Task<string?> ArmGoLiveAsync(string? confirmation, bool acceptFailedChecks, CancellationToken cancellationToken)
	{
		var spare = volumes.SpareName;
		if (!string.Equals(confirmation?.Trim(), spare, StringComparison.Ordinal))
		{
			return $"Type {spare} to confirm.";
		}

		if (jobs.Current is not null)
		{
			return "Another job is running.";
		}

		if (await SpareProblemAsync(cancellationToken) is { } problem)
		{
			return problem;
		}

		var (restore, old) = state.Read(s => (s.Spare, s.OldVolume));
		if (restore is null && old is null)
		{
			return $"{spare} holds no restore.";
		}

		if (restore is { Ok: false } && !acceptFailedChecks)
		{
			return "Some checks failed. Tick the box to go live anyway.";
		}

		volumes.SetNext(volumes.Spare);
		await ops.AppendHistoryAsync(new HistoryEntry(DateTimeOffset.UtcNow, "golive", null,
			$"Console: {spare} goes live on the next redeploy{(restore is null ? " (switch back)" : $" (restored to {restore.TargetDescription})")}"), cancellationToken);
		return null;
	}

	public async Task CancelGoLiveAsync(CancellationToken cancellationToken)
	{
		var pending = volumes.GoLivePending ? volumes.SpareName : null;
		volumes.SetNext(volumes.Live);
		if (pending is not null)
		{
			await ops.AppendHistoryAsync(new HistoryEntry(DateTimeOffset.UtcNow, "golive", null, $"Console: go-live of {pending} cancelled"), cancellationToken);
		}
	}

	public async Task<List<MigrationRow>> MigrationsAsync(CancellationToken cancellationToken) =>
		await ops.ReadAsync<List<MigrationRow>>(OpsBucket.MigrationsKey, preferRemote: false, cancellationToken) ?? [];

	public async Task<(RestoreTarget? Target, string? Error)> ResolveAsync(RestoreRequest request, CancellationToken cancellationToken)
	{
		var snapshot = repo.Current;
		switch (request.Mode)
		{
			case RestoreMode.Time:
				{
					if (Fmt.ParseLocal(request.Time) is not { } target)
					{
						return (null, "Pick a time.");
					}

					if (snapshot.RangeFor(target) is not { } range)
					{
						return (null, $"{Fmt.DateTime(target)} is not restorable.");
					}

					return (new RestoreTarget(
						["--type=time", $"--target={Fmt.PgTimestamp(target)}", $"--target-timeline={range.Timeline}"],
						Fmt.DateTime(target), target), null);
				}

			case RestoreMode.Migration:
				{
					var migration = (await MigrationsAsync(cancellationToken)).FirstOrDefault(m => m.MigrationId == request.MigrationId);
					if (migration?.AppliedAt is not { } appliedAt)
					{
						return (null, "Pick a migration with a known time.");
					}

					if (snapshot.RangeFor(appliedAt) is not { } range)
					{
						return (null, $"Migration applied {Fmt.DateTime(appliedAt)}, which is not restorable.");
					}

					// Exclusive: recovery stops just before the transaction that applied the migration.
					return (new RestoreTarget(
						["--type=time", $"--target={Fmt.PgTimestamp(appliedAt)}", "--target-exclusive", $"--target-timeline={range.Timeline}"],
						$"just before {migration.MigrationId} ({Fmt.DateTimeSeconds(appliedAt)})", appliedAt), null);
				}

			default:
				{
					var backup = snapshot.Backups.FirstOrDefault(b => b.Label == request.BackupLabel);
					return backup is null
						? (null, "Pick a backup.")
						: (new RestoreTarget(["--type=immediate", $"--set={backup.Label}"], $"backup {backup.Label} ({Fmt.DateTime(backup.StoppedAt)})", null), null);
				}
		}
	}

	/// <summary>Validates the request and starts the restore job, or returns why not.</summary>
	public async Task<(Job? Job, string? Error)> StartAsync(RestoreRequest request, CancellationToken cancellationToken)
	{
		if (!string.Equals(request.Confirmation?.Trim(), volumes.SpareName, StringComparison.Ordinal))
		{
			return (null, $"Type {volumes.SpareName} to confirm.");
		}

		if (await SpareProblemAsync(cancellationToken) is { } problem)
		{
			return (null, problem);
		}

		var (target, error) = await ResolveAsync(request, cancellationToken);
		if (target is null)
		{
			return (null, error);
		}

		var slot = volumes.Spare;
		var job = jobs.TryStart(JobKind.Restore, $"Restore to {target.Description} ({volumes.Name(slot)})",
			(job, ct) => RestoreAsync(job, slot, target, request.PrepareResurrected, ct));
		return job is null ? (null, "Another job is running.") : (job, null);
	}

	private async Task<JobOutcome> RestoreAsync(Job job, string slot, RestoreTarget target, bool prepareResurrected, CancellationToken cancellationToken)
	{
		var stopwatch = Stopwatch.StartNew();
		var spare = volumes.Directory(slot);
		var name = volumes.Name(slot);
		if (await SpareProblemAsync(cancellationToken) is { } problem)
		{
			return new JobOutcome(false, problem);
		}

		if (slot != volumes.Spare)
		{
			return new JobOutcome(false, $"{name} went live while the restore was starting. Nothing was wiped.");
		}

		job.Log($"Wiping {name} ({spare})…");
		Volumes.WipeContents(spare);
		state.Update(s =>
		{
			s.Spare = null;
			s.OldVolume = null;
		});

		job.Log($"Restoring to {target.Description}…");
		var restore = await pgBackRest.RestoreAsync(spare, target.Arguments, job.Log, cancellationToken);
		if (!restore.Ok)
		{
			return new JobOutcome(false, $"pgbackrest restore failed: {restore.Tail(2)}");
		}

		job.Log("Starting a temporary Postgres on the spare, replaying WAL…");
		await using var temp = await TempPostgres.StartAsync(spare, "restore", 5433, job.Log, cancellationToken);
		var report = await checks.RunAsync(temp, target.Target, job.Log, cancellationToken);
		var reached = report.ReachedAt ?? target.Target ?? DateTimeOffset.UtcNow;

		await using (var app = temp.Connect(_options.AppDatabase))
		{
			await app.OpenAsync(cancellationToken);
			var resurrected = await ResurrectedAsync(app, reached, cancellationToken);
			var prepared = false;
			if (prepareResurrected && resurrected.Any(r => !r.DeletedAutomatically))
			{
				var ids = resurrected.Where(r => !r.DeletedAutomatically).Select(r => r.SchoolId).ToArray();
				job.Log($"Backdating the deletion warning of {ids.Length} resurrected schools, so SchoolRetentionJob deletes them at API start…");
				await PrepareRedeletionAsync(app, ids, cancellationToken);
				resurrected = await ResurrectedAsync(app, reached, cancellationToken);
				prepared = true;
			}

			job.Log("Clearing recovery settings, stopping Postgres cleanly…");
			await temp.ClearRecoverySettingsAsync(cancellationToken);
			await temp.StopAsync();

			var ok = report.Checks.All(c => c.Ok != false);
			var result = new SpareRestore(DateTimeOffset.UtcNow, ok, target.Description, report.ReachedAt, stopwatch.Elapsed.TotalSeconds,
				report.Checks, report.Tables, resurrected, prepared, report.MigrationId, report.KeycloakUsers);
			state.Update(s =>
			{
				s.Spare = result;
				s.Checklist = [];
			});

			var skipped = report.Checks.Count(c => c.Ok is null);
			var summary = $"{name} restored to {target.Description} in {Fmt.Duration(stopwatch.Elapsed.TotalSeconds)}. " +
				$"Checks: {report.Checks.Count(c => c.Ok == true)} OK, {report.Checks.Count(c => c.Ok == false)} failed" +
				$"{(skipped > 0 ? $", {skipped} skipped (live down)" : "")}. Resurrected schools: {resurrected.Count}.";
			return new JobOutcome(ok, summary, new
			{
				target.Description,
				ReachedAt = report.ReachedAt,
				Checks = report.Checks,
				report.TableCount,
				report.Rows,
				ResurrectedSchools = resurrected.Count,
				ResurrectedPrepared = prepared,
				DurationSeconds = stopwatch.Elapsed.TotalSeconds,
			});
		}
	}

	/// <summary>
	/// Schools in the restore that were deleted after the restore point. Compared with ledger.json in
	/// the ops bucket, not the restored table: the restore rewound that table.
	/// </summary>
	private async Task<List<ResurrectedSchool>> ResurrectedAsync(NpgsqlConnection app, DateTimeOffset reached, CancellationToken cancellationToken)
	{
		var ledger = await ops.ReadAsync<List<LedgerEntry>>(OpsBucket.LedgerKey, preferRemote: true, cancellationToken) ?? [];
		var deletedLater = ledger.Where(e => e.DeletedAt > reached).GroupBy(e => e.SchoolId).ToDictionary(g => g.Key, g => g.Max(e => e.DeletedAt));
		if (deletedLater.Count == 0)
		{
			return [];
		}

		await using var command = new NpgsqlCommand("""
			SELECT s."Id", s."Name", sub."CanceledAt", sub."DeletionWarningSentAt"
			FROM "Schools" s LEFT JOIN "Subscriptions" sub ON sub."SchoolId" = s."Id"
			WHERE s."Id" = ANY(@ids)
			""", app);
		command.Parameters.AddWithValue("ids", deletedLater.Keys.ToArray());
		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		var now = DateTimeOffset.UtcNow;
		var result = new List<ResurrectedSchool>();
		while (await reader.ReadAsync(cancellationToken))
		{
			var canceledAt = reader.IsDBNull(2) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(2);
			var warnedAt = reader.IsDBNull(3) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(3);
			var automatic = canceledAt <= now - SchoolRetention && warnedAt <= now - WarningNotice;
			var id = reader.GetGuid(0);
			result.Add(new ResurrectedSchool(id, reader.GetString(1), deletedLater[id], automatic));
		}

		return result;
	}

	/// <summary>
	/// A school restored to before its deletion warning would get a fresh warning email and live 7 more
	/// days. Backdating the warning makes SchoolRetentionJob delete it on its first pass instead.
	/// </summary>
	private static async Task PrepareRedeletionAsync(NpgsqlConnection app, Guid[] schoolIds, CancellationToken cancellationToken)
	{
		await using var command = new NpgsqlCommand("""
			UPDATE "Subscriptions" SET "DeletionWarningSentAt" = now() - interval '8 days'
			WHERE "SchoolId" = ANY(@ids) AND "CanceledAt" IS NOT NULL
			  AND ("DeletionWarningSentAt" IS NULL OR "DeletionWarningSentAt" > now() - interval '7 days')
			""", app);
		command.Parameters.AddWithValue("ids", schoolIds);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<(Job? Job, string? Error)> StartDeleteOldVolumeAsync(string? confirmation, CancellationToken cancellationToken)
	{
		var name = volumes.SpareName;
		if (!string.Equals(confirmation?.Trim(), name, StringComparison.Ordinal))
		{
			return (null, $"Type {name} to confirm.");
		}

		if (await SpareProblemAsync(cancellationToken) is { } problem)
		{
			return (null, problem);
		}

		var directory = volumes.SpareDirectory;
		var job = jobs.TryStart(JobKind.DeleteOldVolume, $"Delete old volume ({name})", (job, _) =>
		{
			var old = state.Read(s => s.OldVolume);
			job.Log($"Wiping {directory}…");
			Volumes.WipeContents(directory);
			state.Update(s =>
			{
				s.OldVolume = null;
				s.Spare = null;
			});
			var summary = old is null
				? $"{name} wiped"
				: $"Old database on {name} deleted after {(DateTimeOffset.UtcNow - old.Since).TotalDays:0.#} days";
			return Task.FromResult(new JobOutcome(true, summary));
		});
		return job is null ? (null, "Another job is running.") : (job, null);
	}
}
