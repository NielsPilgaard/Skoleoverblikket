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
  LEAVE_STATUS_LABEL,
  addDaysIso,
  formatDateRange,
  todayIso,
} from '../../lib/absence'
import { problemDetail } from '../../lib/problem'

export function CategoryBadge({
  record,
}: {
  record: Pick<AbsenceRecordDto, 'category' | 'leaveStatus' | 'halfDay'>
}) {
  const leave = record.category === 'ExtraordinaryLeave' && record.leaveStatus
  return (
    <span
      className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${CATEGORY_BADGE[record.category]}`}
    >
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

  const selectClass =
    'px-3 py-1.5 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500'

  return (
    <div>
      <div className="flex flex-wrap gap-2 items-center mb-4">
        <select
          value={classId}
          onChange={(e) => setClassId(e.target.value)}
          className={selectClass}
          data-testid="absence-filter-class"
        >
          <option value="">Alle klasser</option>
          {classes.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </select>
        <select
          value={category}
          onChange={(e) => setCategory(e.target.value as AbsenceCategory | '')}
          className={selectClass}
          data-testid="absence-filter-category"
        >
          <option value="">Alle kategorier</option>
          {(Object.keys(CATEGORY_LABEL) as AbsenceCategory[]).map((c) => (
            <option key={c} value={c}>
              {CATEGORY_LABEL[c]}
            </option>
          ))}
        </select>
        <DatePicker value={from} onChange={setFrom} />
        <span className="text-gray-400 text-sm">–</span>
        <DatePicker value={to} onChange={setTo} min={from} />
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
                  <select
                    value={r.category}
                    disabled={changeCategory.isPending}
                    onChange={(e) =>
                      changeCategory.mutate({
                        path: { id: r.id },
                        body: { category: e.target.value as AbsenceCategory },
                      })
                    }
                    aria-label="Kategori"
                    data-testid={`absence-category-${r.id}`}
                    className={`px-2 py-1 rounded text-xs font-medium border-0 ${CATEGORY_BADGE[r.category]}`}
                  >
                    <option value="Unauthorized">{CATEGORY_LABEL.Unauthorized}</option>
                    <option value="Illness">{CATEGORY_LABEL.Illness}</option>
                  </select>
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
