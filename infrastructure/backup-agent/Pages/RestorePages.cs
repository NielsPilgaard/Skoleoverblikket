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
		("full-backup", "Fuld backup efter go-live (agenten tager den selv; den nye timeline skal have sin egen)"),
		("stripe", "Stripe: gensend webhook-hændelser siden gendannelsestidspunktet (Developers → Webhooks, eller stripe events resend)"),
		("resurrected", "Genopståede skoler er slettet igen (se Slettede skoler og loggen fra SchoolRetentionJob)"),
		("smoke", "Røgtest med smoke-skolen: login, skema, ugeplan, en fil"),
		("elmah", "elmah.io: ingen nye fejl efter go-live"),
		("gdpr", "GDPR: vurdér inden 72 timer, om tabt data er et brud, der skal anmeldes, og om skolerne skal have besked (RESTORE.md §6)"),
	];

	public static void Map(WebApplication app)
	{
		app.MapGet("/gendan", Wizard);
		app.MapPost("/gendan", StartRestore);
		app.MapPost("/gendan/slet-gammel", DeleteOldVolume);
		app.MapPost("/gendan/tjekliste", TickChecklist);
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
		var body = new StringBuilder("<h1>Gendan</h1>");

		body.Append($"""
			<p class="muted">En gendannelse skriver aldrig over live-databasen. Den går til spare-volumen <b>{E(opts.SpareVolumeName)}</b>,
			bliver tjekket med en midlertidig Postgres her i agenten, og går først i drift, når du skifter <code>PG_VOLUME</code> i Dokploy.
			Den gamle volume gemmes til efterforskning.</p>
			""");

		if (spareProblem is not null)
		{
			body.Append($"""<div class="alert bad"><b>Spare-volumen kan ikke bruges:</b> {E(spareProblem)}</div>""");
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
				<h2>Gammel volume</h2>
				<div class="alert {tone}"><b>{E(oldVolume.Name)}</b> har holdt den gamle live-database i {days:0.#} dage (siden {E(Fmt.DateTime(oldVolume.Since))}).
				Slet den, når efterforskningen er slut: senest efter {opts.RetentionDays} dage (databehandleraftalen).</div>
				""");
			body.Append(Form(context, "/gendan/slet-gammel", $"""
				<label>Skriv <code>{E(opts.SpareVolumeName)}</code> for at slette den gamle database permanent
				<input name="confirmation" autocomplete="off" required></label>
				<div><button class="danger" type="submit"{(spareProblem is null && jobs.Current is null ? "" : " disabled")}>Slet gammel volume</button></div>
				""", "stack"));
		}

		if (spare is not null)
		{
			body.Append(SpareSummary(spare, opts));
		}

		body.Append(RestoreForm(context, opts, snapshot, migrations, spare, oldVolume, spareProblem, restore.SpareHasPidFile, jobs.Current is not null));
		return Page(context, "Gendan", body.ToString(), status);
	}

	private static string SpareSummary(SpareRestore spare, AgentOptions opts)
	{
		var tables = string.Concat(spare.Tables.Select(t =>
			$"<tr><td>{E(t.Table)}</td><td>{t.Restored}</td><td>{(t.Live is { } live ? live.ToString(Fmt.Danish) : "—")}</td></tr>"));
		var resurrected = spare.Resurrected.Count == 0
			? "<p class=\"muted\">Ingen skoler er slettet efter gendannelsestidspunktet.</p>"
			: $"""
				<p>Disse skoler er slettet efter tidspunktet og kommer tilbage med gendannelsen. Sammenlignet med <code>ledger.json</code> i ops-bucket'en, ikke med den gendannede tabel.</p>
				<div class="table-wrap"><table><thead><tr><th>Skole</th><th>Slettet</th><th>Slettes igen</th></tr></thead><tbody>
				{string.Concat(spare.Resurrected.Select(r => $"<tr><td>{E(r.Name)}</td><td>{E(Fmt.DateTime(r.DeletedAt))}</td><td>{(r.DeletedAutomatically
					? "<span class=\"pill good\">automatisk</span> SchoolRetentionJob sletter den ca. 3 min efter API-start"
					: "<span class=\"pill bad\">manuelt</span> Jobbet ville sende en ny advarselsmail og vente 7 dage. Gendan igen med &laquo;Forbered gen-sletning&raquo;, eller slet den i hånden før API-start.")}</td></tr>"))}
				</tbody></table></div>
				{(spare.ResurrectedPrepared ? "<p class=\"small muted\">Advarselsdatoen er sat 8 dage tilbage på den gendannede kopi, så sletningen sker ved første kørsel.</p>" : "")}
				""";

		return $"""
			<h2>På {E(opts.SpareVolumeName)} nu</h2>
			<div class="hero {(spare.Ok ? "good" : "bad")}">
			  <p class="mid">Gendannet til {E(spare.TargetDescription)}</p>
			  <p class="muted">Klar {E(Fmt.DateTime(spare.RestoredAt))} efter {E(Fmt.Duration(spare.DurationSeconds))}. Nåede {E(Fmt.DateTimeSeconds(spare.ReachedAt))}
			  (seneste agent-heartbeat). Migration {E(spare.MigrationId ?? "—")}. Keycloak-brugere: {spare.KeycloakUsers?.ToString(Fmt.Danish) ?? "—"}.</p>
			</div>
			{Checks(spare.Checks)}
			<details><summary>Rækker pr. tabel, gendannet mod live</summary>
			<div class="table-wrap"><table><thead><tr><th>Tabel</th><th>Gendannet</th><th>Live (ca.)</th></tr></thead><tbody>{tables}</tbody></table></div>
			</details>
			<h2>Genopståede skoler</h2>
			{resurrected}
			<h2>Sæt i drift</h2>
			<div class="alert warn"><ol class="steps">
			  <li>Skaler <code>api</code> og <code>keycloak</code> til 0 i Dokploy, så intet skriver til den gamle database.</li>
			  <li>Sæt <code>PG_VOLUME={E(opts.SpareVolumeName)}</code> og <code>PG_SPARE_VOLUME={E(opts.LiveVolumeName)}</code> i compose-appens miljø i Dokploy.</li>
			  <li>Redeploy. Postgres starter på den gendannede volume, agenten opdager skiftet, opretter et nyt slot og tager en fuld backup med det samme.</li>
			  <li>Gå igennem tjeklisten, der dukker op her.</li>
			</ol>
			<p class="small">Fortryd: sæt de to variabler tilbage og redeploy. {E(opts.LiveVolumeName)} røres ikke af gendannelsen.</p></div>
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
		if (snapshot.Ranges.Count == 0)
		{
			return "<h2>Ny gendannelse</h2><p class=\"muted\">Der er intet at gendanne endnu: ingen backup med WAL i repo'et.</p>";
		}

		var oldest = snapshot.OldestRestorable!.Value;
		var newest = snapshot.NewestRestorable!.Value;
		var ranges = string.Join("<br>", snapshot.Ranges.OrderByDescending(r => r.To).Select(r => $"{E(Fmt.DateTime(r.From))} – {E(Fmt.DateTime(r.To))} (timeline {r.Timeline})"));
		var migrationOptions = string.Concat(migrations.AsEnumerable().Reverse().Select(m =>
		{
			var reachable = m.AppliedAt is { } at && snapshot.RangeFor(at) is not null;
			return $"<option value=\"{Attr(m.MigrationId)}\"{(reachable ? "" : " disabled")}>{E(m.MigrationId)} – {(m.AppliedAt is { } applied ? E(Fmt.DateTimeSeconds(applied)) : "tidspunkt ukendt")}</option>";
		}));
		var backupOptions = string.Concat(snapshot.Backups.OrderByDescending(b => b.StoppedAt).Select(b =>
			$"<option value=\"{Attr(b.Label)}\">{E(b.Label)} – {E(Fmt.DateTime(b.StoppedAt))}</option>"));
		var warnings = new StringBuilder();
		if (oldVolume is not null)
		{
			warnings.Append($"<div class=\"alert warn\">{E(opts.SpareVolumeName)} holder den gamle live-database. En ny gendannelse sletter den.</div>");
		}
		else if (spare is not null)
		{
			warnings.Append($"<div class=\"alert warn\">{E(opts.SpareVolumeName)} holder gendannelsen ovenfor. En ny gendannelse sletter den.</div>");
		}

		if (pidFile)
		{
			warnings.Append($"<div class=\"alert warn\">Der ligger en <code>postmaster.pid</code> på {E(opts.SpareVolumeName)}: en Postgres blev ikke lukket pænt ned der. Tjek, at ingen container bruger volumen.</div>");
		}

		var form = Form(context, "/gendan", $"""
			<fieldset class="stack">
			<legend>Gendan til</legend>
			<label class="choice"><input type="radio" name="mode" value="Time" checked> Et tidspunkt</label>
			<label class="choice"><input type="radio" name="mode" value="Migration"> Lige før en migration</label>
			<label class="choice"><input type="radio" name="mode" value="Backup"> En fuld backup, som den var</label>
			</fieldset>
			<div id="mode-Time"><label>Tidspunkt (dansk tid)
			<input type="datetime-local" name="time" min="{Fmt.InputValue(oldest)}" max="{Fmt.InputValue(newest)}" value="{Fmt.InputValue(newest)}"></label>
			<p class="small muted">Kan gendannes:<br>{ranges}</p></div>
			<div id="mode-Migration"><label>Migration <select name="migrationId">{migrationOptions}</select></label>
			<p class="small muted">Tidspunktet er migrationens commit-tid. Gendannelsen stopper lige før den. Gråt = uden for det, der kan gendannes.</p></div>
			<div id="mode-Backup"><label>Backup <select name="backupLabel">{backupOptions}</select></label></div>
			<label class="choice"><input type="checkbox" name="prepareResurrected" value="true" checked>
			Forbered gen-sletning af skoler, der er slettet efter tidspunktet (sætter deres advarselsdato 8 dage tilbage på kopien)</label>
			<label>Skriv <code>{E(opts.SpareVolumeName)}</code> for at slette spare-volumen og gendanne
			<input name="confirmation" autocomplete="off" required></label>
			<div><button class="danger" type="submit"{(spareProblem is null && !busy ? "" : " disabled")}>Gendan til {E(opts.SpareVolumeName)}</button></div>
			""", "stack");

		return $"""
			<h2>Ny gendannelse</h2>
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
				: $"<li>{Form(context, "/gendan/tjekliste", $"<input type=\"hidden\" name=\"item\" value=\"{Attr(key)}\"><button type=\"submit\">Afkryds</button> {E(label)}")}</li>");
		}

		return $"""
			<h2>Efter go-live</h2>
			<p>{E(goLive.Volume)} blev sat i drift {E(Fmt.Ago(goLive.At, now))}{(goLive.TargetDescription is null ? "" : $", gendannet til {E(goLive.TargetDescription)}")}. Afkrydsninger gemmes i historikken.</p>
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
			return Results.Redirect(Redirect("/gendan", error: error));
		}

		await BackupPages.Audit(ops, $"Konsol: Gendan til spare-volumen ({job.Title})", cancellationToken);
		return Results.Redirect($"/job/{job.Id}");
	}

	private static async Task<IResult> DeleteOldVolume([FromForm] string? confirmation, RestoreService restore, OpsBucket ops, CancellationToken cancellationToken)
	{
		var (job, error) = await restore.StartDeleteOldVolumeAsync(confirmation, cancellationToken);
		if (job is null)
		{
			return Results.Redirect(Redirect("/gendan", error: error));
		}

		await BackupPages.Audit(ops, "Konsol: Slet gammel volume", cancellationToken);
		return Results.Redirect($"/job/{job.Id}");
	}

	private static async Task<IResult> TickChecklist([FromForm] string? item, StateStore state, OpsBucket ops, CancellationToken cancellationToken)
	{
		var match = ChecklistItems.FirstOrDefault(i => i.Key == item);
		if (match.Key is null)
		{
			return Results.Redirect(Redirect("/gendan", error: "Ukendt punkt."));
		}

		state.Update(s => s.Checklist[match.Key] = DateTimeOffset.UtcNow);
		await ops.AppendHistoryAsync(new HistoryEntry(DateTimeOffset.UtcNow, "tjekliste", true, $"Afkrydset: {match.Label}"), cancellationToken);
		return Results.Redirect("/gendan");
	}
}
