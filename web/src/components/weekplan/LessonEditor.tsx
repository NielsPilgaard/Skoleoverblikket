import { useEffect, useRef, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  deleteApiV1ClassesByClassIdWeekPlanSlotsBySlotIdFilesByFileIdMutation,
  getApiV1ClassesByClassIdWeekPlanQueryKey,
  postApiV1ClassesByClassIdWeekPlanSlotsBySlotIdFilesMutation,
  putApiV1ClassesByClassIdWeekPlanSlotsMutation,
} from '../../api/generated/@tanstack/react-query.gen'
import { FilePicker } from '../files/FilePicker'
import { Modal } from '../Modal'
import { MarkdownTextarea } from '../markdown/MarkdownTextarea'
import {
  CheckIcon,
  ChevronLeftIcon,
  ChevronRightIcon,
  CloseIcon,
  PaperclipIcon,
  SpinnerIcon,
  SwapIcon,
  UndoIcon,
  UserIcon,
} from '../ui/icons'
import { Select } from '../ui/select'
import { WEEKDAY_KEYS, WEEKDAY_LABELS } from '../../lib/weekdays'
import { capitalizeFirst } from '../../lib/absence'
import { formatDayDate } from '../../utils/isoWeek'

const AUTOSAVE_DELAY_MS = 800
const EMPTY_ID = '00000000-0000-0000-0000-000000000000'

export interface LessonSlot {
  id: string
  schemaSlotId: string
  weekday: string
  startTime: string
  endTime: string
  courseId: string
  courseName: string
  originalCourseId: string | null
  originalCourseName: string | null
  description: string | null
  lektier: string | null
  files: { id: string; schoolFileId: string; fileName: string; url: string }[]
  substituteTeacherName: string | null
  substituteAideName: string | null
}

interface LessonEditorProps {
  slot: LessonSlot
  /** Every lesson in the week, to find the next one. */
  slots: LessonSlot[]
  classId: string
  className: string
  isoYear: number
  isoWeek: number
  schemaId: string | null
  weekStartDate: string
  courses: { id: string; name: string }[]
  onGoToLesson: (schemaSlotId: string) => void
  onOpenSubstitute: () => void
  onClose: () => void
}

type WeekPlanCache = { slots: LessonSlot[] } | undefined
type Status = 'saved' | 'saving' | 'error'
type Step = 'lesson' | 'files' | 'course'

interface Values {
  description: string
  lektier: string
  overrideCourseId: string
}

function weekOrder(a: LessonSlot, b: LessonSlot): number {
  return (
    WEEKDAY_KEYS.indexOf(a.weekday as never) - WEEKDAY_KEYS.indexOf(b.weekday as never) ||
    a.startTime.localeCompare(b.startTime)
  )
}

function nextLessonLabel(current: LessonSlot, next: LessonSlot): string {
  const time = next.startTime.slice(0, 5)
  if (next.weekday === current.weekday) return `${next.courseName} ${time}`
  const day = WEEKDAY_LABELS[WEEKDAY_KEYS.indexOf(next.weekday as never)]
  return `${day} ${next.courseName} ${time}`
}

function RowButton({
  icon,
  label,
  value,
  onClick,
  testId,
}: {
  icon: React.ReactNode
  label: string
  value: string
  onClick: () => void
  testId?: string
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      data-testid={testId}
      className="w-full flex items-center gap-3 px-3 h-12 text-left text-gray-900 hover:bg-gray-50"
    >
      <span className="text-gray-600">{icon}</span>
      <span className="flex-1 font-semibold">{label}</span>
      <span className="text-sm text-gray-700 truncate max-w-[50%]">{value}</span>
      <ChevronRightIcon className="w-5 h-5 text-gray-500 shrink-0" />
    </button>
  )
}

/**
 * Edit one lesson. Text saves itself about 800 ms after typing stops, and at once on blur, "Næste"
 * and "Luk". If a save fails the text stays in the field and the dialog stays open.
 */
export function LessonEditor({
  slot,
  slots,
  classId,
  className,
  isoYear,
  isoWeek,
  schemaId,
  weekStartDate,
  courses,
  onGoToLesson,
  onOpenSubstitute,
  onClose,
}: LessonEditorProps) {
  const qc = useQueryClient()
  const query = { isoYear, isoWeek, ...(schemaId ? { schemaId } : {}) }
  const weekPlanQueryKey = getApiV1ClassesByClassIdWeekPlanQueryKey({
    path: { classId },
    query,
  })

  const [opened] = useState<Values>(() => ({
    description: slot.description ?? '',
    lektier: slot.lektier ?? '',
    overrideCourseId: slot.originalCourseId ? slot.courseId : '',
  }))
  const [values, setValues] = useState(opened)
  const [status, setStatus] = useState<Status>('saved')
  const [step, setStep] = useState<Step>('lesson')
  const [fileError, setFileError] = useState(false)
  const [uploading, setUploading] = useState(false)
  const pendingToggles = useRef<Promise<boolean>[]>([])

  // Latest typed values, saved values and slot id live in refs: saves run after renders have moved on.
  const latest = useRef(opened)
  const lastSaved = useRef(JSON.stringify(opened))
  const slotId = useRef(slot.id)
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)
  // Saves run one at a time, so an older response can never land after a newer one.
  const saveChain = useRef<Promise<boolean>>(Promise.resolve(true))
  const pendingSaves = useRef(0)
  const { mutationFn: putSlot } = putApiV1ClassesByClassIdWeekPlanSlotsMutation()

  function mergeIntoCache(updated: Partial<LessonSlot> & { schemaSlotId?: string }) {
    qc.setQueryData(weekPlanQueryKey, (old: WeekPlanCache) =>
      old
        ? {
            ...old,
            slots: old.slots.map((s) =>
              s.schemaSlotId === updated.schemaSlotId ? { ...s, ...updated } : s
            ),
          }
        : old
    )
  }

  function saveNow({ force = false } = {}): Promise<boolean> {
    if (timer.current) clearTimeout(timer.current)
    timer.current = null
    pendingSaves.current++
    saveChain.current = saveChain.current.then(async () => {
      const snapshot = latest.current
      const key = JSON.stringify(snapshot)
      const creating = force && slotId.current === EMPTY_ID
      if (key === lastSaved.current && !creating) {
        pendingSaves.current--
        setStatus('saved')
        return true
      }
      setStatus('saving')
      try {
        const updated = await putSlot!(
          {
            path: { classId },
            query,
            body: {
              schemaSlotId: slot.schemaSlotId,
              description: snapshot.description || null,
              lektier: snapshot.lektier || null,
              overrideCourseId: snapshot.overrideCourseId || null,
            },
          },
          undefined as never
        )
        lastSaved.current = key
        slotId.current = (updated as { id: string }).id
        mergeIntoCache(updated as Partial<LessonSlot>)
        // Typing went on during the request: a timer is already running for that text.
        setStatus(JSON.stringify(latest.current) === key ? 'saved' : 'saving')
        return true
      } catch {
        setStatus('error')
        return false
      } finally {
        pendingSaves.current--
      }
    })
    return saveChain.current
  }

  // Newest saveNow for the unmount flush below, which only runs once.
  const saveRef = useRef(saveNow)
  saveRef.current = saveNow

  useEffect(
    () => () => {
      if (timer.current) void saveRef.current()
    },
    []
  )

  useEffect(() => {
    function warnIfUnsaved(e: BeforeUnloadEvent) {
      if (pendingSaves.current > 0 || JSON.stringify(latest.current) !== lastSaved.current) {
        e.preventDefault()
      }
    }
    window.addEventListener('beforeunload', warnIfUnsaved)
    return () => window.removeEventListener('beforeunload', warnIfUnsaved)
  }, [])

  function change(patch: Partial<Values>, delay = AUTOSAVE_DELAY_MS) {
    latest.current = { ...latest.current, ...patch }
    setValues(latest.current)
    setStatus('saving')
    if (timer.current) clearTimeout(timer.current)
    timer.current = setTimeout(() => void saveNow(), delay)
  }

  function undo() {
    latest.current = opened
    setValues(opened)
    void saveNow()
  }

  /** Saves until nothing is left, also what was typed while an earlier save was on its way. */
  async function saveAll(): Promise<boolean> {
    do {
      if (!(await saveNow())) return false
    } while (JSON.stringify(latest.current) !== lastSaved.current)
    return true
  }

  async function handleClose() {
    if (await saveAll()) onClose()
  }

  const addFile = useMutation({
    ...postApiV1ClassesByClassIdWeekPlanSlotsBySlotIdFilesMutation(),
    onSuccess: (link) => {
      qc.setQueryData(weekPlanQueryKey, (old: WeekPlanCache) =>
        old
          ? {
              ...old,
              slots: old.slots.map((s) =>
                s.schemaSlotId === slot.schemaSlotId
                  ? { ...s, files: [...s.files, link as LessonSlot['files'][number]] }
                  : s
              ),
            }
          : old
      )
    },
  })
  const removeFile = useMutation({
    ...deleteApiV1ClassesByClassIdWeekPlanSlotsBySlotIdFilesByFileIdMutation(),
    onSuccess: (_, variables) => {
      qc.setQueryData(weekPlanQueryKey, (old: WeekPlanCache) =>
        old
          ? {
              ...old,
              slots: old.slots.map((s) =>
                s.schemaSlotId === slot.schemaSlotId
                  ? { ...s, files: s.files.filter((f) => f.id !== variables.path.fileId) }
                  : s
              ),
            }
          : old
      )
    },
  })

  function handleFileToggle(fileId: string, checked: boolean): Promise<boolean> {
    const toggle = saveFileToggle(fileId, checked).finally(() => {
      pendingToggles.current = pendingToggles.current.filter((t) => t !== toggle)
    })
    pendingToggles.current.push(toggle)
    return toggle
  }

  async function saveFileToggle(fileId: string, checked: boolean): Promise<boolean> {
    setFileError(false)
    if (!(await saveNow({ force: true }))) {
      setFileError(true)
      return false
    }
    const link = slot.files.find((f) => f.schoolFileId === fileId)
    const path = { classId, slotId: slotId.current }
    try {
      if (checked && !link) await addFile.mutateAsync({ path, body: { schoolFileId: fileId } })
      if (!checked && link) await removeFile.mutateAsync({ path: { ...path, fileId: link.id } })
      return true
    } catch {
      setFileError(true)
      return false
    }
  }

  async function handleOpenSubstitute() {
    if ((await saveNow({ force: true })) && (await saveAll())) onOpenSubstitute()
  }

  const ordered = [...slots].sort(weekOrder)
  const next = ordered[ordered.findIndex((s) => s.schemaSlotId === slot.schemaSlotId) + 1]

  async function goToNext() {
    if (next && (await saveAll())) onGoToLesson(next.schemaSlotId)
  }

  const dayIndex = WEEKDAY_KEYS.indexOf(slot.weekday as never)
  const monday = new Date(`${weekStartDate}T00:00:00`)
  const date = new Date(monday.getFullYear(), monday.getMonth(), monday.getDate() + dayIndex)
  const dateLabel = capitalizeFirst(formatDayDate(date))
  const subtitle = [
    dateLabel,
    `${slot.startTime.slice(0, 5)}–${slot.endTime.slice(0, 5)}`,
    className,
  ]
    .filter(Boolean)
    .join(' · ')

  const changedSinceOpened =
    values.description !== opened.description || values.lektier !== opened.lektier
  const substitute = slot.substituteTeacherName ?? slot.substituteAideName
  const fileCount = slot.files.length

  const modalProps = {
    isOpen: true,
    onClose: () => void handleClose(),
    backdropClassName:
      'fixed inset-0 z-50 flex items-end sm:items-center justify-center sm:p-4 bg-black/40',
    contentClassName:
      'bg-white rounded-t-2xl sm:rounded-2xl shadow-xl w-full sm:max-w-lg max-h-[92vh] sm:max-h-[90vh] flex flex-col overflow-hidden',
  }

  if (step === 'files') {
    // A running upload or tick shows any failure in the picker, so it can't close before they end.
    const closePicker = async () => {
      if (uploading) return
      const results = await Promise.all(pendingToggles.current)
      if (results.every(Boolean)) setStep('lesson')
    }
    return (
      <Modal {...modalProps} onClose={closePicker}>
        <FilePicker
          courseId={slot.courseId}
          courseName={slot.courseName}
          className={className}
          selectedFileIds={slot.files.map((f) => f.schoolFileId)}
          onToggle={handleFileToggle}
          error={fileError ? 'Filen kunne ikke gemmes på lektionen. Prøv igen.' : undefined}
          onUploadingChange={setUploading}
          onClose={closePicker}
        />
      </Modal>
    )
  }

  return (
    <Modal {...modalProps}>
      <div className="px-5 pt-4 pb-3 flex items-start gap-3 border-b border-gray-100">
        <div className="flex-1 min-w-0">
          <h2 className="font-display text-xl font-bold text-gray-900 truncate">
            {slot.courseName}
          </h2>
          <p className="text-sm text-gray-700">{subtitle}</p>
        </div>
        <div role="status" data-testid="lesson-save-status" className="mt-1.5 shrink-0">
          {status === 'saving' && (
            <span className="inline-flex items-center gap-1 h-8 px-3 rounded-full bg-gray-100 text-gray-800 text-sm font-bold">
              <SpinnerIcon />
              Gemmer …
            </span>
          )}
          {status === 'saved' && (
            <span className="inline-flex items-center gap-1 h-8 px-3 rounded-full bg-brand-50 text-brand-800 text-sm font-bold">
              <CheckIcon className="w-4 h-4" />
              Gemt
            </span>
          )}
          {status === 'error' && (
            <span className="inline-flex items-center h-8 px-3 rounded-full bg-red-100 text-red-800 text-sm font-bold">
              Ikke gemt
            </span>
          )}
        </div>
        <button
          type="button"
          aria-label="Luk"
          onClick={handleClose}
          className="w-11 h-11 -mr-2 shrink-0 rounded-full flex items-center justify-center text-gray-800 hover:bg-gray-100"
        >
          <CloseIcon />
        </button>
      </div>

      {step === 'course' ? (
        <div className="px-5 py-4 space-y-4 overflow-y-auto">
          <Select
            label="Byt fag"
            testId="lesson-course"
            options={[
              { value: '', label: `Nej, brug skemaets fag` },
              ...courses
                .filter((c) => c.id !== (slot.originalCourseId ?? slot.courseId))
                .map((c) => ({ value: c.id, label: c.name })),
            ]}
            value={values.overrideCourseId}
            onChange={(overrideCourseId) => change({ overrideCourseId }, 0)}
          />
          <button
            type="button"
            onClick={() => setStep('lesson')}
            className="h-11 px-3 -ml-3 rounded-lg font-semibold text-gray-800 hover:bg-gray-100 inline-flex items-center gap-1.5"
          >
            <ChevronLeftIcon />
            Tilbage til lektionen
          </button>
        </div>
      ) : (
        <div className="px-5 py-4 space-y-4 overflow-y-auto">
          {status === 'error' && (
            <div
              className="rounded-lg bg-red-50 border border-red-200 p-3 text-red-800"
              data-testid="lesson-save-error"
            >
              <p className="font-semibold">Det blev ikke gemt. Din tekst står her stadig.</p>
              <button
                type="button"
                onClick={() => void saveNow()}
                data-testid="lesson-save-retry"
                className="mt-2 h-11 px-4 rounded-lg bg-white border border-red-300 font-semibold hover:bg-red-100"
              >
                Prøv igen
              </button>
            </div>
          )}
          <div>
            <label
              htmlFor="lesson-description"
              className="block text-sm font-bold text-gray-800 mb-1"
            >
              Hvad skal I lave?
            </label>
            <MarkdownTextarea
              id="lesson-description"
              autoFocus
              rows={3}
              value={values.description}
              onChange={(description) => change({ description })}
              onBlur={() => void saveNow()}
              maxLength={8000}
              data-testid="lesson-description"
              className="w-full px-3 py-2 rounded-lg border border-gray-300 text-base focus:outline-hidden focus:ring-2 focus:ring-brand-500"
            />
          </div>
          <div>
            <label htmlFor="lesson-lektier" className="block text-sm font-bold text-gray-800 mb-1">
              Lektier <span className="font-normal text-gray-700">(valgfrit)</span>
            </label>
            <MarkdownTextarea
              id="lesson-lektier"
              rows={2}
              value={values.lektier}
              onChange={(lektier) => change({ lektier })}
              onBlur={() => void saveNow()}
              maxLength={8000}
              placeholder="Fx: Læs s. 41–48"
              data-testid="lesson-lektier"
              className="w-full px-3 py-2 rounded-lg border border-gray-300 text-base placeholder:text-gray-500 focus:outline-hidden focus:ring-2 focus:ring-brand-500"
            />
          </div>
          {changedSinceOpened && (
            <button
              type="button"
              onClick={undo}
              data-testid="lesson-undo"
              className="-mt-2 h-11 px-2 -ml-2 rounded-lg text-sm font-semibold text-gray-800 hover:bg-gray-100 inline-flex items-center gap-1.5"
            >
              <UndoIcon className="w-4 h-4" />
              Fortryd mine ændringer
            </button>
          )}
          <ul className="rounded-xl border border-gray-200 divide-y divide-gray-100">
            <li>
              <RowButton
                icon={<PaperclipIcon />}
                label="Filer"
                value={fileCount === 0 ? 'Ingen' : `${fileCount} vedhæftet`}
                onClick={() => setStep('files')}
                testId="lesson-files"
              />
            </li>
            <li>
              <RowButton
                icon={<SwapIcon />}
                label="Byt fag"
                value={slot.originalCourseName ? slot.courseName : 'Nej'}
                onClick={() => setStep('course')}
                testId="lesson-swap-course"
              />
            </li>
            <li>
              <RowButton
                icon={<UserIcon />}
                label="Vikar"
                value={substitute ?? 'Ingen'}
                onClick={() => void handleOpenSubstitute()}
                testId="assign-substitute-button"
              />
            </li>
          </ul>
        </div>
      )}

      <div className="mt-auto px-5 py-3 border-t border-gray-100 bg-white flex gap-2">
        <button
          type="button"
          onClick={handleClose}
          data-testid="lesson-close"
          className="flex-1 h-12 rounded-lg border border-gray-300 font-semibold text-gray-900 hover:bg-gray-50"
        >
          Luk
        </button>
        <button
          type="button"
          onClick={goToNext}
          disabled={!next}
          data-testid="lesson-next"
          className="flex-[2] min-w-0 h-12 px-3 rounded-lg bg-brand-600 text-white font-bold hover:bg-brand-700 disabled:bg-gray-200 disabled:text-gray-700 disabled:cursor-not-allowed inline-flex items-center justify-center gap-1.5"
        >
          {next ? (
            <>
              <span className="truncate">
                <span className="font-normal">Næste:</span> {nextLessonLabel(slot, next)}
              </span>
              <ChevronRightIcon className="w-5 h-5 shrink-0" />
            </>
          ) : (
            'Sidste lektion i ugen'
          )}
        </button>
      </div>
    </Modal>
  )
}
