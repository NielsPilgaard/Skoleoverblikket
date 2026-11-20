# The realm file in realms/ is for development: it has the Debugskolen login, a fixed admin client
# secret and localhost redirects, and the repo is public. Production imports the realm from this image
# whenever its Keycloak database is empty (a new server, or a restore without the Keycloak database),
# so the image gets a copy without them. Keycloak then generates the client secret on import.
# The copy also turns on "Superadmin requires two-factor", which the dev realm leaves off so dev and
# the e2e tests can log in as the Debugskolen superadmin with only a password (task 64).
# Dev (Aspire) and the e2e stack mount realms/ over the import folder instead.
FROM alpine:3.24 AS realm
RUN apk add --no-cache jq
COPY realms/ /realms/
RUN mkdir /prod && for f in /realms/*.json; do \
      jq '(.users // []) |= map(select(.serviceAccountClientId)) \
          | (.clients // []) |= map( \
              del(.secret) \
              | (.redirectUris // empty) |= map(select(startswith("http://localhost") | not)) \
              | (.webOrigins // empty) |= map(select(startswith("http://localhost") | not))) \
          | (.authenticationFlows // []) |= map(.authenticationExecutions |= map( \
              if .flowAlias == "Superadmin requires two-factor" then .requirement = "CONDITIONAL" else . end))' \
        "$f" > "/prod/$(basename "$f")"; \
    done \
    # Fail the build if a secret or password is still there. authenticatorConfig is skipped: the
    # "Condition - credential" config names credential types in a "credentials" key.
    && jq -s -e '[.[] | del(.authenticatorConfig) | .. | objects | select(has("secret") or has("credentials"))] | length == 0' /prod/*.json > /dev/null

# Pinned by digest so a moved tag cannot change Keycloak under us: 26.8 moved to 26.8.2 on 2026-11-05,
# whose admin API audience check broke signup and invitations until the realm gave the
# skoleoverblikket-admin client the kc-admin-api scope. Dependabot proposes new digests as PRs, so e2e
# runs against a new Keycloak before it ships. Keep the Aspire AppHost on the same digest.
FROM quay.io/keycloak/keycloak:26.8.2@sha256:c68e286cacd87b942ac1939a0a1481461109d6acd3a57dc496969a319a76bbf0

# The realm's "email" event listener tells users when their password or two-factor changes (task 64).
# Only those events: by default it also mails on every failed login.
ENV KC_SPI_EVENTS_LISTENER__EMAIL__INCLUDE_EVENTS=UPDATE_CREDENTIAL,REMOVE_CREDENTIAL

COPY --from=realm /prod/ /opt/keycloak/data/import/
COPY themes/ /opt/keycloak/themes/
