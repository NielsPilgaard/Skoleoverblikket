import type { DayOfWeek } from '../api/generated/types.gen'

export interface WeekdayEntry {
  key: DayOfWeek
  label: string
  num: number
}

export const WEEKDAYS: WeekdayEntry[] = [
  { key: 'Monday', label: 'Mandag', num: 1 },
  { key: 'Tuesday', label: 'Tirsdag', num: 2 },
  { key: 'Wednesday', label: 'Onsdag', num: 3 },
  { key: 'Thursday', label: 'Torsdag', num: 4 },
  { key: 'Friday', label: 'Fredag', num: 5 },
]

export const WEEKDAY_KEYS = WEEKDAYS.map((w) => w.key)
export const WEEKDAY_LABELS = WEEKDAYS.map((w) => w.label)

export const WEEKDAY_NUM: Record<string, number> = {
  Sunday: 0,
  Monday: 1,
  Tuesday: 2,
  Wednesday: 3,
  Thursday: 4,
  Friday: 5,
  Saturday: 6,
}

export function weekdayLabel(day: string | undefined): string {
  if (!day) return ''
  const entry = WEEKDAYS.find((w) => w.key === day)
  return entry ? entry.label : day
}
