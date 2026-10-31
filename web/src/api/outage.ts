import { useSyncExternalStore } from 'react'

// A deploy replaces the API and Keycloak containers, so for a minute or two requests fail. Instead
// of an error per request, callers retry with growing gaps and OutageNotice shows while any waits.

const DELAYS_MS = [2_000, 3_000, 5_000, 8_000, 13_000, 20_000, 30_000]

/** Set by the maintenance container (infrastructure/maintenance): the API never saw the request. */
const MAINTENANCE_HEADER = 'X-Maintenance'

let waiting = 0
const listeners = new Set<() => void>()
function setWaiting(delta: number) {
  waiting += delta
  for (const listener of listeners) listener()
}

/** Calls attempt until it returns a value, waiting with growing gaps while it returns undefined. */
export async function retryWhileDown<T>(attempt: () => Promise<T | undefined>): Promise<T> {
  let counted = false
  try {
    for (let i = 0; ; i++) {
      const result = await attempt()
      if (result !== undefined) return result
      if (!counted) {
        counted = true
        setWaiting(1)
      }
      await new Promise((resolve) =>
        setTimeout(resolve, DELAYS_MS[Math.min(i, DELAYS_MS.length - 1)])
      )
    }
  } finally {
    if (counted) setWaiting(-1)
  }
}

/** fetch for the API that resends whatever the maintenance container answered, until the API is back. */
export function apiFetch(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  return retryWhileDown(async () => {
    // A Request body can only be read once, so each attempt sends a copy.
    const res = await fetch(input instanceof Request ? input.clone() : input, init)
    return res.headers.has(MAINTENANCE_HEADER) ? undefined : res
  })
}

const subscribe = (listener: () => void) => {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

/** True while any request is waiting for the API or Keycloak to come back. */
export function useOutage(): boolean {
  return useSyncExternalStore(
    subscribe,
    () => waiting > 0,
    () => false
  )
}
