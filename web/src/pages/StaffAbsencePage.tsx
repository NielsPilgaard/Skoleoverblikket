import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { usePageTitle } from '../hooks/usePageTitle'
import {
  deleteApiV1StaffAbsencesByIdMutation,
  getApiV1StaffAbsencesMineOptions,
  getApiV1SubstitutionsMineOptions,
  postApiV1StaffAbsencesMutation,
  putApiV1StaffAbsencesByIdMutation,
} from '../api/generated/@tanstack/react-query.gen'
import { StaffAbsenceForm } from '../components/absence/StaffAbsenceForm'
import {
  addDaysIso,
  capitalizeFirst,
  formatDateTimeRange,
  formatLongDate,
  todayIso,
} from '../lib/absence'
import { problemDetail } from '../lib/problem'

/** Staff: report yourself absent, see your reports, and see the lektioner you cover as vikar. */
export default function StaffAbsencePage() {
  usePageTitle('Mit fravær')
  const qc = useQueryClient()
  const today = todayIso()
  const [sent, setSent] = useState(false)
  const [formKey, setFormKey] = useState(0)
  const [editingId, setEditingId] = useState<string | null>(null)

  const { data: absences = [] } = useQuery(getApiV1StaffAbsencesMineOptions())
  const { data: substitutions = [] } = useQuery(getApiV1SubstitutionsMineOptions())

  const report = useMutation({
    ...postApiV1StaffAbsencesMutation(),
    onSuccess: () => {
      setSent(true)
      setFormKey((k) => k + 1)
      qc.invalidateQueries({ queryKey: [{ _id: 'getApiV1StaffAbsencesMine' }] })
    },
  })

  const update = useMutation({
    ...putApiV1StaffAbsencesByIdMutation(),
    onSuccess: () => {
      setEditingId(null)
      qc.invalidateQueries({ queryKey: [{ _id: 'getApiV1StaffAbsencesMine' }] })
    },
  })

  const remove = useMutation({
    ...deleteApiV1StaffAbsencesByIdMutation(),
    onSuccess: () => qc.invalidateQueries({ queryKey: [{ _id: 'getApiV1StaffAbsencesMine' }] }),
  })

  return (
    <div className="max-w-2xl mx-auto px-4 py-8 space-y-8">
      <h1 className="font-display text-2xl font-semibold text-gray-900">Mit fravær</h1>

      <div className="bg-white border border-gray-200 rounded-xl p-5 space-y-4">
        <h2 className="text-sm font-semibold text-gray-900">Meld fravær</h2>
        <p className="text-sm text-gray-600">
          Kontoret får besked med det samme og finder vikarer til dine lektioner.
        </p>
        <StaffAbsenceForm
          key={formKey}
          submitLabel="Meld fravær"
          pendingLabel="Sender…"
          reasonLabel="Besked til kontoret (valgfrit)"
          reasonPlaceholder="Fx syg, kursus, barns første sygedag"
          isPending={report.isPending}
          error={
            report.isError
              ? (problemDetail(report.error) ?? 'Fraværet kunne ikke meldes. Prøv igen.')
              : null
          }
          onSubmit={({ staffId: _, ...body }) => {
            setSent(false)
            report.mutate({ body })
          }}
          testIdPrefix="staff-absence"
        />
        {sent && (
          <p className="text-sm text-green-700" data-testid="staff-absence-sent">
            Fraværet er meldt. God bedring, hvis du er syg.
          </p>
        )}
      </div>

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
                <p className="text-sm font-medium text-gray-900">
                  {capitalizeFirst(formatLongDate(s.date))} · {s.startTime.slice(0, 5)}–
                  {s.endTime.slice(0, 5)}
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
            {absences.map((a) =>
              editingId === a.id ? (
                <li
                  key={a.id}
                  className="bg-white border border-brand-300 rounded-xl p-4"
                  data-testid={`staff-absence-${a.id}`}
                >
                  <StaffAbsenceForm
                    initial={a}
                    minDate={addDaysIso(today, 1)}
                    submitLabel="Gem ændringer"
                    pendingLabel="Gemmer…"
                    reasonLabel="Besked til kontoret (valgfrit)"
                    isPending={update.isPending}
                    error={
                      update.isError
                        ? (problemDetail(update.error) ?? 'Ændringen kunne ikke gemmes.')
                        : null
                    }
                    onSubmit={({ staffId: _, ...body }) =>
                      update.mutate({ path: { id: a.id }, body })
                    }
                    onCancel={() => setEditingId(null)}
                    testIdPrefix="staff-absence-edit"
                  />
                </li>
              ) : (
                <li
                  key={a.id}
                  className="bg-white border border-gray-200 rounded-xl px-4 py-3 flex items-start justify-between gap-3"
                  data-testid={`staff-absence-${a.id}`}
                >
                  <div>
                    <p className="text-sm font-medium text-gray-900">
                      {formatDateTimeRange(a.date, a.endDate, a.startTime, a.endTime)}
                    </p>
                    <p className="text-sm text-gray-600">
                      {a.affectedLessonCount === 0
                        ? 'Ingen lektioner berørt'
                        : `${a.coveredLessonCount} af ${a.affectedLessonCount} lektioner har vikar`}
                      {a.reason ? ` · ${a.reason}` : ''}
                    </p>
                  </div>
                  {a.canDelete && (
                    <div className="flex gap-3 shrink-0">
                      <button
                        type="button"
                        onClick={() => {
                          update.reset()
                          setEditingId(a.id)
                        }}
                        data-testid={`staff-absence-edit-${a.id}`}
                        className="text-xs text-brand-700 hover:text-brand-800"
                      >
                        Ret
                      </button>
                      <button
                        type="button"
                        onClick={() => {
                          if (window.confirm('Annuller fraværet?')) {
                            remove.mutate({ path: { id: a.id } })
                          }
                        }}
                        disabled={remove.isPending}
                        className="text-xs text-gray-500 hover:text-red-600"
                      >
                        Annuller
                      </button>
                    </div>
                  )}
                </li>
              )
            )}
          </ul>
        )}
      </section>
    </div>
  )
}
