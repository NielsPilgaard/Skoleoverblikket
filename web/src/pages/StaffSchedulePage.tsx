import { useParams, Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import {
  getApiV1StaffByIdOptions,
  getApiV1StaffByStaffIdScheduleOptions,
} from '../api/generated/@tanstack/react-query.gen'
import type { ScheduleSlotDto } from '../api/client'
import { buildTimeAxis } from '../lib/timeAxis'
import { WEEKDAYS, WEEKDAY_NUM } from '../lib/weekdays'

function toNum(weekday: string | number): number {
  return typeof weekday === 'string' ? (WEEKDAY_NUM[weekday] ?? -1) : weekday
}

export default function StaffSchedulePage() {
  const { staffId } = useParams<{ staffId: string }>()

  const { data: staff } = useQuery({
    ...getApiV1StaffByIdOptions({ path: { id: staffId! } }),
    enabled: !!staffId,
  })

  const {
    data: rawSlots,
    isLoading,
    isError,
  } = useQuery({
    ...getApiV1StaffByStaffIdScheduleOptions({ path: { staffId: staffId! } }),
    enabled: !!staffId,
  })
  const slots: ScheduleSlotDto[] = (rawSlots ?? []) as ScheduleSlotDto[]

  const timeAxis = buildTimeAxis(slots)

  // slotMap: startTime → weekdayNum → slots[]
  const slotMap: Record<string, Record<number, ScheduleSlotDto[]>> = {}
  for (const s of slots) {
    if (!s.startTime || !s.weekday) continue
    const day = toNum(s.weekday)
    if (!slotMap[s.startTime]) slotMap[s.startTime] = {}
    if (!slotMap[s.startTime][day]) slotMap[s.startTime][day] = []
    slotMap[s.startTime][day].push(s)
  }

  return (
    <div className="p-6 lg:p-8 max-w-5xl mx-auto space-y-6">
      <div className="flex items-center gap-3">
        <Link to="/medarbejdere" className="text-gray-400 hover:text-gray-600 transition-colors">
          <svg
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
          >
            <polyline points="15 18 9 12 15 6" />
          </svg>
        </Link>
        <div>
          <div className="flex items-center gap-2 flex-wrap">
            <h1 className="font-display text-2xl font-semibold text-gray-900">
              {staff ? `${staff.name}s skema` : 'Skema'}
            </h1>
            <span className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-600">
              Medarbejderskema
            </span>
          </div>
          <p className="mt-1 text-sm text-gray-500">Ugentligt skema — aktive klasser</p>
        </div>
        {staffId && (
          <a
            href={`/udskriv/medarbejder/${staffId}`}
            target="_blank"
            rel="noopener noreferrer"
            className="ml-auto flex items-center gap-2 px-3 py-1.5 text-sm text-gray-600 border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
          >
            <svg
              width="14"
              height="14"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
            >
              <polyline points="6 9 6 2 18 2 18 9" />
              <path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2" />
              <rect x="6" y="14" width="12" height="8" />
            </svg>
            Udskriv
          </a>
        )}
      </div>

      {isError && (
        <div className="bg-red-50 border border-red-200 rounded-xl p-5">
          <p className="text-red-700 text-sm font-medium">Kunne ikke hente skema</p>
        </div>
      )}

      {isLoading && (
        <div className="bg-white rounded-xl border border-gray-200 p-5 animate-pulse space-y-3">
          <div className="h-5 w-48 bg-gray-200 rounded" />
          {[1, 2, 3, 4].map((i) => (
            <div key={i} className="h-12 bg-gray-100 rounded-lg" />
          ))}
        </div>
      )}

      {!isLoading && slots.length === 0 && (
        <div className="bg-white rounded-xl border border-gray-200 p-10 text-center text-gray-400 text-sm">
          Ingen aktive lektioner denne uge
        </div>
      )}

      {!isLoading && slots.length > 0 && (
        <div className="bg-white rounded-xl border border-gray-200 overflow-x-auto">
          <table className="w-full text-sm border-collapse">
            <thead>
              <tr className="bg-gray-50 border-b border-gray-200">
                <th className="px-3 py-2.5 text-right text-xs font-semibold text-gray-500 uppercase tracking-wide w-20 border-r border-gray-200">
                  Tid
                </th>
                {WEEKDAYS.map((d) => (
                  <th
                    key={d.key}
                    className="px-3 py-2.5 text-center text-xs font-semibold text-gray-700 uppercase tracking-wide"
                  >
                    {d.label}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {timeAxis.map((ts) => (
                <tr key={ts.startTime} className="hover:bg-gray-50/50">
                  <td className="px-3 py-2 text-right align-top border-r border-gray-200 bg-gray-50/50 whitespace-nowrap">
                    <span className="block text-xs font-semibold text-gray-600">
                      {ts.startTime}
                    </span>
                    <span className="block text-xs text-gray-400">{ts.endTime}</span>
                  </td>
                  {WEEKDAYS.map((d) => {
                    const daySlots = slotMap[ts.startTime]?.[d.num]
                    return (
                      <td key={d.key} className="px-2 py-2 align-top">
                        {daySlots?.map((slot, i) => (
                          <div
                            key={i}
                            className="rounded-lg px-2 py-1.5 mb-1 last:mb-0"
                            style={
                              slot.courseColor
                                ? {
                                    backgroundColor: `${slot.courseColor}22`,
                                    borderLeft: `3px solid ${slot.courseColor}`,
                                  }
                                : {
                                    backgroundColor: '#f3f4f6',
                                    borderLeft: '3px solid #d1d5db',
                                  }
                            }
                          >
                            <p
                              className="text-xs font-semibold leading-tight truncate"
                              style={
                                slot.courseColor
                                  ? { color: slot.courseColor }
                                  : { color: '#111827' }
                              }
                            >
                              {slot.courseName}
                            </p>
                            <p className="text-xs text-gray-500 truncate">{slot.className}</p>
                            {slot.roomName && (
                              <p className="text-xs text-gray-400 truncate">{slot.roomName}</p>
                            )}
                          </div>
                        ))}
                      </td>
                    )
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
