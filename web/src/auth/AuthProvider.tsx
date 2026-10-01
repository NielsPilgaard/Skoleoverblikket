import { useEffect, useState, type ReactNode } from 'react'
import keycloak, { getInitPromise } from './keycloak'
import { AuthContext, type ViewAs } from './AuthContext'
import type { StaffRole } from '../api/generated/types.gen'
import { getApiV1StaffMe } from '../api/generated/sdk.gen'

export function AuthProvider({
  children,
  fallback = null,
}: {
  children: ReactNode
  /** Rendered while Keycloak initializes. */
  fallback?: ReactNode
}) {
  const [authenticated, setAuthenticated] = useState(false)
  const [initialized, setInitialized] = useState(false)
  const [staffRole, setStaffRole] = useState<StaffRole | null>(null)
  const [staffId, setStaffId] = useState<string | null>(null)
  const [viewAs, setViewAs] = useState<ViewAs>('default')

  useEffect(() => {
    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible' && keycloak.authenticated) {
        keycloak.updateToken(300).catch(() => keycloak.login())
      }
    }
    document.addEventListener('visibilitychange', handleVisibilityChange)

    let intervalId: ReturnType<typeof setInterval> | undefined

    getInitPromise()
      .then((auth) => {
        setAuthenticated(auth)
        setInitialized(true)

        if (!auth) return

        // Proactively refresh token before it expires (30s before expiry)
        intervalId = setInterval(() => {
          keycloak.updateToken(30).catch(() => {
            if (document.visibilityState === 'visible') {
              keycloak.login()
            }
          })
        }, 60_000)
      })
      .catch(() => {
        setInitialized(true)
      })

    return () => {
      document.removeEventListener('visibilitychange', handleVisibilityChange)
      clearInterval(intervalId)
    }
  }, [])

  useEffect(() => {
    if (!authenticated) return

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

  if (!initialized) {
    return <>{fallback}</>
  }

  const parsed = keycloak.tokenParsed as Record<string, unknown> | undefined
  const nameRaw = parsed?.name
  const preferredRaw = parsed?.preferred_username
  const userName =
    typeof nameRaw === 'string'
      ? nameRaw
      : typeof preferredRaw === 'string'
        ? preferredRaw
        : undefined
  const realmAccess = parsed?.realm_access
  const rawRoles =
    realmAccess !== null && typeof realmAccess === 'object' && !Array.isArray(realmAccess)
      ? (realmAccess as Record<string, unknown>).roles
      : undefined
  // UI-only: used for display/UI hints only. Server enforces actual authorization.
  const roles = Array.isArray(rawRoles)
    ? rawRoles.filter((r): r is string => typeof r === 'string')
    : []
  const isSuperAdmin = roles.includes('superadmin')

  const effectiveViewAs = isSuperAdmin ? viewAs : 'default'
  const isAdmin =
    effectiveViewAs === 'admin' || (effectiveViewAs === 'default' && roles.includes('admin'))
  const isParent =
    effectiveViewAs === 'parent' || (effectiveViewAs === 'default' && roles.includes('parent'))
  const isBoard =
    effectiveViewAs === 'board' || (effectiveViewAs === 'default' && roles.includes('board'))

  return (
    <AuthContext.Provider
      value={{
        authenticated,
        isAdmin,
        isParent,
        isBoard,
        isSuperAdmin,
        staffRole,
        staffId,
        token: keycloak.token,
        userName,
        logout: () => keycloak.logout({ redirectUri: 'https://skoleoverblikket.dk' }),
        viewAs: effectiveViewAs,
        setViewAs,
      }}
    >
      {children}
    </AuthContext.Provider>
  )
}
