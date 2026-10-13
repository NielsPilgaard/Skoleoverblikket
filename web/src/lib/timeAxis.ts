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
