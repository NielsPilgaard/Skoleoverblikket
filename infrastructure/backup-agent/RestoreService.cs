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
/// and let a human make it live by flipping PG_VOLUME in Dokploy. The live volume is mounted
/// read-only and never touched; the old one stays for forensics until "Slet gammel volume".
/// </summary>
public sealed class RestoreService(
	IOptions<AgentOptions> options,
	PgBackRest pgBackRest,
	RepoInfoCache repo,
	DrillChecks checks,
	StateStore state,
	OpsBucket ops,
	JobRunner jobs)
{
	// Mirrors SchoolDeletionService (API): deletion needs 90 days since cancellation and a warning 7 days old.
	private static readonly TimeSpan SchoolRetention = TimeSpan.FromDays(90);
	private static readonly TimeSpan WarningNotice = TimeSpan.FromDays(7);

	private readonly AgentOptions _options = options.Value;

	/// <summary>Why the spare volume must not be written to, or null when it's safe.</summary>
	public async Task<string?> SpareProblemAsync(CancellationToken cancellationToken)
	{
		if (string.Equals(_options.LiveVolumeName, _options.SpareVolumeName, StringComparison.Ordinal))
		{
			return $"PG_VOLUME og PG_SPARE_VOLUME er begge {_options.LiveVolumeName}. Sæt PG_SPARE_VOLUME til den anden volume.";
		}

		if (!Directory.Exists(_options.SpareDataDirectory))
		{
			return $"Spare-volumen er ikke monteret på {_options.SpareDataDirectory}.";
		}

		var liveId = await Volumes.IdentityAsync(_options.LiveDataDirectory, cancellationToken);
		var spareId = await Volumes.IdentityAsync(_options.SpareDataDirectory, cancellationToken);
		if (liveId is null || spareId is null)
		{
			return "Kunne ikke afgøre, om spare- og live-volumen er forskellige (stat fejlede).";
		}

		return liveId == spareId
			? "Spare-volumen er den samme som live-volumen. Har du kun ændret PG_VOLUME og ikke PG_SPARE_VOLUME?"
			: null;
	}

	public bool SpareHasPidFile => File.Exists(Path.Combine(_options.SpareDataDirectory, "postmaster.pid"));

	public bool SpareIsEmpty => !Directory.Exists(_options.SpareDataDirectory) || !Directory.EnumerateFileSystemEntries(_options.SpareDataDirectory).Any();

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
						return (null, "Vælg et tidspunkt.");
					}

					if (snapshot.RangeFor(target) is not { } range)
					{
						return (null, $"{Fmt.DateTime(target)} ligger uden for det, der kan gendannes.");
					}

					return (new RestoreTarget(
						["--type=time", $"--target={Fmt.PgTimestamp(target)}", $"--target-timeline={range.Timeline}"],
						$"tidspunkt {Fmt.DateTime(target)}", target), null);
				}

			case RestoreMode.Migration:
				{
					var migration = (await MigrationsAsync(cancellationToken)).FirstOrDefault(m => m.MigrationId == request.MigrationId);
					if (migration?.AppliedAt is not { } appliedAt)
					{
						return (null, "Vælg en migration med kendt tidspunkt.");
					}

					if (snapshot.RangeFor(appliedAt) is not { } range)
					{
						return (null, $"Migrationen blev anvendt {Fmt.DateTime(appliedAt)}, uden for det, der kan gendannes.");
					}

					// Exclusive: recovery stops just before the transaction that applied the migration.
					return (new RestoreTarget(
						["--type=time", $"--target={Fmt.PgTimestamp(appliedAt)}", "--target-exclusive", $"--target-timeline={range.Timeline}"],
						$"lige før migration {migration.MigrationId} ({Fmt.DateTimeSeconds(appliedAt)})", appliedAt), null);
				}

			default:
				{
					var backup = snapshot.Backups.FirstOrDefault(b => b.Label == request.BackupLabel);
					return backup is null
						? (null, "Vælg en backup.")
						: (new RestoreTarget(["--type=immediate", $"--set={backup.Label}"], $"backup {backup.Label} ({Fmt.DateTime(backup.StoppedAt)})", null), null);
				}
		}
	}

	/// <summary>Validates the request and starts the restore job, or returns why not.</summary>
	public async Task<(Job? Job, string? Error)> StartAsync(RestoreRequest request, CancellationToken cancellationToken)
	{
		if (!string.Equals(request.Confirmation?.Trim(), _options.SpareVolumeName, StringComparison.Ordinal))
		{
			return (null, $"Skriv navnet på spare-volumen ({_options.SpareVolumeName}) for at bekræfte, at den må slettes.");
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

		var job = jobs.TryStart(JobKind.Restore, $"Gendan {target.Description} til {_options.SpareVolumeName}",
			(job, ct) => RestoreAsync(job, target, request.PrepareResurrected, ct));
		return job is null ? (null, "Et andet job kører. Vent til det er færdigt.") : (job, null);
	}

	private async Task<JobOutcome> RestoreAsync(Job job, RestoreTarget target, bool prepareResurrected, CancellationToken cancellationToken)
	{
		var stopwatch = Stopwatch.StartNew();
		var spare = _options.SpareDataDirectory;
		if (await SpareProblemAsync(cancellationToken) is { } problem)
		{
			return new JobOutcome(false, problem);
		}

		job.Log($"Sletter indholdet af {_options.SpareVolumeName} ({spare})…");
		Volumes.WipeContents(spare);
		state.Update(s =>
		{
			s.Spare = null;
			s.OldVolume = null;
		});

		job.Log($"Gendanner {target.Description}…");
		var restore = await pgBackRest.RestoreAsync(spare, target.Arguments, job.Log, cancellationToken);
		if (!restore.Ok)
		{
			return new JobOutcome(false, $"pgbackrest restore fejlede: {restore.Tail(2)}");
		}

		job.Log("Starter midlertidig Postgres på spare-volumen og afspiller WAL…");
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
				job.Log($"Markerer {ids.Length} genopståede skoler, så SchoolRetentionJob sletter dem igen få minutter efter API-start…");
				await PrepareRedeletionAsync(app, ids, cancellationToken);
				resurrected = await ResurrectedAsync(app, reached, cancellationToken);
				prepared = true;
			}

			job.Log("Fjerner pgBackRests recovery-indstillinger og lukker Postgres pænt ned…");
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
			var summary = $"{_options.SpareVolumeName} gendannet til {target.Description} på {Fmt.Duration(stopwatch.Elapsed.TotalSeconds)}. " +
				$"{report.Checks.Count(c => c.Ok == true)} tjek OK, {report.Checks.Count(c => c.Ok == false)} fejlede" +
				$"{(skipped > 0 ? $", {skipped} sprunget over (live svarede ikke)" : "")}. {resurrected.Count} genopståede skoler.";
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
		if (!string.Equals(confirmation?.Trim(), _options.SpareVolumeName, StringComparison.Ordinal))
		{
			return (null, $"Skriv {_options.SpareVolumeName} for at bekræfte.");
		}

		if (await SpareProblemAsync(cancellationToken) is { } problem)
		{
			return (null, problem);
		}

		var job = jobs.TryStart(JobKind.DeleteOldVolume, $"Slet indholdet af {_options.SpareVolumeName}", (job, _) =>
		{
			var old = state.Read(s => s.OldVolume);
			job.Log($"Sletter {_options.SpareDataDirectory}…");
			Volumes.WipeContents(_options.SpareDataDirectory);
			state.Update(s =>
			{
				s.OldVolume = null;
				s.Spare = null;
			});
			var summary = old is null
				? $"{_options.SpareVolumeName} er tømt"
				: $"Gammel database på {_options.SpareVolumeName} slettet efter {(DateTimeOffset.UtcNow - old.Since).TotalDays:0.#} dage";
			return Task.FromResult(new JobOutcome(true, summary));
		});
		return job is null ? (null, "Et andet job kører. Vent til det er færdigt.") : (job, null);
	}
}
