import { useState, useRef, useEffect } from 'react'
import { Modal } from '../components/Modal'
import { MarkdownTextarea, type SaveStatus } from '../components/markdown/MarkdownTextarea'
import { Markdown } from '../components/markdown/Markdown'
import { Link } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1SfoShiftsOptions,
  getApiV1SfoShiftsQueryKey,
  postApiV1SfoShiftsMutation,
  putApiV1SfoShiftsByIdMutation,
  deleteApiV1SfoShiftsByIdMutation,
  postApiV1SfoShiftsByIdStaffByStaffIdMutation,
  deleteApiV1SfoShiftsByIdStaffByStaffIdMutation,
  getApiV1StaffOptions,
  getApiV1SfoUgeplanOptions,
  getApiV1SfoUgeplanQueryKey,
  putApiV1SfoUgeplanShiftsMutation,
  putApiV1SfoUgeplanGenereltMutation,
} from '../api/generated/@tanstack/react-query.gen'
import type { SfoShiftDto, SfoWeekPlanShiftDto } from '../api/client'
import { usePageTitle } from '../hooks/usePageTitle'

function getISOWeek(date: Date): number {
  const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()))
  d.setUTCDate(d.getUTCDate() + 4 - (d.getUTCDay() || 7))
  const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1))
  return Math.ceil(((d.getTime() - yearStart.getTime()) / 86400000 + 1) / 7)
}

function getISOWeekYear(date: Date): number {
  const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()))
  d.setUTCDate(d.getUTCDate() + 4 - (d.getUTCDay() || 7))
  return d.getUTCFullYear()
}

function getISOWeeksInYear(year: number): number {
  const dec28 = new Date(Date.UTC(year, 11, 28))
  return getISOWeek(dec28)
}

const DAY_NAMES = ['', 'Mandag', 'Tirsdag', 'Onsdag', 'Torsdag', 'Fredag']
const DAY_NAMES_SHORT = ['', 'Man', 'Tir', 'Ons', 'Tor', 'Fre']

interface ShiftForm {
  dayOfWeek: number
  startTime: string
  endTime: string
  label: string
}

const emptyForm = (): ShiftForm => ({
  dayOfWeek: 1,
  startTime: '06:30',
  endTime: '08:00',
  label: '',
})

function SfoGenereltEditor({
  isoYear,
  isoWeek,
  value,
}: {
  isoYear: number
  isoWeek: number
  value: string | null | undefined
}) {
  const qc = useQueryClient()
  const [text, setText] = useState(value ?? '')
  const [saveStatus, setSaveStatus] = useState<SaveStatus>('idle')

  // A save that resolves after the user has kept typing must not have its
  // (older) result pushed back into the textarea. While a save is in flight we
  // stop syncing the incoming `value`, and once it resolves we only adopt a
  // server value that isn't just the stale echo of what we already saved.
  const pendingSaveRef = useRef(false)
  const lastSavedRef = useRef(value ?? '')
  // The value confirmed by the server before the in-flight save, so onError can
  // undo handleBlur's optimistic advance of lastSavedRef.
  const prevSavedRef = useRef(value ?? '')

  useEffect(() => {
    if (pendingSaveRef.current) return
    const incoming = value ?? ''
    // Ignore a refetch that only echoes the value we last saved while the user
    // has since edited further — that edit is the newer state.
    if (incoming === lastSavedRef.current && text !== incoming) return
    lastSavedRef.current = incoming
    prevSavedRef.current = incoming
    setText(incoming)
  }, [value, text])

  // Was the value this save submitted still the current editor text when it
  // resolved? If the user has since typed on, an earlier save landing must not
  // stamp its outcome onto the newer, unsaved edit.
  const isCurrentEdit = (submitted: string | null) => (submitted ?? '') === (text || '')

  const mutation = useMutation({
    ...putApiV1SfoUgeplanGenereltMutation(),
    onSuccess: (_data, variables) => {
      pendingSaveRef.current = false
      prevSavedRef.current = lastSavedRef.current
      void qc.invalidateQueries({
        queryKey: getApiV1SfoUgeplanQueryKey({ query: { isoYear, isoWeek } }),
      })
      if (isCurrentEdit(variables.body?.generelt ?? null)) {
        setSaveStatus('saved')
      }
    },
    onError: (_err, variables) => {
      pendingSaveRef.current = false
      // handleBlur optimistically moved lastSavedRef to the value it just tried
      // to save. That save failed, so roll it back to the last confirmed value —
      // otherwise an unchanged retry blur short-circuits and never re-fires.
      lastSavedRef.current = prevSavedRef.current
      if (isCurrentEdit(variables.body?.generelt ?? null)) {
        setSaveStatus('error')
      }
    },
  })

  function handleChange(next: string) {
    setText(next)
    setSaveStatus('idle')
  }

  function handleBlur() {
    const normalized = text || null
    if (normalized === (lastSavedRef.current || null)) return
    pendingSaveRef.current = true
    prevSavedRef.current = lastSavedRef.current
    lastSavedRef.current = text
    setSaveStatus('saving')
    mutation.mutate({ body: { isoYear, isoWeek, generelt: normalized } })
  }

  return (
    <div className="px-4 sm:px-6 lg:px-8 pt-4 sm:pt-6">
      <div className="max-w-5xl mx-auto">
        <label className="block text-sm font-medium text-gray-700 mb-1">Generelt for ugen</label>
        <MarkdownTextarea
          value={text}
          onChange={handleChange}
          onBlur={handleBlur}
          rows={5}
          maxLength={8000}
          placeholder="Ture, huskeliste, kommende temaer…"
          aria-label="Generelt for ugen"
          data-testid="sfo-generelt-editor"
          saveStatus={saveStatus}
          className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent bg-white"
        />
      </div>
    </div>
  )
}

export default function SfoPage() {
  usePageTitle('SFO vagtplan')
  const qc = useQueryClient()

  const { data: shifts, isLoading } = useQuery(getApiV1SfoShiftsOptions())
  const { data: staff } = useQuery(getApiV1StaffOptions())

  const [showForm, setShowForm] = useState(false)
  const [editingShift, setEditingShift] = useState<SfoShiftDto | null>(null)
  const [form, setForm] = useState<ShiftForm>(emptyForm())
  const [allDays, setAllDays] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  const [isoYear, setIsoYear] = useState(() => getISOWeekYear(new Date()))
  const [isoWeek, setIsoWeek] = useState(() => getISOWeek(new Date()))
  const [showYearPicker, setShowYearPicker] = useState(false)
  const [selectedCell, setSelectedCell] = useState<{
    shift: SfoShiftDto
    weekShift: SfoWeekPlanShiftDto | undefined
  } | null>(null)

  const { data: weekPlan } = useQuery(getApiV1SfoUgeplanOptions({ query: { isoYear, isoWeek } }))

  const upsertBeskrivelseMutation = useMutation({
    ...putApiV1SfoUgeplanShiftsMutation(),
    onSuccess: () => {
      void qc.invalidateQueries({
        queryKey: getApiV1SfoUgeplanQueryKey({ query: { isoYear, isoWeek } }),
      })
      setSelectedCell(null)
    },
  })

  function prevWeek() {
    if (isoWeek === 1) {
      setIsoYear((y) => y - 1)
      setIsoWeek(getISOWeeksInYear(isoYear - 1))
    } else setIsoWeek((w) => w - 1)
  }
  function nextWeek() {
    const weeksInYear = getISOWeeksInYear(isoYear)
    if (isoWeek === weeksInYear) {
      setIsoYear((y) => y + 1)
      setIsoWeek(1)
    } else setIsoWeek((w) => w + 1)
  }
  function goToThisWeek() {
    setIsoYear(getISOWeekYear(new Date()))
    setIsoWeek(getISOWeek(new Date()))
  }

  const invalidate = () => qc.invalidateQueries({ queryKey: getApiV1SfoShiftsQueryKey() })

  const createMutation = useMutation({
    ...postApiV1SfoShiftsMutation(),
    onSuccess: () => {
      invalidate()
      setShowForm(false)
      setForm(emptyForm())
    },
    onError: () => setFormError('Vagten kunne ikke oprettes.'),
  })

  const updateMutation = useMutation({
    ...putApiV1SfoShiftsByIdMutation(),
    onSuccess: () => {
      invalidate()
      setEditingShift(null)
    },
    onError: () => setFormError('Vagten kunne ikke opdateres.'),
  })

  const deleteMutation = useMutation({
    ...deleteApiV1SfoShiftsByIdMutation(),
    onSuccess: () => invalidate(),
  })

  const assignStaffMutation = useMutation({
    ...postApiV1SfoShiftsByIdStaffByStaffIdMutation(),
    onSuccess: () => invalidate(),
  })

  const removeStaffMutation = useMutation({
    ...deleteApiV1SfoShiftsByIdStaffByStaffIdMutation(),
    onSuccess: () => invalidate(),
  })

  function openCreate() {
    setEditingShift(null)
    setForm(emptyForm())
    setAllDays(false)
    setFormError(null)
    setShowForm(true)
  }

  function openEdit(shift: SfoShiftDto) {
    setEditingShift(shift)
    setForm({
      dayOfWeek: shift.dayOfWeek ?? 1,
      startTime: shift.startTime ?? '06:30',
      endTime: shift.endTime ?? '08:00',
      label: shift.label ?? '',
    })
    setFormError(null)
    setShowForm(true)
  }

  async function handleSave() {
    const baseBody = {
      startTime: form.startTime,
      endTime: form.endTime,
      label: form.label || null,
    }
    if (editingShift?.id) {
      updateMutation.mutate({
        path: { id: editingShift.id },
        body: { ...baseBody, dayOfWeek: form.dayOfWeek },
      })
      return
    }
    if (allDays) {
      try {
        await Promise.all(
          [1, 2, 3, 4, 5].map((day) =>
            createMutation.mutateAsync({ body: { ...baseBody, dayOfWeek: day } })
          )
        )
        invalidate()
        setShowForm(false)
        setForm(emptyForm())
      } catch {
        setFormError('En eller flere vagter kunne ikke oprettes.')
      }
    } else {
      createMutation.mutate({ body: { ...baseBody, dayOfWeek: form.dayOfWeek } })
    }
  }

  // Collect unique time slots across all days, sorted by start time
  const uniqueSlotKeys = new Map<
    string,
    { startTime: string; endTime: string; label: string | null | undefined }
  >()
  for (const shift of shifts ?? []) {
    const key = `${shift.startTime}–${shift.endTime}`
    if (!uniqueSlotKeys.has(key)) {
      uniqueSlotKeys.set(key, {
        startTime: shift.startTime ?? '',
        endTime: shift.endTime ?? '',
        label: shift.label,
      })
    }
  }
  const sortedTimeSlots = Array.from(uniqueSlotKeys.entries()).sort((a, b) =>
    a[1].startTime.localeCompare(b[1].startTime)
  )

  if (isLoading) {
    return (
      <div className="flex flex-col h-full">
        <div className="shrink-0 sticky top-0 z-10 bg-white border-b border-gray-200 px-4 lg:px-6 py-3">
          <div className="h-6 w-48 bg-gray-200 rounded animate-pulse" />
        </div>
        <div className="flex-1 p-6">
          <div className="h-64 bg-gray-100 rounded-xl animate-pulse" />
        </div>
      </div>
    )
  }

  return (
    <div>
      {/* Top bar */}
      <div className="sticky top-0 z-10 bg-white border-b border-gray-200 px-3 sm:px-4 lg:px-8 py-3 flex items-center justify-between gap-1 sm:gap-2">
        <h1 className="font-display text-sm sm:text-base font-semibold text-gray-900 shrink-0 truncate">
          SFO Ugeplan
        </h1>

        {/* Week navigator */}
        <div className="flex items-center gap-0 sm:gap-1 shrink-0">
          <button
            onClick={prevWeek}
            className="p-1 sm:p-2 rounded-md text-gray-500 hover:bg-gray-100 transition-colors"
            title="Forrige uge"
          >
            <svg
              width="18"
              height="18"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
            >
              <polyline points="15 18 9 12 15 6" />
            </svg>
          </button>
          <div className="flex items-center gap-1">
            <button
              onClick={goToThisWeek}
              className="px-1.5 sm:px-3 py-1.5 text-xs sm:text-sm font-semibold text-gray-900 hover:bg-gray-100 rounded-md transition-colors tabular-nums whitespace-nowrap"
              title="Gå til denne uge"
            >
              Uge {isoWeek}
            </button>
            <div className="relative">
              <button
                onClick={() => setShowYearPicker((p) => !p)}
                className="px-1 sm:px-2 py-1.5 text-xs sm:text-sm font-semibold text-brand-600 hover:bg-brand-50 rounded-md transition-colors tabular-nums"
                title="Skift år"
              >
                {isoYear}
              </button>
              {showYearPicker && (
                <>
                  <button
                    type="button"
                    tabIndex={-1}
                    aria-label="Luk årvælger"
                    className="fixed inset-0 z-20"
                    onClick={() => setShowYearPicker(false)}
                  />
                  <div className="absolute left-1/2 -translate-x-1/2 top-9 z-30 bg-white border border-gray-200 rounded-lg shadow-lg py-1 min-w-[90px]">
                    {[-2, -1, 0, 1, 2].map((offset) => {
                      const y = getISOWeekYear(new Date()) + offset
                      return (
                        <button
                          key={y}
                          onClick={() => {
                            setIsoYear(y)
                            setShowYearPicker(false)
                          }}
                          className={`w-full px-4 py-2 text-sm text-center hover:bg-gray-50 transition-colors tabular-nums ${y === isoYear ? 'font-semibold text-brand-600' : 'text-gray-700'}`}
                        >
                          {y}
                        </button>
                      )
                    })}
                  </div>
                </>
              )}
            </div>
          </div>
          <button
            onClick={nextWeek}
            className="p-1 sm:p-2 rounded-md text-gray-500 hover:bg-gray-100 transition-colors"
            title="Næste uge"
          >
            <svg
              width="18"
              height="18"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
            >
              <polyline points="9 18 15 12 9 6" />
            </svg>
          </button>
        </div>

        <div className="flex items-center gap-1 sm:gap-2 shrink-0">
          <Link
            to={`/udskriv/sfo?isoYear=${isoYear}&isoWeek=${isoWeek}`}
            target="_blank"
            className="flex items-center gap-2 p-2 sm:px-3 sm:py-2 text-sm border border-gray-300 text-gray-700 rounded-lg hover:bg-gray-50 transition-colors"
            title="Udskriv"
          >
            <svg
              width="16"
              height="16"
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
            <span className="hidden sm:inline">Udskriv</span>
          </Link>
          <button
            onClick={openCreate}
            aria-label="Ny vagt"
            className="flex items-center gap-1.5 sm:gap-2 p-2 sm:px-3 sm:py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 transition-colors"
          >
            <svg
              width="15"
              height="15"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.5"
            >
              <line x1="12" y1="5" x2="12" y2="19" />
              <line x1="5" y1="12" x2="19" y2="12" />
            </svg>
            <span className="hidden sm:inline" aria-hidden="true">
              Ny vagt
            </span>
          </button>
        </div>
      </div>

      <SfoGenereltEditor isoYear={isoYear} isoWeek={isoWeek} value={weekPlan?.generelt} />

      {/* Grid area */}
      <div className="p-4 sm:p-6 lg:p-8">
        {(shifts ?? []).length === 0 ? (
          <div className="py-16 text-center">
            <p className="text-sm text-gray-500">Ingen vagtblokke oprettet endnu.</p>
            <button
              onClick={openCreate}
              className="mt-3 px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 transition-colors"
            >
              Opret første vagt
            </button>
          </div>
        ) : (
          <div className="max-w-5xl mx-auto rounded-xl border border-gray-200 overflow-x-auto">
            <div className="grid grid-cols-[56px_1fr_1fr_1fr_1fr_1fr] sm:grid-cols-[80px_1fr_1fr_1fr_1fr_1fr] min-w-[360px] sm:min-w-[520px]">
              {/* Header row */}
              <div className="bg-gray-50 border-b border-r border-gray-200 p-1.5 sm:p-3" />
              {[1, 2, 3, 4, 5].map((day) => (
                <div
                  key={day}
                  className="bg-gray-50 border-b border-r border-gray-200 py-1.5 px-1 sm:py-3 sm:px-3 text-center"
                >
                  <div className="text-xs sm:text-sm font-semibold text-gray-700 hidden sm:block">
                    {DAY_NAMES[day]}
                  </div>
                  <div className="text-xs sm:text-sm font-semibold text-gray-700 sm:hidden">
                    {DAY_NAMES_SHORT[day]}
                  </div>
                </div>
              ))}

              {/* Time slot rows */}
              {sortedTimeSlots.map(([slotKey, slotMeta]) => (
                <div key={slotKey} className="contents">
                  {/* Row label */}
                  <div className="bg-gray-50 border-b border-r border-gray-200 p-1.5 sm:p-3 flex flex-col justify-center overflow-hidden">
                    <span className="text-[10px] sm:text-xs text-gray-500 font-mono leading-tight whitespace-nowrap">
                      {slotMeta.startTime}
                    </span>
                    <span className="text-[10px] sm:text-xs text-gray-400 font-mono leading-tight whitespace-nowrap">
                      – {slotMeta.endTime}
                    </span>
                    {slotMeta.label && (
                      <span className="text-xs text-gray-400 mt-1 truncate hidden sm:block">
                        {slotMeta.label}
                      </span>
                    )}
                  </div>

                  {/* Day cells */}
                  {[1, 2, 3, 4, 5].map((day) => {
                    const shift = (shifts ?? []).find(
                      (s) => s.dayOfWeek === day && `${s.startTime}–${s.endTime}` === slotKey
                    )
                    if (!shift) {
                      return (
                        <div
                          key={`empty-${slotKey}-${day}`}
                          className="border-b border-r border-gray-200 bg-gray-50 min-h-[90px] sm:min-h-[160px]"
                        />
                      )
                    }

                    const weekShift = weekPlan?.shifts?.find((ws) => ws.sfoShiftId === shift.id)

                    return (
                      <button
                        key={`cell-${shift.id}`}
                        type="button"
                        className="text-left border-b border-r border-gray-200 bg-white min-h-[90px] sm:min-h-[160px] p-1.5 sm:p-3 flex flex-col gap-1 sm:gap-2 overflow-hidden cursor-pointer hover:bg-brand-50/30 transition-colors"
                        onClick={() => setSelectedCell({ shift, weekShift })}
                      >
                        {(shift.staff ?? []).length > 0 ? (
                          <div className="flex flex-wrap gap-1">
                            {(shift.staff ?? []).map((s) => (
                              <span
                                key={s.id}
                                className="px-1 sm:px-1.5 py-0.5 text-[10px] sm:text-xs bg-brand-50 text-brand-700 rounded-full truncate max-w-full"
                              >
                                {s.name}
                              </span>
                            ))}
                          </div>
                        ) : (
                          <div className="flex items-center gap-1 text-[10px] sm:text-xs text-gray-300">
                            <svg
                              width="12"
                              height="12"
                              viewBox="0 0 24 24"
                              fill="none"
                              stroke="currentColor"
                              strokeWidth="2"
                              className="hidden sm:block shrink-0"
                            >
                              <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" />
                              <circle cx="12" cy="7" r="4" />
                            </svg>
                            <span>Ingen</span>
                          </div>
                        )}
                        <div className="flex-1 min-w-0 hidden sm:block">
                          {weekShift?.beskrivelse ? (
                            <div className="text-xs text-gray-600 line-clamp-4 prose prose-xs max-w-none [&_p]:m-0 [&_ul]:my-0.5 [&_li]:my-0">
                              <Markdown>{weekShift.beskrivelse}</Markdown>
                            </div>
                          ) : (
                            <p className="text-xs text-gray-300 italic">Aktivitet…</p>
                          )}
                        </div>
                      </button>
                    )
                  })}
                </div>
              ))}
            </div>
          </div>
        )}
      </div>

      {/* Cell modal */}
      {selectedCell && (
        <CellModal
          shift={selectedCell.shift}
          weekShift={selectedCell.weekShift}
          isoYear={isoYear}
          isoWeek={isoWeek}
          staff={staff ?? []}
          onClose={() => setSelectedCell(null)}
          onSaveBeskrivelse={(beskrivelse) =>
            upsertBeskrivelseMutation.mutate({
              body: { isoYear, isoWeek, sfoShiftId: selectedCell.shift.id!, beskrivelse },
            })
          }
          isSavingBeskrivelse={upsertBeskrivelseMutation.isPending}
          onAssignStaff={(staffId) =>
            assignStaffMutation.mutate({ path: { id: selectedCell.shift.id!, staffId } })
          }
          onRemoveStaff={(staffId) =>
            removeStaffMutation.mutate({ path: { id: selectedCell.shift.id!, staffId } })
          }
          onEdit={() => {
            setSelectedCell(null)
            openEdit(selectedCell.shift)
          }}
          onDelete={() => {
            deleteMutation.mutate({ path: { id: selectedCell.shift.id! } })
            setSelectedCell(null)
          }}
        />
      )}

      {/* Shift form modal */}
      <Modal isOpen={showForm} onClose={() => setShowForm(false)} size="sm">
        <div className="p-6 space-y-4">
          <h2 className="text-base font-semibold text-gray-900">
            {editingShift ? 'Rediger vagt' : 'Ny vagtblok'}
          </h2>

          {!editingShift && (
            <label className="flex items-center gap-2 cursor-pointer select-none">
              <input
                type="checkbox"
                checked={allDays}
                onChange={(e) => setAllDays(e.target.checked)}
                className="w-4 h-4 rounded border-gray-300 text-brand-600 focus:ring-brand-500"
              />
              <span className="text-sm font-medium text-gray-700">Alle 5 dage</span>
            </label>
          )}

          {!allDays && (
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Ugedag</label>
              <select
                value={form.dayOfWeek}
                onChange={(e) => setForm((f) => ({ ...f, dayOfWeek: Number(e.target.value) }))}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
              >
                {[1, 2, 3, 4, 5].map((d) => (
                  <option key={d} value={d}>
                    {DAY_NAMES[d]}
                  </option>
                ))}
              </select>
            </div>
          )}

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Start</label>
              <TimeSelect
                value={form.startTime}
                onChange={(v) => setForm((f) => ({ ...f, startTime: v }))}
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Slut</label>
              <TimeSelect
                value={form.endTime}
                onChange={(v) => setForm((f) => ({ ...f, endTime: v }))}
              />
            </div>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Betegnelse (valgfri)
            </label>
            <input
              value={form.label}
              onChange={(e) => setForm((f) => ({ ...f, label: e.target.value }))}
              placeholder="f.eks. Morgenvagt"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
            />
          </div>

          {formError && <p className="text-sm text-red-600">{formError}</p>}

          <div className="flex justify-end gap-3 pt-1">
            <button
              onClick={() => setShowForm(false)}
              className="px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors"
            >
              Annuller
            </button>
            <button
              onClick={handleSave}
              disabled={createMutation.isPending || updateMutation.isPending}
              className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 transition-colors"
            >
              {createMutation.isPending || updateMutation.isPending ? 'Gemmer...' : 'Gem'}
            </button>
          </div>
        </div>
      </Modal>
    </div>
  )
}

const HOURS = Array.from({ length: 24 }, (_, i) => String(i).padStart(2, '0'))
const MINUTES = ['00', '15', '30', '45']

function TimeSelect({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const [h, m] = value.split(':')
  return (
    <div className="flex items-center gap-1">
      <select
        value={h}
        onChange={(e) => onChange(`${e.target.value}:${m}`)}
        className="flex-1 px-2 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
      >
        {HOURS.map((hour) => (
          <option key={hour} value={hour}>
            {hour}
          </option>
        ))}
      </select>
      <span className="text-gray-400 font-medium">:</span>
      <select
        value={m}
        onChange={(e) => onChange(`${h}:${e.target.value}`)}
        className="flex-1 px-2 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
      >
        {MINUTES.map((min) => (
          <option key={min} value={min}>
            {min}
          </option>
        ))}
      </select>
    </div>
  )
}

function CellModal({
  shift,
  weekShift,
  isoYear,
  isoWeek,
  staff,
  onClose,
  onSaveBeskrivelse,
  isSavingBeskrivelse,
  onAssignStaff,
  onRemoveStaff,
  onEdit,
  onDelete,
}: {
  shift: SfoShiftDto
  weekShift: SfoWeekPlanShiftDto | undefined
  isoYear: number
  isoWeek: number
  staff: { id?: string; name?: string | null }[]
  onClose: () => void
  onSaveBeskrivelse: (beskrivelse: string | null) => void
  isSavingBeskrivelse: boolean
  onAssignStaff: (staffId: string) => void
  onRemoveStaff: (staffId: string) => void
  onEdit: () => void
  onDelete: () => void
}) {
  const [text, setText] = useState(weekShift?.beskrivelse ?? '')
  const [staffQuery, setStaffQuery] = useState('')
  const [staffOpen, setStaffOpen] = useState(false)
  const comboboxRef = useRef<HTMLDivElement>(null)
  const assignedIds = new Set((shift.staff ?? []).map((s) => s.id))
  const filteredUnassigned = staff.filter(
    (s) =>
      s.id &&
      !assignedIds.has(s.id) &&
      (s.name ?? '').toLowerCase().includes(staffQuery.toLowerCase())
  )

  useEffect(() => {
    function onPointerDown(e: PointerEvent) {
      if (comboboxRef.current && !comboboxRef.current.contains(e.target as Node)) {
        setStaffOpen(false)
        setStaffQuery('')
      }
    }
    document.addEventListener('pointerdown', onPointerDown)
    return () => document.removeEventListener('pointerdown', onPointerDown)
  }, [])

  function handleKeyDown(e: React.KeyboardEvent) {
    if (e.key === 'Escape') {
      if (staffOpen) {
        setStaffOpen(false)
        setStaffQuery('')
      } else onClose()
    }
    if ((e.ctrlKey || e.metaKey) && e.key === 's') {
      e.preventDefault()
      onSaveBeskrivelse(text || null)
    }
  }

  return (
    <Modal
      isOpen
      onClose={onClose}
      contentClassName="bg-white rounded-xl shadow-xl max-w-md w-full mx-4 flex flex-col max-h-[90dvh]"
    >
      {/* Header */}
      <div className="px-5 pt-5 pb-3 border-b border-gray-100">
        <div className="flex items-start justify-between gap-3">
          <div>
            <h2 className="text-base font-semibold text-gray-900">
              {shift.startTime} – {shift.endTime}
              {shift.label ? ` · ${shift.label}` : ''}
            </h2>
            <p className="text-xs text-gray-400 mt-0.5">
              Uge {isoWeek}, {isoYear}
            </p>
          </div>
          <button
            onClick={onClose}
            className="p-1 text-gray-400 hover:text-gray-600 rounded transition-colors shrink-0"
          >
            <svg
              width="16"
              height="16"
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
      </div>

      <div className="overflow-y-auto flex-1 px-5 py-4 space-y-4">
        {/* Staff */}
        <div>
          <p className="text-xs font-medium text-gray-500 uppercase tracking-wide mb-2">
            Medarbejdere
          </p>
          {/* Assigned chips */}
          {(shift.staff ?? []).length > 0 && (
            <div className="flex flex-wrap gap-1.5 mb-2">
              {(shift.staff ?? []).map((s) => (
                <button
                  key={s.id}
                  onClick={() => onRemoveStaff(s.id!)}
                  className="inline-flex items-center gap-1 px-2.5 py-1 rounded-full text-sm bg-brand-600 text-white hover:bg-brand-700 transition-colors"
                >
                  {s.name}
                  <svg
                    width="10"
                    height="10"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="3"
                  >
                    <line x1="18" y1="6" x2="6" y2="18" />
                    <line x1="6" y1="6" x2="18" y2="18" />
                  </svg>
                </button>
              ))}
            </div>
          )}
          {/* Combobox */}
          {staff.length > 0 ? (
            <div ref={comboboxRef} className="relative">
              <div className="flex items-center gap-2 px-3 py-2 border border-gray-200 rounded-lg">
                <svg
                  width="13"
                  height="13"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2"
                  className="text-gray-400 shrink-0"
                >
                  <circle cx="11" cy="11" r="8" />
                  <line x1="21" y1="21" x2="16.65" y2="16.65" />
                </svg>
                <input
                  value={staffQuery}
                  onChange={(e) => {
                    setStaffQuery(e.target.value)
                    setStaffOpen(true)
                  }}
                  onFocus={() => setStaffOpen(true)}
                  placeholder="Tilføj medarbejder…"
                  className="flex-1 text-sm outline-none placeholder-gray-400 bg-transparent"
                />
              </div>
              {staffOpen && filteredUnassigned.length > 0 && (
                <div className="absolute left-0 right-0 top-full mt-1 z-10 bg-white border border-gray-200 rounded-lg shadow-lg max-h-48 overflow-y-auto">
                  {filteredUnassigned.map((s) => (
                    <button
                      key={s.id}
                      onPointerDown={(e) => {
                        e.preventDefault()
                        onAssignStaff(s.id!)
                        setStaffQuery('')
                        setStaffOpen(false)
                      }}
                      className="w-full px-3 py-2 text-sm text-left text-gray-700 hover:bg-gray-50 transition-colors"
                    >
                      {s.name}
                    </button>
                  ))}
                </div>
              )}
              {staffOpen && staffQuery.length > 0 && filteredUnassigned.length === 0 && (
                <div className="absolute left-0 right-0 top-full mt-1 z-10 bg-white border border-gray-200 rounded-lg shadow-lg">
                  <p className="px-3 py-2 text-sm text-gray-400">Ingen resultater</p>
                </div>
              )}
            </div>
          ) : (
            <p className="text-sm text-gray-400">Ingen medarbejdere oprettet.</p>
          )}
        </div>

        {/* Beskrivelse */}
        <div>
          <p className="text-xs font-medium text-gray-500 uppercase tracking-wide mb-2">
            Aktivitet denne uge
          </p>
          <MarkdownTextarea
            value={text}
            onChange={setText}
            onKeyDown={handleKeyDown}
            placeholder="Beskriv aktiviteter for denne vagt…"
            rows={4}
            maxLength={8000}
            aria-label="Aktivitet denne uge"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
          />
          <p className="text-xs text-gray-400 mt-1">Ctrl+S for at gemme</p>
        </div>
      </div>

      {/* Footer */}
      <div className="px-5 py-4 border-t border-gray-100 flex items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <button
            onClick={onEdit}
            className="flex items-center gap-1.5 px-3 py-1.5 text-sm text-gray-600 border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors"
          >
            <svg
              width="13"
              height="13"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
            >
              <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
              <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
            </svg>
            Rediger vagt
          </button>
          <button
            onClick={onDelete}
            className="flex items-center gap-1.5 px-3 py-1.5 text-sm text-red-600 border border-red-200 rounded-lg hover:bg-red-50 transition-colors"
          >
            <svg
              width="13"
              height="13"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
            >
              <polyline points="3 6 5 6 21 6" />
              <path d="M19 6l-1 14H6L5 6" />
              <path d="M10 11v6" />
              <path d="M14 11v6" />
              <path d="M9 6V4h6v2" />
            </svg>
            Slet
          </button>
        </div>
        <button
          onClick={() => onSaveBeskrivelse(text || null)}
          disabled={isSavingBeskrivelse}
          className="px-4 py-1.5 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 transition-colors"
        >
          {isSavingBeskrivelse ? 'Gemmer...' : 'Gem'}
        </button>
      </div>
    </Modal>
  )
}
