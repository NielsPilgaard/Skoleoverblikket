using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using static Skoleoverblikket.BackupAgent.Pages.Html;

namespace Skoleoverblikket.BackupAgent.Pages;

/// <summary>Backups, Drills, the safe actions (backup, verify, drill) and the live job log.</summary>
public static class BackupPages
{
	private static readonly TimeSpan ManualBackupInterval = TimeSpan.FromHours(1);

	public static void Map(WebApplication app)
	{
		app.MapGet("/backups", Backups);
		app.MapGet("/drills", Drills);
		app.MapPost("/handling/backup", StartBackup);
		app.MapPost("/handling/verify", StartVerify);
		app.MapPost("/handling/drill", StartDrill);
		app.MapPost("/drills/manuel", LogManualDrill);
		app.MapGet("/job/{id:guid}", JobPage);
		app.MapGet("/job/{id:guid}/log", JobLog);
	}

	/// <summary>School hours, when a backup's I/O competes with teachers and parents.</summary>
	private static bool InSchoolHours(DateTimeOffset now) => Fmt.Local(now).Hour is >= 7 and < 16;

	private static async Task<IResult> Backups(HttpContext context, StatusBuilder statusBuilder, RepoInfoCache repo, StateStore state, JobRunner jobs, CancellationToken cancellationToken)
	{
		var status = await statusBuilder.BuildAsync(cancellationToken);
		var snapshot = repo.Current;
		var now = DateTimeOffset.UtcNow;
		var (lastManual, gaps) = state.Read(s => (s.LastManualBackupAt, s.Gaps.ToList()));
		var busy = jobs.Current is not null;
		var tooSoon = lastManual is { } last && now - last < ManualBackupInterval;
		var schoolHours = InSchoolHours(now);

		var ioConfirm = schoolHours
			? "<label class=\"choice\"><input type=\"checkbox\" name=\"confirmIo\" value=\"true\"> Det er skoletid (07–16). Jeg ved, at en backup giver ekstra I/O på serveren.</label>"
			: "";
		var backupForm = Form(context, "/handling/backup", $"""
			<div class="actions">
			  <label class="choice"><input type="radio" name="type" value="full" checked> Fuld</label>
			  <label class="choice"><input type="radio" name="type" value="incr"> Inkrementel</label>
			</div>
			{ioConfirm}
			<div class="actions"><button class="primary" type="submit"{(busy || tooSoon ? " disabled" : "")}>Tag backup nu</button>
			<span class="muted small">Højst én gang i timen.{(lastManual is null ? "" : $" Seneste manuelle: {E(Fmt.Ago(lastManual, now))}.")}</span></div>
			""", "stack");
		var verifyForm = Form(context, "/handling/verify", $"""
			<div class="actions"><button type="submit"{(busy ? " disabled" : "")}>Kør verify nu</button>
			<span class="muted small">Læser og dekrypterer alle backups og WAL i repo'et.</span></div>
			""");

		var ranges = string.Concat(snapshot.Ranges.OrderByDescending(r => r.To).Select(r =>
			$"<tr><td>{r.Timeline}</td><td>{E(Fmt.DateTime(r.From))}</td><td>{E(Fmt.DateTime(r.To))}</td><td class=\"mono\">{E(r.FromBackup)}</td></tr>"));
		var backupRows = string.Concat(snapshot.Backups.OrderByDescending(b => b.StartedAt).Select(b => $"""
			<tr><td class="mono">{E(b.Label)}</td><td>{(b.Type == "full" ? "Fuld" : b.Type == "incr" ? "Inkrementel" : E(b.Type))}</td>
			<td>{E(Fmt.DateTime(b.StartedAt))}</td><td>{E(Fmt.Duration(b.DurationSeconds))}</td>
			<td>{E(Fmt.Bytes(b.DatabaseBytes))}</td><td>{E(Fmt.Bytes(b.RepoBytes))}</td><td class="mono small">{E(b.WalStart)}<br>{E(b.WalStop)}</td></tr>
			"""));
		var gapRows = string.Concat(gaps.OrderByDescending(g => g.DetectedAt).Take(20).Select(g => $"""
			<tr><td>{E(Fmt.DateTime(g.DetectedAt))}</td><td>{E(g.Reason)}</td><td class="mono">{E(g.LastPushedSegment ?? "—")}</td>
			<td>{(g.ClosedAt is null ? "<span class=\"pill bad\">åben</span>" : $"lukket {E(Fmt.DateTime(g.ClosedAt))} af <span class=\"mono\">{E(g.ClosedByBackup)}</span>")}</td></tr>
			"""));
		var segments = snapshot.Segments;
		var walRange = segments.Count == 0
			? "Ingen WAL i repo'et endnu."
			: $"{segments.Count} segmenter fra <span class=\"mono\">{E(segments.MinBy(s => (s.Timeline, s.Index))!.Name)}</span> til <span class=\"mono\">{E(snapshot.NewestSegment!.Name)}</span> (skubbet {E(Fmt.Ago(snapshot.NewestSegment.PushedAt, now))}).";

		var body = $"""
			<h1>Backups</h1>
			<div class="grid">
			{Card("Tag backup nu", backupForm)}
			{Card("Verify", verifyForm)}
			</div>
			<h2>Kan gendannes</h2>
			<p class="muted">Hver backup kan gendannes fra sit sluttidspunkt og frem, så langt WAL-kæden er ubrudt. {walRange}</p>
			<div class="table-wrap"><table><thead><tr><th>Timeline</th><th>Fra</th><th>Til</th><th>Fra backup</th></tr></thead>
			<tbody>{(ranges.Length == 0 ? "<tr><td colspan=\"4\" class=\"muted\">Intet kan gendannes endnu</td></tr>" : ranges)}</tbody></table></div>
			<h2>Backups i repo'et</h2>
			<div class="table-wrap"><table><thead><tr><th>Label</th><th>Type</th><th>Start</th><th>Varighed</th><th>Database</th><th>I repo</th><th>WAL start/stop</th></tr></thead>
			<tbody>{(backupRows.Length == 0 ? "<tr><td colspan=\"7\" class=\"muted\">Ingen backups</td></tr>" : backupRows)}</tbody></table></div>
			<p class="muted small">Læst fra <code>pgbackrest info</code> {E(Fmt.Ago(snapshot.ReadAt == DateTimeOffset.MinValue ? null : snapshot.ReadAt, now))}.</p>
			<h2>Huller i WAL-kæden</h2>
			<p class="muted">Opstår, når Postgres opgiver agentens slot (agenten var mere end 4 GB bagud). Point-in-time-gendannelse kan ikke krydse et hul; den næste fulde backup lukker det.</p>
			<div class="table-wrap"><table><thead><tr><th>Opdaget</th><th>Årsag</th><th>Sidste WAL før</th><th>Status</th></tr></thead>
			<tbody>{(gapRows.Length == 0 ? "<tr><td colspan=\"4\" class=\"muted\">Ingen huller</td></tr>" : gapRows)}</tbody></table></div>
			""";
		return Page(context, "Backups", body, status);
	}

	private static async Task<IResult> StartBackup(
		[FromForm] string? type,
		[FromForm] bool? confirmIo,
		JobRunner jobs,
		BackupService backups,
		StateStore state,
		OpsBucket ops,
		Scheduler scheduler,
		CancellationToken cancellationToken)
	{
		var now = DateTimeOffset.UtcNow;
		var backupType = type == "incr" ? "incr" : "full";
		if (state.Read(s => s.LastManualBackupAt) is { } last && now - last < ManualBackupInterval)
		{
			return Results.Redirect(Redirect("/backups", error: $"Der blev taget en manuel backup {Fmt.Ago(last, now)}. Højst én i timen."));
		}

		if (InSchoolHours(now) && confirmIo != true)
		{
			return Results.Redirect(Redirect("/backups", error: "Det er skoletid. Sæt flueben for at bekræfte, at en backup må give ekstra I/O nu."));
		}

		await Audit(ops, $"Konsol: Tag backup nu ({(backupType == "full" ? "fuld" : "inkrementel")})", cancellationToken);
		var job = jobs.TryStart(JobKind.Backup, backupType == "full" ? "Manuel fuld backup" : "Manuel inkrementel backup", async (job, ct) =>
		{
			var outcome = await backups.BackupAsync(job, backupType, "Startet fra konsollen", ct);
			scheduler.RefreshSoon();
			return outcome;
		});
		if (job is null)
		{
			return Results.Redirect(Redirect("/backups", error: "Et andet job kører. Vent til det er færdigt."));
		}

		state.Update(s => s.LastManualBackupAt = now);
		return Results.Redirect($"/job/{job.Id}");
	}

	private static async Task<IResult> StartVerify(JobRunner jobs, BackupService backups, OpsBucket ops, Scheduler scheduler, CancellationToken cancellationToken)
	{
		await Audit(ops, "Konsol: Kør verify nu", cancellationToken);
		var job = jobs.TryStart(JobKind.Verify, "Manuel verify", async (job, ct) =>
		{
			var outcome = await backups.VerifyAsync(job, ct);
			scheduler.RefreshSoon();
			return outcome;
		});
		return job is null
			? Results.Redirect(Redirect("/backups", error: "Et andet job kører. Vent til det er færdigt."))
			: Results.Redirect($"/job/{job.Id}");
	}

	private static async Task<IResult> StartDrill(JobRunner jobs, BackupService backups, DrillChecks checks, OpsBucket ops, Scheduler scheduler, CancellationToken cancellationToken)
	{
		await Audit(ops, "Konsol: Kør drill nu", cancellationToken);
		var job = jobs.TryStart(JobKind.Drill, "Manuel drill", async (job, ct) =>
		{
			var outcome = await backups.DrillAsync(job, checks, ct);
			scheduler.RefreshSoon();
			return outcome;
		});
		return job is null
			? Results.Redirect(Redirect("/drills", error: "Et andet job kører. Vent til det er færdigt."))
			: Results.Redirect($"/job/{job.Id}");
	}

	public static Task Audit(OpsBucket ops, string action, CancellationToken cancellationToken) =>
		ops.AppendHistoryAsync(new HistoryEntry(DateTimeOffset.UtcNow, "handling", null, action), cancellationToken);

	private static async Task<IResult> Drills(HttpContext context, StatusBuilder statusBuilder, StateStore state, OpsBucket ops, JobRunner jobs, CancellationToken cancellationToken)
	{
		var status = await statusBuilder.BuildAsync(cancellationToken);
		var now = DateTimeOffset.UtcNow;
		var (lastDrill, manual) = state.Read(s => (s.LastDrill, s.LastManualDrill));
		var history = ops.ReadHistory(months: 6);
		var drills = history.Where(h => h.Kind == "drill" && h.Details is not null)
			.Select(h => (Entry: h, Result: h.Details!.Value.Deserialize<DrillResult>(AgentJson.Options)))
			.Where(x => x.Result is not null)
			.ToList();
		var manualDrills = history.Where(h => h.Kind == "manuel-drill").ToList();
		var busy = jobs.Current is not null;

		var drillRows = string.Concat(drills.Take(30).Select(d => $"""
			<tr><td>{E(Fmt.DateTime(d.Entry.At))}</td><td>{Ok(d.Result!.Ok)}</td><td>{E(Fmt.Duration(d.Result.TotalSeconds))}</td>
			<td>{E(Fmt.Duration(d.Result.RestoreSeconds))} + {E(Fmt.Duration(d.Result.RecoverySeconds))}</td><td>{E(d.Result.TargetDescription)}</td>
			<td>{d.Result.Checks.Count(c => c.Ok == true)}/{d.Result.Checks.Count}{string.Concat(d.Result.Checks.Where(c => c.Ok == false).Select(c => $"<br><span class=\"small\">{E(c.Name)}: {E(c.Detail)}</span>"))}</td></tr>
			"""));
		var trend = Sparkline([.. drills.Select(d => d.Result!.TotalSeconds / 60).Reverse()], "Gendannelsestid pr. drill i minutter");
		var nextManual = manual is null ? (DateOnly?)null : manual.Date.AddMonths(3);
		var overdue = nextManual is { } due && due < Fmt.LocalDate(now);
		var manualRows = string.Concat(manualDrills.Take(12).Select(m => $"<tr><td>{E(Fmt.Date(m.At))}</td><td>{E(m.Summary)}</td></tr>"));

		var drillForm = Form(context, "/handling/drill", $"""
			<div class="actions"><button class="primary" type="submit"{(busy ? " disabled" : "")}>Kør drill nu</button>
			<span class="muted small">Gendanner til tmpfs, tjekker mod live og smider kopien væk.</span></div>
			""");
		var manualForm = Form(context, "/drills/manuel", $"""
			<label>Dato <input type="date" name="date" required value="{Fmt.LocalDate(now):yyyy-MM-dd}"></label>
			<label>Målt RTO (minutter, fra "VPS væk" til appen kører) <input type="number" name="rtoMinutes" min="1" max="10000" required></label>
			<label>Hvad gik i stykker? (ingen persondata) <textarea name="notes" rows="3" maxlength="2000"></textarea></label>
			<div><button type="submit">Log manuel drill</button></div>
			""", "stack");

		var body = $"""
			<h1>Drills</h1>
			<div class="grid">
			{Card("Ugentlig drill", drillForm + (lastDrill is null ? "<p class=\"muted\">Ingen drill endnu.</p>" : $"<p>Seneste: {Ok(lastDrill.Ok)} {E(Fmt.DateTime(lastDrill.At))} · RTO {E(Fmt.Duration(lastDrill.TotalSeconds))}</p>"))}
			{Card("Gendannelsestid (målt RTO)", trend.Length == 0 ? "<p class=\"muted\">Kræver mindst to drills.</p>" : trend + "<p class=\"muted small\">Ældste til venstre, minutter.</p>")}
			{Card("Kvartalsvis manuel drill (53 fase 3)", $"<p>Seneste: {(manual is null ? "aldrig" : $"{E(manual.Date.ToString("d. MMM yyyy", Fmt.Danish))}, RTO {manual.RtoMinutes} min")}</p><p class=\"{(overdue || manual is null ? "pill bad" : "muted")}\">Næste senest: {(nextManual is { } n ? E(n.ToString("d. MMM yyyy", Fmt.Danish)) : "nu")}</p>")}
			</div>
			{(lastDrill is null ? "" : $"<h2>Seneste drill: tjek</h2>{Checks(lastDrill.Checks)}")}
			<h2>Drill-historik</h2>
			<div class="table-wrap"><table><thead><tr><th>Tid</th><th></th><th>RTO</th><th>Gendan + WAL</th><th>Mål</th><th>Tjek</th></tr></thead>
			<tbody>{(drillRows.Length == 0 ? "<tr><td colspan=\"6\" class=\"muted\">Ingen drills endnu</td></tr>" : drillRows)}</tbody></table></div>
			<h2>Log kvartalets manuelle drill</h2>
			<p class="muted">Genopbyg prod fra bunden med runbook, password manager og off-site kopi (RESTORE.md). Log resultatet her.</p>
			{manualForm}
			<div class="table-wrap"><table><thead><tr><th>Dato</th><th>Resultat</th></tr></thead>
			<tbody>{(manualRows.Length == 0 ? "<tr><td colspan=\"2\" class=\"muted\">Ingen manuelle drills logget</td></tr>" : manualRows)}</tbody></table></div>
			""";
		return Page(context, "Drills", body, status);
	}

	private static async Task<IResult> LogManualDrill([FromForm] string? date, [FromForm] int? rtoMinutes, [FromForm] string? notes, StateStore state, OpsBucket ops, CancellationToken cancellationToken)
	{
		if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) || rtoMinutes is not > 0)
		{
			return Results.Redirect(Redirect("/drills", error: "Udfyld dato og RTO."));
		}

		var text = (notes ?? "").Trim();
		if (text.Length > 2000)
		{
			text = text[..2000];
		}

		var drill = new ManualDrill(day, rtoMinutes.Value, text, DateTimeOffset.UtcNow);
		state.Update(s => s.LastManualDrill = drill);
		var at = new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
		await ops.AppendHistoryAsync(new HistoryEntry(at, "manuel-drill", true,
			$"RTO {rtoMinutes} min.{(text.Length == 0 ? "" : $" {text}")}", rtoMinutes * 60.0, drill.ToJson()), cancellationToken);
		return Results.Redirect(Redirect("/drills", ok: "Manuel drill logget."));
	}

	private static async Task<IResult> JobPage(HttpContext context, Guid id, JobRunner jobs, StatusBuilder statusBuilder, CancellationToken cancellationToken)
	{
		var status = await statusBuilder.BuildAsync(cancellationToken);
		if (jobs.Find(id) is not { } job)
		{
			return Page(context, "Job", "<h1>Job</h1><p class=\"muted\">Jobbet findes ikke længere (agenten er genstartet). Se Historik.</p>", status);
		}

		var body = $"""
			<h1>{E(job.Title)}</h1>
			<p>Startet {E(Fmt.DateTimeSeconds(job.StartedAt))} · <span id="job-state" class="pill warn">Kører…</span></p>
			<div id="job-summary"></div>
			<pre class="log" id="log" data-job="{job.Id}"></pre>
			<p><a href="/">Til overblik</a> · <a href="/historik">Historik</a></p>
			""";
		return Page(context, job.Title, body, status);
	}

	private static IResult JobLog(Guid id, int? fra, JobRunner jobs)
	{
		if (jobs.Find(id) is not { } job)
		{
			return Results.NotFound();
		}

		var (lines, next) = job.LinesFrom(fra ?? 0);
		return Results.Json(new { lines, next, done = job.EndedAt is not null, ok = job.Ok, summary = job.Summary });
	}
}
