import { useState, useRef, useEffect } from 'react'
import type { CalendarEntryDto } from '../../api/client'
import { capitalizeFirst } from '../../lib/absence'

export const TYPE_LABELS: Record<string, string> = {
  Ferie: 'Ferie',
  Lukkedag: 'Lukkedag',
  Arbejdsdag: 'Arbejdsdag',
  Begivenhed: 'Begivenhed',
}

export const TYPE_COLORS: Record<string, string> = {
  Ferie: 'bg-blue-200 text-blue-900',
  Lukkedag: 'bg-red-200 text-red-900',
  Arbejdsdag: 'bg-amber-200 text-amber-900',
  Begivenhed: 'bg-purple-200 text-purple-900',
}

export const TYPE_BADGE_COLORS: Record<string, string> = {
  Ferie: 'bg-blue-100 text-blue-800',
  Lukkedag: 'bg-red-100 text-red-800',
  Arbejdsdag: 'bg-amber-100 text-amber-800',
  Begivenhed: 'bg-purple-100 text-purple-800',
}

export const MONTH_NAMES = [
  'Januar',
  'Februar',
  'Marts',
  'April',
  'Maj',
  'Juni',
  'Juli',
  'August',
  'September',
  'Oktober',
  'November',
  'December',
]

const WEEKDAY_HEADERS = ['Ma', 'Ti', 'On', 'To', 'Fr', 'Lø', 'Sø']

export function getSchoolYears(schoolStartYear: number): { startYear: number; endYear: number } {
  return { startYear: schoolStartYear, endYear: schoolStartYear + 1 }
}

export function getSchoolYearMonths(
  schoolStartYear: number
): Array<{ year: number; month: number }> {
  const months: Array<{ year: number; month: number }> = []
  for (let m = 8; m <= 12; m++) months.push({ year: schoolStartYear, month: m })
  for (let m = 1; m <= 7; m++) months.push({ year: schoolStartYear + 1, month: m })
  return months
}

export function getISOWeek(year: number, month: number, day: number): number {
  const date = new Date(year, month - 1, day)
  const thursday = new Date(date)
  thursday.setDate(date.getDate() - ((date.getDay() + 6) % 7) + 3)
  const firstThursday = new Date(thursday.getFullYear(), 0, 4)
  firstThursday.setDate(firstThursday.getDate() - ((firstThursday.getDay() + 6) % 7) + 3)
  return Math.round((thursday.getTime() - firstThursday.getTime()) / 604800000) + 1
}

export function isEntryInSchoolYear(entry: CalendarEntryDto, startYear: number): boolean {
  const schoolStart = new Date(startYear, 7, 1)
  const schoolEnd = new Date(startYear + 1, 6, 31)
  const entryStart = new Date(`${entry.startDate}T00:00:00`)
  const entryEnd = new Date(`${entry.endDate ?? entry.startDate}T00:00:00`)
  return entryStart <= schoolEnd && entryEnd >= schoolStart
}

export function buildMonthGrid(year: number, month: number): (number | null)[][] {
  const firstDay = new Date(year, month - 1, 1)
  const startOffset = (firstDay.getDay() + 6) % 7
  const daysInMonth = new Date(year, month, 0).getDate()
  const cells: (number | null)[] = [
    ...Array(startOffset).fill(null),
    ...Array.from({ length: daysInMonth }, (_, i) => i + 1),
  ]
  const weeks: (number | null)[][] = []
  for (let i = 0; i < cells.length; i += 7) {
    const week = cells.slice(i, i + 7)
    while (week.length < 7) week.push(null)
    weeks.push(week)
  }
  while (weeks.length < 6) weeks.push(Array(7).fill(null))
  return weeks
}

export function getDayEntries(
  year: number,
  month: number,
  day: number,
  entries: CalendarEntryDto[]
): CalendarEntryDto[] {
  const pad = (n: number) => n.toString().padStart(2, '0')
  const dateStr = `${year}-${pad(month)}-${pad(day)}`
  return entries.filter(
    (e) => (e.startDate ?? '') <= dateStr && dateStr <= (e.endDate ?? e.startDate ?? '')
  )
}

export function formatDateDDMMYYYY(isoDate: string): string {
  const [year, month, day] = isoDate.split('-')
  return `${day}/${month}/${year}`
}

export function formatDateRange(startDate: string, endDate: string): string {
  if (startDate === endDate) return formatDateDDMMYYYY(startDate)
  return `${formatDateDDMMYYYY(startDate)} – ${formatDateDDMMYYYY(endDate)}`
}

export function toDateString(year: number, month: number, day: number): string {
  const pad = (n: number) => n.toString().padStart(2, '0')
  return `${year}-${pad(month)}-${pad(day)}`
}

// ─── DayPopover ───────────────────────────────────────────────────────────────

interface DayPopoverProps {
  year: number
  month: number
  day: number
  entries: CalendarEntryDto[]
  isAdmin: boolean
  onCreateForDate?: (dateStr: string) => void
  onEdit?: (entry: CalendarEntryDto) => void
  onDelete?: (entry: CalendarEntryDto) => void
  onClose: () => void
}

function DayPopover({
  year,
  month,
  day,
  entries,
  isAdmin,
  onCreateForDate,
  onEdit,
  onDelete,
  onClose,
}: DayPopoverProps) {
  const ref = useRef<HTMLDivElement>(null)
  const dateStr = toDateString(year, month, day)
  const dayEntries = getDayEntries(year, month, day, entries)

  useEffect(() => {
    function handleKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKey)
    return () => document.removeEventListener('keydown', handleKey)
  }, [onClose])

  useEffect(() => {
    function handleClick(e: MouseEvent | PointerEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) onClose()
    }
    const timer = setTimeout(() => {
      document.addEventListener('pointerdown', handleClick)
    }, 0)
    return () => {
      clearTimeout(timer)
      document.removeEventListener('pointerdown', handleClick)
    }
  }, [onClose])

  const formattedDate = new Date(`${dateStr}T00:00:00`).toLocaleDateString('da-DK', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
  })

  return (
    <div
      ref={ref}
      className="absolute z-30 top-full left-1/2 -translate-x-1/2 mt-1 bg-white rounded-xl shadow-lg border border-gray-200 w-52 text-left"
      onClick={(e) => e.stopPropagation()}
      onPointerDown={(e) => e.stopPropagation()}
      onKeyDown={(e) => {
        if (e.key === 'Escape') {
          onClose()
          return
        }
        e.stopPropagation()
      }}
      role="dialog"
    >
      <div className="px-3 py-2 border-b border-gray-100">
        <p className="text-xs font-medium text-gray-700">{capitalizeFirst(formattedDate)}</p>
      </div>
      <div className="px-3 py-2 space-y-1">
        {dayEntries.length === 0 && (
          <p className="text-xs text-gray-400 italic">Ingen begivenheder</p>
        )}
        {dayEntries.map((entry) => (
          <div key={`${entry.id}-${entry.startDate}`} className="flex items-center gap-1.5">
            <span
              className={`inline-block px-1.5 py-0.5 rounded-full text-xs font-medium ${TYPE_BADGE_COLORS[entry.type ?? ''] ?? 'bg-gray-100 text-gray-700'}`}
            >
              {TYPE_LABELS[entry.type ?? ''] ?? entry.type}
            </span>
            <span className="text-xs text-gray-800 flex-1 truncate">{entry.title}</span>
            {isAdmin && (
              <div className="flex items-center gap-1 flex-shrink-0">
                <button
                  onClick={() => {
                    onEdit?.(entry)
                    onClose()
                  }}
                  className="text-gray-400 hover:text-gray-700"
                  title="Rediger"
                >
                  <svg
                    width="12"
                    height="12"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                  >
                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
                    <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
                  </svg>
                </button>
                <button
                  onClick={() => {
                    onDelete?.(entry)
                    onClose()
                  }}
                  className="text-gray-400 hover:text-red-600"
                  title="Slet"
                >
                  <svg
                    width="12"
                    height="12"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                  >
                    <polyline points="3 6 5 6 21 6" />
                    <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" />
                    <path d="M10 11v6M14 11v6" />
                    <path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
                  </svg>
                </button>
              </div>
            )}
          </div>
        ))}
      </div>
      {isAdmin && (
        <div className="px-3 py-2 border-t border-gray-100">
          <button
            onClick={() => {
              onCreateForDate?.(dateStr)
              onClose()
            }}
            className="w-full text-left text-xs text-brand-600 hover:text-brand-800 font-medium"
          >
            + Tilføj begivenhed
          </button>
        </div>
      )}
    </div>
  )
}

// ─── CalendarGrid ───────────────────────────────────────────────────────────

export interface CalendarGridProps {
  schoolStartYear: number
  entries: CalendarEntryDto[]
  isAdmin: boolean
  onCreateForDate?: (dateStr: string) => void
  onEdit?: (entry: CalendarEntryDto) => void
  onDelete?: (entry: CalendarEntryDto) => void
  onEntryClick?: (entry: CalendarEntryDto) => void
}

export function CalendarGrid({
  schoolStartYear,
  entries,
  isAdmin,
  onCreateForDate,
  onEdit,
  onDelete,
  onEntryClick,
}: CalendarGridProps) {
  const schoolYearMonths = getSchoolYearMonths(schoolStartYear)

  const today = new Date()
  const nowMonthStr = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}`

  function findCurrentMonthIndex(months: Array<{ year: number; month: number }>) {
    const idx = months.findIndex(
      ({ year, month }) => `${year}-${String(month).padStart(2, '0')}` >= nowMonthStr
    )
    return idx >= 0 ? idx : 0
  }

  const [carouselIndex, setCarouselIndex] = useState(() => findCurrentMonthIndex(schoolYearMonths))

  useEffect(() => {
    setCarouselIndex(findCurrentMonthIndex(getSchoolYearMonths(schoolStartYear)))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [schoolStartYear])

  const [highlightedRange, setHighlightedRange] = useState<{ start: string; end: string } | null>(
    null
  )
  const monthRefs = useRef<Map<string, HTMLDivElement>>(new Map())
  const touchStartX = useRef<number | null>(null)

  function handleTouchStart(e: React.TouchEvent) {
    touchStartX.current = e.touches[0].clientX
  }
  function handleTouchEnd(e: React.TouchEvent) {
    if (touchStartX.current === null) return
    const delta = e.changedTouches[0].clientX - touchStartX.current
    if (delta < -40) setCarouselIndex((i) => Math.min(i + 1, schoolYearMonths.length - 1))
    else if (delta > 40) setCarouselIndex((i) => Math.max(i - 1, 0))
    touchStartX.current = null
  }

  const [openPopover, setOpenPopover] = useState<string | null>(null)

  function handleDayClick(year: number, month: number, day: number, isWeekend: boolean) {
    if (isWeekend) return
    const key = `${year}-${month}-${day}`
    setOpenPopover((prev) => (prev === key ? null : key))
  }

  function handleEntryClick(entry: CalendarEntryDto) {
    const dateStr = entry.startDate!
    const [y, m] = dateStr.split('-').map(Number)
    setHighlightedRange({ start: entry.startDate!, end: entry.endDate ?? entry.startDate! })
    const idx = schoolYearMonths.findIndex(({ year, month }) => year === y && month === m)
    if (idx >= 0) setCarouselIndex(idx)
    setTimeout(() => {
      const isMobile = window.matchMedia('(max-width: 1023px)').matches
      const key = `${y}-${m}-${isMobile ? 'mobile' : 'desktop'}`
      const el = monthRefs.current.get(key)
      if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' })
    }, 0)
    setTimeout(() => setHighlightedRange(null), 8000)
    onEntryClick?.(entry)
  }

  function renderMonthCard(year: number, month: number, large = false) {
    const weeks = buildMonthGrid(year, month)
    const dayCellClass = large
      ? 'w-full text-base text-center py-2.5 h-11 rounded-lg select-none font-medium'
      : 'w-full text-sm text-center py-1 h-8 rounded select-none'
    const headerClass = large ? 'text-sm text-center pb-2' : 'text-xs text-center pb-1'
    const weekNumClass = large
      ? 'text-xs text-gray-400 text-right pr-2 leading-none flex items-center justify-end'
      : 'text-xs text-gray-400 text-right pr-1 py-0.5 leading-none flex items-center justify-end'
    const weekNumCol = large ? '2.5rem' : '2rem'

    const refKey = `${year}-${month}-${large ? 'mobile' : 'desktop'}`

    return (
      <div
        key={refKey}
        ref={(el) => {
          if (el) monthRefs.current.set(refKey, el)
          else monthRefs.current.delete(refKey)
        }}
        className={`bg-white rounded-xl border border-gray-200 ${large ? 'p-6' : 'p-5'}`}
      >
        <p
          className={`font-display font-semibold text-gray-700 mb-3 ${large ? 'text-xl' : 'text-base'}`}
        >
          {MONTH_NAMES[month - 1]} {year}
        </p>
        <div className="grid gap-0" style={{ gridTemplateColumns: `${weekNumCol} repeat(7, 1fr)` }}>
          <div />
          {WEEKDAY_HEADERS.map((h, hi) => (
            <div
              key={h}
              className={`${headerClass} ${hi >= 5 ? 'text-gray-400' : 'text-gray-600'}`}
            >
              {h}
            </div>
          ))}
          {weeks.map((week, wi) => {
            const firstDay = week.find((d) => d !== null)
            const weekNum = firstDay != null ? getISOWeek(year, month, firstDay) : null
            return [
              <div key={`wn-${wi}`} className={weekNumClass} data-testid="week-num">
                {weekNum}
              </div>,
              ...week.map((day, di) => {
                if (day === null) {
                  return (
                    <div
                      key={`${wi}-${di}`}
                      className={`${large ? 'h-11' : 'h-8'} ${di >= 5 ? 'bg-gray-100 rounded' : ''}`}
                    />
                  )
                }
                const isWeekend = di >= 5
                const cellDateStr = toDateString(year, month, day)
                const isHighlighted =
                  highlightedRange !== null &&
                  cellDateStr >= highlightedRange.start &&
                  cellDateStr <= highlightedRange.end
                const dayEntries = isWeekend ? [] : getDayEntries(year, month, day, entries)
                const firstEntry = dayEntries[0]
                const colorClass = firstEntry ? (TYPE_COLORS[firstEntry.type ?? ''] ?? '') : ''
                const popoverKey = `${year}-${month}-${day}`
                const isOpen = openPopover === popoverKey

                return (
                  <div key={`${wi}-${di}`} className="relative">
                    <button
                      type="button"
                      disabled={isWeekend}
                      onClick={() => handleDayClick(year, month, day, isWeekend)}
                      className={[
                        dayCellClass,
                        isHighlighted ? 'ring-2 ring-brand-500' : '',
                        isWeekend
                          ? 'bg-gray-100 text-gray-400 cursor-default'
                          : colorClass
                            ? `${colorClass} cursor-pointer`
                            : 'text-gray-700 cursor-pointer hover:bg-gray-100',
                      ].join(' ')}
                    >
                      {day}
                    </button>
                    {isOpen && (
                      <DayPopover
                        year={year}
                        month={month}
                        day={day}
                        entries={entries}
                        isAdmin={isAdmin}
                        onCreateForDate={onCreateForDate}
                        onEdit={onEdit}
                        onDelete={onDelete}
                        onClose={() => setOpenPopover(null)}
                      />
                    )}
                  </div>
                )
              }),
            ]
          })}
        </div>
      </div>
    )
  }

  const currentCarouselMonth = schoolYearMonths[carouselIndex]

  return (
    <div className="space-y-6">
      {/* Mobile carousel — visible below lg */}
      <div className="lg:hidden">
        <div onTouchStart={handleTouchStart} onTouchEnd={handleTouchEnd}>
          {renderMonthCard(currentCarouselMonth.year, currentCarouselMonth.month, true)}
        </div>
        {/* Pagination: arrow ← dots → arrow */}
        <div className="flex items-center justify-center gap-3 mt-4">
          <button
            onClick={() => setCarouselIndex((i) => Math.max(i - 1, 0))}
            disabled={carouselIndex === 0}
            className="p-2 rounded-lg text-gray-400 hover:text-gray-700 hover:bg-gray-100 disabled:opacity-30 disabled:cursor-not-allowed transition-colors"
            aria-label="Forrige måned"
          >
            <svg
              width="18"
              height="18"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.5"
            >
              <polyline points="15 18 9 12 15 6" />
            </svg>
          </button>
          <div className="flex items-center gap-1.5">
            {schoolYearMonths.map((_, i) => (
              <button
                key={i}
                onClick={() => setCarouselIndex(i)}
                aria-label={`Gå til måned ${i + 1}`}
                className={`rounded-full transition-all ${i === carouselIndex ? 'w-4 h-2 bg-brand-600' : 'w-2 h-2 bg-gray-300 hover:bg-gray-400'}`}
              />
            ))}
          </div>
          <button
            onClick={() => setCarouselIndex((i) => Math.min(i + 1, schoolYearMonths.length - 1))}
            disabled={carouselIndex === schoolYearMonths.length - 1}
            className="p-2 rounded-lg text-gray-400 hover:text-gray-700 hover:bg-gray-100 disabled:opacity-30 disabled:cursor-not-allowed transition-colors"
            aria-label="Næste måned"
          >
            <svg
              width="18"
              height="18"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.5"
            >
              <polyline points="9 18 15 12 9 6" />
            </svg>
          </button>
        </div>
      </div>

      {/* Desktop grid — visible from lg */}
      <div
        className="hidden lg:grid lg:grid-cols-2 xl:grid-cols-3 gap-6"
        data-testid="desktop-month-grid"
      >
        {schoolYearMonths.map(({ year, month }) => renderMonthCard(year, month))}
      </div>

      {/* Entry list */}
      {entries.length > 0 && (
        <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-sm min-w-[480px]">
              <thead>
                <tr className="border-b border-gray-100 bg-gray-50">
                  <th className="px-5 py-3 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide">
                    Type
                  </th>
                  <th className="px-5 py-3 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide">
                    Titel
                  </th>
                  <th className="px-5 py-3 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide hidden sm:table-cell">
                    Dato
                  </th>
                  {isAdmin && (
                    <th className="px-5 py-3 text-right text-xs font-semibold text-gray-500 uppercase tracking-wide">
                      Handlinger
                    </th>
                  )}
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-50">
                {entries.map((entry) => (
                  <tr
                    key={`${entry.id}-${entry.startDate}`}
                    onClick={() => handleEntryClick(entry)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault()
                        handleEntryClick(entry)
                      }
                    }}
                    tabIndex={0}
                    className="hover:bg-gray-50 transition-colors cursor-pointer focus:outline-none focus:ring-2 focus:ring-inset focus:ring-brand-500"
                  >
                    <td className="px-5 py-3">
                      <span
                        className={`inline-block px-2 py-0.5 rounded-full text-xs font-medium ${TYPE_BADGE_COLORS[entry.type ?? ''] ?? 'bg-gray-100 text-gray-700'}`}
                      >
                        {TYPE_LABELS[entry.type ?? ''] ?? entry.type}
                      </span>
                    </td>
                    <td className="px-5 py-3 font-medium text-gray-900">{entry.title}</td>
                    <td className="px-5 py-3 text-gray-500 hidden sm:table-cell">
                      {formatDateRange(
                        entry.startDate ?? '',
                        entry.endDate ?? entry.startDate ?? ''
                      )}
                    </td>
                    {isAdmin && (
                      <td className="px-5 py-3 text-right">
                        <div className="flex items-center justify-end gap-2">
                          <button
                            onClick={(e) => {
                              e.stopPropagation()
                              onEdit?.(entry)
                            }}
                            className="p-1.5 text-gray-400 hover:text-gray-700 rounded-md hover:bg-gray-100 transition-colors"
                            title="Rediger"
                          >
                            <svg
                              width="15"
                              height="15"
                              viewBox="0 0 24 24"
                              fill="none"
                              stroke="currentColor"
                              strokeWidth="2"
                            >
                              <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
                              <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
                            </svg>
                          </button>
                          <button
                            onClick={(e) => {
                              e.stopPropagation()
                              onDelete?.(entry)
                            }}
                            className="p-1.5 text-gray-400 hover:text-red-600 rounded-md hover:bg-red-50 transition-colors"
                            title="Slet"
                          >
                            <svg
                              width="15"
                              height="15"
                              viewBox="0 0 24 24"
                              fill="none"
                              stroke="currentColor"
                              strokeWidth="2"
                            >
                              <polyline points="3 6 5 6 21 6" />
                              <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" />
                              <path d="M10 11v6M14 11v6" />
                              <path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
                            </svg>
                          </button>
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  )
}
