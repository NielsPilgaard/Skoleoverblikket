import { useState } from 'react'
import { Markdown } from '../markdown/Markdown'
import { WEEKDAY_KEYS, weekdayLabel } from '../../lib/weekdays'

export interface WeekPlanListSlot {
  id: string
  schemaSlotId: string
  weekday: string
  startTime: string
  courseName: string
  beskrivelse?: string | null
  lektier?: string | null
}

interface WeekPlanListProps {
  generelt?: string | null
  slots: WeekPlanListSlot[]
  isHolidayWeek?: boolean
  holidayTitle?: string | null
}

/** Read-only day-by-day list rendering of a week plan — the same format parents see. */
export function WeekPlanList({ generelt, slots, isHolidayWeek, holidayTitle }: WeekPlanListProps) {
  const [openDays, setOpenDays] = useState<Record<string, boolean>>({})

  function toggleDay(day: string) {
    setOpenDays((prev) => ({ ...prev, [day]: !prev[day] }))
  }

  if (isHolidayWeek) {
    return (
      <div>
        {generelt && (
          <div className="mb-3 p-3 bg-blue-50 border border-blue-100 rounded-lg text-sm text-gray-700 prose prose-sm max-w-none [&_p]:m-0 [&_ul]:my-0.5 [&_li]:my-0">
            <Markdown>{generelt}</Markdown>
          </div>
        )}
        <div className="p-3 bg-blue-50 text-blue-700 text-sm rounded-lg">
          {holidayTitle ?? 'Ferie'}
        </div>
      </div>
    )
  }

  const byDay = slots.reduce<Record<string, WeekPlanListSlot[]>>((acc, s) => {
    const day = s.weekday ?? 'Monday'
    if (!acc[day]) acc[day] = []
    acc[day].push(s)
    return acc
  }, {})

  for (const day of Object.keys(byDay)) {
    byDay[day].sort((a, b) => a.startTime.localeCompare(b.startTime))
  }

  return (
    <div>
      {generelt && (
        <div className="mb-3 p-3 bg-blue-50 border border-blue-100 rounded-lg text-sm text-gray-700 prose prose-sm max-w-none [&_p]:m-0 [&_ul]:my-0.5 [&_li]:my-0">
          <Markdown>{generelt}</Markdown>
        </div>
      )}
      <div className="space-y-2">
        {WEEKDAY_KEYS.map((day) => {
          const daySlots = byDay[day] ?? []
          if (daySlots.length === 0) return null
          const isOpen = !!openDays[day]
          return (
            <div key={day} className="border border-gray-100 rounded-lg overflow-hidden">
              <button
                type="button"
                onClick={() => toggleDay(day)}
                className="w-full flex items-center justify-between px-3 py-2 bg-gray-50 hover:bg-gray-100 transition-colors"
                data-testid={`weekplan-day-toggle-${day}`}
              >
                <span className="text-xs font-semibold text-gray-600 uppercase tracking-wide">
                  {weekdayLabel(day)}
                </span>
                <span className="flex items-center gap-1.5 text-xs text-gray-400">
                  {daySlots.length} {daySlots.length === 1 ? 'lektion' : 'lektioner'}
                  <span className={`transition-transform ${isOpen ? 'rotate-90' : ''}`}>▶</span>
                </span>
              </button>
              {isOpen && (
                <div className="p-2 space-y-1.5 bg-white">
                  {daySlots.map((s) => (
                    <div
                      key={s.schemaSlotId}
                      className="bg-white border border-gray-100 rounded-lg px-3 py-2"
                    >
                      <div className="flex items-center gap-2">
                        <span className="text-xs text-gray-400 w-16 shrink-0">
                          {s.startTime.slice(0, 5)}
                        </span>
                        <span className="text-sm font-medium text-gray-900">{s.courseName}</span>
                      </div>
                      {s.beskrivelse && (
                        <div className="mt-1 text-xs text-gray-600 ml-18 prose prose-xs max-w-none [&_p]:m-0 [&_ul]:my-0.5 [&_li]:my-0">
                          <Markdown>{s.beskrivelse}</Markdown>
                        </div>
                      )}
                      {s.lektier && (
                        <div className="mt-1 text-xs text-blue-700 ml-18 prose prose-xs max-w-none [&_p]:m-0 [&_ul]:my-0.5 [&_li]:my-0">
                          <span className="font-medium">Lektier: </span>
                          <Markdown>{s.lektier}</Markdown>
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>
          )
        })}
        {slots.length === 0 && <p className="text-sm text-gray-400">Ingen lektioner denne uge.</p>}
      </div>
    </div>
  )
}
