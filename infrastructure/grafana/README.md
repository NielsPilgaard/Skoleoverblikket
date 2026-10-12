# Grafana dashboards and alerts

Production telemetry for `Skoleoverblikket.Api` goes to Grafana Cloud (`minimoeder.grafana.net`, shared with other products) over OTLP. Everything lives in the **Skoleoverblikket** folder.

| File | What |
|---|---|
| `dashboards/health.json` | Service health: up/down, requests, 5xx rate, latency (cold vs warm, slow requests and their causes), endpoints table, memory |
| `dashboards/features.json` | Feature usage: requests, errors and slowest request per product module (skema, ugeplan, vikar …) |
| `dashboards/errors.json` | Errors & logs: exceptions, distinct error messages, warnings, startups, log volume |
| `alert-rules.json` | Alert rules: API down, errors logged, restart loop, slow API |
| `build_dashboards.py` | Generates the three dashboard files |

## Changing a dashboard

The dashboard JSON is generated. Edit `build_dashboards.py`, then run it (standard library only):

```sh
python infrastructure/grafana/build_dashboards.py
```

Import the changed file in Grafana: **Dashboards → New → Import**, paste the JSON, pick the Skoleoverblikket folder and overwrite. The `uid` stays the same, so links keep working.

Or push it with the Grafana MCP server (`update_dashboard` with the generated JSON, folder `skoleoverblikket`, overwrite).

Small tweaks made in the Grafana UI are lost on the next import. Copy them back into the script.

## Changing an alert rule

Edit the rule in Grafana, then refresh `alert-rules.json` from **Alerting → Alert rules → Export** (folder Skoleoverblikket, JSON). Keep `"folder": "Skoleoverblikket"` in the group so the file also works with Grafana file provisioning.

Notifications need a contact point. Create an email contact point named `Skoleoverblikket` and add a notification policy matching `service=skoleoverblikket` that routes to it. Until then the rules evaluate and show state in Grafana, but nothing is sent.

## Why request numbers come from Loki

Request counts, status codes, latency and the module breakdown are queried from the HttpLogging lines in Loki (`event_name="ResponseLog"` and `event_name="Duration"`), not from the `http_server_request_duration_seconds` metrics. At current traffic most metric series are created with their final value, and `increase()`/`rate()` miss it: seven real 500s showed up as about three. Grafana Cloud Adaptive Metrics also merges some `http_route` values into `<aggregated>`.

So keep `AddHttpLogging` in `Program.cs` logging method, path, status code and duration, or these panels go blank. Health probes (`/alive`, `/health`) are excluded from HttpLogging in `MapDefaultEndpoints`. Query strings are not logged, because they carry invitation and export tokens.

The "API alive" panel and the API down alert use the Prometheus metric for `/alive`. The probe hits it every few seconds, so the undercounting doesn't matter there.

## Reading the latency panels

Traffic is low (around a hundred real API requests a month), so a plain p95 is just the slowest one or two requests, and those are nearly always cold starts: the first page load after a deploy sends 6-8 parallel calls to a fresh instance (Keycloak discovery, EF model build, JIT) that take 0.5-1 s, while the same endpoints take 1-40 ms warm. The panels are built around that:

- **Warm vs cold.** A request is *warm* when it arrived on a reused connection: HttpLogging's `RequestId` is `<ConnectionId>:<seq>` and warm means seq > `00000001`. A fresh instance has no connections, so every cold-start request is seq 1. Browsers also open new connections on a warm instance (about a third of requests), so the "new connection" series holds cold starts plus some fast requests. LogQL can't measure time since `Application started`, so this is the best cold-start signal in the logs.
- **Sample size.** Percentiles are hidden below a minimum count: the "API p95, warm" stat shows "too few" under 50 warm requests and shows the count beside it; the p95 line in the latency graph needs 20 requests per bucket; the Endpoints p95 column needs 20 per path. Max and median are shown instead.
- **Causes.** "Slow API requests" (Tempo, server spans over 300 ms) and "Slow calls inside API requests" (Tempo, outgoing HTTP and DB spans over 50 ms) link each slow request to its trace. Keycloak `.well-known/openid-configuration` = cold start, `api.stripe.com` = Stripe, `db.*` columns = SQL (Npgsql spans). A 5xx status means the slow request was an error path. Tempo search covers at most 30 days, and TraceQL metrics at most 25 hours, which is why the aggregate panels use Loki.
- Deploys are the purple annotations on every graph.

The "API slow" alert uses the same warm filter, so a deploy's cold-start burst can't trigger it.
