import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { usePageTitle } from '../hooks/usePageTitle'
import {
  deleteApiV1StaffAbsencesByIdMutation,
  getApiV1StaffAbsencesByIdOptions,
  putApiV1StaffAbsencesByIdMutation,
  putApiV1StaffAbsencesByIdSubstituteMutation,
} from '../api/generated/@tanstack/react-query.gen'
import type { AffectedLessonDto } from '../api/generated/types.gen'
import { StaffAbsenceForm } from '../components/absence/StaffAbsenceForm'
import { capitalizeFirst, formatDateTimeRange, formatLongDate } from '../lib/absence'
import { problemDetail } from '../lib/problem'

const ROLE_LABEL = { Teacher: 'Lærer', Aide: 'Pædagog', Substitute: 'Vikar' } as const

/** Admin: one staff absence, every lektion it affects, and a vikar per lektion. */
export default function SubstituteCoverDetailPage() {
  usePageTitle('Vikardækning')
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState(false)

  const options = getApiV1StaffAbsencesByIdOptions({ path: { id } })
  const { data, isLoading, isError } = useQuery(options)

  const assign = useMutation({
    ...putApiV1StaffAbsencesByIdSubstituteMutation(),
    onSuccess: () => {
      setError(null)
      qc.invalidateQueries({ queryKey: options.queryKey })
      qc.invalidateQueries({ queryKey: [{ _id: 'getApiV1StaffAbsences' }] })
    },
    onError: (err) => {
      setError(problemDetail(err) ?? 'Vikaren kunne ikke tildeles.')
      // A 409 means someone else just booked the candidate — refresh the lists.
      qc.invalidateQueries({ queryKey: options.queryKey })
    },
  })

  const update = useMutation({
    ...putApiV1StaffAbsencesByIdMutation(),
    onSuccess: () => {
      setEditing(false)
      qc.invalidateQueries({ queryKey: options.queryKey })
      qc.invalidateQueries({ queryKey: [{ _id: 'getApiV1StaffAbsences' }] })
    },
  })

  const remove = useMutation({
    ...deleteApiV1StaffAbsencesByIdMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: [{ _id: 'getApiV1StaffAbsences' }] })
      navigate('/vikardaekning')
    },
    onError: (err) => setError(problemDetail(err) ?? 'Fraværet kunne ikke slettes.'),
  })

  if (isLoading) return <p className="px-4 py-8 text-sm text-gray-400">Indlæser…</p>
  if (isError || !data?.absence) {
    return (
      <div className="px-4 py-8">
        <p className="text-sm text-red-600">Fraværet findes ikke.</p>
        <Link to="/vikardaekning" className="text-sm text-brand-700 underline">
          Tilbage
        </Link>
      </div>
    )
  }

  const absence = data.absence
  const lessons = data.lessons ?? []
  const byDate = lessons.reduce<Record<string, AffectedLessonDto[]>>((acc, l) => {
    acc[l.date] = [...(acc[l.date] ?? []), l]
    return acc
  }, {})

  function setSubstitute(lesson: AffectedLessonDto, staffId: string | null) {
    assign.mutate({
      path: { id },
      body: { schemaSlotId: lesson.schemaSlotId, date: lesson.date, staffId },
    })
  }

  return (
    <div className="max-w-3xl mx-auto px-4 py-8 space-y-6">
      <div>
        <Link to="/vikardaekning" className="text-sm text-gray-500 hover:text-gray-800">
          ← Vikardækning
        </Link>
        <h1 className="font-display text-2xl font-semibold text-gray-900 mt-1">
          {absence.staffName}
        </h1>
        <p className="text-sm text-gray-600">
          {ROLE_LABEL[absence.role]} ·{' '}
          {formatDateTimeRange(absence.date, absence.endDate, absence.startTime, absence.endTime)}
          {absence.reason ? ` · ${absence.reason}` : ''}
        </p>
        {absence.reportedByName && (
          <p className="text-xs text-gray-400 mt-0.5">Meldt af {absence.reportedByName}</p>
        )}
      </div>

      {editing && (
        <div className="bg-white border border-brand-300 rounded-xl p-5">
          <StaffAbsenceForm
            initial={absence}
            submitLabel="Gem ændringer"
            pendingLabel="Gemmer…"
            reasonLabel="Note (valgfrit)"
            isPending={update.isPending}
            error={
              update.isError
                ? (problemDetail(update.error) ?? 'Ændringen kunne ikke gemmes.')
                : null
            }
            onSubmit={({ staffId: _, ...body }) => update.mutate({ path: { id }, body })}
            onCancel={() => setEditing(false)}
            testIdPrefix="cover-edit"
          />
          <p className="text-xs text-gray-500 mt-3">
            Vikarer på lektioner, som fraværet ikke længere rammer, fjernes.
          </p>
        </div>
      )}

      {error && (
        <p className="text-sm text-red-600" data-testid="cover-error">
          {error}
        </p>
      )}

      {lessons.length === 0 && (
        <p className="text-sm text-gray-500">Fraværet rammer ingen lektioner på skoledage.</p>
      )}

      {Object.entries(byDate).map(([date, dayLessons]) => (
        <section key={date}>
          <h2 className="text-sm font-semibold text-gray-900 mb-2">
            {capitalizeFirst(formatLongDate(date))}
          </h2>
          <ul className="space-y-2">
            {dayLessons.map((l) => (
              <li
                key={`${l.schemaSlotId}-${l.date}`}
                className={`bg-white border rounded-xl px-4 py-3 flex flex-wrap items-center gap-3 ${
                  l.substituteId ? 'border-gray-200' : 'border-amber-300'
                }`}
                data-testid={`cover-lesson-${l.schemaSlotId}-${l.date}`}
              >
                <div className="flex-1 min-w-48">
                  <p className="text-sm font-medium text-gray-900">
                    {l.startTime.slice(0, 5)}–{l.endTime.slice(0, 5)} · {l.className} ·{' '}
                    {l.courseName}
                  </p>
                  <p className="text-xs text-gray-500">
                    {l.seat === 'Teacher' ? 'Som lærer' : 'Som pædagog'}
                  </p>
                </div>
                {l.substituteId ? (
                  <div className="flex items-center gap-2">
                    <span className="inline-flex items-center gap-1 text-sm text-green-800">
                      <span aria-hidden>✓</span> {l.substituteName}
                    </span>
                    <button
                      type="button"
                      onClick={() => setSubstitute(l, null)}
                      disabled={assign.isPending}
                      data-testid={`cover-unassign-${l.schemaSlotId}-${l.date}`}
                      className="text-xs text-gray-500 hover:text-red-600"
                    >
                      Fjern
                    </button>
                  </div>
                ) : (
                  <select
                    value=""
                    onChange={(e) => e.target.value && setSubstitute(l, e.target.value)}
                    disabled={assign.isPending}
                    aria-label="Vælg vikar"
                    data-testid={`cover-candidates-${l.schemaSlotId}-${l.date}`}
                    className="px-3 py-1.5 border border-gray-300 rounded-lg text-sm max-w-full"
                  >
                    <option value="">
                      {(l.candidates ?? []).length === 0 ? 'Ingen ledige' : 'Vælg vikar'}
                    </option>
                    {(l.candidates ?? []).map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.name}
                        {c.role === 'Substitute' ? ' (vikar)' : ''}
                      </option>
                    ))}
                  </select>
                )}
              </li>
            ))}
          </ul>
        </section>
      ))}

      {absence.canDelete && (
        <div className="pt-4 border-t border-gray-200 flex gap-6">
          {!editing && (
            <button
              type="button"
              onClick={() => {
                update.reset()
                setEditing(true)
              }}
              data-testid="cover-edit-open"
              className="text-sm text-brand-700 hover:text-brand-800"
            >
              Ret datoer eller tidsrum
            </button>
          )}
          <button
            type="button"
            onClick={() => {
              if (window.confirm('Slet fraværet? Vikarer på de berørte lektioner fjernes også.')) {
                remove.mutate({ path: { id } })
              }
            }}
            disabled={remove.isPending}
            className="text-sm text-red-600 hover:text-red-700"
          >
            Slet fravær
          </button>
        </div>
      )}
    </div>
  )
}
