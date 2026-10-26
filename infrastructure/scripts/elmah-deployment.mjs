#!/usr/bin/env node
// Records a deployment in elmah.io (task 44), so its UI shows deploy markers and error counts per
// version. The API also stamps every message with its version (App:Version), which is what the
// CD watch job counts on. Best effort: without a key, or when elmah.io fails, it only warns.
//
// Env vars:
//   VERSION                     — commit sha now running in production
//   DESCRIPTION                 — e.g. the commit subject (optional)
//   DEPLOYED_BY                 — e.g. github.actor (optional)
//   ELMAHIO_DEPLOYMENT_API_KEY  — key with only "Deployments - Write"
//   ELMAHIO_LOG_ID

const { VERSION, DESCRIPTION, DEPLOYED_BY, ELMAHIO_DEPLOYMENT_API_KEY, ELMAHIO_LOG_ID, GITHUB_ACTIONS } = process.env;

const warn = message => console.log(GITHUB_ACTIONS ? `::warning title=elmah.io deployment::${message}` : message);

if (!VERSION) throw new Error("VERSION is not set");
if (!ELMAHIO_DEPLOYMENT_API_KEY || !ELMAHIO_LOG_ID) {
    warn("ELMAHIO_DEPLOYMENT_API_KEY or ELMAHIO_LOG_ID is not set, no deployment recorded");
    process.exit(0);
}

try {
    const res = await fetch(`https://api.elmah.io/v3/deployments?api_key=${encodeURIComponent(ELMAHIO_DEPLOYMENT_API_KEY)}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
            version: VERSION,
            description: DESCRIPTION || undefined,
            userName: DEPLOYED_BY || undefined,
            logId: ELMAHIO_LOG_ID,
        }),
        signal: AbortSignal.timeout(30_000),
    });
    if (!res.ok) throw new Error(`HTTP ${res.status} ${await res.text()}`);
    console.log(`elmah.io deployment recorded for ${VERSION}`);
} catch (err) {
    warn(`Could not record deployment for ${VERSION}: ${err.message}`);
}
