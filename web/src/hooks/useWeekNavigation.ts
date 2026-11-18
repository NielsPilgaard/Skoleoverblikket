import { useState } from 'react'
import type { StepperJump } from '../components/ui/select'
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

/** The Monday of the week 1 August is in. */
function schoolYearStart(year: number): Date {
  const august1 = new Date(year, 7, 1)
  return isoWeekToMonday(getISOWeekYear(august1), getISOWeek(august1))
}

/** Every week of the school year the given week is in, from the week of 1 August to the next. */
function schoolYearWeeks(isoYear: number, isoWeek: number) {
  const monday = isoWeekToMonday(isoYear, isoWeek)
  const calendarYear = monday.getFullYear()
  const startYear = monday >= schoolYearStart(calendarYear) ? calendarYear : calendarYear - 1
  const start = schoolYearStart(startYear)
  const end = schoolYearStart(startYear + 1)

  const weeks: { value: string; label: string }[] = []
  for (let d = start; d < end; d = new Date(d.getFullYear(), d.getMonth(), d.getDate() + 7)) {
    const y = getISOWeekYear(d)
    const w = getISOWeek(d)
    weeks.push({
      value: `${y}-${w}`,
      label: y === getISOWeekYear(start) ? `Uge ${w}` : `Uge ${w}, ${y}`,
    })
  }
  return weeks
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

  /** Dropdown with every week of the school year. */
  const weekJump: StepperJump = {
    label: 'Vælg uge',
    options: schoolYearWeeks(isoYear, isoWeek),
    value: `${isoYear}-${isoWeek}`,
    onChange: (value) => {
      const [y, w] = value.split('-').map(Number)
      setWeek({ isoYear: y, isoWeek: w })
    },
    testId: 'week-jump',
  }

  /** Dropdown with the years around this one. The week number stays where it can. */
  const thisYear = getISOWeekYear(current)
  const yearJump: StepperJump = {
    label: 'Vælg år',
    options: [...new Set([-2, -1, 0, 1, 2].map((offset) => thisYear + offset).concat(isoYear))]
      .sort((a, b) => a - b)
      .map((y) => ({ value: String(y), label: String(y) })),
    value: String(isoYear),
    onChange: (value) => {
      const y = Number(value)
      setWeek({ isoYear: y, isoWeek: Math.min(isoWeek, getISOWeeksInYear(y)) })
    },
    testId: 'year-jump',
    className: 'w-24 shrink-0',
  }

  return { isoYear, isoWeek, setWeek, stepper, weekJump, yearJump }
}
