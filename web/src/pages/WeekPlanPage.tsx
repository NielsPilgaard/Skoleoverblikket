import React, { useState, useRef, useEffect } from 'react'
import { MarkdownTextarea, type SaveStatus } from '../components/markdown/MarkdownTextarea'
import { LessonEditor } from '../components/weekplan/LessonEditor'
import { Markdown } from '../components/markdown/Markdown'
import { ParentWeekPlan } from '../components/weekplan/ParentWeekPlan'
import { preselectedDate } from '../utils/isoWeek'
import { useParams, useSearchParams, useNavigate } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1ClassesByClassIdWeekPlanOptions,
  getApiV1ClassesByClassIdWeekPlanQueryKey,
  putApiV1ClassesByClassIdWeekPlanNotesMutation,
  getApiV1CoursesOptions,
  getApiV1ClassesOptions,
} from '../api/generated/@tanstack/react-query.gen'
import type { ClassDto } from '../api/client'
import { usePageTitle } from '../hooks/usePageTitle'
import { AssignSubstitutePanel } from '../components/substitute/AssignSubstitutePanel'
import { useWeekNavigation } from '../hooks/useWeekNavigation'

// ─── Local types ─────────────────────────────────────────────────────────────

interface WeekPlanSlotFileDto {
  id: string
  schoolFileId: string
  fileName: string
  url: string
}

interface WeekPlanSlotDto {
  id: string
  schemaSlotId: string
  weekday: 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday'
  timeSlotId: string
  timeSlotLabel: string
  startTime: string
  endTime: string
  courseId: string
  courseName: string
  originalCourseId: string | null
  originalCourseName: string | null
  description: string | null
  lektier: string | null
  files: WeekPlanSlotFileDto[]
  substituteTeacherId: string | null
  substituteTeacherName: string | null
  substituteAideId: string | null
  substituteAideName: string | null
  weekPlanId: string
}

interface HolidayDayDto {
  weekday: 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday'
  title: string
}

interface BreakTimeSlotDto {
  timeSlotId: string
  timeSlotLabel: string
  startTime: string
  endTime: string
}

interface WeekPlanDto {
  id: string
  classId: string
  isoYear: number
  isoWeek: number
  weekStartDate: string
  weekEndDate: string
  isHolidayWeek: boolean
  holidayTitle: string | null
  holidayDays: HolidayDayDto[]
  breakSlots: BreakTimeSlotDto[]
  slots: WeekPlanSlotDto[]
  notes: string | null
  notesUpdatedBy: string | null
  notesUpdatedAt: string | null
}

interface CourseDto {
  id: string
  name: string
}

// ─── Course colors (matches SchemaBuilderPage) ────────────────────────────────

const COURSE_COLORS = [
  'bg-blue-100 text-blue-800 border-blue-200',
  'bg-purple-100 text-purple-800 border-purple-200',
  'bg-teal-100 text-teal-800 border-teal-200',
  'bg-orange-100 text-orange-800 border-orange-200',
  'bg-pink-100 text-pink-800 border-pink-200',
  'bg-indigo-100 text-indigo-800 border-indigo-200',
  'bg-cyan-100 text-cyan-800 border-cyan-200',
  'bg-yellow-100 text-yellow-800 border-yellow-200',
  'bg-lime-100 text-lime-800 border-lime-200',
  'bg-rose-100 text-rose-800 border-rose-200',
]

function getCourseColor(courseId: string, courseIds: string[]): string {
  const idx = courseIds.indexOf(courseId)
  return idx >= 0 ? COURSE_COLORS[idx % COURSE_COLORS.length] : COURSE_COLORS[0]
}

// ─── Constants ────────────────────────────────────────────────────────────────

import { WEEKDAY_LABELS as WEEKDAYS, WEEKDAY_KEYS } from '../lib/weekdays'
import { WeekStepper } from '../components/ui/select'

// ─── Generelt block ───────────────────────────────────────────────────────────

function NotesEditor({
  classId,
  isoYear,
  isoWeek,
  schemaId,
  value,
}: {
  classId: string
  isoYear: number
  isoWeek: number
  schemaId: string | null
  value: string | null
}) {
  const qc = useQueryClient()
  const [text, setText] = useState(value ?? '')
  const [saveStatus, setSaveStatus] = useState<SaveStatus>('idle')

  // True from the moment a blur queues a save until that save settles. While set
  // we stop syncing the incoming `value` into the textarea, so a refetch that
  // lands mid-save can't push a stale server value over what the user is typing.
  const pendingSaveRef = useRef(false)
  // Only advanced once a PUT has actually confirmed a value. If a save fails,
  // this keeps the last server-confirmed text so an unchanged retry still fires.
  const lastSavedRef = useRef(value ?? '')

  useEffect(() => {
    if (pendingSaveRef.current) return
    const incoming = value ?? ''
    if (incoming === lastSavedRef.current && text !== incoming) return
    lastSavedRef.current = incoming
    setText(incoming)
  }, [value, text])

  const weekPlanQueryKey = getApiV1ClassesByClassIdWeekPlanQueryKey({
    path: { classId },
    query: { isoYear, isoWeek, ...(schemaId ? { schemaId } : {}) },
  })

  // Serialize saves: each blur chains onto the previous save's promise so two
  // overlapping PUTs can never let an older response land after a newer one and
  // overwrite the newer `notes`. A monotonic counter identifies the newest
  // save so only its result updates cache/status.
  const saveChainRef = useRef<Promise<unknown>>(Promise.resolve())
  const saveSeqRef = useRef(0)

  // Was the value this save submitted still the current editor text when it
  // resolved? If the user has since typed on, an earlier save landing must not
  // overwrite the newer local edit or stamp its outcome onto it.
  const isCurrentEdit = (submitted: string | null | undefined) => (submitted ?? '') === (text || '')

  const { mutationFn } = putApiV1ClassesByClassIdWeekPlanNotesMutation()

  function handleChange(next: string) {
    setText(next)
    setSaveStatus('idle')
  }

  function handleBlur() {
    const normalized = text || null
    if (normalized === (lastSavedRef.current || null)) return

    const submitted = text
    const seq = ++saveSeqRef.current
    pendingSaveRef.current = true
    setSaveStatus('saving')

    saveChainRef.current = saveChainRef.current
      .catch(() => {})
      .then(async () => {
        // A newer blur superseded this one before it started — skip the request.
        if (seq !== saveSeqRef.current) return
        try {
          const result = await mutationFn!(
            {
              path: { classId },
              query: { isoYear, isoWeek, ...(schemaId ? { schemaId } : {}) },
              body: { notes: normalized },
            },
            undefined as never
          )
          lastSavedRef.current = submitted
          if (seq === saveSeqRef.current) {
            pendingSaveRef.current = false
            if (isCurrentEdit(normalized)) {
              qc.setQueryData(weekPlanQueryKey, (old: WeekPlanDto | undefined) =>
                old ? { ...old, notes: result.notes ?? null } : old
              )
              setSaveStatus('saved')
            }
          }
        } catch {
          if (seq === saveSeqRef.current) {
            pendingSaveRef.current = false
            if (isCurrentEdit(normalized)) {
              setSaveStatus('error')
            }
          }
        }
      })
  }

  return (
    <div className="shrink-0 px-4 lg:px-6 py-3 border-b border-gray-200">
      <label className="block text-sm font-medium text-gray-700 mb-1">Generelt for ugen</label>
      <MarkdownTextarea
        value={text}
        onChange={handleChange}
        onBlur={handleBlur}
        rows={5}
        maxLength={8000}
        placeholder="Ture, huskeliste, kommende temaer…"
        aria-label="Generelt for ugen"
        data-testid="notes-editor"
        saveStatus={saveStatus}
        className="w-full px-3 py-2 border border-gray-300 rounded-lg text-base focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent bg-white"
      />
    </div>
  )
}

// ─── Main page ────────────────────────────────────────────────────────────────

export default function WeekPlanPage() {
  usePageTitle('Ugeplan')
  const { classId } = useParams<{ classId: string }>()
  const [searchParams] = useSearchParams()
  const schemaId = searchParams.get('schemaId')
  const navigate = useNavigate()

  const { isoYear, isoWeek, stepper } = useWeekNavigation()
  const [editingSchemaSlotId, setEditingSchemaSlotId] = useState<string | null>(null)
  const [substituteSchemaSlotId, setSubstituteSchemaSlotId] = useState<string | null>(null)
  const [showParentPreview, setShowParentPreview] = useState(false)

  const { data: rawWeekPlanData, isLoading } = useQuery({
    ...getApiV1ClassesByClassIdWeekPlanOptions({
      path: { classId: classId! },
      query: { isoYear, isoWeek, ...(schemaId ? { schemaId } : {}) },
    }),
    enabled: !!classId,
  })
  const weekPlanData = rawWeekPlanData as WeekPlanDto | undefined
  const editingSlot =
    weekPlanData?.slots.find((s) => s.schemaSlotId === editingSchemaSlotId) ?? null

  const { data: rawCourses } = useQuery(getApiV1CoursesOptions())
  const courses = (rawCourses ?? []) as CourseDto[]

  // Collect all unique course IDs for color assignment
  const allCourseIds =
    weekPlanData?.slots.map((s) => s.courseId).filter((v, i, a) => a.indexOf(v) === i) ?? []

  // Build a combined sorted list of rows: regular lesson slots + break slots
  type GridRow =
    | {
        kind: 'slot'
        timeSlotId: string
        timeSlotLabel: string
        startTime: string
        endTime: string
      }
    | {
        kind: 'break'
        timeSlotId: string
        timeSlotLabel: string
        startTime: string
        endTime: string
      }

  const uniqueTimeSlots: GridRow[] = (() => {
    if (!weekPlanData) return []
    const lessonRows: GridRow[] = weekPlanData.slots
      .filter((s, i, a) => a.findIndex((x) => x.timeSlotId === s.timeSlotId) === i)
      .map((s) => ({
        kind: 'slot' as const,
        timeSlotId: s.timeSlotId,
        timeSlotLabel: s.timeSlotLabel,
        startTime: s.startTime,
        endTime: s.endTime,
      }))
    const breakRows: GridRow[] = (weekPlanData.breakSlots ?? []).map((b) => ({
      kind: 'break' as const,
      timeSlotId: b.timeSlotId,
      timeSlotLabel: b.timeSlotLabel,
      startTime: b.startTime,
      endTime: b.endTime,
    }))
    return [...lessonRows, ...breakRows].sort((a, b) => a.startTime.localeCompare(b.startTime))
  })()

  // Parse weekStartDate to compute column dates
  const weekStartDate = weekPlanData?.weekStartDate
    ? new Date(`${weekPlanData.weekStartDate}T00:00:00`)
    : null

  const { data: classData } = useQuery({
    ...getApiV1ClassesOptions(),
    select: (all) => (all ?? []).find((c) => c.id === classId) as ClassDto | undefined,
    enabled: !!classId,
  })

  const className = classData?.name ?? ''

  return (
    <div className="flex flex-col h-full">
      {/* Top bar */}
      <div className="shrink-0 sticky top-0 z-10 bg-white border-b border-gray-200 px-4 lg:px-6 py-2 flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2 min-w-0">
          <button
            onClick={() => navigate(-1)}
            className="text-gray-400 hover:text-gray-700 transition-colors shrink-0"
            aria-label="Tilbage"
          >
            <svg
              width="20"
              height="20"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
            >
              <polyline points="15 18 9 12 15 6" />
            </svg>
          </button>
          <h1 className="font-display text-base font-semibold text-gray-900 truncate">
            {className} · Ugeplan
          </h1>
        </div>
        <WeekStepper
          {...stepper}
          className="min-w-48 flex-1 max-w-xs order-last md:order-none basis-full md:basis-auto"
        />
        <div className="flex justify-end w-32">
          <button
            onClick={() =>
              window.open(
                `/udskriv/ugeplan?classId=${classId}&isoYear=${isoYear}&isoWeek=${isoWeek}${schemaId ? `&schemaId=${schemaId}` : ''}`,
                '_blank'
              )
            }
            className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-gray-600 border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors"
            title="Udskriv ugeplan"
          >
            <svg
              width="14"
              height="14"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <polyline points="6 9 6 2 18 2 18 9" />
              <path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2" />
              <rect x="6" y="14" width="12" height="8" />
            </svg>
            Udskriv
          </button>
        </div>
      </div>

      {/* Grid area */}
      <div className="flex-1 overflow-y-auto">
        {/* Generelt for ugen */}
        {classId && (
          <NotesEditor
            classId={classId}
            isoYear={isoYear}
            isoWeek={isoWeek}
            schemaId={schemaId}
            value={weekPlanData?.notes ?? null}
          />
        )}

        {/* Holiday banner */}
        {weekPlanData?.isHolidayWeek && (
          <div className="shrink-0 bg-blue-50 border-b border-blue-200 px-4 lg:px-6 py-2">
            <span className="text-blue-700 text-sm font-medium">
              Feriuge — {weekPlanData.holidayTitle}
            </span>
          </div>
        )}

        {isLoading && (
          <div className="p-8 text-center text-gray-400 text-sm animate-pulse">
            Henter ugeplan...
          </div>
        )}

        {!isLoading && weekPlanData && (
          <div
            data-testid="weekplan-grid"
            className={`grid grid-cols-[auto_1fr_1fr_1fr_1fr_1fr] min-w-0 ${weekPlanData.isHolidayWeek ? 'pointer-events-none opacity-60' : ''}`}
          >
            {/* Header row */}
            <div className="bg-gray-50 border-b border-r border-gray-200 p-2" />{' '}
            {/* empty corner */}
            {WEEKDAYS.map((label, i) => {
              const weekday = WEEKDAY_KEYS[i]
              const date = weekStartDate ? new Date(weekStartDate.getTime() + i * 86400000) : null
              const dateLabel = date
                ? date.toLocaleDateString('da-DK', { day: 'numeric', month: 'short' })
                : ''
              const holidayDay = (weekPlanData.holidayDays ?? []).find((h) => h.weekday === weekday)
              return (
                <div
                  key={label}
                  className={`border-b border-r border-gray-200 p-2 text-center ${holidayDay ? 'bg-amber-50' : 'bg-gray-50'}`}
                >
                  <div className="text-xs font-semibold text-gray-700">{label}</div>
                  {dateLabel && <div className="text-xs text-gray-400">{dateLabel}</div>}
                  {holidayDay && (
                    <div className="text-xs text-amber-600 font-medium mt-0.5 leading-tight">
                      {holidayDay.title}
                    </div>
                  )}
                </div>
              )
            })}
            {/* Data rows */}
            {uniqueTimeSlots.map((ts) => (
              <React.Fragment key={`row-${ts.timeSlotId}`}>
                {/* Time label */}
                <div
                  className={`border-b border-r border-gray-200 p-2 flex flex-col justify-center ${ts.kind === 'break' ? 'bg-gray-100' : 'bg-gray-50'}`}
                >
                  <span className="text-xs text-gray-500 font-mono whitespace-nowrap">
                    {ts.timeSlotLabel}
                  </span>
                  <span className="text-xs text-gray-400 font-mono">
                    {ts.startTime.slice(0, 5)}
                  </span>
                </div>

                {/* Break row: grey separator spanning all columns */}
                {ts.kind === 'break' &&
                  WEEKDAY_KEYS.map((_, dayIdx) => (
                    <div
                      key={`break-${ts.timeSlotId}-${dayIdx}`}
                      className="border-b border-r border-gray-200 bg-gray-100 min-h-[28px]"
                    />
                  ))}

                {/* Day cells (lesson rows only) */}
                {ts.kind === 'slot' &&
                  WEEKDAY_KEYS.map((dayKey, dayIdx) => {
                    const slot = weekPlanData.slots.find(
                      (s) => s.timeSlotId === ts.timeSlotId && s.weekday === dayKey
                    )
                    const isHolidayCol = (weekPlanData.holidayDays ?? []).some(
                      (h) => h.weekday === dayKey
                    )

                    if (!slot) {
                      return (
                        <div
                          key={`empty-${ts.timeSlotId}-${dayIdx}`}
                          className={`border-b border-r border-gray-200 min-h-[80px] ${isHolidayCol ? 'bg-amber-50' : 'bg-gray-50'}`}
                        />
                      )
                    }

                    const colorClass = getCourseColor(slot.courseId, allCourseIds)

                    return (
                      <button
                        key={`slot-${slot.schemaSlotId}`}
                        data-testid={`weekplan-slot-${slot.schemaSlotId}`}
                        type="button"
                        onClick={() => setEditingSchemaSlotId(slot.schemaSlotId)}
                        className={`w-full text-left border-b border-r border-gray-200 p-2 min-h-[80px] cursor-pointer transition-colors ${isHolidayCol ? 'bg-amber-50 pointer-events-none' : 'bg-white hover:bg-gray-50'}`}
                      >
                        {/* Course badge */}
                        <span
                          className={`inline-block px-1.5 py-0.5 rounded text-xs font-medium border ${colorClass}`}
                        >
                          {slot.originalCourseName ? (
                            <>
                              <span className="line-through text-gray-400 mr-1">
                                {slot.originalCourseName}
                              </span>
                              <span className="font-semibold text-brand-700">
                                {slot.courseName}
                              </span>
                            </>
                          ) : (
                            slot.courseName
                          )}
                        </span>

                        {/* Beskrivelse */}
                        {slot.description && (
                          <div className="text-xs text-gray-700 line-clamp-3 mt-1 prose prose-xs max-w-none [&_p]:m-0 [&_ul]:my-0.5 [&_li]:my-0">
                            <Markdown>{slot.description}</Markdown>
                          </div>
                        )}

                        {/* Lektier indicator */}
                        {slot.lektier && (
                          <div className="flex items-start gap-1 mt-1">
                            <svg
                              width="11"
                              height="11"
                              viewBox="0 0 24 24"
                              fill="none"
                              stroke="currentColor"
                              strokeWidth="2"
                              className="text-blue-500 shrink-0 mt-0.5"
                            >
                              <path d="M12 20h9" />
                              <path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4 12.5-12.5z" />
                            </svg>
                            <div className="text-xs text-blue-700 line-clamp-2 prose prose-xs max-w-none [&_p]:m-0 [&_ul]:my-0.5 [&_li]:my-0">
                              <Markdown>{slot.lektier}</Markdown>
                            </div>
                          </div>
                        )}

                        {/* Files indicator */}
                        {slot.files.length > 0 && (
                          <div className="flex items-center gap-1 mt-1">
                            <svg
                              width="11"
                              height="11"
                              viewBox="0 0 24 24"
                              fill="none"
                              stroke="currentColor"
                              strokeWidth="2"
                              className="text-gray-400 shrink-0"
                            >
                              <path d="M21.44 11.05l-9.19 9.19a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.83-2.83l8.49-8.48" />
                            </svg>
                            <span className="text-xs text-gray-500">{slot.files.length}</span>
                          </div>
                        )}

                        {/* Substitute indicator */}
                        {(slot.substituteTeacherName || slot.substituteAideName) && (
                          <div
                            className="flex items-center gap-1 mt-1"
                            data-testid="substitute-badge"
                          >
                            <span className="inline-flex items-center justify-center w-4 h-4 rounded-full bg-amber-400 text-white text-xs font-bold shrink-0">
                              V
                            </span>
                            <span className="text-xs text-amber-700 truncate">
                              {slot.substituteTeacherName ?? slot.substituteAideName}
                            </span>
                          </div>
                        )}
                      </button>
                    )
                  })}
              </React.Fragment>
            ))}
          </div>
        )}

        {!isLoading &&
          weekPlanData?.slots.length === 0 &&
          weekPlanData?.breakSlots.length === 0 && (
            <div className="p-8 text-center text-gray-400 text-sm">
              Ingen aktivt skema for denne klasse — opret et skema og sæt en datoperiode under
              Klasser.
            </div>
          )}

        {/* Parent-view preview */}
        {!isLoading && weekPlanData && (
          <div className="border-t border-gray-200 px-4 lg:px-6 py-3">
            <button
              type="button"
              onClick={() => setShowParentPreview((v) => !v)}
              className="flex items-center gap-1.5 text-sm font-medium text-gray-600 hover:text-gray-900 transition-colors"
              data-testid="parent-preview-toggle"
            >
              <span className={`transition-transform ${showParentPreview ? 'rotate-90' : ''}`}>
                ▶
              </span>
              Se som forældre ser det
            </button>
            {showParentPreview && (
              <div className="mt-3 max-w-2xl" data-testid="parent-preview">
                <ParentWeekPlan
                  key={`${isoYear}-${isoWeek}`}
                  plan={weekPlanData}
                  preselected={preselectedDate()}
                />
              </div>
            )}
          </div>
        )}
      </div>

      {/* Lesson editor */}
      {editingSlot && classId && weekPlanData && !substituteSchemaSlotId && (
        <LessonEditor
          key={editingSlot.schemaSlotId}
          slot={editingSlot}
          slots={weekPlanData.slots}
          classId={classId}
          className={className}
          isoYear={isoYear}
          isoWeek={isoWeek}
          schemaId={schemaId}
          weekStartDate={weekPlanData.weekStartDate}
          courses={courses}
          onGoToLesson={setEditingSchemaSlotId}
          onOpenSubstitute={() => setSubstituteSchemaSlotId(editingSlot.schemaSlotId)}
          onClose={() => setEditingSchemaSlotId(null)}
        />
      )}

      {/* Substitute panel */}
      {substituteSchemaSlotId &&
        classId &&
        (() => {
          const slot = weekPlanData?.slots.find((s) => s.schemaSlotId === substituteSchemaSlotId)
          if (!slot || slot.weekPlanId === '00000000-0000-0000-0000-000000000000') return null
          return (
            <AssignSubstitutePanel
              weekPlanId={slot.weekPlanId}
              slotId={slot.id}
              classId={classId}
              isoYear={isoYear}
              isoWeek={isoWeek}
              weekday={WEEKDAY_KEYS.indexOf(slot.weekday) + 1}
              timeSlotId={slot.timeSlotId}
              courseName={slot.courseName}
              weekdayLabel={WEEKDAYS[WEEKDAY_KEYS.indexOf(slot.weekday)] ?? ''}
              startTime={slot.startTime}
              currentSubstituteTeacherId={slot.substituteTeacherId}
              currentSubstituteTeacherName={slot.substituteTeacherName}
              currentSubstituteAideId={slot.substituteAideId}
              currentSubstituteAideName={slot.substituteAideName}
              schemaId={schemaId}
              onClose={() => setSubstituteSchemaSlotId(null)}
            />
          )
        })()}
    </div>
  )
}
