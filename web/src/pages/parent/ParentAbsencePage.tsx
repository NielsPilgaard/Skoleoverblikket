import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { usePageTitle } from '../../hooks/usePageTitle'
import {
  getApiV1ParentsMeOptions,
  getApiV1AbsenceMineOptions,
  postApiV1AbsenceMutation,
  deleteApiV1AbsenceByIdMutation,
} from '../../api/generated/@tanstack/react-query.gen'
import type { AbsenceCategory } from '../../api/generated/types.gen'
import { DatePicker } from '../../components/DatePicker'
import { CategoryBadge } from '../../components/absence/AbsenceRegisterTab'
import {
  addDaysIso,
  formatDateRange,
  onlyWeekendDays,
  todayIso,
  weekdayFrom,
} from '../../lib/absence'
import { problemDetail } from '../../lib/problem'

type Kind = Extract<AbsenceCategory, 'Illness' | 'ExtraordinaryLeave'>

export default function ParentAbsencePage() {
  usePageTitle('Fravær')
  const qc = useQueryClient()
  const today = todayIso()
  // At the weekend the next school day is the one a parent is reporting for.
  const firstDay = weekdayFrom(today)
  const [showForm, setShowForm] = useState(false)
  const [kind, setKind] = useState<Kind>('Illness')
  const [studentId, setStudentId] = useState('')
  const [date, setDate] = useState(firstDay)
  const [endDate, setEndDate] = useState(firstDay)
  const [reason, setReason] = useState('')
  const [deleteTargetId, setDeleteTargetId] = useState<string | null>(null)

  const { data: me } = useQuery(getApiV1ParentsMeOptions())
  const { data: records = [] } = useQuery(getApiV1AbsenceMineOptions())

  const mineKey = [{ _id: 'getApiV1AbsenceMine' }] as const

  function resetForm() {
    setShowForm(false)
    setKind('Illness')
    setStudentId('')
    setDate(firstDay)
    setEndDate(firstDay)
    setReason('')
  }

  const report = useMutation({
    ...postApiV1AbsenceMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: mineKey })
      resetForm()
    },
  })

  const cancel = useMutation({
    ...deleteApiV1AbsenceByIdMutation(),
    onSuccess: () => qc.invalidateQueries({ queryKey: mineKey }),
  })

  const children = me?.students ?? []
  const weekendOnly = onlyWeekendDays(date, endDate)
  const deleteTarget = records.find((r) => r.id === deleteTargetId)
  const deleteTargetRejected = deleteTarget?.leaveStatus === 'Rejected'

  function handleDateChange(value: string) {
    setDate(value)
    if (endDate < value) setEndDate(value)
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    if (!studentId || !date || weekendOnly) return
    report.mutate({
      body: {
        studentId,
        date,
        endDate: endDate && endDate !== date ? endDate : null,
        category: kind,
        reason: reason.trim() || null,
      },
    })
  }

  return (
    <div className="max-w-2xl mx-auto px-4 py-8">
      <div className="flex items-center justify-between mb-6">
        <h1 className="font-display text-2xl font-semibold text-gray-900">Fravær</h1>
        <button
          type="button"
          onClick={() => (showForm ? resetForm() : setShowForm(true))}
          data-testid="parent-absence-new"
          className="px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors"
        >
          Meld fravær
        </button>
      </div>

      {showForm && (
        <form
          onSubmit={handleSubmit}
          className="bg-white border border-gray-200 rounded-xl p-5 mb-6 space-y-4"
        >
          <div className="grid grid-cols-2 gap-2">
            {(
              [
                ['Illness', 'Syg'],
                ['ExtraordinaryLeave', 'Fri'],
              ] as const
            ).map(([value, label]) => (
              <button
                key={value}
                type="button"
                aria-pressed={kind === value}
                onClick={() => {
                  setKind(value)
                  if (value === 'ExtraordinaryLeave' && date < today) handleDateChange(today)
                }}
                data-testid={`parent-absence-kind-${value}`}
                className={`h-12 rounded-lg border text-sm font-medium ${
                  kind === value
                    ? 'border-brand-600 bg-brand-50 text-brand-800'
                    : 'border-gray-300 text-gray-700'
                }`}
              >
                {label}
              </button>
            ))}
          </div>
          {kind === 'ExtraordinaryLeave' && (
            <p className="text-sm text-gray-600 bg-gray-50 rounded-lg p-3">
              Skolens leder skal godkende fri. Søg i god tid, før den første dag.
            </p>
          )}

          <div>
            <label htmlFor="absence-child" className="block text-sm font-medium text-gray-700 mb-1">
              Barn
            </label>
            <select
              id="absence-child"
              value={studentId}
              onChange={(e) => setStudentId(e.target.value)}
              required
              data-testid="parent-absence-child"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
            >
              <option value="">Vælg barn</option>
              {children.map((s) => (
                <option key={s.studentId} value={s.studentId}>
                  {s.studentName}
                </option>
              ))}
            </select>
          </div>
          <div className="flex gap-3">
            <div className="flex-1">
              <span className="block text-sm font-medium text-gray-700 mb-1">Fra dato</span>
              <DatePicker
                value={date}
                onChange={handleDateChange}
                min={kind === 'ExtraordinaryLeave' ? today : addDaysIso(today, -14)}
              />
            </div>
            <div className="flex-1">
              <span className="block text-sm font-medium text-gray-700 mb-1">Til dato</span>
              <DatePicker value={endDate} onChange={setEndDate} min={date} align="right" />
            </div>
          </div>
          {weekendOnly && (
            <p
              className="text-sm text-amber-900 bg-amber-50 rounded-lg p-3"
              data-testid="parent-absence-weekend"
            >
              Der er ikke skole i weekenden. Vælg en hverdag.
            </p>
          )}
          <div>
            <label
              htmlFor="absence-reason"
              className="block text-sm font-medium text-gray-700 mb-1"
            >
              {kind === 'ExtraordinaryLeave'
                ? 'Hvorfor skal barnet have fri?'
                : 'Besked til skolen (valgfrit)'}
            </label>
            <input
              id="absence-reason"
              type="text"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              maxLength={500}
              data-testid="parent-absence-reason"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
            />
            <p className="text-xs text-gray-500 mt-1">
              {kind === 'ExtraordinaryLeave'
                ? 'Skriv kort, hvad fri er til, fx familiebegivenhed.'
                : 'Skriv ikke diagnoser. Det er nok, at barnet er sygt.'}
            </p>
          </div>
          <div className="flex gap-3">
            <button
              type="submit"
              disabled={report.isPending || weekendOnly}
              data-testid="parent-absence-submit"
              className="px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors disabled:opacity-50"
            >
              {report.isPending
                ? 'Sender…'
                : kind === 'ExtraordinaryLeave'
                  ? 'Søg om fri'
                  : 'Meld syg'}
            </button>
            <button
              type="button"
              onClick={resetForm}
              className="px-4 py-2 text-sm text-gray-600 hover:text-gray-900 transition-colors"
            >
              Annuller
            </button>
          </div>
          {report.isError && (
            <p className="text-sm text-red-600">
              {problemDetail(report.error) ?? 'Der opstod en fejl. Prøv igen.'}
            </p>
          )}
        </form>
      )}

      {records.length === 0 && (
        <p className="text-sm text-gray-500 py-8">Der er ikke registreret fravær.</p>
      )}

      <ul className="space-y-3">
        {records.map((r) => (
          <li
            key={r.id}
            className="bg-white border border-gray-200 rounded-xl p-4"
            data-testid={`parent-absence-record-${r.id}`}
          >
            {/* On phones the badge sits on its own row, so it doesn't squeeze the name and date. */}
            <div className="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-2 sm:gap-3">
              <div className="min-w-0">
                <p className="font-medium text-gray-900 text-sm">{r.studentName}</p>
                <p className="text-sm text-gray-600 mt-0.5">
                  {formatDateRange(r.date, r.endDate)}
                  {r.reason ? ` · ${r.reason}` : ''}
                </p>
                <p className="text-xs text-gray-400 mt-0.5">
                  {r.source === 'Parent' ? 'Meldt af jer' : 'Noteret af skolen'}
                </p>
              </div>
              <div className="flex sm:flex-col items-center sm:items-end justify-between gap-2 shrink-0">
                <CategoryBadge record={r} />
                {r.canCancel && (
                  <button
                    type="button"
                    onClick={() => setDeleteTargetId(r.id)}
                    data-testid={`parent-absence-cancel-${r.id}`}
                    className="text-xs text-gray-500 hover:text-red-600 transition-colors"
                  >
                    {r.leaveStatus === 'Rejected' ? 'Fjern' : 'Annuller'}
                  </button>
                )}
              </div>
            </div>
          </li>
        ))}
      </ul>

      <p className="mt-8 text-xs text-gray-500" data-testid="parent-absence-retention">
        Vi gemmer fravær i indeværende og forrige skoleår.
      </p>

      {deleteTargetId && (
        <div className="fixed inset-0 bg-black/40 flex items-center justify-center p-4 z-50">
          <div className="bg-white rounded-xl p-5 max-w-sm w-full space-y-4">
            <p className="text-sm text-gray-900">
              {deleteTargetRejected
                ? 'Vil du fjerne den afviste anmodning fra listen?'
                : 'Vil du annullere dette fravær?'}
            </p>
            {cancel.isError && (
              <p className="text-sm text-red-600">
                {problemDetail(cancel.error) ?? 'Fraværet kunne ikke annulleres.'}
              </p>
            )}
            <div className="flex justify-end gap-3">
              <button
                type="button"
                onClick={() => {
                  cancel.reset()
                  setDeleteTargetId(null)
                }}
                className="px-4 py-2 text-sm text-gray-600 hover:text-gray-900 transition-colors"
              >
                Fortryd
              </button>
              <button
                type="button"
                disabled={cancel.isPending}
                data-testid="parent-absence-cancel-confirm"
                onClick={() =>
                  cancel.mutate(
                    { path: { id: deleteTargetId } },
                    { onSuccess: () => setDeleteTargetId(null) }
                  )
                }
                className="px-4 py-2 bg-red-600 text-white text-sm font-medium rounded-lg hover:bg-red-700 transition-colors disabled:opacity-50"
              >
                {cancel.isPending ? 'Gemmer…' : deleteTargetRejected ? 'Ja, fjern' : 'Ja, annuller'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
