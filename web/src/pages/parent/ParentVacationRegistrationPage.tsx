import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { usePageTitle } from '../../hooks/usePageTitle'
import {
  getApiV1VacationRegistrationOpenOptions,
  getApiV1VacationRegistrationByIdMyEntriesOptions,
  putApiV1VacationRegistrationByIdEntriesByStudentIdMutation,
} from '../../api/generated/@tanstack/react-query.gen'
import type {
  VacationRegistrationControllerWindowDto as WindowDto,
  VacationRegistrationControllerMyEntryDto as MyEntryDto,
  VacationRegistrationGranularity,
} from '../../api/generated/types.gen'

// ── Date utilities ────────────────────────────────────────────────────────────

function getIsoWeek(date: Date): number {
  const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()))
  const dayNum = d.getUTCDay() || 7
  d.setUTCDate(d.getUTCDate() + 4 - dayNum)
  const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1))
  return Math.ceil(((d.getTime() - yearStart.getTime()) / 86400000 + 1) / 7)
}

function getMondayOfWeek(date: Date): Date {
  const d = new Date(date)
  const day = d.getDay()
  const diff = day === 0 ? -6 : 1 - day
  d.setDate(d.getDate() + diff)
  d.setHours(0, 0, 0, 0)
  return d
}

function toIso(date: Date): string {
  return date.toISOString().slice(0, 10)
}

function parseDate(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number)
  return new Date(y, m - 1, d)
}

// Returns Monday ISO strings for each week that overlaps [start, end]
function weeksInRange(start: string, end: string): string[] {
  const startDate = parseDate(start)
  const endDate = parseDate(end)
  const monday = getMondayOfWeek(startDate)
  const weeks: string[] = []
  const cur = new Date(monday)
  while (cur <= endDate) {
    weeks.push(toIso(cur))
    cur.setDate(cur.getDate() + 7)
  }
  return weeks
}

// Returns ISO date strings for Mon–Fri within [start, end]
function daysInRange(start: string, end: string): string[] {
  const startDate = parseDate(start)
  const endDate = parseDate(end)
  const days: string[] = []
  const cur = new Date(startDate)
  while (cur <= endDate) {
    const dow = cur.getDay()
    if (dow >= 1 && dow <= 5) {
      days.push(toIso(cur))
    }
    cur.setDate(cur.getDate() + 1)
  }
  return days
}

// Group days by ISO week Monday key
function groupByWeek(days: string[]): Map<string, string[]> {
  const map = new Map<string, string[]>()
  for (const d of days) {
    const monday = toIso(getMondayOfWeek(parseDate(d)))
    if (!map.has(monday)) map.set(monday, [])
    map.get(monday)!.push(d)
  }
  return map
}

function weekLabel(monday: string): string {
  const d = parseDate(monday)
  const end = new Date(d)
  end.setDate(end.getDate() + 4)
  return `Uge ${getIsoWeek(d)} · ${d.toLocaleDateString('da-DK', { day: 'numeric', month: 'short' })} – ${end.toLocaleDateString('da-DK', { day: 'numeric', month: 'short' })}`
}

function dayLabel(iso: string): string {
  const d = parseDate(iso)
  return d.toLocaleDateString('da-DK', { weekday: 'short', day: 'numeric', month: 'short' })
}

function formatDate(d: string | undefined) {
  if (!d) return ''
  return new Date(d).toLocaleDateString('da-DK', { day: 'numeric', month: 'long' })
}

// ── Week picker ───────────────────────────────────────────────────────────────

function WeekPicker({
  weeks,
  selected,
  onChange,
}: {
  weeks: string[]
  selected: Set<string>
  onChange: (next: Set<string>) => void
}) {
  const allSelected = weeks.every((w) => selected.has(w))

  function toggle(w: string) {
    const next = new Set(selected)
    if (next.has(w)) next.delete(w)
    else next.add(w)
    onChange(next)
  }

  function toggleAll() {
    if (allSelected) {
      const next = new Set(selected)
      for (const w of weeks) next.delete(w)
      onChange(next)
    } else {
      onChange(new Set([...selected, ...weeks]))
    }
  }

  return (
    <div className="space-y-2">
      <label className="flex items-center gap-2 cursor-pointer text-sm font-medium text-gray-700">
        <input
          type="checkbox"
          checked={allSelected}
          onChange={toggleAll}
          className="accent-brand-600"
        />
        Vælg alle uger
      </label>
      <div className="space-y-1 pl-1">
        {weeks.map((w) => (
          <label key={w} className="flex items-center gap-2 cursor-pointer">
            <input
              type="checkbox"
              checked={selected.has(w)}
              onChange={() => toggle(w)}
              className="accent-brand-600"
            />
            <span className="text-sm text-gray-700">{weekLabel(w)}</span>
          </label>
        ))}
      </div>
    </div>
  )
}

// ── Day picker ────────────────────────────────────────────────────────────────

function DayPicker({
  days,
  selected,
  onChange,
}: {
  days: string[]
  selected: Set<string>
  onChange: (next: Set<string>) => void
}) {
  const grouped = groupByWeek(days)
  const allSelected = days.every((d) => selected.has(d))

  function toggle(d: string) {
    const next = new Set(selected)
    if (next.has(d)) next.delete(d)
    else next.add(d)
    onChange(next)
  }

  function toggleWeek(weekDays: string[]) {
    const allIn = weekDays.every((d) => selected.has(d))
    const next = new Set(selected)
    if (allIn) {
      for (const d of weekDays) next.delete(d)
    } else {
      for (const d of weekDays) next.add(d)
    }
    onChange(next)
  }

  function toggleAll() {
    if (allSelected) onChange(new Set())
    else onChange(new Set(days))
  }

  return (
    <div className="space-y-3">
      <label className="flex items-center gap-2 cursor-pointer text-sm font-medium text-gray-700">
        <input
          type="checkbox"
          checked={allSelected}
          onChange={toggleAll}
          className="accent-brand-600"
        />
        Vælg alle dage
      </label>
      {Array.from(grouped.entries()).map(([monday, weekDays]) => {
        const allInWeek = weekDays.every((d) => selected.has(d))
        return (
          <div key={monday} className="pl-1">
            <label className="flex items-center gap-2 cursor-pointer text-sm font-medium text-gray-600 mb-1">
              <input
                type="checkbox"
                checked={allInWeek}
                onChange={() => toggleWeek(weekDays)}
                className="accent-brand-600"
              />
              {weekLabel(monday)}
            </label>
            <div className="pl-5 space-y-1">
              {weekDays.map((d) => (
                <label key={d} className="flex items-center gap-2 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={selected.has(d)}
                    onChange={() => toggle(d)}
                    className="accent-brand-600"
                  />
                  <span className="text-sm text-gray-700 capitalize">{dayLabel(d)}</span>
                </label>
              ))}
            </div>
          </div>
        )
      })}
    </div>
  )
}

// ── Child registration card ───────────────────────────────────────────────────

function ChildCard({ window_, entry }: { window_: WindowDto; entry: MyEntryDto }) {
  const qc = useQueryClient()
  const MY_ENTRIES_KEY = [
    { _id: 'getApiV1VacationRegistrationByIdMyEntries', path: { id: window_.id } },
  ] as const

  const granularity: VacationRegistrationGranularity = window_.granularity ?? 'Weeks'
  const careStart = window_.careStartDate
  const careEnd = window_.careEndDate

  const allSlots =
    careStart && careEnd
      ? granularity === 'Weeks'
        ? weeksInRange(careStart, careEnd)
        : daysInRange(careStart, careEnd)
      : []

  const [selected, setSelected] = useState<Set<string>>(() => new Set(entry.selectedDates ?? []))
  const [note, setNote] = useState(entry.note ?? '')
  const [saved, setSaved] = useState(!!entry.submittedAt)

  const upsertMutation = useMutation({
    ...putApiV1VacationRegistrationByIdEntriesByStudentIdMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: MY_ENTRIES_KEY })
      setSaved(true)
    },
  })

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    if (!window_.id || !entry.studentId) return
    upsertMutation.mutate({
      path: { id: window_.id, studentId: entry.studentId },
      body: { selectedDates: Array.from(selected), note: note || null },
    })
  }

  const isDirty =
    JSON.stringify(Array.from(selected).sort()) !==
      JSON.stringify([...(entry.selectedDates ?? [])].sort()) || note !== (entry.note ?? '')

  return (
    <div className="bg-white border border-gray-200 rounded-xl p-5">
      <div className="flex items-center justify-between mb-4">
        <h3 className="font-semibold text-gray-900">{entry.studentName}</h3>
        {saved && !isDirty && (
          <span className="text-xs text-green-700 bg-green-50 border border-green-200 px-2 py-0.5 rounded">
            Gemt
          </span>
        )}
      </div>

      <form onSubmit={handleSubmit} className="space-y-4">
        {granularity === 'Weeks' ? (
          <WeekPicker weeks={allSlots} selected={selected} onChange={setSelected} />
        ) : (
          <DayPicker days={allSlots} selected={selected} onChange={setSelected} />
        )}

        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Note (valgfrit)</label>
          <textarea
            value={note}
            onChange={(e) => setNote(e.target.value)}
            rows={2}
            placeholder="Eventuelle bemærkninger…"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 resize-none"
          />
        </div>

        {upsertMutation.isError && (
          <p className="text-sm text-red-600">Der opstod en fejl. Prøv igen.</p>
        )}

        <button
          type="submit"
          disabled={upsertMutation.isPending || (!isDirty && saved)}
          className="px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors disabled:opacity-50"
        >
          {upsertMutation.isPending ? 'Gemmer…' : 'Gem tilmelding'}
        </button>
      </form>
    </div>
  )
}

// ── Window section ────────────────────────────────────────────────────────────

function WindowSection({ window_ }: { window_: WindowDto }) {
  const { data: entries = [] } = useQuery({
    ...getApiV1VacationRegistrationByIdMyEntriesOptions({ path: { id: window_.id! } }),
    select: (d) => d as MyEntryDto[],
  })

  return (
    <div className="mb-10">
      <div className="mb-4">
        <h2 className="font-display text-lg font-semibold text-gray-900">{window_.title}</h2>
        <p className="text-sm text-gray-500 mt-0.5">
          Frist: {formatDate(window_.registrationDeadline)}
          {' · '}Pasning: {formatDate(window_.careStartDate)} – {formatDate(window_.careEndDate)}
        </p>
      </div>
      <div className="space-y-4">
        {entries.map((e) => (
          <ChildCard key={e.studentId} window_={window_} entry={e} />
        ))}
      </div>
    </div>
  )
}

// ── Page ──────────────────────────────────────────────────────────────────────

export default function ParentVacationRegistrationPage() {
  usePageTitle('Ferietilmelding')

  const { data: windows = [] } = useQuery({
    ...getApiV1VacationRegistrationOpenOptions(),
    select: (d) => d as WindowDto[],
  })

  return (
    <div className="max-w-2xl mx-auto px-4 py-8">
      <h1 className="font-display text-2xl font-semibold text-gray-900 mb-6">Ferietilmelding</h1>

      {windows.length === 0 && (
        <p className="text-sm text-gray-500 py-8">Ingen åbne ferietilmeldinger.</p>
      )}

      {windows.map((w) => (
        <WindowSection key={w.id} window_={w} />
      ))}
    </div>
  )
}
