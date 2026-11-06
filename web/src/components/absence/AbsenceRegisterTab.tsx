import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1AbsenceOptions,
  getApiV1ClassesOptions,
  putApiV1AbsenceByIdCategoryMutation,
} from '../../api/generated/@tanstack/react-query.gen'
import type { AbsenceCategory, AbsenceRecordDto } from '../../api/generated/types.gen'
import { DatePicker } from '../DatePicker'
import {
  CATEGORY_BADGE,
  CATEGORY_LABEL,
  LEAVE_STATUS_BADGE,
  LEAVE_STATUS_LABEL,
  addDaysIso,
  formatDateRange,
  todayIso,
} from '../../lib/absence'
import { problemDetail } from '../../lib/problem'
import { Select } from '../ui/select'

export function CategoryBadge({
  record,
}: {
  record: Pick<AbsenceRecordDto, 'category' | 'leaveStatus' | 'halfDay'>
}) {
  const leave = record.category === 'ExtraordinaryLeave' && record.leaveStatus
  const colors = leave ? LEAVE_STATUS_BADGE[record.leaveStatus!] : CATEGORY_BADGE[record.category]
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${colors}`}>
      {CATEGORY_LABEL[record.category]}
      {record.halfDay ? ' · halv dag' : ''}
      {leave ? ` · ${LEAVE_STATUS_LABEL[record.leaveStatus!].toLowerCase()}` : ''}
    </span>
  )
}

export function AbsenceRegisterTab() {
  const qc = useQueryClient()
  const today = todayIso()
  const [from, setFrom] = useState(addDaysIso(today, -30))
  const [to, setTo] = useState(today)
  const [classId, setClassId] = useState('')
  const [category, setCategory] = useState<AbsenceCategory | ''>('')
  const [error, setError] = useState<string | null>(null)

  const { data: classes = [] } = useQuery(getApiV1ClassesOptions())
  const { data: records = [], isLoading } = useQuery(
    getApiV1AbsenceOptions({
      query: { from, to, classId: classId || undefined, category: category || undefined },
    })
  )

  const changeCategory = useMutation({
    ...putApiV1AbsenceByIdCategoryMutation(),
    onSuccess: () => {
      setError(null)
      qc.invalidateQueries({ queryKey: [{ _id: 'getApiV1Absence' }] })
    },
    onError: (err) => setError(problemDetail(err) ?? 'Kategorien kunne ikke ændres.'),
  })

  return (
    <div>
      <div className="flex flex-wrap gap-2 items-center mb-4">
        <Select
          label="Klasse"
          hideLabel
          options={[
            { value: '', label: 'Alle klasser' },
            ...classes.map((c) => ({ value: c.id, label: c.name })),
          ]}
          value={classId}
          onChange={setClassId}
          testId="absence-filter-class"
        />
        <Select
          label="Kategori"
          hideLabel
          options={[
            { value: '', label: 'Alle kategorier' },
            ...(Object.keys(CATEGORY_LABEL) as AbsenceCategory[]).map((c) => ({
              value: c,
              label: CATEGORY_LABEL[c],
            })),
          ]}
          value={category}
          onChange={(value) => setCategory(value as AbsenceCategory | '')}
          testId="absence-filter-category"
        />
        {/* One unit, so the range wraps as a whole instead of leaving a lone dash behind. */}
        <div className="flex items-center gap-2">
          <DatePicker value={from} onChange={setFrom} />
          <span className="text-gray-400 text-sm">–</span>
          <DatePicker value={to} onChange={setTo} min={from} />
        </div>
      </div>

      {error && <p className="mb-3 text-sm text-red-600">{error}</p>}
      {isLoading && <p className="text-sm text-gray-400">Indlæser…</p>}
      {!isLoading && records.length === 0 && (
        <p className="text-sm text-gray-500 py-8">Intet fravær i perioden.</p>
      )}

      <ul className="space-y-2">
        {records.map((r) => (
          <li
            key={r.id}
            className="bg-white border border-gray-200 rounded-xl px-4 py-3"
            data-testid={`absence-record-${r.id}`}
          >
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div>
                <p className="text-sm font-medium text-gray-900">
                  {r.studentName} <span className="text-gray-500 font-normal">· {r.className}</span>
                </p>
                <p className="text-sm text-gray-600 mt-0.5">
                  {formatDateRange(r.date, r.endDate)}
                  {r.reason ? ` · ${r.reason}` : ''}
                </p>
                <p className="text-xs text-gray-400 mt-0.5">
                  {r.source === 'Parent'
                    ? 'Meldt af forælder'
                    : `Noteret af ${r.registeredByName ?? 'personale'}`}
                </p>
              </div>
              <div className="flex items-center gap-2">
                {r.source === 'Staff' ? (
                  <Select
                    label="Kategori"
                    hideLabel
                    options={[
                      { value: 'Unauthorized', label: CATEGORY_LABEL.Unauthorized },
                      { value: 'Illness', label: CATEGORY_LABEL.Illness },
                    ]}
                    value={r.category}
                    disabled={changeCategory.isPending}
                    onChange={(value) =>
                      changeCategory.mutate({
                        path: { id: r.id },
                        body: { category: value as AbsenceCategory },
                      })
                    }
                    testId={`absence-category-${r.id}`}
                  />
                ) : (
                  <CategoryBadge record={r} />
                )}
              </div>
            </div>
          </li>
        ))}
      </ul>
    </div>
  )
}
