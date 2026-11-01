import { useEffect, useState, type ReactNode } from 'react'
import keycloak, { getInitPromise, logout, sessionRefused } from './keycloak'
import { AuthContext, type SchoolRoleState, type ViewAs } from './AuthContext'
import {
  isSchoolRole,
  readStoredActingAs,
  setActingAs,
  storeActingAs,
  type SchoolRole,
} from './actingAs'
import type { MeControllerMeDto, StaffRole } from '../api/generated/types.gen'
import { getApiV1Me, getApiV1StaffMe } from '../api/generated/sdk.gen'

/**
 * The stored "Vis som" choice if the person still holds that role and its module is on,
 * otherwise the API's default (staff, then parent, then board, skipping roles whose module is off).
 */
function chooseActingAs(me: MeControllerMeDto): SchoolRole | null {
  const stored = readStoredActingAs()
  if (stored && (me.roles ?? []).some((r) => r.role === stored && r.active)) return stored
  return isSchoolRole(me.actingAs) ? me.actingAs : null
}

/** Where each role lands, like HomeRedirect, which would send a superadmin to /backoffice instead. */
function startPage(role: SchoolRole, isAdmin: boolean): string {
  if (role === 'parent') return '/foraeldrevisning/skema'
  if (role === 'board') return '/bestyrelse/oversigt'
  return isAdmin ? '/dashboard' : '/mig/oversigt'
}

/**
 * Always renders children, so public pages stay mounted when Keycloak finishes initializing.
 * Routes that need auth must wait for `initialized` themselves.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [authenticated, setAuthenticated] = useState(false)
  const [keycloakReady, setKeycloakReady] = useState(false)
  const [me, setMe] = useState<MeControllerMeDto | null>(null)
  const [rolesLoaded, setRolesLoaded] = useState(false)
  const [actingAs, setActingAsState] = useState<SchoolRole | null>(null)
  const [staffRole, setStaffRole] = useState<StaffRole | null>(null)
  const [staffId, setStaffId] = useState<string | null>(null)
  const [viewAs, setViewAs] = useState<ViewAs>('default')

  useEffect(() => {
    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible' && keycloak.authenticated) {
        // Keycloak down (a deploy) is not a logout: the next API call waits for it instead.
        keycloak.updateToken(300).catch((error) => {
          if (sessionRefused(error)) keycloak.login()
        })
      }
    }
    document.addEventListener('visibilitychange', handleVisibilityChange)

    let intervalId: ReturnType<typeof setInterval> | undefined

    getInitPromise()
      .then((auth) => {
        setAuthenticated(auth)
        setKeycloakReady(true)

        if (!auth) return

        // Proactively refresh token before it expires (30s before expiry)
        intervalId = setInterval(() => {
          keycloak.updateToken(30).catch((error) => {
            if (sessionRefused(error) && document.visibilityState === 'visible') {
              keycloak.login()
            }
          })
        }, 60_000)
      })
      .catch(() => {
        setKeycloakReady(true)
      })

    return () => {
      document.removeEventListener('visibilitychange', handleVisibilityChange)
      clearInterval(intervalId)
    }
  }, [])

  useEffect(() => {
    if (!authenticated) return

    // Roles come from the API (Staff, BoardMember and Parent rows), not from the Keycloak token.
    // Asked without X-Acting-As, so the API returns its default role alongside the list.
    getApiV1Me()
      .then((res) => {
        if (!res.data) return
        const acting = chooseActingAs(res.data)
        setActingAs(acting)
        setActingAsState(acting)
        setMe(res.data)
      })
      .catch(() => {
        // Leaves the person without school roles; the API still enforces access.
      })
      .finally(() => setRolesLoaded(true))

    getApiV1StaffMe()
      .then((res) => {
        if (res.data) {
          setStaffRole(res.data.role ?? null)
          setStaffId(res.data.id ?? null)
        }
      })
      .catch(() => {
        // non-critical; leave null
      })
  }, [authenticated])

  const parsed = keycloak.tokenParsed as Record<string, unknown> | undefined
  const nameRaw = parsed?.name
  const preferredRaw = parsed?.preferred_username
  const userName =
    typeof nameRaw === 'string'
      ? nameRaw
      : typeof preferredRaw === 'string'
        ? preferredRaw
        : undefined

  const schoolRoles: SchoolRoleState[] = (me?.roles ?? []).flatMap((r) =>
    isSchoolRole(r.role) ? [{ role: r.role, active: r.active }] : []
  )
  // UI-only: used for display/UI hints only. Server enforces actual authorization.
  const isSuperAdmin = me?.isSuperAdmin ?? false
  const effectiveViewAs = isSuperAdmin ? viewAs : 'default'
  const isDefault = effectiveViewAs === 'default'
  const isAdmin =
    effectiveViewAs === 'admin' || (isDefault && actingAs === 'staff' && (me?.isAdmin ?? false))
  const isParent = effectiveViewAs === 'parent' || (isDefault && actingAs === 'parent')
  const isBoard = effectiveViewAs === 'board' || (isDefault && actingAs === 'board')

  return (
    <AuthContext.Provider
      value={{
        initialized: keycloakReady && (!authenticated || rolesLoaded),
        authenticated,
        isAdmin,
        isParent,
        isBoard,
        isSuperAdmin,
        schoolRoles,
        actingAs,
        switchRole: (role) => {
          storeActingAs(role)
          // A full reload re-reads every page's data as the new role and lands on its start page.
          window.location.assign(startPage(role, me?.isAdmin ?? false))
        },
        staffRole,
        staffId,
        token: keycloak.token,
        userName,
        logout: () => logout({ redirectUri: 'https://skoleoverblikket.dk' }),
        viewAs: effectiveViewAs,
        setViewAs,
      }}
    >
      {children}
    </AuthContext.Provider>
  )
}
