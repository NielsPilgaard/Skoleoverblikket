import { WEEKDAY_NUM } from './weekdays'

export interface TimeAxisRow {
  startTime: string
  endTime: string
}

/** Unique start/end pairs from a list of slots, sorted by start time. Slots missing a time are skipped. */
export function buildTimeAxis(
  slots: { startTime?: string | null; endTime?: string | null }[]
): TimeAxisRow[] {
  const seen = new Map<string, TimeAxisRow>()
  for (const s of slots) {
    if (!s.startTime || !s.endTime) continue
    const key = `${s.startTime}-${s.endTime}`
    if (!seen.has(key)) seen.set(key, { startTime: s.startTime, endTime: s.endTime })
  }
  return [...seen.values()].sort((a, b) => a.startTime.localeCompare(b.startTime))
}

/**
 * Groups slots by start time, then by weekday number (Monday = 1, as in `WEEKDAYS[].num`).
 * Each cell is a list so overlapping slots (conflicts) are kept. Slots missing a start time or
 * with an unknown weekday are skipped.
 */
export function buildSlotGrid<T extends { startTime?: string | null; weekday?: string | number }>(
  slots: T[]
): Record<string, Record<number, T[]>> {
  const grid: Record<string, Record<number, T[]>> = {}
  for (const s of slots) {
    if (!s.startTime) continue
    const day = typeof s.weekday === 'string' ? WEEKDAY_NUM[s.weekday] : s.weekday
    if (day === undefined) continue
    grid[s.startTime] ??= {}
    grid[s.startTime][day] ??= []
    grid[s.startTime][day].push(s)
  }
  return grid
}
