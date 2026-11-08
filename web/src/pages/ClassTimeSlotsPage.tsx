import { useState, useEffect, useMemo } from 'react'
import { useParams, Link, useNavigate } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1ClassesOptions,
  getApiV1ClassesByClassIdTimeSlotsOptions,
  getApiV1ClassesByClassIdTimeSlotsQueryKey,
  putApiV1ClassesByClassIdTimeSlotsMutation,
  getApiV1ClassesByClassIdSchemasBySchemaIdTimeSlotsOptions,
  getApiV1ClassesByClassIdSchemasBySchemaIdTimeSlotsQueryKey,
  putApiV1ClassesByClassIdSchemasBySchemaIdTimeSlotsMutation,
} from '../api/generated/@tanstack/react-query.gen'
import type { TimeSlotDto, ClassDto } from '../api/client'
import { usePageTitle } from '../hooks/usePageTitle'
import { TimeInput } from '../components/TimeInput'
import { LessonDurationSlider } from '../components/LessonDurationSlider'

interface BreakEntry {
  startTime: string
  durationMinutes: number
}

// Generate time slot rows from template params (same logic as backend)
function generateSlots(
  dayStart: string,
  dayEnd: string,
  lessonDuration: number,
  breaks: BreakEntry[]
): Omit<TimeSlotDto, 'id' | 'classId'>[] {
  const slots: Omit<TimeSlotDto, 'id' | 'classId'>[] = []
  const toMinutes = (t: string) => {
    const [h, m] = t.split(':').map(Number)
    return h * 60 + m
  }
  const toTime = (m: number) =>
    `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}:00`

  const endMin = toMinutes(dayEnd)
  const sortedBreaks = [...breaks].sort((a, b) => toMinutes(a.startTime) - toMinutes(b.startTime))
  let current = toMinutes(dayStart)
  let sortOrder = 1

  while (current < endMin) {
    const breakHere = sortedBreaks.find((b) => toMinutes(b.startTime) === current)
    if (breakHere) {
      slots.push({
        sortOrder: sortOrder++,
        startTime: toTime(current),
        endTime: toTime(current + breakHere.durationMinutes),
        label: 'Pause',
        isBreak: true,
      })
      current += breakHere.durationMinutes
      continue
    }
    const lessonEnd = current + lessonDuration
    if (lessonEnd > endMin) break
    slots.push({
      sortOrder: sortOrder++,
      startTime: toTime(current),
      endTime: toTime(lessonEnd),
      label: null,
      isBreak: false,
    })
    current = lessonEnd
  }

  return slots
}

function validateForm(
  dayStart: string,
  dayEnd: string,
  lessonDuration: number,
  breaks: BreakEntry[]
): string | null {
  if (dayStart >= dayEnd) return 'Skoledagen skal slutte efter den starter.'
  if (lessonDuration <= 0) return 'Lektionslængde skal være større end 0.'
  const toMin = (t: string) => {
    const [h, m] = t.split(':').map(Number)
    return h * 60 + m
  }
  const fmt = (m: number) =>
    `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`
  const dayStartMinutes = toMin(dayStart)
  const sortedBreaks = [...breaks].sort((a, b) => toMin(a.startTime) - toMin(b.startTime))

  for (const b of sortedBreaks) {
    const breakStart = toMin(b.startTime)
    if (breakStart < dayStartMinutes) return `Pausen kl. ${b.startTime} starter før skoledagen.`

    // Walk the timeline from dayStart, advancing by lessonDuration and break durations,
    // to find whether this break lands exactly on a module boundary.
    let cursor = dayStartMinutes
    let moduleNumber = 1
    while (cursor < breakStart) {
      // Check if a previous break starts exactly here
      const prevBreak = sortedBreaks.find((pb) => toMin(pb.startTime) === cursor && pb !== b)
      if (prevBreak) {
        cursor += prevBreak.durationMinutes
        continue
      }
      const nextBoundary = cursor + lessonDuration
      if (nextBoundary > breakStart) {
        // Break falls mid-module
        return `Pausen kl. ${b.startTime} falder midt i modul ${moduleNumber} (${fmt(cursor)}–${fmt(cursor + lessonDuration)}). Pauser skal starte præcis ved en lektionsovergang.`
      }
      cursor = nextBoundary
      if (cursor < breakStart) moduleNumber++
    }
  }
  return null
}

export default function ClassTimeSlotsPage() {
  usePageTitle('Lektionsstruktur')
  const { classId, schemaId } = useParams<{ classId: string; schemaId?: string }>()
  const navigate = useNavigate()
  const qc = useQueryClient()

  const { data: cls } = useQuery({
    ...getApiV1ClassesOptions(),
    select: (all) => (all ?? []).filter((c) => c.id === classId) as ClassDto[],
  })
  const className = cls?.[0]?.name

  const timeSlotsKey = schemaId
    ? getApiV1ClassesByClassIdSchemasBySchemaIdTimeSlotsQueryKey({
        path: { classId: classId!, schemaId },
      })
    : getApiV1ClassesByClassIdTimeSlotsQueryKey({ path: { classId: classId! } })

  const { data: rawClassTimeSlots, isLoading: isLoadingClass } = useQuery({
    ...getApiV1ClassesByClassIdTimeSlotsOptions({ path: { classId: classId! } }),
    enabled: !!classId && !schemaId,
  })
  const { data: rawSchemaTimeSlots, isLoading: isLoadingSchema } = useQuery({
    ...getApiV1ClassesByClassIdSchemasBySchemaIdTimeSlotsOptions({
      path: { classId: classId!, schemaId: schemaId! },
    }),
    enabled: !!classId && !!schemaId,
  })
  const isLoading = schemaId ? isLoadingSchema : isLoadingClass
  const timeSlots = useMemo(
    () => ((schemaId ? rawSchemaTimeSlots : rawClassTimeSlots) ?? []) as TimeSlotDto[],
    [schemaId, rawSchemaTimeSlots, rawClassTimeSlots]
  )

  const isSchemaCustom = timeSlots?.some((s) => s.classId != null)
  const isCustom = schemaId ? isSchemaCustom : isSchemaCustom

  const resetMutation = useMutation({
    mutationFn: () => {
      if (schemaId) {
        const { mutationFn } = putApiV1ClassesByClassIdSchemasBySchemaIdTimeSlotsMutation()
        return mutationFn!({ path: { classId: classId!, schemaId }, body: [] }, undefined as never)
      }
      const { mutationFn } = putApiV1ClassesByClassIdTimeSlotsMutation()
      return mutationFn!({ path: { classId: classId! }, body: [] }, undefined as never)
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: timeSlotsKey }),
  })

  // ─── Edit form state (schema-scoped only) ──────────────────────────────────
  const [lessonDuration, setLessonDuration] = useState(45)
  const [dayStart, setDayStart] = useState('08:00')
  const [dayEnd, setDayEnd] = useState('15:00')
  const [breaks, setBreaks] = useState<BreakEntry[]>([])
  const [initialized, setInitialized] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [saveSuccess, setSaveSuccess] = useState(false)

  // Seed the edit form from current time slots on first load
  useEffect(() => {
    if (!schemaId || !timeSlots || initialized) return
    if (timeSlots.length === 0) {
      setInitialized(true)
      return
    }

    const lessons = timeSlots.filter((s) => !s.isBreak)
    const bks = timeSlots.filter((s) => s.isBreak)

    if (lessons.length > 0) {
      setDayStart(lessons[0].startTime?.slice(0, 5) ?? '08:00')
      const last = lessons[lessons.length - 1]
      setDayEnd(last.endTime?.slice(0, 5) ?? '15:00')
      // Derive lesson duration from first slot
      const [sh, sm] = (lessons[0].startTime?.slice(0, 5) ?? '08:00').split(':').map(Number)
      const [eh, em] = (lessons[0].endTime?.slice(0, 5) ?? '08:45').split(':').map(Number)
      setLessonDuration(eh * 60 + em - (sh * 60 + sm))
    }

    setBreaks(
      bks.map((b) => ({
        startTime: b.startTime?.slice(0, 5) ?? '10:00',
        durationMinutes: (() => {
          const [sh2, sm2] = (b.startTime?.slice(0, 5) ?? '10:00').split(':').map(Number)
          const [eh2, em2] = (b.endTime?.slice(0, 5) ?? '10:15').split(':').map(Number)
          return eh2 * 60 + em2 - (sh2 * 60 + sm2)
        })(),
      }))
    )
    setInitialized(true)
  }, [schemaId, timeSlots, initialized])

  const saveMutation = useMutation({
    ...putApiV1ClassesByClassIdSchemasBySchemaIdTimeSlotsMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: timeSlotsKey })
      setSaveError(null)
      setSaveSuccess(true)
      setTimeout(() => setSaveSuccess(false), 3000)
    },
    onError: () => {
      setSaveError('Kunne ikke gemme lektionsstruktur. Prøv igen.')
      setSaveSuccess(false)
    },
  })

  const lessonSlots = timeSlots?.filter((s) => !s.isBreak) ?? []
  const breakSlots = timeSlots?.filter((s) => s.isBreak) ?? []

  function handleSave() {
    const err = validateForm(dayStart, dayEnd, lessonDuration, breaks)
    if (err) {
      setSaveError(err)
      return
    }
    const slots = generateSlots(dayStart, dayEnd, lessonDuration, breaks).map((s, i) => ({
      sortOrder: i + 1,
      startTime: s.startTime,
      endTime: s.endTime,
      label: s.label ?? null,
      isBreak: s.isBreak,
    }))
    saveMutation.mutate({ path: { classId: classId!, schemaId: schemaId! }, body: slots })
  }

  return (
    <div className="flex flex-col h-full">
      <div className="shrink-0 bg-white border-b border-gray-200 px-4 lg:px-6 py-3 flex items-center justify-between gap-2">
        <div className="flex items-center gap-3 min-w-0">
          <button
            onClick={() =>
              schemaId ? navigate(`/klasser/${classId}/skema/${schemaId}`) : navigate(-1)
            }
            className="text-gray-400 hover:text-gray-600 transition-colors shrink-0"
            aria-label="Tilbage"
          >
            <svg
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
            >
              <polyline points="15 18 9 12 15 6" />
            </svg>
          </button>
          <h1 className="font-display text-base font-semibold text-gray-900 truncate">
            Lektionsstruktur{className ? ` — ${className}` : ''}
          </h1>
          {isCustom && !schemaId && (
            <span className="shrink-0 px-2 py-0.5 text-xs font-medium rounded-full bg-amber-100 text-amber-700">
              Tilpasset
            </span>
          )}
        </div>
        {isCustom && !schemaId && (
          <button
            onClick={() => resetMutation.mutate()}
            disabled={resetMutation.isPending}
            className="px-3 py-1.5 text-xs font-medium text-gray-600 border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50 transition-colors"
          >
            {resetMutation.isPending ? 'Nulstiller...' : 'Nulstil til skolens standard'}
          </button>
        )}
      </div>

      <div className="flex-1 overflow-auto p-4 lg:p-6">
        {isLoading ? (
          <div className="animate-pulse space-y-2 max-w-sm">
            {Array.from({ length: 8 }).map((_, i) => (
              <div key={i} className="h-10 bg-gray-100 rounded-lg" />
            ))}
          </div>
        ) : schemaId ? (
          /* ── Schema-scoped edit form ── */
          <div className="max-w-sm space-y-5">
            <p className="text-sm text-gray-500">
              Tilpas lektionsstrukturen for dette skema. Ændringer påvirker kun dette skema.
            </p>

            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Skoledag starter
                </label>
                <TimeInput label="Skoledag starter" value={dayStart} onChange={setDayStart} />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Skoledag slutter
                </label>
                <TimeInput label="Skoledag slutter" value={dayEnd} onChange={setDayEnd} />
              </div>
            </div>

            <LessonDurationSlider
              value={lessonDuration}
              onChange={setLessonDuration}
              data-testid="schema-lesson-duration"
            />

            <div>
              <div className="flex items-center justify-between mb-2">
                <label className="text-sm font-medium text-gray-700">Pauser</label>
                <button
                  onClick={() =>
                    setBreaks((prev) => [...prev, { startTime: '10:00', durationMinutes: 15 }])
                  }
                  className="flex items-center gap-1 text-sm text-brand-600 hover:text-brand-700"
                  data-testid="schema-add-break"
                >
                  <svg
                    width="13"
                    height="13"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2.5"
                  >
                    <line x1="12" y1="5" x2="12" y2="19" />
                    <line x1="5" y1="12" x2="19" y2="12" />
                  </svg>
                  Tilføj pause
                </button>
              </div>
              {breaks.length === 0 && (
                <p className="text-sm text-gray-400 italic">Ingen faste pauser.</p>
              )}
              <div className="space-y-2">
                {breaks.map((b, i) => (
                  <div key={i} className="flex items-end gap-2">
                    <div className="flex-1 grid grid-cols-2 gap-2">
                      <div>
                        <label className="block text-xs text-gray-500 mb-0.5">Starttidspunkt</label>
                        <TimeInput
                          label={`Pause ${i + 1}, starttidspunkt`}
                          value={b.startTime}
                          onChange={(v) =>
                            setBreaks((prev) =>
                              prev.map((x, idx) => (idx === i ? { ...x, startTime: v } : x))
                            )
                          }
                        />
                      </div>
                      <div>
                        <label className="block text-xs text-gray-500 mb-0.5">Varighed (min)</label>
                        <input
                          type="number"
                          min={5}
                          max={60}
                          value={b.durationMinutes}
                          aria-label={`Pause ${i + 1}, varighed (min)`}
                          onFocus={(e) => e.target.select()}
                          onChange={(e) =>
                            setBreaks((prev) =>
                              prev.map((x, idx) =>
                                idx === i ? { ...x, durationMinutes: Number(e.target.value) } : x
                              )
                            )
                          }
                          className="w-full px-2 py-1.5 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
                        />
                      </div>
                    </div>
                    <button
                      onClick={() => setBreaks((prev) => prev.filter((_, idx) => idx !== i))}
                      aria-label={`Fjern pause ${i + 1}`}
                      className="p-1.5 text-gray-400 hover:text-red-500 rounded-md hover:bg-red-50 transition-colors mb-0.5"
                    >
                      <svg
                        width="14"
                        height="14"
                        viewBox="0 0 24 24"
                        fill="none"
                        stroke="currentColor"
                        strokeWidth="2"
                      >
                        <line x1="18" y1="6" x2="6" y2="18" />
                        <line x1="6" y1="6" x2="18" y2="18" />
                      </svg>
                    </button>
                  </div>
                ))}
              </div>
            </div>

            {saveError && <p className="text-sm text-red-600">{saveError}</p>}
            {saveSuccess && <p className="text-sm text-green-600">Lektionsstruktur gemt.</p>}

            <div className="flex items-start gap-3 rounded-lg border border-amber-300 bg-amber-50 px-4 py-3">
              <svg
                className="mt-0.5 shrink-0 text-amber-500"
                width="16"
                height="16"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2.5"
              >
                <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
                <line x1="12" y1="9" x2="12" y2="13" />
                <line x1="12" y1="17" x2="12.01" y2="17" />
              </svg>
              <p className="text-sm text-amber-800">
                <span className="font-semibold">Advarsel:</span> Ændringer i lektionsstrukturen
                sletter alle eksisterende skemaindhold for dette skema. Dette kan ikke fortrydes.
              </p>
            </div>

            <div className="flex items-center gap-3 pt-1">
              <button
                onClick={() => {
                  if (
                    !window.confirm(
                      'Er du sikker? Ændringer i lektionsstrukturen sletter alle eksisterende skemaindhold for dette skema. Dette kan ikke fortrydes.'
                    )
                  )
                    return
                  handleSave()
                }}
                disabled={saveMutation.isPending}
                className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                data-testid="schema-save-timeslots"
              >
                {saveMutation.isPending ? 'Gemmer...' : 'Gem lektionsstruktur'}
              </button>
              {timeSlots && timeSlots.length > 0 && (
                <button
                  onClick={() => resetMutation.mutate()}
                  disabled={resetMutation.isPending}
                  className="px-3 py-2 text-sm text-gray-500 hover:text-gray-700 transition-colors"
                >
                  {resetMutation.isPending ? 'Nulstiller...' : 'Nulstil til klassens standard'}
                </button>
              )}
            </div>

            {/* Preview */}
            {timeSlots && timeSlots.length > 0 && (
              <div className="pt-2 border-t border-gray-100">
                <p className="text-xs font-medium text-gray-500 mb-2 uppercase tracking-wide">
                  Nuværende struktur
                </p>
                <div className="space-y-1">
                  {timeSlots.map((slot, idx) =>
                    slot.isBreak ? (
                      <div
                        key={slot.id ?? idx}
                        className="flex items-center gap-3 px-3 py-1.5 bg-gray-50 border border-dashed border-gray-200 rounded-lg"
                      >
                        <span className="text-xs text-gray-400 tabular-nums">
                          {slot.startTime?.slice(0, 5)}–{slot.endTime?.slice(0, 5)}
                        </span>
                        <span className="text-xs text-gray-400">Pause</span>
                      </div>
                    ) : (
                      <div
                        key={slot.id ?? idx}
                        className="flex items-center gap-3 px-3 py-1.5 bg-white border border-gray-200 rounded-lg"
                      >
                        <span className="text-xs font-medium text-gray-400 w-5 text-right shrink-0 tabular-nums">
                          {lessonSlots.findIndex((s) => s.id === slot.id) + 1}.
                        </span>
                        <span className="text-sm tabular-nums text-gray-700">
                          {slot.startTime?.slice(0, 5)}–{slot.endTime?.slice(0, 5)}
                        </span>
                      </div>
                    )
                  )}
                  <p className="text-xs text-gray-400 pt-1">
                    {lessonSlots.length} lektioner · {breakSlots.length} pause
                    {breakSlots.length !== 1 ? 'r' : ''}
                  </p>
                </div>
              </div>
            )}
          </div>
        ) : (
          /* ── Class-level read-only view ── */
          <div className="max-w-sm space-y-1.5">
            {!isCustom && (
              <p className="text-sm text-gray-500 mb-4">
                Denne klasse bruger skolens standard lektionsstruktur. Lektionsstrukturen redigeres
                under{' '}
                <Link to="/indstillinger" className="text-brand-600 hover:underline">
                  Skoleindstillinger
                </Link>
                .
              </p>
            )}

            {(timeSlots ?? []).length === 0 && (
              <div className="py-10 text-center">
                <p className="text-sm text-gray-500">Ingen lektionsstruktur defineret endnu.</p>
                <Link
                  to="/indstillinger"
                  className="inline-block mt-3 px-4 py-2 text-sm font-medium bg-brand-600 text-white rounded-lg hover:bg-brand-700 transition-colors"
                >
                  Opsæt skoledagen
                </Link>
              </div>
            )}

            {(timeSlots ?? []).map((slot, idx) =>
              slot.isBreak ? (
                <div
                  key={slot.id ?? idx}
                  className="flex items-center gap-3 px-3 py-2 bg-gray-50 border border-dashed border-gray-200 rounded-lg"
                >
                  <svg
                    width="13"
                    height="13"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                    className="text-gray-400 shrink-0"
                  >
                    <path d="M18 8h1a4 4 0 0 1 0 8h-1" />
                    <path d="M2 8h16v9a4 4 0 0 1-4 4H6a4 4 0 0 1-4-4V8z" />
                    <line x1="6" y1="1" x2="6" y2="4" />
                    <line x1="10" y1="1" x2="10" y2="4" />
                    <line x1="14" y1="1" x2="14" y2="4" />
                  </svg>
                  <span className="text-xs text-gray-500 tabular-nums">
                    {slot.startTime?.slice(0, 5)}–{slot.endTime?.slice(0, 5)}
                  </span>
                  <span className="text-xs text-gray-400">Pause</span>
                </div>
              ) : (
                <div
                  key={slot.id ?? idx}
                  className="flex items-center gap-3 px-3 py-2.5 bg-white border border-gray-200 rounded-lg"
                >
                  <span className="text-xs font-medium text-gray-400 w-5 text-right shrink-0 tabular-nums">
                    {lessonSlots.findIndex((s) => s.id === slot.id) + 1}.
                  </span>
                  <span className="text-sm tabular-nums text-gray-700">
                    {slot.startTime?.slice(0, 5)}–{slot.endTime?.slice(0, 5)}
                  </span>
                  {slot.label && <span className="text-xs text-gray-400">{slot.label}</span>}
                </div>
              )
            )}

            {(timeSlots ?? []).length > 0 && (
              <p className="text-xs text-gray-400 pt-2">
                {lessonSlots.length} lektioner · {breakSlots.length} pause
                {breakSlots.length !== 1 ? 'r' : ''}
              </p>
            )}

            {resetMutation.isError && (
              <p className="text-sm text-red-600 mt-2">Der opstod en fejl. Prøv igen.</p>
            )}
          </div>
        )}
      </div>
    </div>
  )
}
