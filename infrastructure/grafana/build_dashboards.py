"""Generates the Skoleoverblikket Grafana dashboards in ./dashboards.

The JSON files are generated; edit this script, not the JSON. Run:

    python infrastructure/grafana/build_dashboards.py

then import the changed files into Grafana (see README.md). Standard library only.
"""
import json
import os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "dashboards")
PROM = {"type": "prometheus", "uid": "grafanacloud-prom"}
LOKI = {"type": "loki", "uid": "grafanacloud-logs"}
JOB = 'job="Skoleoverblikket.Api"'
SVC = '{service_name="Skoleoverblikket.Api"}'

_id = 0


def nid():
    global _id
    _id += 1
    return _id


def tgt(expr, legend="", ref="A", instant=False):
    t = {"datasource": PROM, "refId": ref, "expr": expr, "legendFormat": legend}
    if instant:
        t["instant"] = True
        t["range"] = False
    else:
        t["range"] = True
    return t


def loki(expr, ref="A", legend="", qtype="range"):
    return {"datasource": LOKI, "refId": ref, "expr": expr, "legendFormat": legend, "queryType": qtype}


def row(title, y):
    return {"type": "row", "id": nid(), "title": title, "collapsed": False,
            "gridPos": {"h": 1, "w": 24, "x": 0, "y": y}, "panels": []}


def stat(title, targets, x, y, w=4, h=4, unit="short", thresholds=None, desc="",
         decimals=None, mappings=None, color_mode="value", no_value="0"):
    p = {
        "type": "stat", "id": nid(), "title": title, "description": desc,
        "gridPos": {"h": h, "w": w, "x": x, "y": y},
        "datasource": targets[0]["datasource"], "targets": targets,
        "fieldConfig": {"defaults": {
            "unit": unit, "noValue": no_value,
            "thresholds": {"mode": "absolute", "steps": thresholds or [{"color": "green", "value": None}]},
            "mappings": mappings or [],
            "color": {"mode": "thresholds"},
        }, "overrides": []},
        "options": {"reduceOptions": {"calcs": ["lastNotNull"], "fields": "", "values": False},
                    "colorMode": color_mode, "graphMode": "none", "textMode": "auto",
                    "justifyMode": "center", "orientation": "auto"},
    }
    if decimals is not None:
        p["fieldConfig"]["defaults"]["decimals"] = decimals
    return p


def ts(title, targets, x, y, w=12, h=8, unit="short", desc="", stack=False, overrides=None,
       draw="line", fill=10, min0=True):
    custom = {"drawStyle": draw, "lineWidth": 1, "fillOpacity": fill, "showPoints": "never",
              "spanNulls": False, "stacking": {"mode": "normal" if stack else "none", "group": "A"}}
    if draw == "bars":
        custom["fillOpacity"] = 80
    if draw == "points":
        custom["showPoints"] = "always"
        custom["pointSize"] = 6
    d = {"unit": unit, "custom": custom, "color": {"mode": "palette-classic"}}
    if min0:
        d["min"] = 0
    return {
        "type": "timeseries", "id": nid(), "title": title, "description": desc,
        "gridPos": {"h": h, "w": w, "x": x, "y": y},
        "datasource": targets[0]["datasource"], "targets": targets,
        "fieldConfig": {"defaults": d, "overrides": overrides or []},
        "options": {"legend": {"displayMode": "table", "placement": "right", "calcs": ["mean", "max"], "showLegend": True},
                    "tooltip": {"mode": "multi", "sort": "desc"}},
    }


def status_overrides():
    colors = {"2xx": "green", "3xx": "blue", "4xx": "orange", "5xx": "red"}
    return [{"matcher": {"id": "byName", "options": k},
             "properties": [{"id": "color", "value": {"mode": "fixed", "fixedColor": v}}]} for k, v in colors.items()]


def logs_panel(title, expr, x, y, w=24, h=12, desc=""):
    return {"type": "logs", "id": nid(), "title": title, "description": desc,
            "gridPos": {"h": h, "w": w, "x": x, "y": y}, "datasource": LOKI,
            "targets": [loki(expr)],
            "options": {"showTime": True, "wrapLogMessage": True, "enableLogDetails": True,
                        "sortOrder": "Descending", "dedupStrategy": "none", "prettifyLogMessage": False}}


def table(title, targets, x, y, w=24, h=10, desc="", transformations=None, overrides=None, sort=None):
    opts = {"showHeader": True, "cellHeight": "sm", "footer": {"show": False}}
    if sort:
        opts["sortBy"] = [{"displayName": sort, "desc": True}]
    return {"type": "table", "id": nid(), "title": title, "description": desc,
            "gridPos": {"h": h, "w": w, "x": x, "y": y},
            "datasource": targets[0]["datasource"], "targets": targets,
            "transformations": transformations or [],
            "fieldConfig": {"defaults": {"custom": {"align": "auto", "filterable": True}},
                            "overrides": overrides or []},
            "options": opts}


ANNOTATIONS = {"list": [
    {"builtIn": 1, "datasource": {"type": "grafana", "uid": "-- Grafana --"}, "enable": True, "hide": True,
     "iconColor": "rgba(0, 211, 255, 1)", "name": "Annotations & Alerts", "type": "dashboard"},
    {"datasource": LOKI, "enable": True, "iconColor": "purple", "name": "Deploys / restarts",
     "expr": SVC + ' | scope_name="Microsoft.Hosting.Lifetime" |= "Application started"',
     "titleFormat": "API started", "textFormat": "instance {{service_instance_id}}", "instant": False},
]}

LINKS = [{"title": "Skoleoverblikket", "type": "dashboards", "tags": ["skoleoverblikket"],
          "asDropdown": False, "includeVars": False, "keepTime": True}]


def dashboard(uid, title, desc, panels, time_from="now-24h"):
    return {"uid": uid, "title": title, "description": desc,
            "tags": ["skoleoverblikket", "aspnetcore"], "timezone": "Europe/Copenhagen",
            "editable": True, "graphTooltip": 1, "refresh": "1m", "schemaVersion": 39,
            "time": {"from": time_from, "to": "now"}, "links": LINKS,
            "annotations": ANNOTATIONS, "templating": {"list": []}, "panels": panels}


# Request numbers come from the HttpLogging lines in Loki, not from the
# http_server_* counters: at today's traffic most counter series are born with
# their final value and increase()/rate() miss it (7 real 500s showed as ~3).
# Each request writes a ResponseLog line (StatusCode) and a Duration line (ms),
# both carrying RequestPath as structured metadata. Health probes are no longer
# logged; the filter keeps older log data clean.
NO_PROBES = 'RequestPath!~"/alive|/health"'
RESP = SVC + f' | event_name="ResponseLog" | {NO_PROBES}'
DUR = SVC + f' | event_name="Duration" | {NO_PROBES}'
GUID = "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"
ROUTE = ('label_format route=`{{ regexReplaceAll "[0-9]{4}-[0-9]{2}-[0-9]{2}" '
         f'(regexReplaceAll "[0-9]{{4}}-W[0-9]{{2}}" (regexReplaceAll "{GUID}" .RequestPath "{{id}}") "{{week}}") '
         '"{date}" }}`')
STATUS = 'label_format status=`{{ substr 0 1 .StatusCode }}xx`'
EXC = SVC + ' | scope_name="Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware"'
ERRLVL = 'detected_level=~"error|critical|fatal"'
NOT_HTTPLOG = 'scope_name!="Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware"'

# Product module per request path. First match wins, so specific rules first.
FEATURES = [
    ("SFO", ["/api/v1/sfo"], []),
    ("Vikardækning", ["/api/v1/staff-absences", "/api/v1/staff/available", "/api/v1/substitutions"], ["/substitute"]),
    ("Ugeplan", [], ["/week-plan"]),
    ("Skema", ["/api/v1/classes", "/api/v1/courses", "/api/v1/rooms", "/api/v1/time-slot"], ["/schedule"]),
    ("Personale", ["/api/v1/staff"], []),
    ("Fravær", ["/api/v1/absence", "/api/v1/attendance"], []),
    ("Forældre", ["/api/v1/parent", "/api/v1/contact-directory", "/api/v1/students"], []),
    ("Kommunikation", ["/api/v1/messages", "/api/v1/contact-threads", "/api/v1/class-chats", "/api/v1/notification"], []),
    ("Kalender", ["/api/v1/calendar"], []),
    ("Filer", ["/api/v1/files"], []),
    ("Ferieindmelding", ["/api/v1/vacation-registration"], []),
    ("Bestyrelse", ["/api/v1/board-"], []),
    ("Stå mål med", ["/api/v1/compliance-coverage"], []),
    ("Rapporter, import & eksport", ["/api/v1/reports", "/api/v1/imports", "/api/v1/exports"], []),
    ("Abonnement", ["/api/v1/billing", "/api/v1/modules", "/api/v1/stripe"], []),
    ("Backoffice", ["/api/v1/admin"], []),
    ("Demo-forespørgsler", ["/api/v1/demo-request"], []),
    ("Skole & onboarding", ["/api/v1/schools", "/api/v1/stats", "/api/v1/tenants", "/api/v1/data-processing-agreement"], []),
]
FALLBACK = "Andet / bots"


def feature_fmt():
    parts = []
    for i, (name, prefixes, contains) in enumerate(FEATURES):
        conds = [f'(hasPrefix "{p}" .RequestPath)' for p in prefixes] + [f'(contains "{c}" .RequestPath)' for c in contains]
        parts.append(("{{ if " if i == 0 else "{{ else if ") + "or " + " ".join(conds) + " }}" + name)
    return "label_format feature=`" + "".join(parts) + "{{ else }}" + FALLBACK + "{{ end }}`"


FEATURE = feature_fmt()


def cnt(stream, rng="$__interval", by=None, extra=""):
    inner = f"count_over_time({stream}{extra} [{rng}])"
    return f"sum by ({', '.join(by)}) ({inner})" if by else f"sum({inner})"


def lat(q, rng="$__interval", by="service_name", extra=""):
    return f"quantile_over_time({q}, {DUR}{extra} | unwrap Duration [{rng}]) by ({by})"


# ================================================================ Service health
_id = 0
P = []
y = 0
P.append(row("At a glance (selected range, health probes excluded)", y)); y += 1
P += [
    stat("API alive", [tgt(f'sum(rate(http_server_request_duration_seconds_count{{{JOB}, http_route="/alive"}}[5m])) > bool 0', instant=True)],
         0, y, desc="Container health probe (/alive) answered in the last 5 minutes.",
         mappings=[{"type": "value", "options": {"1": {"text": "UP", "color": "green"}, "0": {"text": "DOWN", "color": "red"}}}],
         thresholds=[{"color": "red", "value": None}, {"color": "green", "value": 1}], no_value="DOWN", color_mode="background"),
    stat("Requests", [loki(cnt(RESP, "$__range"), qtype="instant")], 4, y, decimals=0,
         desc="Every request except health probes, including bots probing for /wp-login, .env etc."),
    stat("5xx error rate", [loki(f'({cnt(RESP, "$__range", extra=" | StatusCode=~`5..`")} or vector(0)) / {cnt(RESP, "$__range")}', qtype="instant")],
         8, y, unit="percentunit", decimals=1,
         thresholds=[{"color": "green", "value": None}, {"color": "orange", "value": 0.01}, {"color": "red", "value": 0.05}]),
    stat("p95 latency (API)", [loki(lat(0.95, "$__range", extra=' | RequestPath=~"/api/v1/.*"'), qtype="instant")],
         12, y, unit="ms", decimals=0,
         thresholds=[{"color": "green", "value": None}, {"color": "orange", "value": 500}, {"color": "red", "value": 1500}]),
    stat("Unhandled exceptions", [loki(cnt(EXC, "$__range"), qtype="instant")],
         16, y, decimals=0, thresholds=[{"color": "green", "value": None}, {"color": "red", "value": 1}]),
    stat("Deploys / restarts", [loki(cnt(SVC + ' | scope_name="Microsoft.Hosting.Lifetime" |= "Application started"', "$__range"), qtype="instant")],
         20, y, decimals=0, color_mode="none", desc="Count of 'Application started' log lines."),
]
y += 4

P.append(row("Traffic", y)); y += 1
P += [
    ts("Requests by status class", [loki(f"sum by (status) (count_over_time({RESP} | {STATUS} [$__interval]))", legend="{{status}}")],
       0, y, stack=True, overrides=status_overrides(), draw="bars", desc="Requests per time bucket."),
    ts("API latency", [
        loki(lat(0.50, extra=' | RequestPath=~"/api/v1/.*"'), "A", "p50"),
        loki(lat(0.95, extra=' | RequestPath=~"/api/v1/.*"'), "B", "p95"),
        loki(lat(0.99, extra=' | RequestPath=~"/api/v1/.*"'), "C", "p99"),
    ], 12, y, unit="ms", draw="points",
       desc="Server time per request from HttpLogging. Spikes right after a deploy are cold starts (JIT + EF model build)."),
]
y += 8
P += [
    ts("5xx by route", [loki(f'sum by (route, StatusCode) (count_over_time({RESP} | StatusCode=~"5.." | {ROUTE} [$__interval]))', legend="{{StatusCode}} {{route}}")],
       0, y, draw="bars", stack=True, desc="Server errors. Each one is a school user who hit a broken page."),
    ts("Auth failures (401 / 403)", [loki(f'sum by (route, StatusCode) (count_over_time({RESP} | StatusCode=~"401|403" | {ROUTE} [$__interval]))', legend="{{StatusCode}} {{route}}")],
       12, y, draw="bars", stack=True, desc="Spikes after a deploy usually mean a role/policy change or a Keycloak problem."),
]
y += 8

P.append(row("Endpoints", y)); y += 1
P.append(table("Endpoints (selected range)", [
    loki(f"sum by (route) (count_over_time({RESP} | {ROUTE} [$__range]))", "A", qtype="instant"),
    loki(f'sum by (route) (count_over_time({RESP} | StatusCode=~"4.." | {ROUTE} [$__range]))', "B", qtype="instant"),
    loki(f'sum by (route) (count_over_time({RESP} | StatusCode=~"5.." | {ROUTE} [$__range]))', "C", qtype="instant"),
    loki(f"quantile_over_time(0.5, {DUR} | {ROUTE} | unwrap Duration [$__range]) by (route)", "D", qtype="instant"),
    loki(f"quantile_over_time(0.95, {DUR} | {ROUTE} | unwrap Duration [$__range]) by (route)", "E", qtype="instant"),
], 0, y, h=12, sort="Requests",
    desc="GUIDs, ISO weeks and dates in the path are replaced with {id}, {week}, {date}.",
    transformations=[
        {"id": "merge", "options": {}},
        {"id": "organize", "options": {"excludeByName": {"Time": True},
                                       "renameByName": {"route": "Path", "Value #A": "Requests", "Value #B": "4xx",
                                                        "Value #C": "5xx", "Value #D": "p50", "Value #E": "p95"}}},
    ],
    overrides=[
        {"matcher": {"id": "byName", "options": "Path"}, "properties": [{"id": "custom.width", "value": 520}]},
        {"matcher": {"id": "byName", "options": "5xx"}, "properties": [
            {"id": "custom.cellOptions", "value": {"type": "color-text"}},
            {"id": "thresholds", "value": {"mode": "absolute", "steps": [{"color": "green", "value": None}, {"color": "red", "value": 0.5}]}}]},
        {"matcher": {"id": "byRegexp", "options": "p50|p95"}, "properties": [
            {"id": "unit", "value": "ms"}, {"id": "decimals", "value": 0}, {"id": "custom.cellOptions", "value": {"type": "color-text"}},
            {"id": "thresholds", "value": {"mode": "absolute", "steps": [{"color": "green", "value": None}, {"color": "orange", "value": 500}, {"color": "red", "value": 1500}]}}]},
    ]))
y += 12

P.append(row("Runtime", y)); y += 1
P += [
    ts("Memory", [
        tgt(f"sum(dotnet_process_memory_working_set_bytes{{{JOB}}})", "working set", "A"),
        tgt(f"sum(dotnet_gc_last_collection_memory_committed_size_bytes{{{JOB}}})", "GC committed", "B"),
    ], 0, y, w=12, unit="bytes", fill=0),
    ts("In-flight work", [
        tgt(f"sum(http_server_active_requests{{{JOB}}})", "active requests", "A"),
        tgt(f"sum(kestrel_active_connections{{{JOB}}})", "kestrel connections", "B"),
        tgt(f"sum(kestrel_queued_connections{{{JOB}}})", "queued connections", "C"),
        tgt(f"sum(microsoft_entityframeworkcore_active_dbcontexts{{{JOB}}})", "active DbContexts", "D"),
    ], 12, y, w=12, fill=0, desc="Queued connections > 0, or DbContexts climbing without traffic = thread pool or DB trouble."),
]
y += 8

P.append(row("Recent errors and warnings", y)); y += 1
P.append(logs_panel("Errors and warnings", SVC + f' | detected_level=~"error|critical|fatal|warn" | {NOT_HTTPLOG}', 0, y))

health = dashboard("skoleoverblikket-health", "Skoleoverblikket – Service health",
                   "Is the API up, fast and error-free? Request numbers from HttpLogging in Loki; health probes excluded.", P)

# ================================================================ Feature usage
_id = 0
F = []
y = 0
# Real API traffic: /api/v1 minus 404s (scanners probe /api/v1/.env etc.).
REAL = ' | RequestPath=~"/api/v1/.*" | StatusCode!="404"'
API_PATH = ' | RequestPath=~"/api/v1/.*"'
F.append(row("Which modules do schools use?", y)); y += 1
F += [
    stat("API requests", [loki(cnt(RESP + REAL, "$__range"), qtype="instant")], 0, y, w=6, decimals=0,
         desc="Requests to /api/v1, excluding 404s and health probes."),
    stat("Modules used", [loki(f"count(sum by (feature) (count_over_time({RESP}{REAL} | {FEATURE} [$__range])))", qtype="instant")],
         6, y, w=6, decimals=0),
    stat("Checkouts started", [loki(cnt(RESP, "$__range", extra=' | RequestPath=~"/api/v1/billing/checkout.*" | StatusCode=~"2.."'), qtype="instant")],
         12, y, w=6, decimals=0, desc="Successful Stripe Checkout session creations."),
    stat("404 Not Found (mostly bots)", [loki(cnt(RESP, "$__range", extra=' | StatusCode="404"'), qtype="instant")],
         18, y, w=6, decimals=0, color_mode="none", desc="Scanners probing for /api/.env, /wp-login.php etc. A sudden rise from a real path = broken frontend link."),
]
y += 4
F += [
    table("Requests per module (selected range)", [loki(f"sum by (feature) (count_over_time({RESP}{REAL} | {FEATURE} [$__range]))", qtype="instant")],
          0, y, w=8, h=13, sort="Requests",
          transformations=[{"id": "organize", "options": {"excludeByName": {"Time": True}, "renameByName": {"feature": "Module", "Value #A": "Requests"}}}],
          overrides=[{"matcher": {"id": "byName", "options": "Requests"}, "properties": [
              {"id": "custom.cellOptions", "value": {"type": "gauge", "mode": "basic"}}]}]),
    ts("Requests per module over time", [loki(f"sum by (feature) (count_over_time({RESP}{REAL} | {FEATURE} [$__interval]))", legend="{{feature}}")],
       8, y, w=16, h=13, draw="bars", stack=True),
]
y += 13
F += [
    ts("Errors per module (4xx + 5xx, excl. 401/404)", [loki(f'sum by (feature) (count_over_time({RESP}{REAL} | StatusCode=~"4..|5.." | StatusCode!="401" | {FEATURE} [$__interval]))', legend="{{feature}}")],
       0, y, w=12, draw="bars", stack=True, desc="Validation errors (400), forbidden (403), conflicts (409) and server errors per module."),
    ts("p95 latency per module", [loki(f"quantile_over_time(0.95, {DUR}{API_PATH} | {FEATURE} | unwrap Duration [$__interval]) by (feature)", legend="{{feature}}")],
       12, y, w=12, unit="ms", draw="points"),
]
y += 8
F.append(row("Weekly rhythm", y)); y += 1
rhythm = ts("API requests per hour", [loki(cnt(RESP + REAL, "1h"), legend="requests/h")], 0, y, w=24, h=7,
            draw="bars", desc="When during the week schools use the product. Useful for picking deploy windows.")
rhythm["interval"] = "1h"
F.append(rhythm)

features = dashboard("skoleoverblikket-features", "Skoleoverblikket – Feature usage",
                     "API requests grouped by product module (skema, ugeplan, vikar, fravær, forældre …), from HttpLogging in Loki.",
                     F, time_from="now-7d")
features["refresh"] = "5m"

# ================================================================ Errors & logs
_id = 0
E = []
y = 0
E.append(row("Errors", y)); y += 1
E += [
    ts("Log lines by level (excl. HTTP request logs)", [loki(f"sum by (detected_level) (count_over_time({SVC} | {NOT_HTTPLOG} [$__interval]))", legend="{{detected_level}}")],
       0, y, w=12, draw="bars", stack=True,
       overrides=[{"matcher": {"id": "byName", "options": k}, "properties": [{"id": "color", "value": {"mode": "fixed", "fixedColor": v}}]}
                  for k, v in {"error": "red", "warn": "orange", "info": "green", "debug": "blue"}.items()]),
    ts("Unhandled exceptions by type", [loki(f"sum by (exception_type) (count_over_time({EXC} [$__interval]))", legend="{{exception_type}}")],
       12, y, w=12, draw="bars", stack=True),
]
y += 8
E.append(table("Distinct errors (selected range)", [
    loki(f"sum by (exception_type, exception_message, scope_name) (count_over_time({SVC} | {ERRLVL} [$__range]))", qtype="instant")],
    0, y, h=9, sort="Count",
    desc="Grouped by exception message. Postgres 42703 / 42P01 = a migration did not run in production.",
    transformations=[{"id": "organize", "options": {"excludeByName": {"Time": True},
                                                    "renameByName": {"exception_type": "Type", "exception_message": "Message", "scope_name": "Logger", "Value #A": "Count"}}}]))
y += 9
E.append(logs_panel("Error logs (expand a line for stack trace and TraceId)", SVC + f" | {ERRLVL}", 0, y, h=12))
y += 12
E.append(logs_panel("Warnings", SVC + f' | detected_level="warn" | {NOT_HTTPLOG}', 0, y, h=8))
y += 8

E.append(row("Lifecycle and log volume", y)); y += 1
E += [
    logs_panel("Startups and shutdowns", SVC + ' | scope_name="Microsoft.Hosting.Lifetime"', 0, y, w=12, h=8,
               desc="One 'Application started' per deploy or container restart."),
    ts("Log volume by logger", [loki(f"sum by (scope_name) (count_over_time({SVC} [$__interval]))", legend="{{scope_name}}")], 12, y, w=12, h=8,
       draw="bars", stack=True,
       desc="HttpLoggingMiddleware writes 3 lines per request (health probes are not logged). Main driver of Loki ingest."),
]

errors = dashboard("skoleoverblikket-errors", "Skoleoverblikket – Errors & logs",
                   "Exceptions, error/warning logs, deploys and log volume for Skoleoverblikket.Api.", E)

for name, d in [("health", health), ("features", features), ("errors", errors)]:
    with open(os.path.join(OUT, f"{name}.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(d, f, ensure_ascii=False, indent=2)
        f.write("\n")
    print(f"wrote dashboards/{name}.json")
