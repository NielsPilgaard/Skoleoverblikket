import React, { useState, useRef, useEffect } from 'react'
import { Modal } from '../components/Modal'
import { MarkdownTextarea, type SaveStatus } from '../components/markdown/MarkdownTextarea'
import { Markdown } from '../components/markdown/Markdown'
import { WeekPlanList } from '../components/weekplan/WeekPlanList'
import { useParams, useSearchParams, useNavigate } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1ClassesByClassIdWeekPlanOptions,
  getApiV1ClassesByClassIdWeekPlanQueryKey,
  putApiV1ClassesByClassIdWeekPlanSlotsMutation,
  putApiV1ClassesByClassIdWeekPlanGenereltMutation,
  postApiV1ClassesByClassIdWeekPlanSlotsBySlotIdFilesMutation,
  deleteApiV1ClassesByClassIdWeekPlanSlotsBySlotIdFilesByFileIdMutation,
  getApiV1CoursesOptions,
  getApiV1ClassesOptions,
} from '../api/generated/@tanstack/react-query.gen'
import type { ClassDto } from '../api/client'
import { usePageTitle } from '../hooks/usePageTitle'
import { FilePicker } from '../components/files/FilePicker'
import { AssignSubstitutePanel } from '../components/vikar/AssignSubstitutePanel'
import { getISOWeek, getISOWeekYear, getISOWeeksInYear } from '../utils/isoWeek'

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
  beskrivelse: string | null
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
  generelt: string | null
}

interface CourseDto {
  id: string
  name: string
}

// Returns the Monday of the ISO week
function isoWeekToMonday(isoYear: number, isoWeek: number): Date {
  const jan4 = new Date(Date.UTC(isoYear, 0, 4))
  const dayOfWeek = jan4.getUTCDay() || 7
  const weekOneMonday = new Date(jan4.getTime() - (dayOfWeek - 1) * 86400000)
  return new Date(weekOneMonday.getTime() + (isoWeek - 1) * 7 * 86400000)
}

interface SchoolYearWeek {
  isoYear: number
  isoWeek: number
  label: string
}

// School year: Aug 1 – Jun 30. Given current isoYear/isoWeek, generate all weeks.
function getSchoolYearWeeks(isoYear: number, isoWeek: number): SchoolYearWeek[] {
  // Determine which school year: if week is in Aug–Dec, school year starts that calendar year
  const monday = isoWeekToMonday(isoYear, isoWeek)
  const calYear = monday.getUTCFullYear()
  const calMonth = monday.getUTCMonth() // 0-based
  // School year start = Aug 1 of startYear
  const startYear = calMonth >= 7 ? calYear : calYear - 1
  const startDate = new Date(Date.UTC(startYear, 7, 1)) // Aug 1
  const endDate = new Date(Date.UTC(startYear + 1, 5, 30)) // Jun 30

  // Find ISO week of Aug 1 and Jun 30
  const startWeek = getISOWeek(startDate)
  const startWeekYear = getISOWeekYear(startDate)
  const endWeek = getISOWeek(endDate)
  const endWeekYear = getISOWeekYear(endDate)

  const weeks: SchoolYearWeek[] = []
  let y = startWeekYear
  let w = startWeek
  while (y < endWeekYear || (y === endWeekYear && w <= endWeek)) {
    const label = y !== startWeekYear || w === 1 ? `Uge ${w}, ${y}` : `Uge ${w}`
    weeks.push({ isoYear: y, isoWeek: w, label })
    w++
    if (w > getISOWeeksInYear(y)) {
      y++
      w = 1
    }
  }
  return weeks
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

// ─── Edit modal ───────────────────────────────────────────────────────────────

interface EditSlotModalProps {
  slot: WeekPlanSlotDto
  classId: string
  isoYear: number
  isoWeek: number
  schemaId: string | null
  weekdayLabel: string
  courses: CourseDto[]
  onClose: () => void
  onOpenVikar: () => Promise<void>
}

const AUTOSAVE_PREFIX = 'ugeplan_draft_'

function autosaveKey(schemaSlotId: string) {
  return `${AUTOSAVE_PREFIX}${schemaSlotId}`
}

function EditSlotModal({
  slot,
  classId,
  isoYear,
  isoWeek,
  schemaId,
  weekdayLabel,
  courses,
  onClose,
  onOpenVikar,
}: EditSlotModalProps) {
  const qc = useQueryClient()

  const savedDraft = (() => {
    try {
      return JSON.parse(sessionStorage.getItem(autosaveKey(slot.schemaSlotId)) ?? 'null')
    } catch {
      return null
    }
  })()

  const [beskrivelse, setBeskrivelse] = useState(savedDraft?.beskrivelse ?? slot.beskrivelse ?? '')
  const [lektier, setLektier] = useState(savedDraft?.lektier ?? slot.lektier ?? '')
  const [fagSwapCourseId, setFagSwapCourseId] = useState(slot.originalCourseId ? slot.courseId : '')
  const [filesOpen, setFilesOpen] = useState(false)
  const autosaveTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(() => {
    if (autosaveTimer.current) clearTimeout(autosaveTimer.current)
    autosaveTimer.current = setTimeout(() => {
      sessionStorage.setItem(
        autosaveKey(slot.schemaSlotId),
        JSON.stringify({ beskrivelse, lektier })
      )
    }, 1000)
    return () => {
      if (autosaveTimer.current) clearTimeout(autosaveTimer.current)
    }
  }, [beskrivelse, lektier, slot.schemaSlotId])

  const ugeplanQueryKey = getApiV1ClassesByClassIdWeekPlanQueryKey({
    path: { classId },
    query: { isoYear, isoWeek, ...(schemaId ? { schemaId } : {}) },
  })

  async function ensureSlotSaved(): Promise<string> {
    if (slot.id !== '00000000-0000-0000-0000-000000000000') return slot.id
    const { mutationFn } = putApiV1ClassesByClassIdWeekPlanSlotsMutation()
    const updated = await mutationFn!(
      {
        path: { classId },
        query: { isoYear, isoWeek, ...(schemaId ? { schemaId } : {}) },
        body: {
          schemaSlotId: slot.schemaSlotId,
          beskrivelse: beskrivelse || null,
          lektier: lektier || null,
          fagSwapCourseId: fagSwapCourseId || null,
        },
      },
      undefined as never
    )
    qc.setQueryData(ugeplanQueryKey, (old: WeekPlanDto | undefined) => {
      if (!old) return old
      return {
        ...old,
        slots: old.slots.map((s) =>
          s.schemaSlotId === (updated as WeekPlanSlotDto).schemaSlotId
            ? { ...s, ...(updated as WeekPlanSlotDto) }
            : s
        ),
      }
    })
    return (updated as WeekPlanSlotDto).id
  }

  const upsertMutation = useMutation({
    ...putApiV1ClassesByClassIdWeekPlanSlotsMutation(),
    onSuccess: (updated) => {
      qc.setQueryData(ugeplanQueryKey, (old: WeekPlanDto | undefined) => {
        if (!old) return old
        return {
          ...old,
          slots: old.slots.map((s) =>
            s.schemaSlotId === (updated as WeekPlanSlotDto).schemaSlotId
              ? { ...s, ...(updated as WeekPlanSlotDto) }
              : s
          ),
        }
      })
      sessionStorage.removeItem(autosaveKey(slot.schemaSlotId))
      onClose()
    },
  })

  const addFileMutation = useMutation({
    ...postApiV1ClassesByClassIdWeekPlanSlotsBySlotIdFilesMutation(),
    onSuccess: () => qc.invalidateQueries({ queryKey: ugeplanQueryKey }),
  })

  const removeFileMutation = useMutation({
    ...deleteApiV1ClassesByClassIdWeekPlanSlotsBySlotIdFilesByFileIdMutation(),
    onSuccess: () => qc.invalidateQueries({ queryKey: ugeplanQueryKey }),
  })

  function handleSave() {
    if (upsertMutation.isPending) return
    upsertMutation.mutate({
      path: { classId },
      query: { isoYear, isoWeek, ...(schemaId ? { schemaId } : {}) },
      body: {
        schemaSlotId: slot.schemaSlotId,
        beskrivelse: beskrivelse || null,
        lektier: lektier || null,
        fagSwapCourseId: fagSwapCourseId || null,
      },
    })
  }

  function handleKeyDown(e: React.KeyboardEvent) {
    if (e.key === 'Escape') {
      onClose()
    }
  }

  async function handleFileToggle(fileId: string, checked: boolean) {
    const slotId = await ensureSlotSaved()
    const existingLink = slot.files.find((f) => f.schoolFileId === fileId)
    if (checked && !existingLink) {
      addFileMutation.mutate({ path: { classId, slotId }, body: { schoolFileId: fileId } })
    } else if (!checked && existingLink) {
      removeFileMutation.mutate({ path: { classId, slotId, fileId: existingLink.id } })
    }
  }

  const selectedFileIds = slot.files.map((f) => f.schoolFileId)
  const isFileMutationPending = addFileMutation.isPending || removeFileMutation.isPending

  return (
    <Modal
      isOpen
      onClose={onClose}
      onKeyDown={handleKeyDown}
      backdropClassName="fixed inset-0 z-50 flex items-end sm:items-center justify-center sm:p-4 bg-black/40"
      contentClassName="bg-white rounded-t-2xl sm:rounded-2xl shadow-xl w-full sm:max-w-lg max-h-[92vh] sm:max-h-[90vh] overflow-y-auto"
    >
      <div className="px-6 py-5 border-b border-gray-100">
        <h2 className="font-display text-lg font-semibold text-gray-900">
          {slot.courseName} — {weekdayLabel}
        </h2>
        <p className="text-xs text-gray-400 mt-0.5">
          {slot.startTime.slice(0, 5)}–{slot.endTime.slice(0, 5)}
        </p>
      </div>

      <div className="px-6 py-5 space-y-5">
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Beskrivelse</label>
          <MarkdownTextarea
            autoFocus
            rows={5}
            value={beskrivelse}
            onChange={setBeskrivelse}
            placeholder="Hvad skal der ske i denne lektion?"
            maxLength={8000}
            aria-label="Beskrivelse"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>

        <div>
          <label className="block text-sm font-medium text-blue-700 mb-1">Lektier</label>
          <MarkdownTextarea
            rows={4}
            value={lektier}
            onChange={setLektier}
            placeholder="Opgaver til næste gang..."
            maxLength={8000}
            aria-label="Lektier"
            className="w-full px-3 py-2 border border-blue-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-400 focus:border-transparent bg-blue-50/40"
          />
        </div>

        <div>
          <button
            type="button"
            onClick={() => setFilesOpen((v) => !v)}
            className="flex items-center justify-between w-full text-left px-3 py-2 rounded-lg border border-gray-200 bg-gray-50 hover:bg-gray-100 transition-colors"
          >
            <span className="text-sm font-medium text-gray-700">
              Filer
              {selectedFileIds.length > 0 && (
                <span className="ml-1.5 inline-flex items-center justify-center w-4 h-4 rounded-full bg-brand-600 text-white text-xs font-bold">
                  {selectedFileIds.length}
                </span>
              )}
            </span>
            <span className="text-gray-400 text-xs">{filesOpen ? '▲' : '▼'}</span>
          </button>
          {filesOpen && (
            <div className="mt-2 space-y-2">
              <div className="flex justify-end">
                <a
                  href="/filer"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="text-xs text-gray-400 hover:text-gray-600"
                >
                  Administrer filer ↗
                </a>
              </div>
              <FilePicker
                selectedFileIds={selectedFileIds}
                onToggle={handleFileToggle}
                disabled={isFileMutationPending}
              />
            </div>
          )}
        </div>

        {upsertMutation.isError && (
          <p className="text-sm text-red-600">Der opstod en fejl. Prøv igen.</p>
        )}

        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Fagbytte</label>
          <select
            value={fagSwapCourseId}
            onChange={(e) => setFagSwapCourseId(e.target.value)}
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent bg-white"
          >
            <option value="">Intet fagbytte (brug skemaets fag)</option>
            {courses
              .filter((c) => c.id !== (slot.originalCourseId ?? slot.courseId))
              .map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
          </select>
        </div>
      </div>

      <div className="flex justify-center">
        <button
          type="button"
          onClick={onOpenVikar}
          className="w-3/4 flex items-center justify-center gap-2 px-4 py-2 mb-4 text-sm font-medium rounded-lg border border-blue-300 bg-blue-50 text-blue-800 hover:bg-blue-100 transition-colors"
          data-testid="tildel-vikar-button"
        >
          {slot.substituteTeacherName || slot.substituteAideName ? (
            <>
              <span className="inline-flex items-center justify-center w-4 h-4 rounded-full bg-blue-500 text-white text-xs font-bold shrink-0">
                V
              </span>
              Vikar: {slot.substituteTeacherName ?? slot.substituteAideName} · Skift
            </>
          ) : (
            'Tildel vikar'
          )}
        </button>
      </div>

      <div className="px-6 py-4 border-t border-gray-100">
        <div className="flex justify-end">
          <button
            onClick={handleSave}
            disabled={upsertMutation.isPending}
            className="px-4 py-2 text-sm rounded-lg font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed bg-brand-600 text-white hover:bg-brand-700"
          >
            {upsertMutation.isPending ? 'Gemmer...' : 'Gem'}
          </button>
        </div>
      </div>
    </Modal>
  )
}

// ─── Generelt block ───────────────────────────────────────────────────────────

function GenereltEditor({
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

  const ugeplanQueryKey = getApiV1ClassesByClassIdWeekPlanQueryKey({
    path: { classId },
    query: { isoYear, isoWeek, ...(schemaId ? { schemaId } : {}) },
  })

  // Serialize saves: each blur chains onto the previous save's promise so two
  // overlapping PUTs can never let an older response land after a newer one and
  // overwrite the newer `generelt`. A monotonic counter identifies the newest
  // save so only its result updates cache/status.
  const saveChainRef = useRef<Promise<unknown>>(Promise.resolve())
  const saveSeqRef = useRef(0)

  // Was the value this save submitted still the current editor text when it
  // resolved? If the user has since typed on, an earlier save landing must not
  // overwrite the newer local edit or stamp its outcome onto it.
  const isCurrentEdit = (submitted: string | null | undefined) => (submitted ?? '') === (text || '')

  const { mutationFn } = putApiV1ClassesByClassIdWeekPlanGenereltMutation()

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
              body: { generelt: normalized },
            },
            undefined as never
          )
          lastSavedRef.current = submitted
          if (seq === saveSeqRef.current) {
            pendingSaveRef.current = false
            if (isCurrentEdit(normalized)) {
              qc.setQueryData(ugeplanQueryKey, (old: WeekPlanDto | undefined) =>
                old ? { ...old, generelt: result.generelt ?? null } : old
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
        data-testid="generelt-editor"
        saveStatus={saveStatus}
        className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent bg-white"
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
  const queryClient = useQueryClient()

  const [isoYear, setIsoYear] = useState(() => getISOWeekYear(new Date()))
  const [isoWeek, setIsoWeek] = useState(() => getISOWeek(new Date()))
  const [editingSchemaSlotId, setEditingSchemaSlotId] = useState<string | null>(null)
  const [vikarSchemaSlotId, setVikarSchemaSlotId] = useState<string | null>(null)
  const [showParentPreview, setShowParentPreview] = useState(false)

  function prevWeek() {
    if (isoWeek === 1) {
      const prevYear = isoYear - 1
      setIsoYear(prevYear)
      setIsoWeek(getISOWeeksInYear(prevYear))
    } else {
      setIsoWeek(isoWeek - 1)
    }
  }

  function nextWeek() {
    const weeksInYear = getISOWeeksInYear(isoYear)
    if (isoWeek === weeksInYear) {
      setIsoYear(isoYear + 1)
      setIsoWeek(1)
    } else {
      setIsoWeek(isoWeek + 1)
    }
  }

  function goToThisWeek() {
    const now = new Date()
    setIsoYear(getISOWeekYear(now))
    setIsoWeek(getISOWeek(now))
  }

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
      <div className="shrink-0 sticky top-0 z-10 bg-white border-b border-gray-200 px-4 lg:px-6 py-3 flex items-center justify-between">
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
        {/* Week navigator */}
        <div className="flex items-center gap-1">
          <button
            onClick={prevWeek}
            className="text-sm text-gray-600 hover:text-gray-900 px-2 py-1 rounded hover:bg-gray-100 transition-colors"
          >
            ←
          </button>
          <select
            value={`${isoYear}-${isoWeek}`}
            onChange={(e) => {
              const [y, w] = e.target.value.split('-').map(Number)
              setIsoYear(y)
              setIsoWeek(w)
            }}
            className="text-sm font-semibold text-gray-900 px-2 py-1 border border-gray-200 rounded bg-white focus:outline-none focus:ring-2 focus:ring-brand-500"
          >
            {getSchoolYearWeeks(isoYear, isoWeek).map(({ isoYear: y, isoWeek: w, label }) => (
              <option key={`${y}-${w}`} value={`${y}-${w}`}>
                {label}
              </option>
            ))}
          </select>
          <button
            onClick={nextWeek}
            className="text-sm text-gray-600 hover:text-gray-900 px-2 py-1 rounded hover:bg-gray-100 transition-colors"
          >
            →
          </button>
          <button onClick={goToThisWeek} className="text-xs text-brand-600 hover:underline ml-2">
            Denne uge
          </button>
        </div>
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
          <GenereltEditor
            classId={classId}
            isoYear={isoYear}
            isoWeek={isoWeek}
            schemaId={schemaId}
            value={weekPlanData?.generelt ?? null}
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
            className={`grid grid-cols-[auto_1fr_1fr_1fr_1fr_1fr] min-w-0 ${weekPlanData.isHolidayWeek ? 'pointer-events-none opacity-60' : ''}`}
          >
            {/* Header row */}
            <div className="bg-gray-50 border-b border-r border-gray-200 p-2" />{' '}
            {/* empty corner */}
            {WEEKDAYS.map((label, i) => {
              const weekday = WEEKDAY_KEYS[i]
              const date = weekStartDate ? new Date(weekStartDate.getTime() + i * 86400000) : null
              const dateLabel = date
                ? date.toLocaleDateString('da-DK', { day: '2-digit', month: 'short' })
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
                        {slot.beskrivelse && (
                          <div className="text-xs text-gray-700 line-clamp-3 mt-1 prose prose-xs max-w-none [&_p]:m-0 [&_ul]:my-0.5 [&_li]:my-0">
                            <Markdown>{slot.beskrivelse}</Markdown>
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

                        {/* Substitute (vikar) indicator */}
                        {(slot.substituteTeacherName || slot.substituteAideName) && (
                          <div className="flex items-center gap-1 mt-1" data-testid="vikar-badge">
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
                <WeekPlanList
                  generelt={weekPlanData.generelt}
                  slots={weekPlanData.slots}
                  isHolidayWeek={weekPlanData.isHolidayWeek}
                  holidayTitle={weekPlanData.holidayTitle}
                />
              </div>
            )}
          </div>
        )}
      </div>

      {/* Edit modal */}
      {editingSlot && classId && !vikarSchemaSlotId && (
        <EditSlotModal
          slot={editingSlot}
          classId={classId}
          isoYear={isoYear}
          isoWeek={isoWeek}
          schemaId={schemaId}
          weekdayLabel={WEEKDAYS[WEEKDAY_KEYS.indexOf(editingSlot.weekday)] ?? ''}
          courses={courses}
          onClose={() => setEditingSchemaSlotId(null)}
          onOpenVikar={async () => {
            // Ensure the WeekPlanSlot row exists before opening the vikar panel
            if (editingSlot.id === '00000000-0000-0000-0000-000000000000') {
              const { mutationFn } = putApiV1ClassesByClassIdWeekPlanSlotsMutation()
              await mutationFn!(
                {
                  path: { classId },
                  query: { isoYear, isoWeek, ...(schemaId ? { schemaId } : {}) },
                  body: {
                    schemaSlotId: editingSlot.schemaSlotId,
                    beskrivelse: null,
                    lektier: null,
                    fagSwapCourseId: null,
                  },
                },
                undefined as never
              )
              // Refetch so slot.id and slot.weekPlanId are populated
              await queryClient.invalidateQueries({
                queryKey: getApiV1ClassesByClassIdWeekPlanQueryKey({
                  path: { classId },
                  query: { isoYear, isoWeek, ...(schemaId ? { schemaId } : {}) },
                }),
              })
            }
            setEditingSchemaSlotId(null)
            setVikarSchemaSlotId(editingSlot.schemaSlotId)
          }}
        />
      )}

      {/* Vikar panel */}
      {vikarSchemaSlotId &&
        classId &&
        (() => {
          const slot = weekPlanData?.slots.find((s) => s.schemaSlotId === vikarSchemaSlotId)
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
              onClose={() => setVikarSchemaSlotId(null)}
            />
          )
        })()}
    </div>
  )
}
