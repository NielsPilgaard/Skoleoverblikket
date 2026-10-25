import { createContext } from 'react'
import type { StaffRole } from '../api/generated/types.gen'
import type { SchoolRole } from './actingAs'

export type ViewAs = 'default' | 'parent' | 'admin' | 'staff' | 'board'

/** A school role the person holds. `active` is false when the school no longer has its module. */
export interface SchoolRoleState {
  role: SchoolRole
  active: boolean
}

export interface AuthContextValue {
  /** False until Keycloak init settles and, when logged in, the person's school roles have loaded. */
  initialized: boolean
  authenticated: boolean
  isAdmin: boolean
  isParent: boolean
  isBoard: boolean
  isSuperAdmin: boolean
  /** Every school role the person holds (staff, parent, board), from /api/v1/me. */
  schoolRoles: SchoolRoleState[]
  /** The role this browser acts as; sent to the API as X-Acting-As. */
  actingAs: SchoolRole | null
  /** Remembers the choice on this device and reloads the app as that role. */
  switchRole: (role: SchoolRole) => void
  staffRole: StaffRole | null
  staffId: string | null
  token: string | undefined
  userName: string | undefined
  logout: () => void
  viewAs: ViewAs
  setViewAs: (mode: ViewAs) => void
}

export const AuthContext = createContext<AuthContextValue | null>(null)
