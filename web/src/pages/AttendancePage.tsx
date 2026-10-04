import { useEffect, useMemo, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { usePageTitle } from '../hooks/usePageTitle'
import { useAuth } from '../auth/useAuth'
import {
  getApiV1AttendanceClassesByClassIdOptions,
  putApiV1AttendanceClassesByClassIdMutation,
} from '../api/generated/@tanstack/react-query.gen'
import type {
  AbsenceCategory,
  AttendanceCheckDto,
  AttendanceCheckpoint,
  StudentAttendanceDto,
} from '../api/generated/types.gen'
import {
  CATEGORY_BADGE,
  CATEGORY_LABEL,
  LEAVE_STATUS_LABEL,
  addDaysIso,
  capitalizeFirst,
  formatLongDate,
  todayIso,
} from '../lib/absence'
import { problemDetail } from '../lib/problem'

/** The two categories staff can pick. Ekstraordinær frihed only comes from an approved parent request. */
const STAFF_CATEGORIES: AbsenceCategory[] = ['Unauthorized', 'Illness']

type Selection = Map<string, AbsenceCategory>

function checkLabel(check: AttendanceCheckDto | null | undefined): string {
  if (!check) return 'Ikke noteret endnu'
  const time = new Date(check.takenAt).toLocaleTimeString('da-DK', {
    hour: '2-digit',
    minute: '2-digit',
  })
  return check.takenByName ? `Noteret kl. ${time} af ${check.takenByName}` : `Noteret kl. ${time}`
}

/** A parent's report that staff can't change from here, with the label shown on the row. */
function lockedLabel(s: StudentAttendanceDto): string | null {
  const m = s.morning
  if (m?.source !== 'Parent') return null
  if (m.category === 'ExtraordinaryLeave') {
    return m.leaveStatus === 'Pending' ? 'Fri — afventer godkendelse' : 'Fri — godkendt af skolen'
  }
  return 'Syg — meldt af forælder'
}

export default function AttendancePage() {
  usePageTitle('Fremmøde')
  const { classId = '' } = useParams()
  const [searchParams, setSearchParams] = useSearchParams()
  const today = todayIso()
  const date = searchParams.get('dato') ?? today
  const [checkpoint, setCheckpoint] = useState<AttendanceCheckpoint>(
    searchParams.get('trin') === 'slut' ? 'EndOfDay' : 'StartOfDay'
  )
  const [selection, setSelection] = useState<Selection>(new Map())
  // Set on the first tap. A background refetch must never wipe taps that aren't saved yet.
  const [dirty, setDirty] = useState(false)
  const [saved, setSaved] = useState(false)
  const qc = useQueryClient()
  // The superadmin "Vis som" toolbar floats over the bottom of the screen.
  const { isSuperAdmin } = useAuth()

  const options = getApiV1AttendanceClassesByClassIdOptions({ path: { classId }, query: { date } })
  const { data, isLoading, isError } = useQuery(options)

  // Seed the selection from what is saved, unless the user has started tapping. After a save the
  // selection already matches what was saved, so it stays dirty until the class, date or step changes.
  useEffect(() => {
    if (!data || dirty) return
    const next: Selection = new Map()
    for (const s of data.students ?? []) {
      const mark = checkpoint === 'StartOfDay' ? s.morning : s.endOfDay
      if (mark && mark.source === 'Staff') next.set(s.studentId, mark.category)
    }
    setSelection(next)
  }, [data, checkpoint, dirty])

  // Not keyed on data: the refetch after a save must not hide the confirmation.
  useEffect(() => {
    setSaved(false)
    setDirty(false)
  }, [classId, date, checkpoint])

  const save = useMutation({
    ...putApiV1AttendanceClassesByClassIdMutation(),
    onSuccess: () => {
      setSaved(true)
      qc.invalidateQueries({ queryKey: options.queryKey })
      qc.invalidateQueries({ queryKey: [{ _id: 'getApiV1AttendanceMinePending' }] })
      qc.invalidateQueries({ queryKey: [{ _id: 'getApiV1AttendanceOverview' }] })
    },
  })

  const students = data?.students ?? []
  const check = checkpoint === 'StartOfDay' ? data?.startOfDay : data?.endOfDay
  const readOnly = !data?.canEdit || !data?.isSchoolDay

  const absentCount = useMemo(() => {
    if (checkpoint === 'StartOfDay') {
      return students.filter((s) => selection.has(s.studentId) || lockedLabel(s) !== null).length
    }
    return selection.size
  }, [students, selection, checkpoint])

  function toggle(studentId: string) {
    if (readOnly) return
    setSaved(false)
    setDirty(true)
    setSelection((prev) => {
      const next = new Map(prev)
      if (next.has(studentId)) next.delete(studentId)
      else next.set(studentId, 'Unauthorized')
      return next
    })
  }

  function setCategory(studentId: string, category: AbsenceCategory) {
    setSaved(false)
    setDirty(true)
    setSelection((prev) => new Map(prev).set(studentId, category))
  }

  function goToDate(next: string) {
    const params = new URLSearchParams(searchParams)
    params.set('dato', next)
    setSearchParams(params, { replace: true })
  }

  function submit() {
    save.mutate({
      path: { classId },
      query: { date },
      body: {
        checkpoint,
        absent: [...selection].map(([studentId, category]) => ({ studentId, category })),
      },
    })
  }

  if (isLoading) {
    return (
      <div className="flex justify-center py-16">
        <div className="w-6 h-6 border-2 border-brand-600 border-t-transparent rounded-full animate-spin" />
      </div>
    )
  }

  if (isError || !data) {
    return (
      <div className="max-w-lg mx-auto px-4 py-8">
        <p className="text-sm text-red-600">
          Fremmødet kunne ikke hentes. Du har måske ikke adgang til klassen.
        </p>
        <Link to="/fravaer" className="text-sm text-brand-700 underline mt-2 inline-block">
          Tilbage til fravær
        </Link>
      </div>
    )
  }

  return (
    <div className="max-w-lg mx-auto px-4 pt-4">
      <Link to="/fravaer" className="text-sm text-gray-500 hover:text-gray-800">
        ← Fravær
      </Link>
      <h1 className="font-display text-2xl font-semibold text-gray-900 mt-1">
        Fremmøde · {data.className}
      </h1>

      <div className="flex items-center justify-between mt-3 bg-white border border-gray-200 rounded-xl px-2 py-1.5">
        <button
          type="button"
          onClick={() => goToDate(addDaysIso(date, -1))}
          className="w-11 h-11 flex items-center justify-center rounded-lg text-gray-600 hover:bg-gray-100"
          aria-label="Forrige dag"
          data-testid="attendance-prev-day"
        >
          ‹
        </button>
        <span className="text-sm font-medium text-gray-900" data-testid="attendance-date">
          {date === today
            ? `I dag · ${formatLongDate(date)}`
            : capitalizeFirst(formatLongDate(date))}
        </span>
        <button
          type="button"
          onClick={() => goToDate(addDaysIso(date, 1))}
          disabled={date >= today}
          className="w-11 h-11 flex items-center justify-center rounded-lg text-gray-600 hover:bg-gray-100 disabled:opacity-30"
          aria-label="Næste dag"
          data-testid="attendance-next-day"
        >
          ›
        </button>
      </div>

      {data.requiresEndOfDay && (
        <div className="grid grid-cols-2 gap-1 mt-3 bg-gray-100 rounded-xl p-1">
          {(['StartOfDay', 'EndOfDay'] as const).map((cp) => (
            <button
              key={cp}
              type="button"
              onClick={() => setCheckpoint(cp)}
              data-testid={`attendance-checkpoint-${cp}`}
              className={`h-11 rounded-lg text-sm font-medium transition-colors ${
                checkpoint === cp ? 'bg-white text-gray-900 shadow-sm' : 'text-gray-600'
              }`}
            >
              {cp === 'StartOfDay' ? 'Morgen' : 'Dagens slutning'}
            </button>
          ))}
        </div>
      )}

      <p className="text-xs text-gray-500 mt-3" data-testid="attendance-check-status">
        {checkLabel(check)}
      </p>

      {!data.isSchoolDay && (
        <p className="mt-3 p-3 rounded-lg bg-blue-50 text-sm text-blue-800">
          Der er ikke skole denne dag.
        </p>
      )}
      {data.isSchoolDay && !data.canEdit && (
        <p className="mt-3 p-3 rounded-lg bg-gray-50 text-sm text-gray-700">
          Fremmødet kan kun ses. Kvartalet er afsluttet, eller dagen er ikke kommet endnu.
        </p>
      )}

      {!readOnly && (
        <p className="text-sm text-gray-600 mt-4 mb-2">
          {checkpoint === 'StartOfDay'
            ? 'Tryk på de elever, der ikke er her.'
            : 'Tryk på de elever, der er gået i løbet af dagen.'}
        </p>
      )}

      {/* Nothing to note on a non-school day, so the list would only invite tapping. */}
      <ul
        className={`space-y-2 ${readOnly ? 'mt-4' : ''} ${data.isSchoolDay ? '' : 'hidden'}`}
        data-testid="attendance-student-list"
      >
        {students.map((s) => {
          const locked = checkpoint === 'StartOfDay' ? lockedLabel(s) : null
          const absentAllDay =
            checkpoint === 'EndOfDay' && (s.morning != null || lockedLabel(s) !== null)
          const selected = selection.get(s.studentId)
          const isAbsent = selected !== undefined || locked !== null

          if (absentAllDay) {
            return (
              <li
                key={s.studentId}
                className="flex items-center gap-3 min-h-14 px-3 py-2 rounded-xl bg-gray-50 text-gray-400"
                data-testid={`attendance-student-${s.studentId}`}
              >
                <Avatar name={s.name} url={s.avatarUrl} />
                <span className="text-base">{s.name}</span>
                <span className="ml-auto text-xs">Fraværende fra morgen</span>
              </li>
            )
          }

          return (
            <li
              key={s.studentId}
              className={`rounded-xl border transition-colors ${
                isAbsent ? 'border-orange-300 bg-orange-50' : 'border-gray-200 bg-white'
              }`}
            >
              <button
                type="button"
                onClick={() => !locked && toggle(s.studentId)}
                disabled={readOnly || locked !== null}
                aria-pressed={isAbsent}
                data-testid={`attendance-student-${s.studentId}`}
                className="w-full flex items-center gap-3 min-h-14 px-3 py-2 text-left disabled:cursor-default"
              >
                <Avatar name={s.name} url={s.avatarUrl} />
                <span className="text-base text-gray-900">{s.name}</span>
                <span className="ml-auto text-sm font-medium">
                  {locked ? (
                    <span className="text-xs text-gray-600">{locked}</span>
                  ) : isAbsent ? (
                    <span className="text-orange-800">
                      {checkpoint === 'StartOfDay' ? 'Fraværende' : 'Gået'}
                    </span>
                  ) : (
                    <span className="text-gray-400">Her</span>
                  )}
                </span>
              </button>
              {s.parentReport && checkpoint === 'StartOfDay' && (
                <p className="px-3 pb-2 -mt-1 text-xs text-gray-600">
                  Forælder:{' '}
                  {s.parentReport.category === 'ExtraordinaryLeave'
                    ? `Fri — ${LEAVE_STATUS_LABEL[s.parentReport.leaveStatus ?? 'Pending'].toLowerCase()}`
                    : CATEGORY_LABEL[s.parentReport.category]}
                </p>
              )}
              {selected && !readOnly && (
                <div className="flex gap-2 px-3 pb-3">
                  {STAFF_CATEGORIES.map((c) => (
                    <button
                      key={c}
                      type="button"
                      onClick={() => setCategory(s.studentId, c)}
                      data-testid={`attendance-category-${s.studentId}-${c}`}
                      className={`flex-1 h-10 rounded-lg text-sm font-medium border ${
                        selected === c
                          ? `${CATEGORY_BADGE[c]} border-transparent`
                          : 'bg-white border-gray-200 text-gray-600'
                      }`}
                    >
                      {c === 'Illness' ? 'Syg (forælder har ringet)' : 'Ulovligt'}
                    </button>
                  ))}
                </div>
              )}
            </li>
          )
        })}
      </ul>

      {students.length === 0 && data.isSchoolDay && (
        <p className="text-sm text-gray-500 py-6">Der er ingen elever i klassen.</p>
      )}

      {!readOnly && (
        <div
          className={`sticky bottom-0 -mx-4 mt-6 bg-white border-t border-gray-200 px-4 pt-3 ${
            isSuperAdmin ? 'pb-16' : 'pb-3'
          }`}
        >
          <div>
            {save.isError && (
              <p className="text-sm text-red-600 mb-2">
                {problemDetail(save.error) ?? 'Fremmødet kunne ikke gemmes. Prøv igen.'}
              </p>
            )}
            {saved && !save.isPending && (
              <p className="text-sm text-green-700 mb-2" data-testid="attendance-saved">
                Fremmøde gemt.
              </p>
            )}
            <div className="flex gap-2">
              <button
                type="button"
                onClick={() => {
                  setSaved(false)
                  setDirty(true)
                  setSelection(new Map())
                }}
                data-testid="attendance-all-present"
                className="h-12 px-4 rounded-xl border border-gray-300 text-sm font-medium text-gray-700"
              >
                {checkpoint === 'StartOfDay' ? 'Alle er her' : 'Ingen er gået'}
              </button>
              <button
                type="button"
                onClick={submit}
                disabled={save.isPending}
                data-testid="attendance-save"
                className="flex-1 h-12 rounded-xl bg-brand-600 text-white text-sm font-semibold hover:bg-brand-700 disabled:opacity-50"
              >
                {save.isPending
                  ? 'Gemmer…'
                  : `Gem fremmøde${absentCount > 0 ? ` · ${absentCount} ${checkpoint === 'StartOfDay' ? 'fraværende' : 'gået'}` : ''}`}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

function Avatar({ name, url }: { name: string; url?: string | null }) {
  if (url) {
    return <img src={url} alt="" className="w-9 h-9 rounded-full object-cover shrink-0" />
  }
  return (
    <span className="w-9 h-9 rounded-full bg-gray-100 text-gray-600 text-sm font-medium flex items-center justify-center shrink-0">
      {name.charAt(0)}
    </span>
  )
}
