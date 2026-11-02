import { WEEKDAY_NUM } from './weekdays'

export interface TimeAxisRow {
  /** Identifies the row. Two rows can share a start time, so this includes the end time. */
  key: string
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
    if (!seen.has(key)) seen.set(key, { key, startTime: s.startTime, endTime: s.endTime })
  }
  return [...seen.values()].sort((a, b) => a.startTime.localeCompare(b.startTime))
}

/**
 * Groups slots by time row (`TimeAxisRow.key`), then by weekday number (Monday = 1, as in
 * `WEEKDAYS[].num`). Each cell is a list so overlapping slots (conflicts) are kept. Slots missing
 * a time or with an unknown weekday are skipped.
 */
export function buildSlotGrid<
  T extends { startTime?: string | null; endTime?: string | null; weekday?: string | number },
>(slots: T[]): Record<string, Record<number, T[]>> {
  const grid: Record<string, Record<number, T[]>> = {}
  for (const s of slots) {
    if (!s.startTime || !s.endTime) continue
    const day = typeof s.weekday === 'string' ? WEEKDAY_NUM[s.weekday] : s.weekday
    if (day === undefined) continue
    const key = `${s.startTime}-${s.endTime}`
    grid[key] ??= {}
    grid[key][day] ??= []
    grid[key][day].push(s)
  }
  return grid
}
