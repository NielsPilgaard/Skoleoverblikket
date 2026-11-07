# The realm file in realms/ is for development: it has the Debugskolen login, a fixed admin client
# secret and localhost redirects, and the repo is public. Production imports the realm from this image
# whenever its Keycloak database is empty (a new server, or a restore without the Keycloak database),
# so the image gets a copy without them. Keycloak then generates the client secret on import.
# Dev (Aspire) and the e2e stack mount realms/ over the import folder instead.
FROM alpine:3.24 AS realm
RUN apk add --no-cache jq
COPY realms/ /realms/
RUN mkdir /prod && for f in /realms/*.json; do \
      jq '(.users // []) |= map(select(.serviceAccountClientId)) \
          | (.clients // []) |= map( \
              del(.secret) \
              | (.redirectUris // empty) |= map(select(startswith("http://localhost") | not)) \
              | (.webOrigins // empty) |= map(select(startswith("http://localhost") | not)))' \
        "$f" > "/prod/$(basename "$f")"; \
    done \
    # Fail the build if a secret or password is still there.
    && jq -s -e '[.[] | .. | objects | select(has("secret") or has("credentials"))] | length == 0' /prod/*.json > /dev/null

# Pinned to a digest: the 26.8 tag moved on 2026-11-05/06 to a build whose admin API rejects the
# skoleoverblikket-admin service account's token (401), which broke signup and invitations.
# This is the last digest that passed e2e. Move it forward only when e2e passes on the new one.
FROM quay.io/keycloak/keycloak:26.8@sha256:3f3c5f8e03c614774b4bd9173b88391fefa4153bfc010d8012d59ad825377332

COPY --from=realm /prod/ /opt/keycloak/data/import/
COPY themes/ /opt/keycloak/themes/
