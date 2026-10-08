# Grafana dashboards and alerts

Production telemetry for `Skoleoverblikket.Api` goes to Grafana Cloud (`minimoeder.grafana.net`, shared with other products) over OTLP. Everything lives in the **Skoleoverblikket** folder.

| File | What |
|---|---|
| `dashboards/health.json` | Service health: up/down, requests, 5xx rate, latency, endpoints table, memory |
| `dashboards/features.json` | Feature usage: requests, errors and latency per product module (skema, ugeplan, vikar …) |
| `dashboards/errors.json` | Errors & logs: exceptions, distinct error messages, warnings, startups, log volume |
| `alert-rules.json` | Alert rules: API down, errors logged, restart loop, slow API |
| `build_dashboards.py` | Generates the three dashboard files |

## Changing a dashboard

The dashboard JSON is generated. Edit `build_dashboards.py`, then run it (standard library only):

```sh
python infrastructure/grafana/build_dashboards.py
```

Import the changed file in Grafana: **Dashboards → New → Import**, paste the JSON, pick the Skoleoverblikket folder and overwrite. The `uid` stays the same, so links keep working.

Small tweaks made in the Grafana UI are lost on the next import. Copy them back into the script.

## Changing an alert rule

Edit the rule in Grafana, then refresh `alert-rules.json` from **Alerting → Alert rules → Export** (folder Skoleoverblikket, JSON). Keep `"folder": "Skoleoverblikket"` in the group so the file also works with Grafana file provisioning.

Notifications need a contact point. Create an email contact point named `Skoleoverblikket` and add a notification policy matching `service=skoleoverblikket` that routes to it. Until then the rules evaluate and show state in Grafana, but nothing is sent.

## Why request numbers come from Loki

Request counts, status codes, latency and the module breakdown are queried from the HttpLogging lines in Loki (`event_name="ResponseLog"` and `event_name="Duration"`), not from the `http_server_request_duration_seconds` metrics. At current traffic most metric series are created with their final value, and `increase()`/`rate()` miss it: seven real 500s showed up as about three. Grafana Cloud Adaptive Metrics also merges some `http_route` values into `<aggregated>`.

So keep `AddHttpLogging` in `Program.cs` logging method, path, status code and duration, or these panels go blank. Health probes (`/alive`, `/health`) are excluded from HttpLogging in `MapDefaultEndpoints`. Query strings are not logged, because they carry invitation and export tokens.

The "API alive" panel and the API down alert use the Prometheus metric for `/alive`. The probe hits it every few seconds, so the undercounting doesn't matter there.
