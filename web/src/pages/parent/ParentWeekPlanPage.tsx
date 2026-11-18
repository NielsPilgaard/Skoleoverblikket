import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { getApiV1ClassesByClassIdWeekPlanOptions } from '../../api/generated/@tanstack/react-query.gen'
import { getApiV1ParentsMe } from '../../api/generated/sdk.gen'
import type { ParentMeDto } from '../../api/client'
import { usePageTitle } from '../../hooks/usePageTitle'
import { useWeekNavigation } from '../../hooks/useWeekNavigation'
import { ParentWeekPlan, type ParentWeekPlanData } from '../../components/weekplan/ParentWeekPlan'
import { SegmentedControl, WeekStepper } from '../../components/ui/select'
import { preselectedDate } from '../../utils/isoWeek'

function ClassWeekPlan({
  classId,
  isoYear,
  isoWeek,
  preselected,
  sfoAfterSchool,
}: {
  classId: string
  isoYear: number
  isoWeek: number
  preselected: Date
  sfoAfterSchool: boolean
}) {
  const { data, isLoading, isError } = useQuery(
    getApiV1ClassesByClassIdWeekPlanOptions({ path: { classId }, query: { isoYear, isoWeek } })
  )

  if (isLoading) return <div className="text-gray-700">Indlæser ugeplan...</div>
  if (isError) return <div className="text-red-700">Fejl ved hentning af ugeplan.</div>
  if (!data) return null

  return (
    <ParentWeekPlan
      key={`${classId}-${isoYear}-${isoWeek}`}
      plan={data as ParentWeekPlanData}
      preselected={preselected}
      sfoAfterSchool={sfoAfterSchool}
    />
  )
}

export default function ParentWeekPlanPage() {
  usePageTitle('Ugeplan')

  const [preselected] = useState(() => preselectedDate())
  const { isoYear, isoWeek, stepper } = useWeekNavigation(preselected)
  const [selectedStudentId, setSelectedStudentId] = useState<string | null>(null)

  const {
    data: meRes,
    isLoading,
    isError,
  } = useQuery({
    queryKey: ['parents', 'me'],
    queryFn: () => getApiV1ParentsMe({ throwOnError: false }),
  })

  if (isLoading) return <div className="p-6 text-gray-700">Indlæser...</div>
  if (isError) return <div className="p-6 text-red-700">Noget gik galt. Prøv igen senere.</div>

  const notFound = meRes?.response?.status === 404
  const me = meRes?.data as ParentMeDto | undefined
  const students = notFound ? [] : (me?.students ?? [])
  const student = students.find((s) => s.studentId === selectedStudentId) ?? students[0]
  const className = me?.classes?.find((c) => c.classId === student?.classId)?.className

  return (
    <div className="p-4 md:p-6 space-y-3 max-w-2xl">
      <h1 className="text-xl font-semibold text-gray-900">Ugeplan</h1>

      {!student ? (
        <p className="text-gray-700">
          Din konto er endnu ikke tilknyttet nogen klasser. Kontakt skolen, hvis du mener dette er
          en fejl.
        </p>
      ) : (
        <>
          {students.length > 1 && (
            <SegmentedControl
              label="Barn"
              hideLabel
              value={student.studentId ?? null}
              onChange={setSelectedStudentId}
              options={students.map((s) => ({
                value: s.studentId ?? '',
                label: s.studentName ?? '',
                testId: `weekplan-child-${s.studentId}`,
              }))}
            />
          )}
          <WeekStepper
            {...stepper}
            sub={className ? `${className} · ${stepper.sub}` : stepper.sub}
          />
          <ClassWeekPlan
            classId={student.classId ?? ''}
            isoYear={isoYear}
            isoWeek={isoWeek}
            preselected={preselected}
            sfoAfterSchool={!!student.isEnrolledInSfo}
          />
        </>
      )}
    </div>
  )
}
