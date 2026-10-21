/**
 * The school role this browser acts as when one login has several (staff, parent, board).
 * The API reads it from the X-Acting-As header and only ever narrows to a role the person holds.
 * AuthProvider sets it once /api/v1/me has loaded; every API call sends it from then on.
 */
export type SchoolRole = 'staff' | 'parent' | 'board'

export const ACTING_AS_HEADER = 'X-Acting-As'

const STORAGE_KEY = 'skoleoverblikket.actingAs'

let current: SchoolRole | null = null

export function isSchoolRole(value: unknown): value is SchoolRole {
  return value === 'staff' || value === 'parent' || value === 'board'
}

export function getActingAs(): SchoolRole | null {
  return current
}

export function setActingAs(role: SchoolRole | null) {
  current = role
}

/** Headers for raw `fetch` calls that bypass the generated client. */
export function actingAsHeaders(): Record<string, string> {
  return current ? { [ACTING_AS_HEADER]: current } : {}
}

/** The role chosen under "Vis som" on this device, if any. */
export function readStoredActingAs(): SchoolRole | null {
  try {
    const value = localStorage.getItem(STORAGE_KEY)
    return isSchoolRole(value) ? value : null
  } catch {
    return null
  }
}

export function storeActingAs(role: SchoolRole) {
  try {
    localStorage.setItem(STORAGE_KEY, role)
  } catch {
    // Private mode or blocked storage: the choice lasts until the page reloads.
  }
}
