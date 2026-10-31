#!/bin/sh
# nginx runs this at container start (copied to /docker-entrypoint.d/ by web/Dockerfile). It
# writes /env.js, which index.html loads before the app, so one image works in every
# environment. KEYCLOAK_URL unset: env.js stays empty and the app uses the URL baked in at
# build time (VITE_KEYCLOAK_URL), which is how prod runs today.
set -eu

[ -n "${KEYCLOAK_URL:-}" ] || exit 0

case "$KEYCLOAK_URL" in
  http://* | https://*) ;;
  *) echo "KEYCLOAK_URL must start with http:// or https://" >&2; exit 1 ;;
esac
case "$KEYCLOAK_URL" in
  */realms | */realms/*) echo "KEYCLOAK_URL must be the Keycloak base URL, without /realms/<realm>" >&2; exit 1 ;;
esac
# A line break inside the JS string literal would make env.js unparseable.
lf='
'
cr=$(printf '\r')
case "$KEYCLOAK_URL" in
  *[\"\\]* | *"$lf"* | *"$cr"*) echo "KEYCLOAK_URL must not contain quotes, backslashes or line breaks" >&2; exit 1 ;;
esac

printf 'window.__ENV = { keycloakUrl: "%s" }\n' "$KEYCLOAK_URL" > /usr/share/nginx/html/env.js
