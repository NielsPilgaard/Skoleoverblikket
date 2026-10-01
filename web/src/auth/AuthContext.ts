import { createContext } from 'react'
import type { StaffRole } from '../api/generated/types.gen'

export type ViewAs = 'default' | 'parent' | 'admin' | 'staff' | 'board'

export interface AuthContextValue {
  /** False until Keycloak init settles; `authenticated` is false until then. */
  initialized: boolean
  authenticated: boolean
  isAdmin: boolean
  isParent: boolean
  isBoard: boolean
  isSuperAdmin: boolean
  staffRole: StaffRole | null
  staffId: string | null
  token: string | undefined
  userName: string | undefined
  logout: () => void
  viewAs: ViewAs
  setViewAs: (mode: ViewAs) => void
}

export const AuthContext = createContext<AuthContextValue | null>(null)
