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

export function startOfDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate())
}

/**
 * The day a parent's ugeplan opens on: today until 15:00, then the next school day. Saturday and
 * Sunday open on the coming Monday.
 */
export function preselectedDate(now: Date = new Date()): Date {
  const weekday = now.getDay()
  let add = 0
  if (weekday === 6) add = 2
  else if (weekday === 0) add = 1
  else if (now.getHours() >= 15) add = weekday === 5 ? 3 : 1
  return new Date(now.getFullYear(), now.getMonth(), now.getDate() + add)
}

/** "tirsdag 3. nov" */
export function formatDayDate(date: Date): string {
  const weekday = date.toLocaleDateString('da-DK', { weekday: 'long' })
  const month = date.toLocaleDateString('da-DK', { month: 'short' }).replace('.', '')
  return `${weekday} ${date.getDate()}. ${month}`
}
