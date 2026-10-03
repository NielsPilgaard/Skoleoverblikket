import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { getApiV1ClassesByClassIdWeekPlanOptions } from '../../api/generated/@tanstack/react-query.gen'
import { getApiV1ParentsMe } from '../../api/generated/sdk.gen'
import type { ParentMeDto } from '../../api/client'
import { usePageTitle } from '../../hooks/usePageTitle'
import { WeekPlanList } from '../../components/weekplan/WeekPlanList'
import { getISOWeek, getISOWeekYear, getISOWeeksInYear } from '../../utils/isoWeek'

interface Slot {
  id: string
  schemaSlotId: string
  weekday: string
  timeSlotLabel: string
  startTime: string
  courseName: string
  description?: string | null
  lektier?: string | null
}

function ClassWeekPlan({
  classId,
  className,
  isoYear,
  isoWeek,
}: {
  classId: string
  className: string
  isoYear: number
  isoWeek: number
}) {
  const { data, isLoading, isError } = useQuery(
    getApiV1ClassesByClassIdWeekPlanOptions({ path: { classId }, query: { isoYear, isoWeek } })
  )

  if (isLoading) return <div className="text-sm text-gray-400">Indlæser ugeplan...</div>
  if (isError) return <div className="text-sm text-red-500">Fejl ved hentning af ugeplan.</div>

  const plan = data as
    | {
        isHolidayWeek?: boolean
        holidayTitle?: string | null
        slots?: Slot[]
        notes?: string | null
      }
    | undefined
  if (!plan) return null

  return (
    <div>
      <h2 className="text-base font-semibold text-gray-900 mb-3">{className}</h2>
      <WeekPlanList
        notes={plan.notes}
        slots={plan.slots ?? []}
        isHolidayWeek={plan.isHolidayWeek}
        holidayTitle={plan.holidayTitle}
      />
    </div>
  )
}

export default function ParentWeekPlanPage() {
  usePageTitle('Ugeplan')

  const now = new Date()
  const [isoYear, setIsoYear] = useState(getISOWeekYear(now))
  const [isoWeek, setIsoWeek] = useState(getISOWeek(now))

  const {
    data: meRes,
    isLoading,
    isError,
  } = useQuery({
    queryKey: ['parents', 'me'],
    queryFn: () => getApiV1ParentsMe({ throwOnError: false }),
  })

  function prevWeek() {
    if (isoWeek === 1) {
      const prevYear = isoYear - 1
      setIsoYear(prevYear)
      setIsoWeek(getISOWeeksInYear(prevYear))
    } else {
      setIsoWeek((w) => w - 1)
    }
  }

  function nextWeek() {
    if (isoWeek >= getISOWeeksInYear(isoYear)) {
      setIsoYear((y) => y + 1)
      setIsoWeek(1)
    } else {
      setIsoWeek((w) => w + 1)
    }
  }

  if (isLoading) return <div className="p-6 text-sm text-gray-500">Indlæser...</div>
  if (isError)
    return <div className="p-6 text-sm text-red-600">Noget gik galt. Prøv igen senere.</div>

  const notFound = meRes?.response.status === 404
  const me = meRes?.data as ParentMeDto | undefined
  const classes = notFound ? [] : (me?.classes ?? [])

  return (
    <div className="p-4 md:p-6 space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold text-gray-900">Ugeplan</h1>
        <div className="flex items-center gap-2">
          <button onClick={prevWeek} className="p-1.5 rounded-md text-gray-500 hover:bg-gray-100">
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
          </button>
          <span className="text-sm font-medium text-gray-700">
            Uge {isoWeek}, {isoYear}
          </span>
          <button onClick={nextWeek} className="p-1.5 rounded-md text-gray-500 hover:bg-gray-100">
            <svg
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
            >
              <polyline points="9 18 15 12 9 6" />
            </svg>
          </button>
        </div>
      </div>

      {classes.length === 0 ? (
        <p className="text-sm text-gray-500">
          Din konto er endnu ikke tilknyttet nogen klasser. Kontakt skolen, hvis du mener dette er
          en fejl.
        </p>
      ) : (
        <div className="space-y-8">
          {classes.map((c) => (
            <ClassWeekPlan
              key={c.classId}
              classId={c.classId ?? ''}
              className={c.className ?? ''}
              isoYear={isoYear}
              isoWeek={isoWeek}
            />
          ))}
        </div>
      )}
    </div>
  )
}
