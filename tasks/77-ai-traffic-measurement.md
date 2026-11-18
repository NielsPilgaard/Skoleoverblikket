---
title: 'Measure AI crawler and assistant traffic'
purpose: 'Checklist for a Grafana view showing whether AI crawlers and assistants read and send visitors to the public site.'
description: >-
  Gets the web container's nginx access logs into Loki with User-Agent, Accept
  and Referer, and adds a Grafana panel group for AI bot hits, Markdown and
  llms.txt fetches, and visits referred by chatgpt.com, perplexity.ai and
  claude.ai. Without it, 75 and 76 can't be judged.
status: 'Ready'
---

# Measure AI crawler and assistant traffic

## TL;DR

- Request counts in Grafana come from the API's HttpLogging in Loki today. Marketing pages are static files served by nginx, so their traffic may not appear anywhere.
- Ideally ship before [75](75-agent-readable-site.md), to get a baseline. Doesn't block it.
- Size S. Owner: Niels + agent. Niels checks how container logs reach Loki on the VPS and owns the dashboard. An agent writes the nginx log format and the queries.
- No new vendor, no analytics script, no cookies.

## Steps

- [ ] Niels: check whether the `web` container's stdout already reaches Loki (Dokploy or Alloy), and under which labels. If not, add it the way the API's logs are collected.
- [ ] [nginx.conf](../web/nginx.conf): add a JSON `log_format` with time, method, path, status, bytes, `$http_user_agent`, `$http_accept` and `$http_referer`. Skip the query string, because invitation links carry tokens in the path and the query. Don't log `/env.js` or `/assets/`.
- [ ] Keep the client IP out of the new format unless an existing log already stores it under the same retention, so the logs stay out of new GDPR scope.
- [ ] Grafana: add panels to the existing dashboard (see the Grafana memory or [docs/DEPLOYMENT.md](../docs/DEPLOYMENT.md)):
  - hits per day by AI bot (GPTBot, OAI-SearchBot, ChatGPT-User, ClaudeBot, Claude-SearchBot, Claude-User, PerplexityBot, Perplexity-User, Google-Extended, Applebot-Extended, Bingbot)
  - requests for `.md`, `llms.txt`, `llms-full.txt` and `Accept: text/markdown`, per day
  - visits referred by `chatgpt.com`, `perplexity.ai`, `claude.ai`, `copilot.microsoft.com` and `gemini.google.com`, plus `utm_source=chatgpt.com` if the query string is kept for that one parameter
  - `404` count on `/.well-known/*`, to see which agent protocols are being probed
- [ ] After 75 and 76 ship, write the before/after numbers into this task and decide whether more content work pays off.
