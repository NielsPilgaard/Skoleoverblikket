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

/** Waits ms, or rejects with the abort reason as soon as signal aborts. */
function wait(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) return reject(signal.reason)
    const onAbort = () => {
      clearTimeout(timer)
      reject(signal?.reason)
    }
    const timer = setTimeout(() => {
      signal?.removeEventListener('abort', onAbort)
      resolve()
    }, ms)
    signal?.addEventListener('abort', onAbort, { once: true })
  })
}

/**
 * Calls attempt until it returns a value, waiting with growing gaps while it returns undefined.
 * An abort of signal ends the wait at once, so OutageNotice doesn't linger for a request nobody wants.
 */
export async function retryWhileDown<T>(
  attempt: (retry: number) => Promise<T | undefined>,
  signal?: AbortSignal
): Promise<T> {
  let counted = false
  try {
    for (let i = 0; ; i++) {
      const result = await attempt(i)
      if (result !== undefined) return result
      if (!counted) {
        counted = true
        setWaiting(1)
      }
      await wait(DELAYS_MS[Math.min(i, DELAYS_MS.length - 1)], signal)
    }
  } finally {
    if (counted) setWaiting(-1)
  }
}

let freshToken: (() => Promise<string | undefined>) | undefined

/**
 * Where apiFetch gets a valid access token when it resends a request after a wait. A deploy can take
 * longer than the token's lifetime, and the API would answer 401 to the token built before the wait.
 */
export function setRetryTokenSource(source: () => Promise<string | undefined>) {
  freshToken = source
}

/** The request to send on attempt retry: a copy (a Request body can only be read once), re-authorized after a wait. */
async function prepare(
  input: RequestInfo | URL,
  init: RequestInit | undefined,
  retry: number
): Promise<[RequestInfo | URL, RequestInit | undefined]> {
  const request = input instanceof Request ? input.clone() : input
  if (retry === 0 || !freshToken) return [request, init]

  const bearer = (headers: Headers) => headers.get('Authorization')?.startsWith('Bearer ')
  const initHeaders = init?.headers ? new Headers(init.headers) : undefined
  const inRequest = request instanceof Request && bearer(request.headers)
  if (!inRequest && !(initHeaders && bearer(initHeaders))) return [request, init]

  const token = await freshToken()
  if (!token) return [request, init]
  if (initHeaders && bearer(initHeaders)) {
    initHeaders.set('Authorization', `Bearer ${token}`)
    init = { ...init, headers: initHeaders }
  }
  if (inRequest) request.headers.set('Authorization', `Bearer ${token}`)
  return [request, init]
}

/** fetch for the API that resends whatever the maintenance container answered, until the API is back. */
export function apiFetch(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  const signal = init?.signal ?? (input instanceof Request ? input.signal : undefined)
  return retryWhileDown(async (retry) => {
    const res = await fetch(...(await prepare(input, init, retry)))
    return res.headers.has(MAINTENANCE_HEADER) ? undefined : res
  }, signal ?? undefined)
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
