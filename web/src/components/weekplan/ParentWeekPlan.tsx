import { useLayoutEffect, useRef, useState } from 'react'
import { AlertIcon, FileIcon } from '../ui/icons'
import { Markdown } from '../markdown/Markdown'
import { WEEKDAY_KEYS, WEEKDAY_LABELS } from '../../lib/weekdays'
import { capitalizeFirst } from '../../lib/absence'
import { formatDayDate, startOfDay } from '../../utils/isoWeek'

export interface ParentWeekPlanSlot {
  id: string
  schemaSlotId: string
  weekday: string
  startTime: string
  endTime?: string
  courseName: string
  originalCourseName?: string | null
  description?: string | null
  lektier?: string | null
  files?: { id: string; fileName: string; url: string }[]
  substituteTeacherName?: string | null
  substituteAideName?: string | null
}

export interface ParentWeekPlanData {
  weekStartDate?: string
  notes?: string | null
  notesUpdatedBy?: string | null
  notesUpdatedAt?: string | null
  isHolidayWeek?: boolean
  holidayTitle?: string | null
  holidayDays?: { weekday: string; title: string }[]
  slots?: ParentWeekPlanSlot[]
}

interface ParentWeekPlanProps {
  plan: ParentWeekPlanData
  /** The day chosen for the parent: see preselectedDate. */
  preselected: Date
  /** The child goes to SFO after school. */
  sfoAfterSchool?: boolean
  now?: Date
}

const SHORT_WEEKDAYS = ['Man', 'Tir', 'Ons', 'Tor', 'Fre']

function substituteNames(s: ParentWeekPlanSlot): string | null {
  return [s.substituteTeacherName, s.substituteAideName].filter(Boolean).join(' og ') || null
}

function isChanged(s: ParentWeekPlanSlot): boolean {
  return !!s.originalCourseName || !!substituteNames(s)
}

function changeText(s: ParentWeekPlanSlot): string {
  const substitute = substituteNames(s)
  if (substitute) return `Vikar i ${s.courseName.toLowerCase()}: ${substitute}`
  return `${s.courseName} i stedet for ${s.originalCourseName?.toLowerCase()}`
}

function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0].toUpperCase())
    .join('')
}

function formatWritten(iso: string): string {
  const d = new Date(iso)
  const time = d.toLocaleTimeString('da-DK', { hour: '2-digit', minute: '2-digit' })
  return `${formatDayDate(d)} kl. ${time.replace('.', ':')}`
}

/** "Generelt for ugen": the teacher's note, clamped to 3 lines. */
function GeneralCard({
  notes,
  updatedBy,
  updatedAt,
}: {
  notes: string
  updatedBy?: string | null
  updatedAt?: string | null
}) {
  const [open, setOpen] = useState(false)
  const [overflows, setOverflows] = useState(false)
  const textRef = useRef<HTMLDivElement>(null)

  useLayoutEffect(() => {
    const el = textRef.current
    if (el && !open) setOverflows(el.scrollHeight > el.clientHeight + 1)
  }, [open])

  return (
    <section
      className="bg-white rounded-xl border border-gray-200 p-4"
      data-testid="weekplan-general"
    >
      <div className="flex items-center gap-3">
        {updatedBy && (
          <span
            aria-hidden="true"
            className="w-10 h-10 shrink-0 rounded-full bg-sky-100 text-sky-900 font-bold flex items-center justify-center"
          >
            {initials(updatedBy)}
          </span>
        )}
        <div className="min-w-0">
          <h2 className="font-bold text-gray-900">Generelt for ugen</h2>
          {updatedAt && (
            <p className="text-sm text-gray-700">
              {[updatedBy, formatWritten(updatedAt)].filter(Boolean).join(' · ')}
            </p>
          )}
        </div>
      </div>
      <div
        ref={textRef}
        className={`mt-3 prose prose-sm max-w-none text-base text-gray-900 [&_p]:my-1 [&_p:first-child]:mt-0 [&_ul]:my-1 ${
          open ? '' : 'line-clamp-3'
        }`}
      >
        <Markdown>{notes}</Markdown>
      </div>
      {(overflows || open) && (
        <button
          type="button"
          aria-expanded={open}
          onClick={() => setOpen((v) => !v)}
          className="mt-1 h-11 -ml-2 px-2 rounded-lg font-semibold text-brand-700 hover:bg-brand-50"
        >
          {open ? 'Vis mindre' : 'Læs hele'}
        </button>
      )}
    </section>
  )
}

function SummaryBox({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-xl bg-white/10 p-3">
      <p className="text-sm font-bold">{title}</p>
      {children}
    </div>
  )
}

/** Brand-colored answer to "what do we need to know": hours, changes and lektier. */
function DaySummary({
  date,
  relative,
  slots,
  holidayTitle,
  sfoAfterSchool,
}: {
  date: Date
  relative: string | null
  slots: ParentWeekPlanSlot[]
  holidayTitle?: string
  sfoAfterSchool?: boolean
}) {
  const changes = slots.filter(isChanged)
  const homework = slots.filter((s) => s.lektier)
  const first = slots[0]
  const last = slots[slots.length - 1]
  const dayWord = relative ? relative.toLowerCase() : 'denne dag'
  const hours = first
    ? `${first.startTime.slice(0, 5)}–${(last.endTime ?? last.startTime).slice(0, 5)}`
    : null

  return (
    <div className="rounded-2xl bg-brand-700 text-white p-4" data-testid="weekplan-day-summary">
      {relative && <p className="text-sm font-semibold text-brand-100">{relative}</p>}
      <p className="font-display text-2xl font-bold">{capitalizeFirst(formatDayDate(date))}</p>
      {holidayTitle && <p className="text-brand-100">Fri: {holidayTitle}</p>}
      {hours && (
        <p className="text-brand-100">
          {hours}
          {sfoAfterSchool ? ' · derefter SFO' : ''}
        </p>
      )}
      {first && (
        <div className="mt-3 space-y-2">
          {changes.length > 0 && (
            <div className="rounded-xl bg-amber-100 text-amber-950 p-3">
              <p className="text-sm font-bold flex items-center gap-1.5">
                <AlertIcon className="w-4 h-4" />
                Anderledes {dayWord}
              </p>
              <ul className="mt-1 text-sm space-y-1 list-disc pl-5">
                {changes.map((s) => (
                  <li key={s.schemaSlotId}>{changeText(s)}</li>
                ))}
              </ul>
            </div>
          )}
          <SummaryBox title={`Lektier til ${dayWord}`}>
            {homework.length > 0 ? (
              <ul className="mt-1 text-sm space-y-1 list-disc pl-5">
                {homework.map((s) => (
                  <li key={s.schemaSlotId}>
                    {s.courseName}:{' '}
                    <span className="[&_*]:inline [&_*]:m-0 [&_*]:text-white">
                      <Markdown>{s.lektier ?? ''}</Markdown>
                    </span>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="text-sm">Ingen lektier.</p>
            )}
          </SummaryBox>
        </div>
      )}
    </div>
  )
}

function LessonRow({ slot }: { slot: ParentWeekPlanSlot }) {
  const substitute = substituteNames(slot)
  return (
    <li className={`flex gap-3 py-3 ${isChanged(slot) ? '-mx-4 px-4 bg-amber-50' : ''}`}>
      <span className="w-12 shrink-0 text-sm font-semibold text-gray-700 tabular-nums">
        {slot.startTime.slice(0, 5)}
      </span>
      <div className="flex-1 min-w-0">
        <p className="font-semibold text-gray-900">
          {slot.courseName}
          {slot.originalCourseName && (
            <span className="ml-1 px-1.5 py-0.5 rounded bg-amber-200 text-amber-950 text-xs font-bold">
              Ændret
            </span>
          )}
        </p>
        {slot.originalCourseName && (
          <p className="text-sm text-gray-700">
            <span className="line-through">{slot.originalCourseName}</span> → {slot.courseName}
          </p>
        )}
        {substitute && (
          <p className="text-sm font-semibold text-amber-900" data-testid="weekplan-substitute">
            Vikar: {substitute}
          </p>
        )}
        {slot.description && (
          <div className="text-sm text-gray-700 prose prose-sm max-w-none [&_p]:my-0.5 [&_ul]:my-0.5">
            <Markdown>{slot.description}</Markdown>
          </div>
        )}
        {slot.lektier && (
          <div className="mt-2 rounded-lg bg-sky-50 border border-sky-200 px-3 py-2 text-sm">
            <span className="font-bold text-sky-900">Lektier:</span>{' '}
            <span className="[&_*]:inline [&_*]:m-0">
              <Markdown>{slot.lektier}</Markdown>
            </span>
          </div>
        )}
        {slot.files && slot.files.length > 0 && (
          <div className="mt-2 flex flex-wrap gap-2">
            {slot.files.map((f) => (
              <a
                key={f.id}
                href={f.url}
                target="_blank"
                rel="noopener noreferrer"
                className="max-w-full inline-flex items-center gap-2 h-11 px-3 rounded-lg border border-gray-300 bg-white text-sm font-semibold text-gray-900 hover:bg-gray-50"
              >
                <FileIcon className="w-4 h-4 shrink-0" />
                <span className="truncate">{f.fileName}</span>
              </a>
            ))}
          </div>
        )}
      </div>
    </li>
  )
}

/**
 * What a parent sees for one week: the teacher's note, then day tabs with the right day already
 * chosen. Used on the parent's Ugeplan and in the staff "Se som forældre ser det" preview.
 */
export function ParentWeekPlan({
  plan,
  preselected,
  sfoAfterSchool,
  now = new Date(),
}: ParentWeekPlanProps) {
  const monday = plan.weekStartDate ? new Date(`${plan.weekStartDate}T00:00:00`) : null
  const dateOf = (index: number) =>
    monday ? new Date(monday.getFullYear(), monday.getMonth(), monday.getDate() + index) : null

  const preselectedTime = preselected.getTime()
  const preselectedIndex = WEEKDAY_KEYS.findIndex(
    (_, i) => dateOf(i)?.getTime() === preselectedTime
  )
  const [selected, setSelected] = useState(Math.max(preselectedIndex, 0))
  const tabRefs = useRef<(HTMLButtonElement | null)[]>([])

  const slotsByDay = WEEKDAY_KEYS.map((day) =>
    (plan.slots ?? [])
      .filter((s) => s.weekday === day)
      .sort((a, b) => a.startTime.localeCompare(b.startTime))
  )

  const today = startOfDay(now).getTime()
  function relativeLabel(index: number): string | null {
    const date = dateOf(index)
    if (!date || date.getTime() !== preselectedTime) return null
    const days = Math.round((date.getTime() - today) / 86400000)
    if (days === 0) return 'I dag'
    return days === 1 ? 'I morgen' : null
  }

  function handleTabKey(e: React.KeyboardEvent) {
    const step = e.key === 'ArrowRight' ? 1 : e.key === 'ArrowLeft' ? -1 : 0
    if (!step) return
    e.preventDefault()
    const next = (selected + step + WEEKDAY_KEYS.length) % WEEKDAY_KEYS.length
    setSelected(next)
    tabRefs.current[next]?.focus()
  }

  const selectedDate = dateOf(selected)
  const selectedSlots = slotsByDay[selected]
  const holidayTitle = (plan.holidayDays ?? []).find(
    (h) => h.weekday === WEEKDAY_KEYS[selected]
  )?.title

  return (
    <div className="space-y-3">
      {plan.notes && (
        <GeneralCard
          notes={plan.notes}
          updatedBy={plan.notesUpdatedBy}
          updatedAt={plan.notesUpdatedAt}
        />
      )}

      {plan.isHolidayWeek ? (
        <div className="p-4 rounded-xl bg-blue-50 text-blue-900 font-semibold">
          {plan.holidayTitle ?? 'Ferie'}
        </div>
      ) : (
        <>
          <div>
            <div
              role="tablist"
              aria-label="Dag"
              className="grid grid-cols-5 gap-1 p-1 bg-white rounded-xl border border-gray-200"
            >
              {WEEKDAY_KEYS.map((day, i) => {
                const hasHomework = slotsByDay[i].some((s) => s.lektier)
                const hasChange = slotsByDay[i].some(isChanged)
                return (
                  <button
                    key={day}
                    ref={(el) => {
                      tabRefs.current[i] = el
                    }}
                    type="button"
                    role="tab"
                    id={`weekplan-tab-${day}`}
                    aria-selected={i === selected}
                    aria-controls="weekplan-day-panel"
                    tabIndex={i === selected ? 0 : -1}
                    onClick={() => setSelected(i)}
                    onKeyDown={handleTabKey}
                    data-testid={`weekplan-day-toggle-${day}`}
                    className="relative h-16 rounded-lg flex flex-col items-center justify-center text-gray-800 hover:bg-gray-50 aria-selected:bg-brand-600 aria-selected:text-white aria-selected:hover:bg-brand-600"
                  >
                    <span aria-hidden="true" className="text-xs font-semibold">
                      {relativeLabel(i) ?? SHORT_WEEKDAYS[i]}
                    </span>
                    <span className="sr-only">
                      {relativeLabel(i) ? `${relativeLabel(i)}, ` : ''}
                      {WEEKDAY_LABELS[i]}
                    </span>
                    <span className="font-bold leading-tight">{dateOf(i)?.getDate()}</span>
                    <span className="absolute bottom-1.5 flex gap-1">
                      {hasHomework && (
                        <>
                          <span className="w-1.5 h-1.5 rounded-full bg-sky-500" />
                          <span className="sr-only">lektier</span>
                        </>
                      )}
                      {hasChange && (
                        <>
                          <span className="w-1.5 h-1.5 rounded-full bg-amber-500" />
                          <span className="sr-only">ændring</span>
                        </>
                      )}
                    </span>
                  </button>
                )
              })}
            </div>
            <p className="mt-1.5 text-xs text-gray-700 flex gap-3 px-1">
              <span className="flex items-center gap-1">
                <span className="w-1.5 h-1.5 rounded-full bg-sky-500" />
                lektier
              </span>
              <span className="flex items-center gap-1">
                <span className="w-1.5 h-1.5 rounded-full bg-amber-500" />
                ændring
              </span>
            </p>
          </div>

          <div
            role="tabpanel"
            id="weekplan-day-panel"
            aria-labelledby={`weekplan-tab-${WEEKDAY_KEYS[selected]}`}
            className="space-y-2"
          >
            {selectedDate && (
              <DaySummary
                date={selectedDate}
                relative={relativeLabel(selected)}
                slots={selectedSlots}
                holidayTitle={holidayTitle}
                sfoAfterSchool={sfoAfterSchool}
              />
            )}
            {selectedSlots.length > 0 ? (
              <ol className="bg-white rounded-xl border border-gray-200 px-4 divide-y divide-gray-100 overflow-hidden">
                {selectedSlots.map((s) => (
                  <LessonRow key={s.schemaSlotId} slot={s} />
                ))}
              </ol>
            ) : (
              !holidayTitle && <p className="text-gray-700 px-1">Ingen lektioner denne dag.</p>
            )}
          </div>
        </>
      )}
    </div>
  )
}
