using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using static Skoleoverblikket.BackupAgent.Pages.Html;

namespace Skoleoverblikket.BackupAgent.Pages;

/// <summary>The restore wizard (task 60 Phase C): pick a point, restore to the spare volume, check it, go live by hand.</summary>
public static class RestorePages
{
	private static readonly (string Key, string Label)[] ChecklistItems =
	[
		("full-backup", "Full backup on the new timeline (the agent takes it)"),
		("stripe", "Stripe: resend webhooks since the restore point"),
		("resurrected", "Resurrected schools are deleted again"),
		("smoke", "Smoke test: login, schedule, week plan, a file"),
		("elmah", "elmah.io: no new errors"),
		("gdpr", "GDPR: breach assessment within 72 h (RESTORE.md §6)"),
	];

	public static void Map(WebApplication app)
	{
		app.MapGet("/restore", Wizard);
		app.MapPost("/restore", StartRestore);
		app.MapPost("/restore/delete-old", DeleteOldVolume);
		app.MapPost("/restore/checklist", TickChecklist);
	}

	private static async Task<IResult> Wizard(
		HttpContext context,
		StatusBuilder statusBuilder,
		RestoreService restore,
		RepoInfoCache repo,
		StateStore state,
		JobRunner jobs,
		IOptions<AgentOptions> options,
		CancellationToken cancellationToken)
	{
		var opts = options.Value;
		var status = await statusBuilder.BuildAsync(cancellationToken);
		var now = DateTimeOffset.UtcNow;
		var snapshot = repo.Current;
		var (spare, oldVolume, goLive, checklist) = state.Read(s => (s.Spare, s.OldVolume, s.LastGoLive, new Dictionary<string, DateTimeOffset>(s.Checklist)));
		var spareProblem = await restore.SpareProblemAsync(cancellationToken);
		var migrations = await restore.MigrationsAsync(cancellationToken);
		var body = new StringBuilder("<h1>Restore</h1>");

		body.Append($"""<p class="muted">Restores go to <b>{E(opts.SpareVolumeName)}</b>. Live is never touched.</p>""");

		if (spareProblem is not null)
		{
			body.Append($"""<div class="alert bad">{E(spareProblem)}</div>""");
		}

		if (goLive is not null && now - goLive.At < TimeSpan.FromDays(30))
		{
			body.Append(Checklist(context, goLive, checklist, now));
		}

		if (oldVolume is not null)
		{
			var days = (now - oldVolume.Since).TotalDays;
			var tone = days >= opts.RetentionDays ? "bad" : days >= 7 ? "warn" : "good";
			body.Append($"""
				<h2>Old volume</h2>
				<div class="alert {tone}"><b>{E(oldVolume.Name)}</b> holds the old live database ({days:0.#} days). Delete it within {opts.RetentionDays} days (DPA).</div>
				""");
			body.Append(Form(context, "/restore/delete-old", $"""
				<label>Type <code>{E(opts.SpareVolumeName)}</code> to delete it permanently
				<input name="confirmation" autocomplete="off" required></label>
				<div><button class="danger" type="submit"{(spareProblem is null && jobs.Current is null ? "" : " disabled")}>Delete old volume</button></div>
				""", "stack"));
		}

		if (spare is not null)
		{
			body.Append(SpareSummary(spare, opts));
		}

		body.Append(RestoreForm(context, opts, snapshot, migrations, spare, oldVolume, spareProblem, restore.SpareHasPidFile, jobs.Current is not null));
		return Page(context, "Restore", body.ToString(), status);
	}

	private static string SpareSummary(SpareRestore spare, AgentOptions opts)
	{
		var tables = string.Concat(spare.Tables.Select(t =>
			$"<tr><td>{E(t.Table)}</td><td>{Fmt.Number(t.Restored)}</td><td>{(t.Live is { } live ? Fmt.Number(live) : "—")}</td></tr>"));
		var resurrected = spare.Resurrected.Count == 0
			? "<p class=\"muted\">None.</p>"
			: $"""
				<div class="table-wrap"><table><thead><tr><th>School</th><th>Deleted</th><th>Deleted again</th></tr></thead><tbody>
				{string.Concat(spare.Resurrected.Select(r => $"<tr><td>{E(r.Name)}</td><td>{E(Fmt.DateTime(r.DeletedAt))}</td><td>{(r.DeletedAutomatically
					? "<span class=\"pill good\">auto</span> ~3 min after API start"
					: "<span class=\"pill bad\">manual</span> Delete by hand before API start, or restore again with re-deletion ticked")}</td></tr>"))}
				</tbody></table></div>
				""";

		return $"""
			<h2>On {E(opts.SpareVolumeName)}</h2>
			<div class="hero {(spare.Ok ? "good" : "bad")}">
			  <p class="mid">Restored to {E(spare.TargetDescription)}</p>
			  <p class="muted">Ready {E(Fmt.DateTime(spare.RestoredAt))} · took {E(Fmt.Duration(spare.DurationSeconds))} · reached {E(Fmt.DateTimeSeconds(spare.ReachedAt))}</p>
			</div>
			{Checks(spare.Checks)}
			<details><summary>Rows per table</summary>
			<div class="table-wrap"><table><thead><tr><th>Table</th><th>Restored</th><th>Live (est.)</th></tr></thead><tbody>{tables}</tbody></table></div>
			</details>
			<h2>Resurrected schools</h2>
			{resurrected}
			<h2>Go live</h2>
			<div class="alert warn"><ol class="steps">
			  <li>Dokploy: scale <code>api</code> and <code>keycloak</code> to 0.</li>
			  <li>Set <code>PG_VOLUME={E(opts.SpareVolumeName)}</code> and <code>PG_SPARE_VOLUME={E(opts.LiveVolumeName)}</code>.</li>
			  <li>Redeploy.</li>
			  <li>Work through the checklist that appears here.</li>
			</ol>
			<p class="small">Undo: swap the two back and redeploy. {E(opts.LiveVolumeName)} is untouched.</p></div>
			""";
	}

	private static string RestoreForm(
		HttpContext context,
		AgentOptions opts,
		RepoSnapshot snapshot,
		List<MigrationRow> migrations,
		SpareRestore? spare,
		OldVolume? oldVolume,
		string? spareProblem,
		bool pidFile,
		bool busy)
	{
		if (snapshot.ReadAt == DateTimeOffset.MinValue)
		{
			return "<h2>New restore</h2><p class=\"muted\">Reading the repo. Reload in a few seconds.</p>";
		}

		if (snapshot.Ranges.Count == 0)
		{
			return $"<h2>New restore</h2><p class=\"muted\">Nothing to restore: no backup with WAL in the repo.{(snapshot.Error is null ? "" : $" {E(snapshot.Error)}")}</p>";
		}

		var oldest = snapshot.OldestRestorable!.Value;
		var newest = snapshot.NewestRestorable!.Value;
		var ranges = string.Join("<br>", snapshot.Ranges.OrderByDescending(r => r.To).Select(r => $"{E(Fmt.DateTime(r.From))} – {E(Fmt.DateTime(r.To))} (timeline {r.Timeline})"));
		var migrationOptions = string.Concat(migrations.AsEnumerable().Reverse().Select(m =>
		{
			var reachable = m.AppliedAt is { } at && snapshot.RangeFor(at) is not null;
			return $"<option value=\"{Attr(m.MigrationId)}\"{(reachable ? "" : " disabled")}>{E(m.MigrationId)} – {(m.AppliedAt is { } applied ? E(Fmt.DateTimeSeconds(applied)) : "time unknown")}</option>";
		}));
		var backupOptions = string.Concat(snapshot.Backups.OrderByDescending(b => b.StoppedAt).Select(b =>
			$"<option value=\"{Attr(b.Label)}\">{E(b.Label)} – {E(Fmt.DateTime(b.StoppedAt))}</option>"));
		var warnings = new StringBuilder();
		if (oldVolume is not null)
		{
			warnings.Append($"<div class=\"alert warn\">Wipes the old live database on {E(opts.SpareVolumeName)}.</div>");
		}
		else if (spare is not null)
		{
			warnings.Append("<div class=\"alert warn\">Wipes the restore above.</div>");
		}

		if (pidFile)
		{
			warnings.Append($"<div class=\"alert warn\"><code>postmaster.pid</code> on {E(opts.SpareVolumeName)}: a Postgres didn't shut down cleanly. Check no container uses the volume.</div>");
		}

		var form = Form(context, "/restore", $"""
			<fieldset class="stack">
			<legend>Restore to</legend>
			<label class="choice"><input type="radio" name="mode" value="Time" checked> Point in time</label>
			<label class="choice"><input type="radio" name="mode" value="Migration"> Just before a migration</label>
			<label class="choice"><input type="radio" name="mode" value="Backup"> Full backup as-is</label>
			</fieldset>
			<div id="mode-Time"><label>Time (Copenhagen)
			<input type="datetime-local" name="time" min="{Fmt.InputValue(oldest)}" max="{Fmt.InputValue(newest)}" value="{Fmt.InputValue(newest)}"></label>
			<p class="small muted">Restorable:<br>{ranges}</p></div>
			<div id="mode-Migration"><label>Migration <select name="migrationId">{migrationOptions}</select></label>
			<p class="small muted">Stops just before the migration's commit. Greyed out: not restorable.</p></div>
			<div id="mode-Backup"><label>Backup <select name="backupLabel">{backupOptions}</select></label></div>
			<label class="choice"><input type="checkbox" name="prepareResurrected" value="true" checked>
			Re-delete schools deleted after this point</label>
			<label>Type <code>{E(opts.SpareVolumeName)}</code> to wipe it and restore
			<input name="confirmation" autocomplete="off" required></label>
			<div><button class="danger" type="submit"{(spareProblem is null && !busy ? "" : " disabled")}>Restore to {E(opts.SpareVolumeName)}</button></div>
			""", "stack");

		return $"""
			<h2>New restore</h2>
			{warnings}
			{form}
			""";
	}

	private static string Checklist(HttpContext context, GoLive goLive, Dictionary<string, DateTimeOffset> ticked, DateTimeOffset now)
	{
		var items = new StringBuilder();
		foreach (var (key, label) in ChecklistItems)
		{
			items.Append(ticked.TryGetValue(key, out var at)
				? $"<li>{Ok(true)} {E(label)} <span class=\"muted small\">({E(Fmt.DateTime(at))})</span></li>"
				: $"<li>{Form(context, "/restore/checklist", $"<input type=\"hidden\" name=\"item\" value=\"{Attr(key)}\"><button type=\"submit\">Done</button> {E(label)}")}</li>");
		}

		return $"""
			<h2>After go-live</h2>
			<p>{E(goLive.Volume)} went live {E(Fmt.Ago(goLive.At, now))}{(goLive.TargetDescription is null ? "" : $", restored to {E(goLive.TargetDescription)}")}.</p>
			<ul class="steps">{items}</ul>
			""";
	}

	private static async Task<IResult> StartRestore(
		[FromForm] RestoreMode? mode,
		[FromForm] string? time,
		[FromForm] string? migrationId,
		[FromForm] string? backupLabel,
		[FromForm] string? confirmation,
		[FromForm] bool? prepareResurrected,
		RestoreService restore,
		OpsBucket ops,
		CancellationToken cancellationToken)
	{
		var request = new RestoreRequest(mode ?? RestoreMode.Time, time, migrationId, backupLabel, confirmation, prepareResurrected == true);
		var (job, error) = await restore.StartAsync(request, cancellationToken);
		if (job is null)
		{
			return Results.Redirect(Redirect("/restore", error: error));
		}

		await BackupPages.Audit(ops, $"Console: {job.Title}", cancellationToken);
		return Results.Redirect($"/job/{job.Id}");
	}

	private static async Task<IResult> DeleteOldVolume([FromForm] string? confirmation, RestoreService restore, OpsBucket ops, CancellationToken cancellationToken)
	{
		var (job, error) = await restore.StartDeleteOldVolumeAsync(confirmation, cancellationToken);
		if (job is null)
		{
			return Results.Redirect(Redirect("/restore", error: error));
		}

		await BackupPages.Audit(ops, "Console: delete old volume", cancellationToken);
		return Results.Redirect($"/job/{job.Id}");
	}

	private static async Task<IResult> TickChecklist([FromForm] string? item, StateStore state, OpsBucket ops, CancellationToken cancellationToken)
	{
		var match = ChecklistItems.FirstOrDefault(i => i.Key == item);
		if (match.Key is null)
		{
			return Results.Redirect(Redirect("/restore", error: "Unknown item."));
		}

		state.Update(s => s.Checklist[match.Key] = DateTimeOffset.UtcNow);
		await ops.AppendHistoryAsync(new HistoryEntry(DateTimeOffset.UtcNow, "checklist", true, $"Done: {match.Label}"), cancellationToken);
		return Results.Redirect("/restore");
	}
}
