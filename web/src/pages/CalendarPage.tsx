import { useState, useEffect } from 'react'
import { Modal } from '../components/Modal'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1CalendarOptions,
  getApiV1CalendarQueryKey,
  getApiV1CalendarDefaultsOptions,
  postApiV1CalendarMutation,
  putApiV1CalendarByIdMutation,
  deleteApiV1CalendarByIdMutation,
  deleteApiV1CalendarByIdOccurrencesByDateMutation,
  deleteApiV1CalendarByIdFromByDateMutation,
} from '../api/generated/@tanstack/react-query.gen'
import type { CalendarEntryDto, DefaultHolidayDto } from '../api/client'
import { usePageTitle } from '../hooks/usePageTitle'
import { useIcsExport } from '../hooks/useIcsExport'
import { DatePicker } from '../components/DatePicker'
import { useAuth } from '../auth/useAuth'
import {
  TYPE_LABELS,
  getSchoolYears,
  isEntryInSchoolYear,
  toDateString,
  CalendarGrid,
} from '../components/calendar/CalendarGrid'

// ─── DeleteOccurrenceDialog ───────────────────────────────────────────────────

type DeleteMode = 'single' | 'from' | 'all'

interface DeleteOccurrenceDialogProps {
  entry: CalendarEntryDto
  occurrenceDate: string // ISO yyyy-MM-dd of the specific occurrence being deleted
  onClose: () => void
  onDeleted: () => void
}

function DeleteOccurrenceDialog({
  entry,
  occurrenceDate,
  onClose,
  onDeleted,
}: DeleteOccurrenceDialogProps) {
  const qc = useQueryClient()
  const [mode, setMode] = useState<DeleteMode>('single')

  useEffect(() => {
    function handleKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKey)
    return () => document.removeEventListener('keydown', handleKey)
  }, [onClose])

  const invalidate = () => qc.invalidateQueries({ queryKey: getApiV1CalendarQueryKey() })

  const deleteSingleMutation = useMutation({
    ...deleteApiV1CalendarByIdOccurrencesByDateMutation(),
    onSuccess: () => {
      invalidate()
      onDeleted()
    },
  })
  const deleteFromMutation = useMutation({
    ...deleteApiV1CalendarByIdFromByDateMutation(),
    onSuccess: () => {
      invalidate()
      onDeleted()
    },
  })
  const deleteAllMutation = useMutation({
    ...deleteApiV1CalendarByIdMutation(),
    onSuccess: () => {
      invalidate()
      onDeleted()
    },
  })

  const isPending =
    deleteSingleMutation.isPending || deleteFromMutation.isPending || deleteAllMutation.isPending

  function handleConfirm() {
    if (isPending) return
    if (mode === 'single') {
      deleteSingleMutation.mutate({ path: { id: entry.id!, date: occurrenceDate } })
    } else if (mode === 'from') {
      deleteFromMutation.mutate({ path: { id: entry.id!, date: occurrenceDate } })
    } else {
      deleteAllMutation.mutate({ path: { id: entry.id! } })
    }
  }

  return (
    <Modal isOpen onClose={onClose} size="sm">
      <div className="px-6 py-5 border-b border-gray-100">
        <h2 className="font-display text-lg font-semibold text-gray-900">Slet begivenhed</h2>
        <p className="text-sm text-gray-500 mt-1">"{entry.title}" gentages. Hvad vil du slette?</p>
      </div>
      <div className="px-6 py-5 space-y-3">
        {(
          [
            ['single', 'Kun denne begivenhed'],
            ['from', 'Denne og alle efterfølgende'],
            ['all', 'Alle begivenheder i serien'],
          ] as [DeleteMode, string][]
        ).map(([val, label]) => (
          <label key={val} className="flex items-center gap-3 cursor-pointer group">
            <input
              type="radio"
              name="deleteMode"
              value={val}
              checked={mode === val}
              onChange={() => setMode(val)}
              className="accent-brand-600 w-4 h-4"
            />
            <span className="text-sm text-gray-800 group-hover:text-gray-900">{label}</span>
          </label>
        ))}
      </div>
      <div className="px-6 py-4 border-t border-gray-100 flex justify-end gap-3">
        <button
          type="button"
          onClick={onClose}
          className="px-4 py-2 text-sm text-gray-600 hover:text-gray-900"
        >
          Annuller
        </button>
        <button
          type="button"
          onClick={handleConfirm}
          disabled={isPending}
          className="px-4 py-2 text-sm bg-red-600 text-white rounded-lg hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          {isPending ? 'Sletter...' : 'Slet'}
        </button>
      </div>
    </Modal>
  )
}

// ─── EntryModal ───────────────────────────────────────────────────────────────

interface EntryModalProps {
  initial?: CalendarEntryDto
  defaultDate?: string
  onClose: () => void
  onSaved: () => void
}

function EntryModal({ initial, defaultDate, onClose, onSaved }: EntryModalProps) {
  const qc = useQueryClient()

  useEffect(() => {
    function handleKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKey)
    return () => document.removeEventListener('keydown', handleKey)
  }, [onClose])

  const today = new Date()
  const todayStr = toDateString(today.getFullYear(), today.getMonth() + 1, today.getDate())
  const initialDate = defaultDate ?? todayStr

  type EntryType = 'Ferie' | 'Lukkedag' | 'Arbejdsdag' | 'Begivenhed'
  const [type, setType] = useState<EntryType>((initial?.type as EntryType) ?? 'Ferie')
  const [title, setTitle] = useState(initial?.title ?? '')
  const [startDate, setStartDate] = useState(initial?.startDate ?? initialDate)
  const [endDate, setEndDate] = useState(initial?.endDate ?? initialDate)
  const [recurrenceRule, setRecurrenceRule] = useState<string>(initial?.recurrenceRule ?? '')
  const [recurrenceEnd, setRecurrenceEnd] = useState<string>(initial?.recurrenceEnd ?? '')

  const dateError = endDate < startDate ? 'Slutdato skal være efter eller lig startdato' : null

  const createMutation = useMutation({
    ...postApiV1CalendarMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: getApiV1CalendarQueryKey() })
      onSaved()
    },
  })
  const updateMutation = useMutation({
    ...putApiV1CalendarByIdMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: getApiV1CalendarQueryKey() })
      onSaved()
    },
  })
  const mutation = initial ? updateMutation : createMutation

  const isPending = createMutation.isPending || updateMutation.isPending
  const isError = createMutation.isError || updateMutation.isError

  function handleSave() {
    if (!title.trim() || dateError || isPending) return
    const body = {
      title,
      type,
      startDate,
      endDate,
      recurrenceRule: recurrenceRule || null,
      recurrenceEnd: recurrenceRule && recurrenceEnd ? recurrenceEnd : null,
    }
    if (initial) {
      updateMutation.mutate({ path: { id: initial.id! }, body })
    } else {
      createMutation.mutate({ body })
    }
  }

  return (
    <Modal isOpen onClose={onClose} title={initial ? 'Rediger begivenhed' : 'Tilføj begivenhed'}>
      <div className="px-6 py-5 space-y-4">
        <div>
          <label
            htmlFor="calendar-entry-type"
            className="block text-sm font-medium text-gray-700 mb-1"
          >
            Type
          </label>
          <select
            id="calendar-entry-type"
            value={type}
            onChange={(e) => setType(e.target.value as EntryType)}
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          >
            {Object.keys(TYPE_LABELS).map((t) => (
              <option key={t} value={t}>
                {TYPE_LABELS[t]}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label
            htmlFor="calendar-entry-title"
            className="block text-sm font-medium text-gray-700 mb-1"
          >
            Titel *
          </label>
          <input
            id="calendar-entry-title"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            placeholder="fx Efterårsferie"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>
        <div>
          <span
            id="calendar-entry-start-label"
            className="block text-sm font-medium text-gray-700 mb-1"
          >
            Startdato
          </span>
          <DatePicker
            labelId="calendar-entry-start-label"
            value={startDate}
            onChange={(v) => {
              setStartDate(v)
              if (endDate < v) setEndDate(v)
            }}
          />
        </div>
        <div>
          <span
            id="calendar-entry-end-label"
            className="block text-sm font-medium text-gray-700 mb-1"
          >
            Slutdato
          </span>
          <DatePicker
            labelId="calendar-entry-end-label"
            value={endDate}
            onChange={setEndDate}
            min={startDate}
          />
          {dateError && <p className="mt-1 text-sm text-red-600">{dateError}</p>}
        </div>
        <div>
          <label
            htmlFor="calendar-entry-recurrence"
            className="block text-sm font-medium text-gray-700 mb-1"
          >
            Gentagelse
          </label>
          <select
            id="calendar-entry-recurrence"
            value={recurrenceRule}
            onChange={(e) => setRecurrenceRule(e.target.value)}
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          >
            <option value="">Ingen gentagelse</option>
            <option value="FREQ=WEEKLY">Ugentlig</option>
            <option value="FREQ=WEEKLY;INTERVAL=2">Hver 2. uge</option>
            <option value="FREQ=MONTHLY">Månedlig</option>
          </select>
        </div>
        {recurrenceRule && (
          <div>
            <span
              id="calendar-entry-recurrence-end-label"
              className="block text-sm font-medium text-gray-700 mb-1"
            >
              Gentag indtil
            </span>
            <DatePicker
              labelId="calendar-entry-recurrence-end-label"
              value={recurrenceEnd}
              onChange={setRecurrenceEnd}
              min={endDate}
            />
          </div>
        )}
        {isError && <p className="text-sm text-red-600">Der opstod en fejl. Prøv igen.</p>}
      </div>
      <div className="px-6 py-4 border-t border-gray-100 flex justify-end gap-3">
        <button
          type="button"
          onClick={onClose}
          className="px-4 py-2 text-sm text-gray-600 hover:text-gray-900"
        >
          Annuller
        </button>
        <button
          type="button"
          onClick={handleSave}
          disabled={!title.trim() || !!dateError || mutation.isPending}
          className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          {mutation.isPending ? 'Gemmer...' : 'Gem'}
        </button>
      </div>
    </Modal>
  )
}

// ─── CalendarPage ─────────────────────────────────────────────────────────────

export default function CalendarPage() {
  usePageTitle('Kalender')
  const qc = useQueryClient()
  const { isAdmin } = useAuth()

  const today = new Date()
  const currentSchoolStartYear =
    today.getMonth() >= 7 ? today.getFullYear() : today.getFullYear() - 1
  const [schoolStartYear, setSchoolStartYear] = useState(currentSchoolStartYear)
  const { startYear, endYear } = getSchoolYears(schoolStartYear)

  const { exportPending, exportDone, exportError, handleExportIcs } = useIcsExport()

  const [createDate, setCreateDate] = useState<string | null>(null)
  const [editingEntry, setEditingEntry] = useState<CalendarEntryDto | null>(null)

  const [deleteTarget, setDeleteTarget] = useState<{
    entry: CalendarEntryDto
    occurrenceDate: string
  } | null>(null)

  const { data: entriesStartYear = [] } = useQuery({
    ...getApiV1CalendarOptions({ query: { year: startYear } }),
    select: (d) => (d ?? []) as CalendarEntryDto[],
  })
  const { data: entriesEndYear = [] } = useQuery({
    ...getApiV1CalendarOptions({ query: { year: endYear } }),
    select: (d) => (d ?? []) as CalendarEntryDto[],
  })

  const allEntries: CalendarEntryDto[] = [
    ...entriesStartYear,
    ...entriesEndYear.filter(
      (e) => !entriesStartYear.some((s) => s.id === e.id && s.startDate === e.startDate)
    ),
  ]

  const schoolYearEntries = allEntries.filter((e) => isEntryInSchoolYear(e, schoolStartYear))
  const hasEntries = schoolYearEntries.length > 0

  const { data: defaults = [] } = useQuery({
    ...getApiV1CalendarDefaultsOptions({ query: { year: schoolStartYear } }),
    enabled: isAdmin && !hasEntries,
    select: (d) => (d ?? []) as DefaultHolidayDto[],
  })

  const seedMutation = useMutation({
    mutationFn: (items: DefaultHolidayDto[]) => {
      const { mutationFn } = postApiV1CalendarMutation()
      return Promise.all(items.map((d) => mutationFn!({ body: d }, undefined as never)))
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: getApiV1CalendarQueryKey() })
    },
  })

  const deleteMutation = useMutation({
    ...deleteApiV1CalendarByIdMutation(),
    onSuccess: () => qc.invalidateQueries({ queryKey: getApiV1CalendarQueryKey() }),
  })

  const yearOptions = Array.from({ length: 5 }, (_, i) => currentSchoolStartYear - 2 + i)

  function handleDeleteEntry(entry: CalendarEntryDto) {
    if (entry.recurrenceRule) {
      setDeleteTarget({ entry, occurrenceDate: entry.startDate! })
    } else {
      if (confirm(`Slet "${entry.title}"?`)) deleteMutation.mutate({ path: { id: entry.id! } })
    }
  }

  return (
    <div className="p-6 lg:p-8 max-w-5xl mx-auto space-y-6">
      {/* Header */}
      <div className="space-y-3 sm:space-y-0 sm:flex sm:items-center sm:justify-between sm:gap-4">
        <h1 className="font-display text-2xl font-semibold text-gray-900">Kalender</h1>
        <div className="flex items-center gap-2 flex-wrap">
          {isAdmin && (
            <button
              onClick={() => {
                const d = new Date()
                setCreateDate(toDateString(d.getFullYear(), d.getMonth() + 1, d.getDate()))
              }}
              className="flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium bg-brand-600 text-white rounded-lg hover:bg-brand-700 transition-colors"
            >
              <svg
                width="14"
                height="14"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2.5"
              >
                <line x1="12" y1="5" x2="12" y2="19" />
                <line x1="5" y1="12" x2="19" y2="12" />
              </svg>
              Tilføj begivenhed
            </button>
          )}
          <select
            value={schoolStartYear}
            onChange={(e) => setSchoolStartYear(Number(e.target.value))}
            className="border border-gray-300 rounded-lg px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
          >
            {yearOptions.map((y) => (
              <option key={y} value={y}>
                {y}/{y + 1}
              </option>
            ))}
          </select>
          <button
            onClick={handleExportIcs}
            disabled={exportPending}
            title="Åbn filen i Google Calendar, Outlook eller Kalender (iPhone/Mac) for at importere begivenhederne."
            className="border border-gray-300 text-gray-600 hover:bg-gray-50 rounded-lg px-3 py-1.5 text-sm disabled:opacity-50 transition-colors"
          >
            {exportPending ? 'Tilføjer...' : 'Tilføj til kalender'}
          </button>
          {isAdmin && !hasEntries && defaults.length > 0 && (
            <button
              onClick={() => seedMutation.mutate(defaults)}
              disabled={seedMutation.isPending}
              className="border border-brand-600 text-brand-600 hover:bg-brand-50 rounded-lg px-3 py-1.5 text-sm disabled:opacity-50 transition-colors"
            >
              {seedMutation.isPending ? 'Tilføjer...' : 'Tilføj standardferier'}
            </button>
          )}
        </div>
      </div>

      {/* Export confirmation */}
      {exportDone && (
        <div className="bg-green-50 border border-green-200 rounded-xl px-5 py-3 text-sm text-green-800">
          Filen er hentet. Dobbeltklik på den for at importere – eller åbn din kalender og importer
          derfra.
        </div>
      )}

      {exportError && (
        <div className="bg-red-50 border border-red-200 rounded-xl px-5 py-3 text-sm text-red-800">
          Kunne ikke hente kalenderfilen. Prøv igen.
        </div>
      )}

      {/* Empty state */}
      {isAdmin && !hasEntries && (
        <div className="bg-brand-50 border border-brand-200 rounded-xl p-5">
          <p className="text-sm text-brand-800 font-medium mb-1">Ingen begivenheder endnu</p>
          <p className="text-sm text-brand-700">
            Tilføj ferier, lukkedage og begivenheder for skoleåret {startYear}/{endYear}. Du kan
            bruge &quot;Tilføj standardferier&quot; knappen øverst for at komme hurtigt i gang med
            danske skoleferier.
          </p>
        </div>
      )}

      <CalendarGrid
        schoolStartYear={schoolStartYear}
        entries={schoolYearEntries}
        isAdmin={isAdmin}
        onCreateForDate={(dateStr) => setCreateDate(dateStr)}
        onEdit={(entry) => setEditingEntry(entry)}
        onDelete={handleDeleteEntry}
      />

      {createDate !== null && (
        <EntryModal
          defaultDate={createDate}
          onClose={() => setCreateDate(null)}
          onSaved={() => setCreateDate(null)}
        />
      )}
      {editingEntry && (
        <EntryModal
          initial={editingEntry}
          onClose={() => setEditingEntry(null)}
          onSaved={() => setEditingEntry(null)}
        />
      )}
      {deleteTarget && (
        <DeleteOccurrenceDialog
          entry={deleteTarget.entry}
          occurrenceDate={deleteTarget.occurrenceDate}
          onClose={() => setDeleteTarget(null)}
          onDeleted={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
