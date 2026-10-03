import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { usePageTitle } from '../hooks/usePageTitle'
import {
  getApiV1StaffAbsencesOptions,
  getApiV1StaffOptions,
  postApiV1StaffAbsencesMutation,
} from '../api/generated/@tanstack/react-query.gen'
import { DatePicker } from '../components/DatePicker'
import { addDaysIso, formatDateRange, todayIso } from '../lib/absence'
import { problemDetail } from '../lib/problem'

/** Admin: staff fravær and how much of it has vikar cover. */
export default function SubstituteCoverPage() {
  usePageTitle('Vikardækning')
  const qc = useQueryClient()
  const today = todayIso()
  const [showForm, setShowForm] = useState(false)
  const [staffId, setStaffId] = useState('')
  const [date, setDate] = useState(today)
  const [endDate, setEndDate] = useState(today)
  const [reason, setReason] = useState('')

  const { data: staff = [] } = useQuery(getApiV1StaffOptions())
  const { data: absences = [], isLoading } = useQuery(getApiV1StaffAbsencesOptions())

  const report = useMutation({
    ...postApiV1StaffAbsencesMutation(),
    onSuccess: () => {
      setShowForm(false)
      setStaffId('')
      setReason('')
      qc.invalidateQueries({ queryKey: [{ _id: 'getApiV1StaffAbsences' }] })
    },
  })

  const upcoming = absences.filter((a) => (a.endDate ?? a.date) >= today)
  const past = absences.filter((a) => (a.endDate ?? a.date) < today)

  return (
    <div className="max-w-3xl mx-auto px-4 py-8 space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="font-display text-2xl font-semibold text-gray-900">Vikardækning</h1>
        <button
          type="button"
          onClick={() => setShowForm((v) => !v)}
          data-testid="cover-report-for-staff"
          className="px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700"
        >
          Meld fravær for en medarbejder
        </button>
      </div>

      {showForm && (
        <form
          onSubmit={(e) => {
            e.preventDefault()
            if (!staffId) return
            report.mutate({
              body: {
                staffId,
                date,
                endDate: endDate !== date ? endDate : null,
                reason: reason.trim() || null,
              },
            })
          }}
          className="bg-white border border-gray-200 rounded-xl p-5 space-y-4"
        >
          <div>
            <label htmlFor="cover-staff" className="block text-sm font-medium text-gray-700 mb-1">
              Medarbejder
            </label>
            <select
              id="cover-staff"
              value={staffId}
              onChange={(e) => setStaffId(e.target.value)}
              required
              data-testid="cover-staff"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm"
            >
              <option value="">Vælg medarbejder</option>
              {staff.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </select>
          </div>
          <div className="flex gap-3">
            <div className="flex-1">
              <span className="block text-sm font-medium text-gray-700 mb-1">Fra dato</span>
              <DatePicker
                value={date}
                onChange={(v) => {
                  setDate(v)
                  if (endDate < v) setEndDate(v)
                }}
                min={addDaysIso(today, -14)}
              />
            </div>
            <div className="flex-1">
              <span className="block text-sm font-medium text-gray-700 mb-1">Til dato</span>
              <DatePicker value={endDate} onChange={setEndDate} min={date} />
            </div>
          </div>
          <div>
            <label htmlFor="cover-reason" className="block text-sm font-medium text-gray-700 mb-1">
              Note (valgfrit)
            </label>
            <input
              id="cover-reason"
              type="text"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              maxLength={500}
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm"
            />
            <p className="text-xs text-gray-500 mt-1">Skriv ikke diagnoser.</p>
          </div>
          <div className="flex gap-3">
            <button
              type="submit"
              disabled={report.isPending}
              data-testid="cover-report-submit"
              className="px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 disabled:opacity-50"
            >
              {report.isPending ? 'Gemmer…' : 'Meld fravær'}
            </button>
            <button
              type="button"
              onClick={() => setShowForm(false)}
              className="px-4 py-2 text-sm text-gray-600 hover:text-gray-900"
            >
              Annuller
            </button>
          </div>
          {report.isError && (
            <p className="text-sm text-red-600">
              {problemDetail(report.error) ?? 'Fraværet kunne ikke gemmes.'}
            </p>
          )}
        </form>
      )}

      {isLoading && <p className="text-sm text-gray-400">Indlæser…</p>}

      <section>
        <h2 className="text-sm font-semibold text-gray-900 mb-2">Nu og kommende</h2>
        {!isLoading && upcoming.length === 0 && (
          <p className="text-sm text-gray-500">Ingen medarbejdere har meldt fravær.</p>
        )}
        <AbsenceList absences={upcoming} />
      </section>

      {past.length > 0 && (
        <section>
          <h2 className="text-sm font-semibold text-gray-900 mb-2">Den seneste uge</h2>
          <AbsenceList absences={past} />
        </section>
      )}
    </div>
  )
}

function AbsenceList({
  absences,
}: {
  absences: {
    id: string
    staffName: string
    date: string
    endDate?: string | null
    reason?: string | null
    affectedLessonCount: number
    coveredLessonCount: number
  }[]
}) {
  return (
    <ul className="space-y-2">
      {absences.map((a) => {
        const missing = a.affectedLessonCount - a.coveredLessonCount
        return (
          <li key={a.id}>
            <Link
              to={`/vikardaekning/${a.id}`}
              data-testid={`cover-absence-${a.id}`}
              className="flex flex-wrap items-center justify-between gap-2 bg-white border border-gray-200 rounded-xl px-4 py-3 hover:border-brand-300"
            >
              <div>
                <p className="text-sm font-medium text-gray-900">{a.staffName}</p>
                <p className="text-sm text-gray-600">
                  {formatDateRange(a.date, a.endDate)}
                  {a.reason ? ` · ${a.reason}` : ''}
                </p>
              </div>
              {a.affectedLessonCount === 0 ? (
                <span className="text-xs text-gray-500">Ingen lektioner</span>
              ) : missing > 0 ? (
                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-xs font-medium bg-amber-100 text-amber-900">
                  <span aria-hidden>●</span> {missing}{' '}
                  {missing === 1 ? 'lektion mangler' : 'lektioner mangler'} vikar
                </span>
              ) : (
                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-xs font-medium bg-green-100 text-green-800">
                  <span aria-hidden>✓</span> Alle lektioner dækket
                </span>
              )}
            </Link>
          </li>
        )
      })}
    </ul>
  )
}
