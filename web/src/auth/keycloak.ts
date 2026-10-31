import Keycloak from 'keycloak-js'
import { retryWhileDown } from '../api/outage'

declare global {
  interface Window {
    /** Runtime config from /env.js, written by the web container at start (web/runtime-config.sh). */
    __ENV?: { keycloakUrl?: string }
  }
}

const keycloak = new Keycloak({
  // Runtime value first, so the published image works against any Keycloak (e2e, Staging).
  url: window.__ENV?.keycloakUrl ?? import.meta.env.VITE_KEYCLOAK_URL ?? 'http://localhost:8080',
  realm: 'Skoleoverblikket',
  clientId: 'skoleoverblikket-web',
})

/**
 * keycloak-js rejects a failed refresh with the token endpoint's response. Only a 400 means Keycloak
 * refused the session; anything else (network error, 5xx, maintenance page) means it is unreachable.
 */
export function sessionRefused(error: unknown): boolean {
  return (error as { response?: Response } | undefined)?.response?.status === 400
}

/**
 * Makes sure the access token is valid for minValidity more seconds. Keycloak restarts on every
 * deploy, so while it is down this waits (OutageNotice shows) instead of sending the user to a
 * login page that is down too. Logs in only when Keycloak refuses the session.
 */
export async function refreshToken(minValidity: number): Promise<void> {
  await retryWhileDown(async () => {
    try {
      await keycloak.updateToken(minValidity)
      return true
    } catch (error) {
      if (!keycloak.refreshToken || sessionRefused(error)) {
        keycloak.login()
        throw new Error('Not authenticated')
      }
      return undefined
    }
  })
}

const SIGNUP_TOKEN_KEY = 'skoleoverblikket_signup_token'

type SeededTokens = { accessToken: string; refreshToken: string; idToken?: string }

/**
 * Logs this tab in with tokens the API issued after signup or an invitation password. They come from
 * a password grant, so Keycloak has no SSO session to find on the next page load. The tokens stay in
 * sessionStorage, kept fresh on every refresh, so a reload in this tab stays logged in.
 */
export function seedPostSignupToken(accessToken: string, refreshToken: string) {
  sessionStorage.setItem(SIGNUP_TOKEN_KEY, JSON.stringify({ accessToken, refreshToken }))
}

function readSeededTokens(): SeededTokens | null {
  try {
    const raw = sessionStorage.getItem(SIGNUP_TOKEN_KEY)
    return raw ? (JSON.parse(raw) as SeededTokens) : null
  } catch {
    return null
  }
}

function forgetSeededTokens() {
  try {
    sessionStorage.removeItem(SIGNUP_TOKEN_KEY)
  } catch {
    // Storage blocked: nothing was kept.
  }
}

/** Use instead of keycloak.logout(), so a seeded session does not log the tab straight back in. */
export function logout(options?: Parameters<typeof keycloak.logout>[0]) {
  forgetSeededTokens()
  return keycloak.logout(options)
}

// Module-level promise so init() is only ever called once,
// even if AuthProvider mounts twice (React 18 Strict Mode).
let initPromise: Promise<boolean> | null = null

export function getInitPromise(): Promise<boolean> {
  if (!initPromise) {
    const seeded = readSeededTokens()
    if (seeded) {
      keycloak.onAuthRefreshSuccess = () => {
        if (!keycloak.token || !keycloak.refreshToken) return
        const tokens: SeededTokens = {
          accessToken: keycloak.token,
          refreshToken: keycloak.refreshToken,
          idToken: keycloak.idToken,
        }
        try {
          sessionStorage.setItem(SIGNUP_TOKEN_KEY, JSON.stringify(tokens))
        } catch {
          // Storage blocked: the tab stays logged in until the next reload.
        }
      }
      // Keycloak refused the refresh token (expired or revoked), so the seeded session is over.
      keycloak.onAuthLogout = forgetSeededTokens
    }

    const keycloakInit = keycloak.init({
      // With seeded tokens init refreshes them first and only checks SSO if Keycloak refuses them.
      onLoad: 'check-sso',
      // Check the SSO session in a hidden iframe instead of a full-page redirect to Keycloak.
      // A redirect on every anonymous visit makes Google see the marketing pages as redirects.
      // No fallback to the redirect when the browser blocks the iframe's cookies: the visitor
      // just counts as logged out until they click "Log ind".
      silentCheckSsoRedirectUri: `${window.location.origin}/silent-check-sso.html`,
      silentCheckSsoFallback: false,
      pkceMethod: 'S256',
      checkLoginIframe: false,
      // tokenStore is a valid runtime option not yet reflected in the @types/keycloak-js typings
      ...({ tokenStore: 'localStorage' } as object),
      ...(seeded
        ? { token: seeded.accessToken, refreshToken: seeded.refreshToken, idToken: seeded.idToken }
        : {}),
    })

    // If Keycloak is slow to start (e.g. during dev stack startup), don't block
    // public pages indefinitely — resolve false after 8s and let auth retry on navigation.
    const timeout = new Promise<boolean>((resolve) => setTimeout(() => resolve(false), 8_000))
    initPromise = Promise.race([keycloakInit, timeout])
  }
  return initPromise
}

;(window as unknown as Record<string, unknown>).__keycloak = keycloak

export default keycloak
