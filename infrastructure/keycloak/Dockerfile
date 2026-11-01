# The realm file in realms/ is for development: it has the Debugskolen login, a fixed admin client
# secret and localhost redirects, and the repo is public. Production imports the realm from this image
# whenever its Keycloak database is empty (a new server, or a restore without the Keycloak database),
# so the image gets a copy without them. Keycloak then generates the client secret on import.
# Dev (Aspire) and the e2e stack mount realms/ over the import folder instead.
FROM alpine:3.22 AS realm
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

FROM quay.io/keycloak/keycloak:26.2

COPY --from=realm /prod/ /opt/keycloak/data/import/
COPY themes/ /opt/keycloak/themes/
