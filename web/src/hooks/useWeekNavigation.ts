import { useState } from 'react'
import { getISOWeek, getISOWeekYear, getISOWeeksInYear, getPlanningDate } from '../utils/isoWeek'

/** The Monday of an ISO week, as a local date. */
export function isoWeekToMonday(isoYear: number, isoWeek: number): Date {
  const jan4 = new Date(isoYear, 0, 4)
  const dayOfWeek = jan4.getDay() || 7
  return new Date(isoYear, 0, 4 - (dayOfWeek - 1) + (isoWeek - 1) * 7)
}

function monthShort(date: Date): string {
  return date.toLocaleDateString('da-DK', { month: 'short' }).replace('.', '')
}

/** "2.–6. nov", or "30. nov – 4. dec" across a month. The year shows when it isn't this year. */
export function formatWeekDates(isoYear: number, isoWeek: number, now: Date = new Date()): string {
  const monday = isoWeekToMonday(isoYear, isoWeek)
  const friday = new Date(monday.getFullYear(), monday.getMonth(), monday.getDate() + 4)
  const dayMonth = (d: Date) => `${d.getDate()}. ${monthShort(d)}`
  if (monday.getFullYear() !== friday.getFullYear()) {
    return `${dayMonth(monday)} ${monday.getFullYear()} – ${dayMonth(friday)} ${friday.getFullYear()}`
  }
  const dates =
    monday.getMonth() === friday.getMonth()
      ? `${monday.getDate()}.–${dayMonth(friday)}`
      : `${dayMonth(monday)} – ${dayMonth(friday)}`
  return friday.getFullYear() === now.getFullYear() ? dates : `${dates} ${friday.getFullYear()}`
}

/** State for a WeekStepper: which ISO week is shown, and how to move. */
export function useWeekNavigation(initial: Date = getPlanningDate()) {
  const [week, setWeek] = useState(() => ({
    isoYear: getISOWeekYear(initial),
    isoWeek: getISOWeek(initial),
  }))
  const { isoYear, isoWeek } = week

  function prev() {
    setWeek(
      isoWeek === 1
        ? { isoYear: isoYear - 1, isoWeek: getISOWeeksInYear(isoYear - 1) }
        : { isoYear, isoWeek: isoWeek - 1 }
    )
  }

  function next() {
    setWeek(
      isoWeek >= getISOWeeksInYear(isoYear)
        ? { isoYear: isoYear + 1, isoWeek: 1 }
        : { isoYear, isoWeek: isoWeek + 1 }
    )
  }

  const current = getPlanningDate()
  const isCurrent = isoYear === getISOWeekYear(current) && isoWeek === getISOWeek(current)

  function goToCurrent() {
    setWeek({ isoYear: getISOWeekYear(current), isoWeek: getISOWeek(current) })
  }

  /** Spread onto a WeekStepper. */
  const stepper = {
    main: `Uge ${isoWeek}`,
    sub: formatWeekDates(isoYear, isoWeek),
    onPrev: prev,
    onNext: next,
    onToday: isCurrent ? undefined : goToCurrent,
  }

  return { isoYear, isoWeek, setWeek, stepper }
}
