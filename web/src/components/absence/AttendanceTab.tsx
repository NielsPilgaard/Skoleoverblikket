import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import {
  getApiV1AttendanceMinePendingOptions,
  getApiV1AttendanceOverviewOptions,
  getApiV1ClassesOptions,
} from '../../api/generated/@tanstack/react-query.gen'
import type { AttendanceCheckDto, ClassAttendanceStatusDto } from '../../api/generated/types.gen'
import { DatePicker } from '../DatePicker'
import { todayIso } from '../../lib/absence'

function time(check: AttendanceCheckDto): string {
  return new Date(check.takenAt).toLocaleTimeString('da-DK', { hour: '2-digit', minute: '2-digit' })
}

function CheckCell({
  check,
  label,
  schoolDay,
}: {
  check?: AttendanceCheckDto | null
  label: string
  schoolDay: boolean
}) {
  if (!check && !schoolDay) return null
  return check ? (
    <span className="inline-flex items-center gap-1 text-xs text-green-800">
      <span aria-hidden>✓</span> {label} {time(check)}
    </span>
  ) : (
    <span className="inline-flex items-center gap-1 text-xs text-amber-800">
      <span aria-hidden>●</span> {label} mangler
    </span>
  )
}

function attendanceLink(classId: string, date: string, step?: 'slut') {
  const params = new URLSearchParams({ dato: date })
  if (step) params.set('trin', step)
  return `/fravaer/fremmoede/${classId}?${params}`
}

/** Admin: every klasse for a day, with which fremmøde checkpoints are noted. */
function AttendanceOverview() {
  const [date, setDate] = useState(todayIso())
  const { data, isLoading } = useQuery(getApiV1AttendanceOverviewOptions({ query: { date } }))
  const classes = data?.classes ?? []
  const schoolDay = data?.isSchoolDay ?? true
  const missing = classes.filter((c) => !c.complete).length

  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-3 mb-4">
        <p className="text-sm text-gray-600" data-testid="attendance-overview-summary">
          {data && !data.isSchoolDay
            ? 'Der er ikke skole denne dag.'
            : missing === 0
              ? 'Fremmøde er noteret i alle klasser.'
              : `${missing} ${missing === 1 ? 'klasse mangler' : 'klasser mangler'} fremmøde.`}
        </p>
        <DatePicker value={date} onChange={setDate} align="right" />
      </div>

      {isLoading && <p className="text-sm text-gray-400">Indlæser…</p>}

      <ul className="divide-y divide-gray-100 bg-white border border-gray-200 rounded-xl">
        {classes.map((c: ClassAttendanceStatusDto) => (
          <li
            key={c.classId}
            className="flex flex-wrap items-center gap-x-4 gap-y-1 px-4 py-3"
            data-testid={`attendance-overview-${c.classId}`}
          >
            <Link
              to={attendanceLink(c.classId, date)}
              className="font-medium text-gray-900 hover:text-brand-700 w-24"
            >
              {c.className}
            </Link>
            <CheckCell check={c.startOfDay} label="Morgen" schoolDay={schoolDay} />
            {c.requiresEndOfDay && (
              <CheckCell check={c.endOfDay} label="Slut" schoolDay={schoolDay} />
            )}
            {schoolDay && (
              <span className="ml-auto text-xs text-gray-500">
                {c.absentCount} af {c.studentCount} fraværende
              </span>
            )}
          </li>
        ))}
        {!isLoading && classes.length === 0 && (
          <li className="px-4 py-6 text-sm text-gray-500">Der er ingen klasser endnu.</li>
        )}
      </ul>
    </div>
  )
}

/** Staff: the klasser still missing fremmøde today, then every klasse they can note fremmøde for. */
function MyAttendance() {
  const today = todayIso()
  const { data: pending = [] } = useQuery(getApiV1AttendanceMinePendingOptions())
  const { data: classes = [] } = useQuery(getApiV1ClassesOptions())
  const accessible = classes.filter((c) => c.isAccessibleToCurrentUser)

  return (
    <div className="space-y-6">
      {pending.length > 0 && (
        <div className="bg-amber-50 border border-amber-200 rounded-xl p-4">
          <p className="text-sm font-medium text-amber-900 mb-2">Fremmøde mangler i dag</p>
          <div className="flex flex-wrap gap-2">
            {pending.map((p) => (
              <Link
                key={`${p.classId}-${p.checkpoint}`}
                to={attendanceLink(
                  p.classId,
                  today,
                  p.checkpoint === 'EndOfDay' ? 'slut' : undefined
                )}
                data-testid={`attendance-pending-${p.classId}`}
                className="px-3 py-2 rounded-lg bg-white border border-amber-300 text-sm font-medium text-amber-900"
              >
                {p.className}
                {p.checkpoint === 'EndOfDay' ? ' · dagens slutning' : ''}
              </Link>
            ))}
          </div>
        </div>
      )}

      <div>
        <p className="text-sm text-gray-600 mb-2">Vælg klasse</p>
        <div className="grid grid-cols-2 sm:grid-cols-3 gap-2">
          {accessible.map((c) => (
            <Link
              key={c.id}
              to={attendanceLink(c.id, today)}
              data-testid={`attendance-class-${c.id}`}
              className="h-14 flex items-center justify-center rounded-xl bg-white border border-gray-200 text-base font-medium text-gray-900 hover:border-brand-400"
            >
              {c.name}
            </Link>
          ))}
        </div>
        {accessible.length === 0 && (
          <p className="text-sm text-gray-500">
            Du har ikke adgang til at notere fremmøde i nogen klasser.
          </p>
        )}
      </div>
    </div>
  )
}

export function AttendanceTab({ isAdmin }: { isAdmin: boolean }) {
  return isAdmin ? <AttendanceOverview /> : <MyAttendance />
}
