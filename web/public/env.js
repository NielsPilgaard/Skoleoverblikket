// Runtime config, read by src/auth/keycloak.ts. Empty here: `npm run dev` and an image started
// without KEYCLOAK_URL use the build-time values. The web container overwrites this file at
// start when KEYCLOAK_URL is set (runtime-config.sh).
window.__ENV = {}
