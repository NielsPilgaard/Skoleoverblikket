#!/usr/bin/env node
// Runs when CD's watch job fails (task 44). Rolls production back to the last verified version,
// unless a migration is involved, in which case old code could break against the new schema and a
// human decides. Never pushes to main: it freezes CD with an issue and opens a revert PR that
// closes the issue when merged.
//
// Steps:
//   1. No last good version, or a migration since it (in git or applied in prod): email, stop.
//   2. Redeploy the last good tag with deploy.mjs, then a 3-minute watch.mjs (probes only).
//      That failing too: freeze issue + email, stop.
//   3. Freeze issue, revert PR, CI dispatched on the PR branch, email.
//
// Env vars:
//   SHA, IMAGE_TAG               — the failed deploy
//   LAST_GOOD_SHA, LAST_GOOD_TAG — latest success on the production-verified environment (may be empty)
//   WATCH_REASON                 — why the watch failed
//   DATABASE_URL                 — prod database, read-only use: __EFMigrationsHistory
//   GH_TOKEN, GITHUB_REPOSITORY, GITHUB_SERVER_URL, GITHUB_RUN_ID
//   DOKPLOY_*                    — passed through to deploy.mjs
//   ELMAHIO_DEPLOYMENT_API_KEY, ELMAHIO_LOG_ID — passed through to elmah-deployment.mjs
//   SMTP_USERNAME, SMTP_PASSWORD, ALERT_EMAIL — alert email; without them only an annotation
//   SMTP_HOST (default smtp.tem.scaleway.com), SMTP_FROM (default kontakt@skoleoverblikket.dk)

import { execFileSync, spawnSync } from "node:child_process";
import { appendFileSync } from "node:fs";

const env = process.env;
const { SHA, IMAGE_TAG, LAST_GOOD_SHA, LAST_GOOD_TAG, GITHUB_REPOSITORY, GITHUB_STEP_SUMMARY } = env;
if (!SHA) throw new Error("SHA is not set");
if (!IMAGE_TAG) throw new Error("IMAGE_TAG is not set");

const MIGRATIONS_DIR = "api/Skoleoverblikket.Api/Data/Migrations/";
const short = sha => sha.slice(0, 7);
const runUrl = `${env.GITHUB_SERVER_URL}/${GITHUB_REPOSITORY}/actions/runs/${env.GITHUB_RUN_ID}`;
const reason = env.WATCH_REASON || "ukendt (se kørslen)";

const run = (cmd, args, options = {}) => execFileSync(cmd, args, { encoding: "utf8", ...options }).trim();
const gh = (...args) => run("gh", args);
const git = (...args) => run("git", args);

function summary(line) {
    console.log(line);
    if (GITHUB_STEP_SUMMARY) appendFileSync(GITHUB_STEP_SUMMARY, `${line}\n\n`);
}

function sendEmail(subject, body) {
    const { SMTP_USERNAME, SMTP_PASSWORD, ALERT_EMAIL } = env;
    if (!SMTP_USERNAME || !SMTP_PASSWORD || !ALERT_EMAIL) {
        console.log(`::warning title=No alert email::SMTP_USERNAME, SMTP_PASSWORD or ALERT_EMAIL is not set. ${subject}`);
        return;
    }
    const from = env.SMTP_FROM || "kontakt@skoleoverblikket.dk";
    const message = [
        `From: Skoleoverblikket CD <${from}>`,
        `To: ${ALERT_EMAIL}`,
        `Subject: =?UTF-8?B?${Buffer.from(subject).toString("base64")}?=`,
        "MIME-Version: 1.0",
        "Content-Type: text/plain; charset=utf-8",
        "Content-Transfer-Encoding: 8bit",
        "",
        body,
        "",
        `Kørsel: ${runUrl}`,
    ].join("\r\n");
    const result = spawnSync("curl", [
        "--silent", "--show-error", "--ssl-reqd",
        "--url", `smtp://${env.SMTP_HOST || "smtp.tem.scaleway.com"}:587`,
        "--user", `${SMTP_USERNAME}:${SMTP_PASSWORD}`,
        "--mail-from", from,
        "--mail-rcpt", ALERT_EMAIL,
        "--upload-file", "-",
    ], { input: message, encoding: "utf8" });
    if (result.status !== 0) console.log(`::warning title=Alert email failed::${result.stderr.trim()}`);
}

/** What makes a rollback unsafe, or null. */
function migrationInPlay() {
    if (spawnSync("git", ["diff", "--quiet", `${LAST_GOOD_SHA}..${SHA}`, "--", MIGRATIONS_DIR]).status !== 0) {
        return `migrationer ændret mellem ${short(LAST_GOOD_SHA)} og ${short(SHA)}`;
    }

    // CI migrates prod on every push to main, so prod can be ahead of SHA (a later push whose
    // deploy is still queued).
    let applied;
    try {
        applied = run("psql", [env.DATABASE_URL ?? "", "-At", "-c", 'SELECT "MigrationId" FROM "__EFMigrationsHistory"'])
            .split("\n").filter(Boolean);
    } catch {
        return "kunne ikke læse anvendte migrationer fra produktionsdatabasen";
    }
    const known = new Set(git("ls-tree", "--name-only", LAST_GOOD_SHA, "--", MIGRATIONS_DIR)
        .split("\n")
        .map(path => path.slice(MIGRATIONS_DIR.length))
        .filter(name => /^\d{14}_.+\.cs$/.test(name) && !name.endsWith(".Designer.cs"))
        .map(name => name.slice(0, -".cs".length)));
    const missing = applied.filter(id => !known.has(id));
    return missing.length ? `produktion har migrationer, som ${short(LAST_GOOD_SHA)} ikke kender: ${missing.join(", ")}` : null;
}

function node(script, extraEnv) {
    return spawnSync("node", [`infrastructure/scripts/${script}`], { stdio: "inherit", env: { ...env, ...extraEnv } }).status === 0;
}

function createFreezeIssue(title) {
    gh("label", "create", "deploy-freeze", "--force", "--color", "B60205",
        "--description", "CD deployer ikke, mens et issue med denne label er åbent");
    const body = [
        `Deploy af \`${IMAGE_TAG}\` fejlede efter deploy: **${reason}**`,
        "",
        `- Kørsel: ${runUrl}`,
        `- Sidste gode version: \`${LAST_GOOD_TAG}\``,
        "",
        "CD springer deploys over, så længe dette issue er åbent.",
        "Revert-PR'en lukker issuet, når den merges. Ved en fremadrettet rettelse: luk issuet selv.",
        "Manuelt deploy under frysning: kør CD med `force: true`.",
    ].join("\n");
    const url = gh("issue", "create", "--title", title, "--body", body, "--label", "deploy-freeze");
    return { url, number: url.split("/").pop() };
}

function createRevertPr(issue) {
    const branch = `rollback/${short(SHA)}`;
    git("config", "user.name", "github-actions[bot]");
    git("config", "user.email", "41898282+github-actions[bot]@users.noreply.github.com");
    git("checkout", "-B", branch, SHA);
    // One commit back to last good's tree. git revert can't take a range with PR merge commits.
    git("read-tree", "-u", "--reset", LAST_GOOD_SHA);
    git("commit", "--no-verify", "-m", `Revert to ${short(LAST_GOOD_SHA)}: deploy of ${short(SHA)} failed`,
        "-m", `Restores the tree of ${LAST_GOOD_SHA}, the last version that passed the CD watch.\nWatch failure: ${reason}`);
    git("push", "--force", "origin", branch);

    gh("label", "create", "rollback-revert", "--force", "--color", "B60205",
        "--description", "Automatisk revert efter et fejlet deploy");
    const url = gh("pr", "create", "--base", "main", "--head", branch, "--label", "rollback-revert",
        "--title", `Rul tilbage til ${short(LAST_GOOD_SHA)}: deploy af ${short(SHA)} fejlede`,
        "--body", [
            `Automatisk revert. Deploy af \`${IMAGE_TAG}\` fejlede: **${reason}**`,
            "",
            `Træet sættes tilbage til \`${LAST_GOOD_SHA}\`, den seneste version, der bestod CD's overvågning. Produktion kører allerede den version.`,
            "",
            `Kørsel: ${runUrl}`,
            "",
            `Closes #${issue.number}`,
        ].join("\n"));

    // PRs opened with GITHUB_TOKEN trigger no pull_request workflows; workflow_dispatch is exempt.
    // The dispatched checks land on the branch head and count as the PR's required checks.
    gh("workflow", "run", "ci.yml", "--ref", branch);
    gh("workflow", "run", "migration-gate.yml", "--ref", branch, "-f", `pr=${url.split("/").pop()}`);
    return url;
}

if (!LAST_GOOD_SHA || !LAST_GOOD_TAG) {
    summary(`## ⚠️ No rollback: no verified version on record`);
    sendEmail(`Deploy ${short(SHA)} fejlede — ingen kendt god version, ingen rollback`,
        `Deploy af ${IMAGE_TAG} fejlede: ${reason}\n\nDer er ingen tidligere version, som har bestået overvågningen, så intet er rullet tilbage. Produktion kører stadig ${IMAGE_TAG}.`);
    process.exit(1);
}

const unsafe = migrationInPlay();
if (unsafe) {
    summary(`## ⚠️ No rollback: ${unsafe}`);
    sendEmail(`Deploy ${short(SHA)} fejlede — migration i spil, ingen automatisk rollback`,
        `Deploy af ${IMAGE_TAG} fejlede: ${reason}\n\nIngen automatisk rollback: ${unsafe}. Gammel kode mod et nyt skema kan være værre end fejlen, så det er din beslutning. Produktion kører stadig ${IMAGE_TAG}.`);
    process.exit(1);
}

summary(`## ↩️ Rolling back to \`${LAST_GOOD_TAG}\``);
const redeployed = node("deploy.mjs", { IMAGE_TAG: LAST_GOOD_TAG });
if (redeployed) {
    node("elmah-deployment.mjs", { VERSION: LAST_GOOD_SHA, DESCRIPTION: `Rollback from ${short(SHA)}` });
}
const recovered = redeployed && node("watch.mjs", {
    EXPECTED_VERSION: LAST_GOOD_SHA,
    WATCH_SECONDS: "180",
    SMOKE_AT_SECONDS: "",
    ELMAHIO_READ_API_KEY: "",
});

if (!recovered) {
    const issue = createFreezeIssue(`🚨 Deploy frosset: rollback af ${short(SHA)} til ${short(LAST_GOOD_SHA)} fejlede`);
    summary(`## ❌ Rollback failed. CD frozen: ${issue.url}`);
    sendEmail(`Rollback fejlede: ${short(SHA)} → ${short(LAST_GOOD_SHA)}`,
        `Deploy af ${IMAGE_TAG} fejlede: ${reason}\n\nRollback til ${LAST_GOOD_TAG} fejlede også, så produktion kan være nede. CD er frosset: ${issue.url}`);
    process.exit(1);
}

const issue = createFreezeIssue(`🚨 Deploy frosset: ${short(SHA)} rullet tilbage til ${short(LAST_GOOD_SHA)}`);
let prLine;
try {
    prLine = `Revert-PR: ${createRevertPr(issue)}`;
} catch (err) {
    // E.g. the range changed a workflow file, which GITHUB_TOKEN may not push.
    console.log(`::warning title=No revert PR::${err.message}`);
    prLine = "Revert-PR kunne ikke oprettes (se kørslen). Ret fremad, og luk issuet bagefter.";
}
summary(`## ✅ Rolled back to \`${LAST_GOOD_TAG}\`. CD frozen: ${issue.url}\n\n${prLine}`);
sendEmail(`Deploy ${short(SHA)} rullet tilbage til ${short(LAST_GOOD_SHA)}`,
    `Deploy af ${IMAGE_TAG} fejlede: ${reason}\n\nProduktion kører igen ${LAST_GOOD_TAG}. CD er frosset: ${issue.url}\n${prLine}`);
