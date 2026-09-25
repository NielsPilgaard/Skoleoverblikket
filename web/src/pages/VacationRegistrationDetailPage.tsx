import { useState } from 'react'
import { useParams, useNavigate } from 'react-router'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { usePageTitle } from '../hooks/usePageTitle'
import {
  getApiV1VacationRegistrationOptions,
  getApiV1VacationRegistrationByIdEntriesOptions,
  putApiV1VacationRegistrationByIdMutation,
} from '../api/generated/@tanstack/react-query.gen'
import type {
  VacationRegistrationControllerWindowDto as WindowDto,
  VacationRegistrationControllerEntryDto as EntryDto,
  VacationRegistrationGranularity,
} from '../api/generated/types.gen'

function formatDate(d: string | undefined) {
  if (!d) return ''
  return new Date(d).toLocaleDateString('da-DK', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  })
}

function formatIsoDate(iso: string): string {
  return new Date(iso).toLocaleDateString('da-DK', { day: 'numeric', month: 'short' })
}

function formatWeekLabel(monday: string): string {
  const d = new Date(monday)
  const week = getIsoWeek(d)
  const end = new Date(d)
  end.setDate(end.getDate() + 4)
  return `Uge ${week} · ${d.toLocaleDateString('da-DK', { day: 'numeric', month: 'short' })} – ${end.toLocaleDateString('da-DK', { day: 'numeric', month: 'short' })}`
}

function getIsoWeek(date: Date): number {
  const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()))
  const dayNum = d.getUTCDay() || 7
  d.setUTCDate(d.getUTCDate() + 4 - dayNum)
  const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1))
  return Math.ceil(((d.getTime() - yearStart.getTime()) / 86400000 + 1) / 7)
}

function exportCsv(entries: EntryDto[], granularity: VacationRegistrationGranularity | undefined) {
  const rows = [
    ['Elev', 'Klasse', 'Indmeldt af', granularity === 'Days' ? 'Dage' : 'Uger', 'Note', 'Indmeldt'],
  ]
  for (const e of entries) {
    const datesLabel = (e.selectedDates ?? [])
      .map((d) => (granularity === 'Days' ? formatIsoDate(d) : formatWeekLabel(d)))
      .join('; ')
    rows.push([
      e.studentName ?? '',
      e.className ?? '',
      e.submittedByParentName ?? '',
      datesLabel,
      e.note ?? '',
      e.submittedAt ? new Date(e.submittedAt).toLocaleDateString('da-DK') : '',
    ])
  }
  const csv = rows.map((r) => r.map((c) => `"${c.replace(/"/g, '""')}"`).join(',')).join('\n')
  const blob = new Blob([`﻿${csv}`], { type: 'text/csv;charset=utf-8;' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'ferietilmelding.csv'
  a.click()
  URL.revokeObjectURL(url)
}

export default function VacationRegistrationDetailPage() {
  usePageTitle('Ferietilmelding – svar')
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const qc = useQueryClient()

  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState<{
    title: string
    registrationDeadline: string
    careStartDate: string
    careEndDate: string
    granularity: VacationRegistrationGranularity
    isOpen: boolean
  } | null>(null)

  const WINDOWS_KEY = [{ _id: 'getApiV1VacationRegistration' }] as const

  const { data: windows = [] } = useQuery({
    ...getApiV1VacationRegistrationOptions(),
    select: (d) => d as WindowDto[],
  })

  const window_ = windows.find((w) => w.id === id)

  const { data: entries = [] } = useQuery({
    ...getApiV1VacationRegistrationByIdEntriesOptions({ path: { id: id! } }),
    enabled: !!id,
    select: (d) => d as EntryDto[],
  })

  const updateMutation = useMutation({
    ...putApiV1VacationRegistrationByIdMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: WINDOWS_KEY })
      setEditing(false)
    },
  })

  function startEdit() {
    if (!window_) return
    setForm({
      title: window_.title ?? '',
      registrationDeadline: window_.registrationDeadline ?? '',
      careStartDate: window_.careStartDate ?? '',
      careEndDate: window_.careEndDate ?? '',
      granularity: window_.granularity ?? 'Weeks',
      isOpen: window_.isOpen ?? false,
    })
    setEditing(true)
  }

  function handleSave(e: React.FormEvent) {
    e.preventDefault()
    if (!form || !id) return
    updateMutation.mutate({ path: { id }, body: form })
  }

  if (!window_) {
    return (
      <div className="max-w-3xl mx-auto px-4 py-8">
        <button
          onClick={() => navigate('/ferieindmelding')}
          className="text-sm text-brand-600 hover:underline mb-4 block"
        >
          ← Tilbage
        </button>
        <p className="text-sm text-gray-500">Indmelding ikke fundet.</p>
      </div>
    )
  }

  return (
    <div className="max-w-3xl mx-auto px-4 py-8">
      <button
        onClick={() => navigate('/ferieindmelding')}
        className="text-sm text-brand-600 hover:underline mb-4 block"
      >
        ← Tilbage
      </button>

      {/* Header */}
      {!editing ? (
        <div className="bg-white border border-gray-200 rounded-xl p-5 mb-6">
          <div className="flex items-start justify-between gap-3">
            <div>
              <h1 className="font-display text-xl font-semibold text-gray-900">{window_.title}</h1>
              <p className="text-sm text-gray-500 mt-1">
                Pasning: {formatDate(window_.careStartDate)} – {formatDate(window_.careEndDate)}
                {' · '}Frist: {formatDate(window_.registrationDeadline)}
                {' · '}
                {window_.granularity === 'Days' ? 'Dage' : 'Uger'}
              </p>
            </div>
            <div className="flex items-center gap-2 shrink-0">
              <span
                className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${window_.isOpen ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-600'}`}
              >
                {window_.isOpen ? 'Åben' : 'Lukket'}
              </span>
              <button
                onClick={startEdit}
                className="text-xs text-gray-500 hover:text-gray-900 border border-gray-200 rounded-md px-2 py-1 transition-colors"
              >
                Rediger
              </button>
            </div>
          </div>
        </div>
      ) : (
        <form
          onSubmit={handleSave}
          className="bg-white border border-gray-200 rounded-xl p-5 mb-6 space-y-4"
        >
          <h2 className="font-semibold text-gray-900 text-sm">Rediger indmelding</h2>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Titel</label>
            <input
              type="text"
              required
              value={form!.title}
              onChange={(e) => setForm((f) => f && { ...f, title: e.target.value })}
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
            />
          </div>
          <div className="flex gap-3">
            <div className="flex-1">
              <label className="block text-sm font-medium text-gray-700 mb-1">Pasning start</label>
              <input
                type="date"
                required
                value={form!.careStartDate}
                onChange={(e) => setForm((f) => f && { ...f, careStartDate: e.target.value })}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
              />
            </div>
            <div className="flex-1">
              <label className="block text-sm font-medium text-gray-700 mb-1">Pasning slut</label>
              <input
                type="date"
                required
                value={form!.careEndDate}
                onChange={(e) => setForm((f) => f && { ...f, careEndDate: e.target.value })}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
              />
            </div>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Indmeldelsesfrist
            </label>
            <input
              type="date"
              required
              value={form!.registrationDeadline}
              onChange={(e) => setForm((f) => f && { ...f, registrationDeadline: e.target.value })}
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
            />
          </div>
          <label className="flex items-center gap-2 cursor-pointer">
            <input
              type="checkbox"
              checked={form!.isOpen}
              onChange={(e) => setForm((f) => f && { ...f, isOpen: e.target.checked })}
              className="accent-brand-600"
            />
            <span className="text-sm text-gray-700">Åben for indmeldinger</span>
          </label>
          {updateMutation.isError && (
            <p className="text-sm text-red-600">Der opstod en fejl. Prøv igen.</p>
          )}
          <div className="flex gap-3">
            <button
              type="submit"
              disabled={updateMutation.isPending}
              className="px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 disabled:opacity-50 transition-colors"
            >
              {updateMutation.isPending ? 'Gemmer…' : 'Gem'}
            </button>
            <button
              type="button"
              onClick={() => setEditing(false)}
              className="px-4 py-2 text-sm text-gray-600 hover:text-gray-900 transition-colors"
            >
              Annuller
            </button>
          </div>
        </form>
      )}

      {/* Entries */}
      <div className="flex items-center justify-between mb-3">
        <h2 className="font-semibold text-gray-900 text-sm">{entries.length} svar</h2>
        {entries.length > 0 && (
          <button
            onClick={() => exportCsv(entries, window_.granularity)}
            className="text-xs text-brand-600 hover:text-brand-800 border border-brand-200 rounded-md px-2 py-1 transition-colors"
          >
            Eksporter CSV
          </button>
        )}
      </div>

      {entries.length === 0 && <p className="text-sm text-gray-500 py-4">Ingen svar endnu.</p>}

      <div className="space-y-3">
        {entries.map((e) => (
          <div key={e.id} className="bg-white border border-gray-200 rounded-xl p-4">
            <div className="flex items-start justify-between gap-2">
              <div className="min-w-0">
                <p className="font-medium text-gray-900 text-sm">
                  {e.studentName} <span className="font-normal text-gray-500">· {e.className}</span>
                </p>
                <p className="text-xs text-gray-500 mt-0.5">
                  Indmeldt af {e.submittedByParentName}
                </p>
              </div>
              <p className="text-xs text-gray-400 shrink-0">
                {e.submittedAt ? formatDate(e.submittedAt) : ''}
              </p>
            </div>
            {(e.selectedDates ?? []).length > 0 && (
              <div className="mt-2 flex flex-wrap gap-1">
                {(e.selectedDates ?? []).map((d) => (
                  <span
                    key={d}
                    className="inline-flex items-center px-2 py-0.5 rounded text-xs bg-brand-50 text-brand-700 border border-brand-100"
                  >
                    {window_.granularity === 'Days' ? formatIsoDate(d) : formatWeekLabel(d)}
                  </span>
                ))}
              </div>
            )}
            {e.note && <p className="text-xs text-gray-600 mt-2 italic">{e.note}</p>}
          </div>
        ))}
      </div>
    </div>
  )
}
