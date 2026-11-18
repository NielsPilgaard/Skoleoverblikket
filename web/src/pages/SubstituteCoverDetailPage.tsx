import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { usePageTitle } from '../hooks/usePageTitle'
import {
  deleteApiV1StaffAbsencesByIdMutation,
  getApiV1StaffAbsencesByIdOptions,
  putApiV1StaffAbsencesByIdMutation,
  putApiV1StaffAbsencesByIdSubstituteAllMutation,
  putApiV1StaffAbsencesByIdSubstituteMutation,
} from '../api/generated/@tanstack/react-query.gen'
import type { AffectedLessonDto, AvailableStaffDto } from '../api/generated/types.gen'
import { StaffAbsenceForm } from '../components/absence/StaffAbsenceForm'
import { capitalizeFirst, formatDateTimeRange, formatLongDate } from '../lib/absence'
import { problemDetail } from '../lib/problem'
import { Select } from '../components/ui/select'

const ROLE_LABEL = { Teacher: 'Lærer', Aide: 'Pædagog', Substitute: 'Vikar' } as const

/** Candidates for any uncovered lektion, most lektioner free first; ties keep the API's ranking. */
function coverAllCandidates(uncovered: AffectedLessonDto[]) {
  const byId = new Map<string, { staff: AvailableStaffDto; free: number }>()
  for (const l of uncovered) {
    for (const c of l.candidates ?? []) {
      const entry = byId.get(c.id) ?? { staff: c, free: 0 }
      entry.free++
      byId.set(c.id, entry)
    }
  }
  return [...byId.values()].sort((a, b) => b.free - a.free)
}

function coverAllResultText(assigned: number, stillUncovered: number) {
  const done = `Vikaren er sat på ${assigned} ${assigned === 1 ? 'lektion' : 'lektioner'}.`
  if (stillUncovered === 0) return done
  return `${done} ${stillUncovered} ${stillUncovered === 1 ? 'lektion mangler' : 'lektioner mangler'} stadig vikar.`
}

/** Admin: one staff absence, every lektion it affects, and a vikar per lektion. */
export default function SubstituteCoverDetailPage() {
  usePageTitle('Vikardækning')
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState(false)
  const [coverAllId, setCoverAllId] = useState('')
  const [coverAllResult, setCoverAllResult] = useState<string | null>(null)

  const options = getApiV1StaffAbsencesByIdOptions({ path: { id } })
  const { data, isLoading, isError } = useQuery(options)

  const assignAll = useMutation({
    ...putApiV1StaffAbsencesByIdSubstituteAllMutation(),
    onSuccess: (res) => {
      setError(null)
      setCoverAllId('')
      setCoverAllResult(coverAllResultText(res.assigned, res.stillUncovered))
      qc.invalidateQueries({ queryKey: options.queryKey })
      qc.invalidateQueries({ queryKey: [{ _id: 'getApiV1StaffAbsences' }] })
    },
    onError: (err) => {
      setCoverAllResult(null)
      setError(problemDetail(err) ?? 'Vikaren kunne ikke tildeles.')
      qc.invalidateQueries({ queryKey: options.queryKey })
    },
  })

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
  const uncovered = lessons.filter((l) => !l.substituteId)
  const allCandidates = coverAllCandidates(uncovered)

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

      {coverAllResult && (
        <p className="text-sm text-green-800" data-testid="cover-all-result">
          {coverAllResult}
        </p>
      )}

      {uncovered.length >= 2 && (
        <form
          className="bg-brand-50 border border-brand-200 rounded-xl p-4 space-y-3"
          onSubmit={(e) => {
            e.preventDefault()
            if (coverAllId) assignAll.mutate({ path: { id }, body: { staffId: coverAllId } })
          }}
        >
          <div>
            <h2 className="text-sm font-semibold text-gray-900">Samme vikar på alle lektioner</h2>
            <p className="text-xs text-gray-600">
              {uncovered.length} lektioner mangler vikar. Vælg én person, så sættes de på alle dem,
              de er ledige til.
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-3">
            <Select
              label="Vikar til alle lektioner"
              hideLabel
              options={[
                {
                  value: '',
                  label: allCandidates.length === 0 ? 'Ingen ledige' : 'Vælg vikar',
                },
                ...allCandidates.map(({ staff, free }) => ({
                  value: staff.id,
                  label: `${staff.name}${staff.role === 'Substitute' ? ' (vikar)' : ''}${
                    free === uncovered.length ? '' : ` – ledig til ${free} af ${uncovered.length}`
                  }`,
                })),
              ]}
              value={coverAllId}
              disabled={assignAll.isPending || allCandidates.length === 0}
              onChange={setCoverAllId}
              testId="cover-all-candidates"
              className="max-w-full"
            />
            <button
              type="submit"
              disabled={!coverAllId || assignAll.isPending}
              data-testid="cover-all-submit"
              className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {assignAll.isPending ? 'Sætter på…' : 'Sæt på alle'}
            </button>
          </div>
        </form>
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
                  <Select
                    label="Vælg vikar"
                    hideLabel
                    options={[
                      {
                        value: '',
                        label: (l.candidates ?? []).length === 0 ? 'Ingen ledige' : 'Vælg vikar',
                      },
                      ...(l.candidates ?? []).map((c) => ({
                        value: c.id,
                        label: c.role === 'Substitute' ? `${c.name} (vikar)` : c.name,
                      })),
                    ]}
                    value={''}
                    disabled={assign.isPending}
                    onChange={(value) => value && setSubstitute(l, value)}
                    testId={`cover-candidates-${l.schemaSlotId}-${l.date}`}
                    className="max-w-full"
                  />
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
