import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { usePageTitle } from '../hooks/usePageTitle'
import {
  deleteApiV1StaffAbsencesByIdMutation,
  getApiV1StaffAbsencesMineOptions,
  getApiV1SubstitutionsMineOptions,
  postApiV1StaffAbsencesMutation,
} from '../api/generated/@tanstack/react-query.gen'
import { DatePicker } from '../components/DatePicker'
import { addDaysIso, formatDateRange, formatLongDate, todayIso } from '../lib/absence'
import { problemDetail } from '../lib/problem'

/** Staff: report yourself absent, see your reports, and see the lektioner you cover as vikar. */
export default function StaffAbsencePage() {
  usePageTitle('Mit fravær')
  const qc = useQueryClient()
  const today = todayIso()
  const [date, setDate] = useState(today)
  const [endDate, setEndDate] = useState(today)
  const [reason, setReason] = useState('')
  const [sent, setSent] = useState(false)

  const { data: absences = [] } = useQuery(getApiV1StaffAbsencesMineOptions())
  const { data: substitutions = [] } = useQuery(getApiV1SubstitutionsMineOptions())

  const report = useMutation({
    ...postApiV1StaffAbsencesMutation(),
    onSuccess: () => {
      setSent(true)
      setReason('')
      qc.invalidateQueries({ queryKey: [{ _id: 'getApiV1StaffAbsencesMine' }] })
    },
  })

  const remove = useMutation({
    ...deleteApiV1StaffAbsencesByIdMutation(),
    onSuccess: () => qc.invalidateQueries({ queryKey: [{ _id: 'getApiV1StaffAbsencesMine' }] }),
  })

  function submit(e: React.FormEvent) {
    e.preventDefault()
    setSent(false)
    report.mutate({
      body: {
        date,
        endDate: endDate !== date ? endDate : null,
        reason: reason.trim() || null,
      },
    })
  }

  return (
    <div className="max-w-2xl mx-auto px-4 py-8 space-y-8">
      <h1 className="font-display text-2xl font-semibold text-gray-900">Mit fravær</h1>

      <form onSubmit={submit} className="bg-white border border-gray-200 rounded-xl p-5 space-y-4">
        <h2 className="text-sm font-semibold text-gray-900">Meld fravær</h2>
        <p className="text-sm text-gray-600">
          Kontoret får besked med det samme og finder vikarer til dine lektioner.
        </p>
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
          <label
            htmlFor="staff-absence-reason"
            className="block text-sm font-medium text-gray-700 mb-1"
          >
            Besked til kontoret (valgfrit)
          </label>
          <input
            id="staff-absence-reason"
            type="text"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            maxLength={500}
            placeholder="Fx syg, kursus, barns første sygedag"
            data-testid="staff-absence-reason"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
          />
          <p className="text-xs text-gray-500 mt-1">Skriv ikke diagnoser.</p>
        </div>
        <button
          type="submit"
          disabled={report.isPending}
          data-testid="staff-absence-submit"
          className="px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 disabled:opacity-50"
        >
          {report.isPending ? 'Sender…' : 'Meld fravær'}
        </button>
        {sent && (
          <p className="text-sm text-green-700" data-testid="staff-absence-sent">
            Fraværet er meldt. God bedring, hvis du er syg.
          </p>
        )}
        {report.isError && (
          <p className="text-sm text-red-600">
            {problemDetail(report.error) ?? 'Fraværet kunne ikke meldes. Prøv igen.'}
          </p>
        )}
      </form>

      <section>
        <h2 className="text-sm font-semibold text-gray-900 mb-2">Dine vikartimer</h2>
        {substitutions.length === 0 ? (
          <p className="text-sm text-gray-500">Du skal ikke være vikar de næste 14 dage.</p>
        ) : (
          <ul className="space-y-2" data-testid="my-substitutions">
            {substitutions.map((s) => (
              <li
                key={`${s.date}-${s.startTime}-${s.className}`}
                className="bg-white border border-gray-200 rounded-xl px-4 py-3"
              >
                <p className="text-sm font-medium text-gray-900 capitalize">
                  {formatLongDate(s.date)} · {s.startTime.slice(0, 5)}–{s.endTime.slice(0, 5)}
                </p>
                <p className="text-sm text-gray-600">
                  {s.className} · {s.courseName}
                  {s.roomName ? ` · ${s.roomName}` : ''} · for {s.absentStaffName}
                </p>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section>
        <h2 className="text-sm font-semibold text-gray-900 mb-2">Meldt fravær</h2>
        {absences.length === 0 ? (
          <p className="text-sm text-gray-500">Du har ikke meldt fravær.</p>
        ) : (
          <ul className="space-y-2">
            {absences.map((a) => (
              <li
                key={a.id}
                className="bg-white border border-gray-200 rounded-xl px-4 py-3 flex items-start justify-between gap-3"
                data-testid={`staff-absence-${a.id}`}
              >
                <div>
                  <p className="text-sm font-medium text-gray-900">
                    {formatDateRange(a.date, a.endDate)}
                  </p>
                  <p className="text-sm text-gray-600">
                    {a.affectedLessonCount === 0
                      ? 'Ingen lektioner berørt'
                      : `${a.coveredLessonCount} af ${a.affectedLessonCount} lektioner har vikar`}
                    {a.reason ? ` · ${a.reason}` : ''}
                  </p>
                </div>
                {a.canDelete && (
                  <button
                    type="button"
                    onClick={() => remove.mutate({ path: { id: a.id } })}
                    disabled={remove.isPending}
                    className="text-xs text-gray-500 hover:text-red-600"
                  >
                    Annuller
                  </button>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}
