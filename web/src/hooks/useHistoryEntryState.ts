import { type SetStateAction, useCallback, useEffect, useState } from 'react'
import { useLocation } from 'react-router-dom'

// The router calls every page load's first entry "default"; tell loads apart.
const firstEntryKey = `default-${Math.random().toString(36).slice(2)}`

/**
 * useState that comes back when the user returns to this page with the back button.
 * Kept per history entry, so opening the page anew from the menu starts clean.
 * Without a `name` it is plain useState.
 */
export function useHistoryEntryState<T>(name: string | undefined, initial: T) {
  const { key, pathname } = useLocation()
  const entry = key === 'default' ? firstEntryKey : key
  const storageKey = name ? `history-entry:${entry}:${pathname}:${name}` : undefined
  const load = () => (storageKey ? read<T>(storageKey) : undefined) ?? initial
  const [state, setState] = useState(() => ({ key: storageKey, value: load() }))
  // A new history entry while mounted (e.g. the menu link to this page): load that entry's state.
  let current = state
  if (state.key !== storageKey) {
    current = { key: storageKey, value: load() }
    setState(current)
  }
  const value = current.value
  const initialJson = JSON.stringify(initial)

  const setValue = useCallback((action: SetStateAction<T>) => {
    setState((prev) => ({
      key: prev.key,
      value: typeof action === 'function' ? (action as (prev: T) => T)(prev.value) : action,
    }))
  }, [])

  useEffect(() => {
    if (!storageKey) return
    try {
      const json = JSON.stringify(value)
      if (json === initialJson) sessionStorage.removeItem(storageKey)
      else sessionStorage.setItem(storageKey, json)
    } catch {
      // Storage blocked or full: the state just won't survive navigation.
    }
  }, [storageKey, value, initialJson])

  return [value, setValue] as const
}

function read<T>(storageKey: string): T | undefined {
  try {
    const raw = sessionStorage.getItem(storageKey)
    return raw === null ? undefined : (JSON.parse(raw) as T)
  } catch {
    return undefined
  }
}
