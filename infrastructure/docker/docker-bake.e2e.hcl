# Images for the e2e workflow (.github/workflows/e2e.yml), built with `docker buildx bake`.
# Only PRs build. Staging pulls all three published images.
# No GHA layer cache: PR caches are scoped per branch so it almost never hit, and exporting
# the API SDK stage (mode=max) took ~2.5 min, longer than building without it.
# Tags come from the workflow's API_IMAGE, KEYCLOAK_IMAGE and WEB_IMAGE env vars.
# Paths are relative to the repo root, where the workflow runs bake. `dockerfile` is relative to `context`.

variable "API_IMAGE" {
  default = "skoleoverblikket-api-e2e:latest"
}

variable "KEYCLOAK_IMAGE" {
  default = "skoleoverblikket-keycloak-e2e:latest"
}

variable "WEB_IMAGE" {
  default = "skoleoverblikket-web-e2e:latest"
}

group "default" {
  targets = ["api", "keycloak", "web"]
}

target "api" {
  context    = "."
  dockerfile = "api/Dockerfile"
  tags       = [API_IMAGE]
}

target "keycloak" {
  context    = "infrastructure/keycloak"
  dockerfile = "Dockerfile"
  tags       = [KEYCLOAK_IMAGE]
}

# The Keycloak URL comes from KEYCLOAK_URL in docker-compose.staging.yml at runtime, as on Staging.
target "web" {
  context    = "."
  dockerfile = "web/Dockerfile"
  tags       = [WEB_IMAGE]
}
