export function getISOWeek(date: Date): number {
  const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()))
  const dayNum = d.getUTCDay() || 7
  d.setUTCDate(d.getUTCDate() + 4 - dayNum)
  const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1))
  return Math.ceil(((d.getTime() - yearStart.getTime()) / 86400000 + 1) / 7)
}

export function getISOWeekYear(date: Date): number {
  const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()))
  d.setUTCDate(d.getUTCDate() + 4 - (d.getUTCDay() || 7))
  return d.getUTCFullYear()
}

export function getISOWeeksInYear(year: number): number {
  const jan1 = new Date(Date.UTC(year, 0, 1)).getUTCDay()
  const dec31 = new Date(Date.UTC(year, 11, 31)).getUTCDay()
  return jan1 === 4 || dec31 === 4 ? 53 : 52
}

/**
 * The day whose week a ugeplan opens on: today, or the coming Monday on a Saturday or Sunday,
 * when parents and staff are getting ready for next week. Admin and parent views must agree.
 */
export function getPlanningDate(now: Date = new Date()): Date {
  const day = now.getDay()
  if (day !== 0 && day !== 6) return now
  const monday = new Date(now)
  monday.setDate(now.getDate() + (day === 6 ? 2 : 1))
  return monday
}
