#!/usr/bin/env node
// Watches production after a deploy (task 44). Every 30 s it probes the site, /api/health and
// Keycloak's discovery document. At set times it runs the read-only Playwright smoke test, and at
// the end it compares elmah.io errors for the new version with the version before it.
// Exits 1 on the first sign the deploy is bad; CD then redeploys the previous tag.
//
// Required env vars:
//   EXPECTED_VERSION       — commit sha the API must report in X-App-Version
//
// Optional env vars:
//   SITE_URL               — default https://skoleoverblikket.dk
//   KEYCLOAK_DISCOVERY_URL — default the prod realm's .well-known/openid-configuration
//   WATCH_SECONDS          — default 900
//   SMOKE_AT_SECONDS       — comma-separated offsets for the smoke test, default "120,600"; empty: none
//   SMOKE_USER, SMOKE_PASSWORD — smoke test login; without them the smoke test is skipped
//   ELMAHIO_READ_API_KEY, ELMAHIO_LOG_ID — error spike check; skipped without them
//   PREVIOUS_VERSION       — sha of the version before this one, the error baseline
//   DEPLOYED_AT            — ISO time the deploy finished; default the start of this script
//   ELMAH_MIN_ERRORS       — default 10
//   ELMAH_SPIKE_FACTOR     — default 5
//
// On failure, the reason is written to $GITHUB_OUTPUT as `reason`.

import { spawn } from "node:child_process";
import { appendFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const env = process.env;
const { EXPECTED_VERSION, GITHUB_ACTIONS, GITHUB_OUTPUT, GITHUB_STEP_SUMMARY } = env;
if (!EXPECTED_VERSION) throw new Error("EXPECTED_VERSION is not set");

const SITE_URL = (env.SITE_URL || "https://skoleoverblikket.dk").replace(/\/$/, "");
const KEYCLOAK_DISCOVERY_URL = env.KEYCLOAK_DISCOVERY_URL
    || "https://auth.skoleoverblikket.dk/realms/Skoleoverblikket/.well-known/openid-configuration";
const WATCH_MS = Number(env.WATCH_SECONDS || 900) * 1000;
const SMOKE_AT_MS = (env.SMOKE_AT_SECONDS ?? "120,600").split(",").filter(s => s.trim()).map(s => Number(s) * 1000);
const INTERVAL_MS = 30_000;
const MAX_CONSECUTIVE_FAILURES = 3;
// Start-first deploys keep the old container serving for a moment after Dokploy says "done".
const VERSION_GRACE_MS = 2 * 60_000;
const ELMAH_BASELINE_MS = 15 * 60_000;

const sleep = ms => new Promise(r => setTimeout(r, ms));
const start = Date.now();
const deployedAt = env.DEPLOYED_AT ? new Date(env.DEPLOYED_AT) : new Date(start);
const webDir = fileURLToPath(new URL("../../web/", import.meta.url));

const get = url => fetch(url, { headers: { "cache-control": "no-cache" }, signal: AbortSignal.timeout(10_000) });

// Each probe returns null when healthy, or what is wrong.
const probes = [
    {
        name: "Site",
        async check() {
            const res = await get(`${SITE_URL}/`);
            if (!res.ok) return `HTTP ${res.status}`;
            // A blank page still answers 200, so require Vite's hashed bundle.
            return /\/assets\/[\w-]+-[\w-]{8}\.js/.test(await res.text()) ? null : "no hashed JS bundle in the HTML";
        },
    },
    {
        name: "API health",
        async check(elapsed) {
            const res = await get(`${SITE_URL}/api/health`);
            if (!res.ok) return `HTTP ${res.status}`;
            const version = res.headers.get("x-app-version");
            return elapsed >= VERSION_GRACE_MS && version !== EXPECTED_VERSION
                ? `X-App-Version is ${version ?? "missing"}, expected ${EXPECTED_VERSION}`
                : null;
        },
    },
    {
        name: "Keycloak",
        async check() {
            const res = await get(KEYCLOAK_DISCOVERY_URL);
            if (!res.ok) return `HTTP ${res.status}`;
            return (await res.json()).issuer ? null : "discovery document has no issuer";
        },
    },
].map(p => ({ ...p, failures: 0, checks: 0, lastError: null }));

let smokeChild = null;
const smokeResults = [];

function fail(reason) {
    console.log(GITHUB_ACTIONS ? `::error title=Watch failed::${reason}` : `Watch failed: ${reason}`);
    if (GITHUB_OUTPUT) appendFileSync(GITHUB_OUTPUT, `reason=${reason.replaceAll("\n", " ")}\n`);
    writeSummary(`❌ Watch of \`${EXPECTED_VERSION}\` failed`, reason);
    smokeChild?.kill();
    process.exit(1);
}

function writeSummary(title, detail) {
    if (!GITHUB_STEP_SUMMARY) return;
    const rows = probes.map(p => `| ${p.name} | ${p.checks} | ${p.lastError ?? "—"} |`);
    appendFileSync(GITHUB_STEP_SUMMARY, [
        `## ${title}`,
        "",
        detail ? `**${detail}**\n` : "",
        "| Probe | Checks | Last error |",
        "|---|---|---|",
        ...rows,
        "",
        smokeResults.length ? `Smoke test runs: ${smokeResults.join(", ")}` : "Smoke test: not run",
        "",
    ].join("\n"));
}

async function runProbes(elapsed) {
    await Promise.all(probes.map(async probe => {
        let error;
        try {
            error = await probe.check(elapsed);
        } catch (err) {
            error = err.message;
        }
        probe.checks++;
        if (error) {
            probe.failures++;
            probe.lastError = error;
            console.log(`  ${probe.name}: ${error} (${probe.failures} in a row)`);
        } else {
            probe.failures = 0;
        }
    }));

    const broken = probes.find(p => p.failures >= MAX_CONSECUTIVE_FAILURES);
    if (broken) fail(`${broken.name} failed ${broken.failures} times in a row: ${broken.lastError}`);
}

// The smoke config retries once, so a non-zero exit means it failed twice.
function runSmoke() {
    return new Promise(resolve => {
        console.log("Running smoke test...");
        smokeChild = spawn("npx", ["playwright", "test", "--config", "playwright.smoke.config.ts"], {
            cwd: webDir,
            env: { ...env, SMOKE_BASE_URL: SITE_URL },
            stdio: "inherit",
            shell: process.platform === "win32",
        });
        smokeChild.on("exit", code => {
            smokeChild = null;
            smokeResults.push(code === 0 ? "passed" : "failed");
            resolve(code === 0);
        });
    });
}

async function countErrors(version, from, to) {
    const query = new URLSearchParams({
        query: `version:"${version}" AND (severity:Error OR severity:Fatal)`,
        from: from.toISOString(),
        to: to.toISOString(),
        pageSize: "1",
        includeHeaders: "false",
        api_key: env.ELMAHIO_READ_API_KEY,
    });
    const res = await fetch(`https://api.elmah.io/v3/messages/${env.ELMAHIO_LOG_ID}?${query}`, { signal: AbortSignal.timeout(30_000) });
    if (!res.ok) throw new Error(`elmah.io messages API: HTTP ${res.status}`);
    return (await res.json()).total ?? 0;
}

async function checkErrorSpike() {
    if (!env.ELMAHIO_READ_API_KEY || !env.ELMAHIO_LOG_ID) {
        console.log("elmah.io read key not set, skipping the error spike check");
        return;
    }

    const min = Number(env.ELMAH_MIN_ERRORS || 10);
    const factor = Number(env.ELMAH_SPIKE_FACTOR || 5);
    let count, baseline = 0;
    try {
        count = await countErrors(EXPECTED_VERSION, deployedAt, new Date());
        if (env.PREVIOUS_VERSION) {
            baseline = await countErrors(env.PREVIOUS_VERSION, new Date(deployedAt - ELMAH_BASELINE_MS), deployedAt);
        }
    } catch (err) {
        // elmah.io being down says nothing about our deploy.
        console.log(GITHUB_ACTIONS ? `::warning title=Error spike check skipped::${err.message}` : `Error spike check skipped: ${err.message}`);
        return;
    }

    const limit = Math.max(min, factor * baseline);
    console.log(`elmah.io: ${count} errors for this version, ${baseline} for the last good one, limit ${limit}`);
    if (count > limit) fail(`${count} elmah.io errors since deploy (limit ${limit}, last good version had ${baseline})`);
}

const smokeEnabled = SMOKE_AT_MS.length > 0;
const smokeConfigured = Boolean(env.SMOKE_USER && env.SMOKE_PASSWORD);
if (smokeEnabled && !smokeConfigured) {
    console.log(GITHUB_ACTIONS
        ? "::warning title=Smoke test skipped::SMOKE_USER and SMOKE_PASSWORD are not set"
        : "SMOKE_USER and SMOKE_PASSWORD are not set, skipping the smoke test");
}
const pendingSmoke = smokeConfigured ? [...SMOKE_AT_MS] : [];
let smokeRun = Promise.resolve(true);

console.log(`Watching ${SITE_URL} for ${WATCH_MS / 1000} s, expecting version ${EXPECTED_VERSION}`);
while (Date.now() - start < WATCH_MS) {
    const elapsed = Date.now() - start;
    if (pendingSmoke.length && elapsed >= pendingSmoke[0]) {
        pendingSmoke.shift();
        // Chained, so two runs never overlap; probes keep running meanwhile.
        smokeRun = smokeRun.then(async () => {
            if (!(await runSmoke())) fail("Smoke test failed after a retry");
            return true;
        });
    }
    await runProbes(elapsed);
    await sleep(INTERVAL_MS);
}

await smokeRun;
await checkErrorSpike();
writeSummary(`✅ Watch of \`${EXPECTED_VERSION}\` passed`);
console.log("Watch passed");
