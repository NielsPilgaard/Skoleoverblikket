using Microsoft.Extensions.Options;
using Npgsql;

namespace Skoleoverblikket.BackupAgent;

public sealed record CheckReport(
	List<CheckResult> Checks,
	List<TableComparison> Tables,
	int TableCount,
	long Rows,
	string? MigrationId,
	long? KeycloakUsers,
	DateTimeOffset? ReachedAt);

/// <summary>
/// The 53 drill checks, used by the weekly drill and the restore wizard. The restored copy is
/// counted exactly (it's our own throwaway server); live is only read through pg_stat_user_tables
/// estimates and the two tables the agent may read. Results are names and counts, never row content.
/// </summary>
public sealed class DrillChecks(IOptions<AgentOptions> options, LivePostgres live)
{
	private readonly AgentOptions _options = options.Value;

	public async Task<CheckReport> RunAsync(TempPostgres restored, DateTimeOffset? target, Action<string> log, CancellationToken cancellationToken)
	{
		var checks = new List<CheckResult>();
		var tables = new List<TableComparison>();
		var liveApp = await TryAsync(() => live.TableEstimatesAsync(_options.AppDatabase, cancellationToken), log, "Live-databasen svarer ikke; sammenligning med live springes over");
		var liveKeycloak = await TryAsync(() => live.TableEstimatesAsync(_options.KeycloakDatabase, cancellationToken), log, null);
		var liveMigration = (await TryAsync(() => live.MigrationsAsync(cancellationToken), log,
			"Kan ikke læse live __EFMigrationsHistory (mangler GRANT SELECT til backup_agent?)"))?.LastOrDefault()?.MigrationId;

		string? migrationId = null;
		long rows = 0;
		var tableCount = 0;
		if (!await restored.DatabaseExistsAsync(_options.AppDatabase, cancellationToken))
		{
			checks.Add(new CheckResult("App-database", false, $"{_options.AppDatabase} findes ikke i gendannelsen"));
		}
		else
		{
			await using var app = restored.Connect(_options.AppDatabase);
			await app.OpenAsync(cancellationToken);
			var counts = await CountTablesAsync(app, cancellationToken);
			tableCount = counts.Count;
			rows = counts.Values.Sum();
			migrationId = await ScalarAsync<string>(app, """SELECT max("MigrationId") FROM "__EFMigrationsHistory" """, cancellationToken);
			foreach (var (table, count) in counts.OrderBy(c => c.Key, StringComparer.Ordinal))
			{
				tables.Add(new TableComparison(ShortName(table), count, liveApp?.GetValueOrDefault(table)));
			}

			var liveIsNewer = liveMigration is not null && migrationId is not null && string.CompareOrdinal(liveMigration, migrationId) > 0;
			checks.Add(TablesCheck(counts, liveApp, liveIsNewer));
			checks.Add(liveMigration is null
				? new CheckResult("Migration", null, $"Gendannet: {migrationId ?? "ingen"}. Live kunne ikke læses.")
				: new CheckResult("Migration", migrationId == liveMigration || liveIsNewer,
					migrationId == liveMigration ? $"Samme som live: {migrationId}" : $"Gendannet {migrationId ?? "ingen"}, live {liveMigration}"));
			checks.Add(RowsCheck(counts, liveApp));
			checks.Add(SchoolsCheck(counts, liveApp));
			checks.Add(await TenantIsolationCheckAsync(app, cancellationToken));
		}

		var reachedAt = await ReachedAtAsync(restored, cancellationToken);
		if (target is { } wanted)
		{
			var tolerance = _options.WalSwitchInterval * 2 + TimeSpan.FromMinutes(1);
			checks.Add(reachedAt is { } reached
				? new CheckResult("Tidspunkt nået", reached <= wanted && reached >= wanted - tolerance,
					$"Seneste agent-heartbeat i gendannelsen: {Fmt.DateTimeSeconds(reached)}, mål: {Fmt.DateTimeSeconds(wanted)}")
				: new CheckResult("Tidspunkt nået", null, "Ingen agent-heartbeat i gendannelsen"));
		}

		long? keycloakUsers = null;
		if (await restored.DatabaseExistsAsync(_options.KeycloakDatabase, cancellationToken))
		{
			await using var keycloak = restored.Connect(_options.KeycloakDatabase);
			await keycloak.OpenAsync(cancellationToken);
			keycloakUsers = await KeycloakChecksAsync(keycloak, liveKeycloak, checks, cancellationToken);
		}
		else
		{
			checks.Add(new CheckResult("Keycloak", false, $"{_options.KeycloakDatabase} findes ikke i gendannelsen"));
		}

		foreach (var check in checks)
		{
			log($"{(check.Ok switch { true => "OK  ", false => "FEJL", null => "--  " })} {check.Name}: {check.Detail}");
		}

		return new CheckReport(checks, tables, tableCount, rows, migrationId, keycloakUsers, reachedAt);
	}

	private static async Task<T?> TryAsync<T>(Func<Task<T>> read, Action<string> log, string? message)
	{
		try
		{
			return await read();
		}
		catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or TimeoutException)
		{
			if (message is not null)
			{
				log($"{message} ({ex.Message})");
			}

			return default;
		}
	}

	private static string ShortName(string table) => table.StartsWith("public.", StringComparison.Ordinal) ? table[7..] : table;

	private static async Task<Dictionary<string, long>> CountTablesAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
	{
		var names = new List<(string Schema, string Table)>();
		await using (var command = new NpgsqlCommand("""
			SELECT table_schema, table_name FROM information_schema.tables
			WHERE table_type = 'BASE TABLE' AND table_schema NOT IN ('pg_catalog', 'information_schema')
			""", connection))
		await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
		{
			while (await reader.ReadAsync(cancellationToken))
			{
				names.Add((reader.GetString(0), reader.GetString(1)));
			}
		}

		var counts = new Dictionary<string, long>();
		foreach (var (schema, table) in names)
		{
			await using var count = new NpgsqlCommand($"SELECT count(*) FROM {Quote(schema)}.{Quote(table)}", connection);
			counts[$"{schema}.{table}"] = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
		}

		return counts;
	}

	private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

	private static async Task<T?> ScalarAsync<T>(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
	{
		try
		{
			await using var command = new NpgsqlCommand(sql, connection);
			var value = await command.ExecuteScalarAsync(cancellationToken);
			return value is DBNull or null ? default : (T)value;
		}
		catch (PostgresException)
		{
			return default;
		}
	}

	private static CheckResult TablesCheck(Dictionary<string, long> restored, Dictionary<string, long>? live, bool liveIsNewer)
	{
		if (live is null)
		{
			return new CheckResult("Tabeller", null, $"{restored.Count} tabeller gendannet. Live kunne ikke læses.");
		}

		var missing = live.Keys.Where(t => !restored.ContainsKey(t)).Select(ShortName).ToList();
		if (missing.Count == 0)
		{
			return new CheckResult("Tabeller", true, $"Alle {live.Count} live-tabeller findes ({restored.Count} gendannet)");
		}

		return new CheckResult("Tabeller", liveIsNewer,
			$"{missing.Count} live-tabeller mangler{(liveIsNewer ? " (fra migrationer efter tidspunktet)" : "")}: {string.Join(", ", missing.Take(8))}");
	}

	/// <summary>Catches empty or partial restores: each table live has rows in must have at least half of them.</summary>
	private static CheckResult RowsCheck(Dictionary<string, long> restored, Dictionary<string, long>? live)
	{
		if (live is null)
		{
			return new CheckResult("Rækker pr. tabel", null, $"{restored.Values.Sum()} rækker gendannet. Live kunne ikke læses.");
		}

		// pg_stat estimates are rough on small tables, so only tables with some volume count.
		var tooFew = live
			.Where(l => l.Value >= 20 && restored.TryGetValue(l.Key, out var count) && count < l.Value / 2)
			.Select(l => $"{ShortName(l.Key)} {restored[l.Key]}/{l.Value}")
			.ToList();
		return tooFew.Count == 0
			? new CheckResult("Rækker pr. tabel", true, $"{restored.Values.Sum()} rækker; ingen tabel under halvdelen af live")
			: new CheckResult("Rækker pr. tabel", false, $"Under halvdelen af live: {string.Join(", ", tooFew.Take(8))}");
	}

	private static CheckResult SchoolsCheck(Dictionary<string, long> restored, Dictionary<string, long>? live)
	{
		var count = restored.GetValueOrDefault("public.Schools");
		if (live?.GetValueOrDefault("public.Schools") is not { } liveCount || liveCount == 0)
		{
			return new CheckResult("Skoler", null, $"{count} skoler gendannet");
		}

		return new CheckResult("Skoler", Math.Abs(count - liveCount) <= 2, $"{count} gendannet, live ca. {liveCount} (±2 tilladt)");
	}

	/// <summary>No row in a tenant-scoped table may point at a school that doesn't exist.</summary>
	private static async Task<CheckResult> TenantIsolationCheckAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
	{
		var tables = new List<(string Schema, string Table)>();
		await using (var command = new NpgsqlCommand("""
			SELECT table_schema, table_name FROM information_schema.columns
			WHERE column_name = 'TenantId' AND table_schema NOT IN ('pg_catalog', 'information_schema')
			""", connection))
		await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
		{
			while (await reader.ReadAsync(cancellationToken))
			{
				tables.Add((reader.GetString(0), reader.GetString(1)));
			}
		}

		var orphans = new List<string>();
		foreach (var (schema, table) in tables)
		{
			await using var command = new NpgsqlCommand(
				$"""SELECT count(*) FROM {Quote(schema)}.{Quote(table)} t WHERE NOT EXISTS (SELECT 1 FROM "Schools" s WHERE s."Id" = t."TenantId")""",
				connection);
			var count = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
			if (count > 0)
			{
				orphans.Add($"{table} ({count})");
			}
		}

		return orphans.Count == 0
			? new CheckResult("Tenant-isolation", true, $"Alle rækker i {tables.Count} tabeller med TenantId peger på en skole")
			: new CheckResult("Tenant-isolation", false, $"Rækker uden skole: {string.Join(", ", orphans)}");
	}

	/// <summary>The agent's last heartbeat commit in the restore: how far recovery really got.</summary>
	private static async Task<DateTimeOffset?> ReachedAtAsync(TempPostgres restored, CancellationToken cancellationToken)
	{
		await using var connection = restored.Connect("postgres");
		await connection.OpenAsync(cancellationToken);
		var at = await ScalarAsync<DateTime>(connection, "SELECT max(at) FROM backup_agent_heartbeat", cancellationToken);
		return at == default ? null : new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc));
	}

	private async Task<long> KeycloakChecksAsync(NpgsqlConnection keycloak, Dictionary<string, long>? live, List<CheckResult> checks, CancellationToken cancellationToken)
	{
		await using var realm = new NpgsqlCommand("SELECT count(*) FROM realm WHERE name = @name", keycloak);
		realm.Parameters.AddWithValue("name", _options.KeycloakRealm);
		var realmCount = await SafeCountAsync(realm, cancellationToken);
		checks.Add(new CheckResult("Keycloak-realm", realmCount > 0, realmCount > 0 ? $"Realm {_options.KeycloakRealm} findes" : $"Realm {_options.KeycloakRealm} mangler"));

		await using var users = new NpgsqlCommand("SELECT count(*) FROM user_entity", keycloak);
		var userCount = await SafeCountAsync(users, cancellationToken);
		if (live?.GetValueOrDefault("public.user_entity") is { } liveUsers and > 0)
		{
			var allowed = Math.Max(2, liveUsers * 5 / 100);
			checks.Add(new CheckResult("Keycloak-brugere", Math.Abs(userCount - liveUsers) <= allowed, $"{userCount} gendannet, live ca. {liveUsers} (±{allowed})"));
		}
		else
		{
			checks.Add(new CheckResult("Keycloak-brugere", null, $"{userCount} brugere gendannet"));
		}

		await using var credentials = new NpgsqlCommand("SELECT count(*) FROM credential", keycloak);
		var credentialCount = await SafeCountAsync(credentials, cancellationToken);
		checks.Add(new CheckResult("Keycloak-loginoplysninger", userCount == 0 || credentialCount > 0, $"{credentialCount} credentials"));
		return userCount;
	}

	private static async Task<long> SafeCountAsync(NpgsqlCommand command, CancellationToken cancellationToken)
	{
		try
		{
			return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
		}
		catch (PostgresException)
		{
			return 0;
		}
	}
}
