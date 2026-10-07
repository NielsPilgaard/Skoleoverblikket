using System.Text;
using Microsoft.Extensions.Options;
using static Skoleoverblikket.BackupAgent.Pages.Html;

namespace Skoleoverblikket.BackupAgent.Pages;

/// <summary>Overblik, Slettede skoler and Historik.</summary>
public static class OverviewPages
{
	public static void Map(WebApplication app)
	{
		app.MapGet("/", Overview);
		app.MapGet("/slettede-skoler", Ledger);
		app.MapGet("/historik", History);
		app.MapGet("/healthz", () => Results.Text("ok"));
	}

	private static async Task<IResult> Overview(HttpContext context, StatusBuilder statusBuilder, WalState wal, RepoInfoCache repo, JobRunner jobs, StateStore state, OpsBucket ops, CancellationToken cancellationToken)
	{
		var status = await statusBuilder.BuildAsync(cancellationToken);
		var now = status.GeneratedAt;
		var w = wal.Current;
		var snapshot = repo.Current;
		var secured = status.DataSecuredAt;
		var securedClass = secured is { } at && now - at <= TimeSpan.FromMinutes(15) ? "good" : "bad";
		var body = new StringBuilder();

		body.Append($"""
			<div class="hero {securedClass}">
			  <p class="big">Data sikret {E(Fmt.Ago(secured, now))}</p>
			  <p class="muted">Seneste WAL i repo: <span class="mono">{E(status.Wal.RepoNewestSegment ?? "—")}</span>,
			  skubbet {E(Fmt.Ago(status.Wal.RepoNewestSegmentAt, now))}. Agenten skubbede senest {E(Fmt.Ago(w.LastPushAt, now))}.</p>
			</div>
			""");

		if (jobs.Current is { } running)
		{
			body.Append($"""<div class="alert warn">Kører nu: {E(running.Title)} · <a href="/job/{running.Id}">se log</a></div>""");
		}

		if (status.Issues.Count > 0)
		{
			body.Append($"""<div class="alert {HealthClass(status.Health)}"><ul class="issues">{string.Concat(status.Issues.Select(i => $"<li>{E(i)}</li>"))}</ul></div>""");
		}

		var full = snapshot.NewestFull;
		var retentionAge = status.Backups.OldestRestorableAt is { } oldest ? (now - oldest).TotalDays : (double?)null;
		var retained = status.Wal.RetainedBytes ?? 0;
		var cap = Math.Max(status.Wal.SlotCapBytes, 1);
		var slotPercent = Math.Min(100, retained * 100 / cap);
		var lastDrill = state.Read(s => s.LastDrill);

		body.Append("""<div class="grid">""");
		body.Append(Card("Seneste fulde backup", full is null
			? """<p class="bad">Ingen</p>"""
			: $"""
				<p class="mid">{E(Fmt.Ago(full.StoppedAt, now))}</p>
				<p class="muted small">{E(full.Label)} · {E(Fmt.DateTime(full.StoppedAt))}<br>{E(Fmt.Bytes(full.DatabaseBytes))} ({E(Fmt.Bytes(full.RepoBytes))} i repo) · {E(Fmt.Duration(full.DurationSeconds))}</p>
				"""));
		body.Append(Card("Ældste gendannelsespunkt", status.Backups.OldestRestorableAt is null
			? """<p class="muted">Intet endnu</p>"""
			: $"""
				<p class="mid">{E(Fmt.DateTime(status.Backups.OldestRestorableAt))}</p>
				<p class="small">{(status.Backups.RetentionOk ? "<span class=\"pill good\">Inden for 14 dage</span>" : "<span class=\"pill bad\">Ældre end 14 dage (databehandleraftalen)</span>")}
				<span class="muted">{retentionAge:0.#} dage tilbage · {status.Backups.Count} backups</span></p>
				"""));
		body.Append(Card("Replikerings-slot", $"""
			<p>{SlotLabel(status.Wal.SlotStatus)} · modtager {(status.Wal.ReceiverPaused ? "<span class=\"pill bad\">på pause</span>" : status.Wal.ReceiverRunning ? "<span class=\"pill good\">kører</span>" : "<span class=\"pill warn\">kører ikke</span>")}</p>
			<p class="small muted">Postgres holder {E(Fmt.Bytes(retained))} af højst {E(Fmt.Bytes(status.Wal.SlotCapBytes))} for agenten</p>
			<meter min="0" max="100" low="50" high="75" optimum="0" value="{slotPercent}"></meter>
			<p class="small muted">Ikke skubbet endnu (wal-receive): {E(Fmt.Bytes(status.Wal.SpoolBytes))}{(w.ReceiverLastLine is null ? "" : $"<br>pg_receivewal: {E(w.ReceiverLastLine)}")}</p>
			"""));
		body.Append(Card("Disk", status.Disk is null ? """<p class="muted">Ukendt</p>""" : $"""
			<p class="mid">{status.Disk.UsedPercent}% brugt</p>
			<meter min="0" max="100" low="70" high="80" optimum="0" value="{status.Disk.UsedPercent}"></meter>
			<p class="small muted">{E(Fmt.Bytes(status.Disk.FreeBytes))} fri</p>
			"""));
		body.Append(Card("Seneste drill", lastDrill is null ? """<p class="muted">Ingen endnu</p>""" : $"""
			<p>{Ok(lastDrill.Ok)} {E(Fmt.Ago(lastDrill.At, now))}</p>
			<p class="small muted">Gendannet på {E(Fmt.Duration(lastDrill.TotalSeconds))} (RTO) · {lastDrill.Tables} tabeller, {lastDrill.Rows} rækker<br>{E(lastDrill.TargetDescription)}</p>
			"""));
		body.Append(Card("Seneste verify", status.Verify.LastAt is null ? """<p class="muted">Ingen endnu</p>""" : $"""
			<p>{Ok(status.Verify.LastOk)} {E(Fmt.Ago(status.Verify.LastAt, now))}</p>
			"""));
		body.Append(Card("Næste kørsler", $"""
			<p class="small">Fuld backup: {E(Fmt.DateTime(status.Schedule.NextFull))}<br>Verify: {E(Fmt.DateTime(status.Schedule.NextVerify))}<br>Drill: {E(Fmt.DateTime(status.Schedule.NextDrill))}</p>
			"""));
		body.Append(Card("Heartbeats og ops-bucket", $"""
			<p class="small">{string.Join("<br>", Enum.GetValues<Heartbeats.Kind>().Select(k => $"{k}: {HeartbeatLine(context, k, now)}"))}</p>
			<p class="small muted">Ops-bucket: {(ops.IsConfigured ? ops.LastError is null ? $"skrevet {E(Fmt.Ago(ops.LastWriteOkAt, now))}" : $"<span class=\"pill bad\">fejl</span> {E(ops.LastError)}" : "ikke sat op")}</p>
			"""));
		body.Append(Card("Volumes", $"""
			<p class="small">Live: <b>{E(status.Volumes.Live)}</b> (timeline {status.Postgres.Timeline?.ToString() ?? "?"}, Postgres {(status.Postgres.Reachable ? "svarer" : "<span class=\"pill bad\">svarer ikke</span>")})<br>
			Spare: <b>{E(status.Volumes.Spare)}</b> – {E(status.Volumes.SpareContents)}{(status.Volumes.OldVolumeSince is { } since ? $"<br>Gammel database i {(now - since).TotalDays:0.#} dage" : "")}</p>
			"""));
		body.Append("</div>");

		if (status.SshTunnelCommand is { } ssh)
		{
			body.Append($"""<p class="muted small">Tunnel: <code>{E(ssh)}</code></p>""");
		}

		return Page(context, "Overblik", body.ToString(), status);
	}

	private static string SlotLabel(string status) => status switch
	{
		"reserved" => """<span class="pill good">reserveret</span>""",
		"extended" => """<span class="pill warn">udvidet</span>""",
		"unreserved" => """<span class="pill bad">over grænsen</span>""",
		"lost" => """<span class="pill bad">tabt</span>""",
		"missing" => """<span class="pill bad">mangler</span>""",
		_ => $"""<span class="pill muted">{E(status)}</span>""",
	};

	private static string HeartbeatLine(HttpContext context, Heartbeats.Kind kind, DateTimeOffset now)
	{
		var heartbeats = context.RequestServices.GetRequiredService<Heartbeats>();
		if (!heartbeats.IsConfigured(kind))
		{
			return "<span class=\"muted\">ikke sat op</span>";
		}

		return heartbeats.LastSent(kind) is { } last
			? $"<span class=\"pill {HealthClass(last.Result)}\">{E(last.Result)}</span> {E(Fmt.Ago(last.At, now))}"
			: "<span class=\"muted\">ikke sendt endnu</span>";
	}

	private static async Task<IResult> Ledger(HttpContext context, StatusBuilder statusBuilder, OpsBucket ops, RepoInfoCache repo, IOptions<AgentOptions> options, CancellationToken cancellationToken)
	{
		var status = await statusBuilder.BuildAsync(cancellationToken);
		var ledger = await ops.ReadAsync<List<LedgerEntry>>(OpsBucket.LedgerKey, preferRemote: false, cancellationToken) ?? [];
		var oldest = repo.Current.OldestRestorable;
		var retention = options.Value.RetentionDays;
		var rows = string.Concat(ledger.OrderByDescending(e => e.DeletedAt).Select(e =>
		{
			var inBackups = oldest is null || oldest < e.DeletedAt;
			return $"""
				<tr><td>{E(e.SchoolName)}</td><td>{E(Fmt.DateTime(e.DeletedAt))}</td>
				<td>{(inBackups ? "<span class=\"pill warn\">ja</span>" : "<span class=\"pill good\">nej</span>")}</td>
				<td>{E(Fmt.Date(e.DeletedAt.AddDays(retention)))}</td></tr>
				""";
		}));
		var body = $"""
			<h1>Slettede skoler</h1>
			<p class="muted">Skoler slettet de sidste {retention} dage, som stadig kan være i en backup. En gendannelse til før sletningen
			bringer dem tilbage. Listen kommer fra <code>SchoolDeletionRecords</code> og spejles til <code>ledger.json</code> i ops-bucket'en,
			fordi en gendannelse spoler tabellen tilbage, men ikke bucket'en. Kun skolens navn og datoer, ingen persondata.</p>
			<div class="table-wrap"><table>
			<thead><tr><th>Skole</th><th>Slettet</th><th>Stadig i backup</th><th>Ude af alle backups senest</th></tr></thead>
			<tbody>{(rows.Length == 0 ? "<tr><td colspan=\"4\" class=\"muted\">Ingen skoler slettet de sidste 14 dage</td></tr>" : rows)}</tbody>
			</table></div>
			<p class="muted small">Tom liste, selvom en skole er slettet? Så kan agenten ikke læse tabellen. Kør i app-databasen:
			<code>GRANT SELECT ON "SchoolDeletionRecords", "__EFMigrationsHistory" TO backup_agent;</code></p>
			""";
		return Page(context, "Slettede skoler", body, status);
	}

	private static readonly Dictionary<string, string> KindLabels = new()
	{
		["backup"] = "Backup",
		["drill"] = "Drill",
		["verify"] = "Verify",
		["restore"] = "Gendannelse",
		["deleteoldvolume"] = "Slet gammel volume",
		["golive"] = "Go live",
		["wal-gap"] = "Hul i WAL",
		["slot"] = "Slot",
		["timeline"] = "Timeline",
		["agent"] = "Agent",
		["handling"] = "Handling",
		["tjekliste"] = "Tjekliste",
		["manuel-drill"] = "Manuel drill",
	};

	private static async Task<IResult> History(HttpContext context, StatusBuilder statusBuilder, OpsBucket ops, CancellationToken cancellationToken)
	{
		var status = await statusBuilder.BuildAsync(cancellationToken);
		var rows = string.Concat(ops.ReadHistory().Take(500).Select(e => $"""
			<tr><td>{E(Fmt.DateTime(e.At))}</td><td>{E(KindLabels.GetValueOrDefault(e.Kind, e.Kind))}</td><td>{Ok(e.Ok)}</td>
			<td>{E(e.Summary)}</td><td>{(e.DurationSeconds is { } d ? E(Fmt.Duration(d)) : "")}</td></tr>
			"""));
		var body = $"""
			<h1>Historik</h1>
			<p class="muted">Alle kørsler, handlinger og hændelser de sidste 3 måneder. Ligger også i <code>history/</code> i ops-bucket'en.
			Ingen brugernavne: SSH-nøglen er identiteten, og SSH-login står i værtens <code>auth.log</code>.</p>
			<div class="table-wrap"><table>
			<thead><tr><th>Tid</th><th>Type</th><th></th><th>Resultat</th><th>Varighed</th></tr></thead>
			<tbody>{(rows.Length == 0 ? "<tr><td colspan=\"5\" class=\"muted\">Ingen historik endnu</td></tr>" : rows)}</tbody>
			</table></div>
			""";
		return Page(context, "Historik", body, status);
	}
}
