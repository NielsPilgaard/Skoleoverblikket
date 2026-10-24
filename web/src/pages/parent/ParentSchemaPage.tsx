import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { getApiV1ClassesByClassIdScheduleOptions } from '../../api/generated/@tanstack/react-query.gen'
import { getApiV1ParentsMe } from '../../api/generated/sdk.gen'
import type { ScheduleSlotDto } from '../../api/client'
import type {
  ParentMeControllerParentStudentDto as ParentStudentDto,
  ParentMeControllerParentMeDto as ParentMeDto,
} from '../../api/generated/types.gen'
import { buildTimeAxis } from '../../lib/timeAxis'
import { WEEKDAY_LABELS, WEEKDAY_NUM } from '../../lib/weekdays'
import { usePageTitle } from '../../hooks/usePageTitle'

function ClassScheduleGrid({ classId, className }: { classId: string; className: string }) {
  const {
    data: rawSlots,
    isLoading,
    isError,
  } = useQuery(getApiV1ClassesByClassIdScheduleOptions({ path: { classId } }))

  const slots: ScheduleSlotDto[] = Array.isArray(rawSlots) ? (rawSlots as ScheduleSlotDto[]) : []
  const timeAxis = buildTimeAxis(slots)

  const slotMap: Record<string, Record<number, ScheduleSlotDto[]>> = {}
  for (const s of slots) {
    if (!s.startTime || !s.weekday) continue
    const day = typeof s.weekday === 'string' ? (WEEKDAY_NUM[s.weekday] ?? -1) : s.weekday
    if (day < 0) continue
    if (!slotMap[s.startTime]) slotMap[s.startTime] = {}
    if (!slotMap[s.startTime][day]) slotMap[s.startTime][day] = []
    slotMap[s.startTime][day].push(s)
  }

  if (isLoading) return <div className="p-4 text-sm text-gray-500">Indlæser skema...</div>
  if (isError) return <div className="p-4 text-sm text-red-600">Kunne ikke hente skema.</div>
  if (timeAxis.length === 0)
    return <div className="p-4 text-sm text-gray-500">Intet skema endnu.</div>

  return (
    <div>
      <h2 className="text-lg font-semibold text-gray-900 mb-3">{className}</h2>
      <div className="overflow-x-auto rounded-lg border border-gray-200">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="bg-gray-50">
              <th className="px-3 py-2 text-left text-xs font-medium text-gray-500 border-b border-gray-200 w-20">
                Tid
              </th>
              {WEEKDAY_LABELS.map((d) => (
                <th
                  key={d}
                  className="px-3 py-2 text-left text-xs font-medium text-gray-500 border-b border-gray-200"
                >
                  {d}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {timeAxis.map((ts, i) => (
              <tr key={ts.startTime} className={i % 2 === 0 ? 'bg-white' : 'bg-gray-50'}>
                <td className="px-3 py-2 text-xs text-gray-500 border-b border-gray-100 whitespace-nowrap">
                  <div>{ts.startTime}</div>
                  <div className="text-gray-400">{ts.endTime}</div>
                </td>
                {[0, 1, 2, 3, 4].map((day) => {
                  const daySlots = slotMap[ts.startTime]?.[day] ?? []
                  return (
                    <td key={day} className="px-2 py-1 border-b border-gray-100 align-top">
                      {daySlots.map((s, idx) => (
                        <div
                          key={`${s.schemaId ?? ''}-${s.startTime ?? ''}-${idx}`}
                          className="rounded px-2 py-1 mb-0.5 text-xs"
                          style={{
                            backgroundColor: s.courseColor ? `${s.courseColor}22` : '#f3f4f6',
                            borderLeft: `3px solid ${s.courseColor ?? '#9ca3af'}`,
                          }}
                        >
                          <div className="font-medium text-gray-900">{s.courseName}</div>
                          {s.teacherName && <div className="text-gray-500">{s.teacherName}</div>}
                          {s.roomName && <div className="text-gray-400">{s.roomName}</div>}
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
    </div>
  )
}

export default function ParentSchemaPage() {
  usePageTitle('Skema')

  const {
    data: meRes,
    isLoading,
    isError,
  } = useQuery({
    queryKey: ['parents', 'me'],
    queryFn: () => getApiV1ParentsMe({ throwOnError: false }),
  })

  const notFound = meRes?.response.status === 404
  const me = meRes?.data as ParentMeDto | undefined
  const students: ParentStudentDto[] = notFound ? [] : (me?.students ?? [])

  const [selectedStudentId, setSelectedStudentId] = useState<string | null>(null)

  const selectedStudent =
    students.find((s) => s.studentId === selectedStudentId) ?? students[0] ?? null

  if (isLoading) return <div className="p-6 text-sm text-gray-500">Indlæser...</div>
  if (isError)
    return <div className="p-6 text-sm text-red-600">Noget gik galt. Prøv igen senere.</div>

  return (
    <div className="p-4 md:p-6 space-y-6" data-testid="parent-schema-page">
      <div className="flex items-center justify-between gap-4 flex-wrap">
        <h1 className="text-xl font-semibold text-gray-900">Skema</h1>
        {students.length > 1 && (
          <select
            value={selectedStudent?.studentId ?? ''}
            onChange={(e) => setSelectedStudentId(e.target.value)}
            className="px-3 py-1.5 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
            data-testid="child-picker"
          >
            {students.map((s) => (
              <option key={s.studentId} value={s.studentId}>
                {s.studentName}
              </option>
            ))}
          </select>
        )}
      </div>
      {students.length === 0 ? (
        <p className="text-sm text-gray-500">
          Din konto er endnu ikke tilknyttet nogen klasser. Kontakt skolen, hvis du mener dette er
          en fejl.
        </p>
      ) : selectedStudent?.classId ? (
        <ClassScheduleGrid
          classId={selectedStudent.classId}
          className={selectedStudent.studentName ?? ''}
        />
      ) : (
        <p className="text-sm text-gray-500">Intet skema fundet for dette barn.</p>
      )}
    </div>
  )
}
