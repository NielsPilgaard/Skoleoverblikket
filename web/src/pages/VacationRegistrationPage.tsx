import { useState } from 'react'
import { useNavigate } from 'react-router'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { usePageTitle } from '../hooks/usePageTitle'
import { Modal } from '../components/Modal'
import { DatePicker } from '../components/DatePicker'
import {
  getApiV1VacationRegistrationOptions,
  postApiV1VacationRegistrationMutation,
  putApiV1VacationRegistrationByIdMutation,
  deleteApiV1VacationRegistrationByIdMutation,
} from '../api/generated/@tanstack/react-query.gen'
import type {
  VacationRegistrationControllerWindowDto as WindowDto,
  VacationRegistrationGranularity,
} from '../api/generated/types.gen'

const QUERY_KEY = [{ _id: 'getApiV1VacationRegistration' }] as const

function GranularityBadge({
  granularity,
}: {
  granularity: VacationRegistrationGranularity | undefined
}) {
  return granularity === 'Days' ? (
    <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-purple-100 text-purple-800">
      Dage
    </span>
  ) : (
    <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-blue-100 text-blue-800">
      Uger
    </span>
  )
}

function OpenBadge({ isOpen }: { isOpen: boolean | undefined }) {
  return isOpen ? (
    <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-green-100 text-green-800">
      Åben
    </span>
  ) : (
    <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-gray-100 text-gray-600">
      Lukket
    </span>
  )
}

function formatDate(d: string | undefined) {
  if (!d) return ''
  return new Date(d).toLocaleDateString('da-DK', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  })
}

const emptyForm = {
  title: '',
  registrationDeadline: '',
  careStartDate: '',
  careEndDate: '',
  granularity: 'Weeks' as VacationRegistrationGranularity,
  isOpen: false,
}

export default function VacationRegistrationPage() {
  usePageTitle('Ferietilmelding')
  const navigate = useNavigate()
  const qc = useQueryClient()

  const [showModal, setShowModal] = useState(false)
  const [form, setForm] = useState(emptyForm)
  const [deleteConfirm, setDeleteConfirm] = useState<string | null>(null)

  const { data: windows = [] } = useQuery({
    ...getApiV1VacationRegistrationOptions(),
    select: (d) => d as WindowDto[],
  })

  const createMutation = useMutation({
    ...postApiV1VacationRegistrationMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: QUERY_KEY })
      setShowModal(false)
      setForm(emptyForm)
    },
  })

  const deleteMutation = useMutation({
    ...deleteApiV1VacationRegistrationByIdMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: QUERY_KEY })
      setDeleteConfirm(null)
    },
  })

  // Used inline on the list to toggle open/closed quickly
  const updateMutation = useMutation({
    ...putApiV1VacationRegistrationByIdMutation(),
    onSuccess: () => qc.invalidateQueries({ queryKey: QUERY_KEY }),
  })

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    createMutation.mutate({
      body: {
        title: form.title,
        registrationDeadline: form.registrationDeadline,
        careStartDate: form.careStartDate,
        careEndDate: form.careEndDate,
        granularity: form.granularity,
        isOpen: form.isOpen,
      },
    })
  }

  function toggleOpen(w: WindowDto) {
    if (!w.id) return
    updateMutation.mutate({
      path: { id: w.id },
      body: {
        title: w.title,
        registrationDeadline: w.registrationDeadline,
        careStartDate: w.careStartDate,
        careEndDate: w.careEndDate,
        granularity: w.granularity,
        isOpen: !w.isOpen,
      },
    })
  }

  return (
    <div className="max-w-3xl mx-auto px-4 py-8">
      <div className="flex items-center justify-between mb-6">
        <h1 className="font-display text-2xl font-semibold text-gray-900">Ferietilmelding</h1>
        <button
          onClick={() => setShowModal(true)}
          className="px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors"
        >
          Opret ny
        </button>
      </div>

      {windows.length === 0 && (
        <p className="text-sm text-gray-500 py-8">Ingen ferietilmeldinger oprettet endnu.</p>
      )}

      <div className="space-y-3">
        {windows.map((w) => (
          <div key={w.id} className="bg-white border border-gray-200 rounded-xl p-4">
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <div className="flex items-center gap-2 flex-wrap">
                  <p className="font-medium text-gray-900 text-sm">{w.title}</p>
                  <GranularityBadge granularity={w.granularity} />
                  <OpenBadge isOpen={w.isOpen} />
                </div>
                <p className="text-xs text-gray-500 mt-1">
                  Pasning: {formatDate(w.careStartDate)} – {formatDate(w.careEndDate)}
                  {' · '}Frist: {formatDate(w.registrationDeadline)}
                  {' · '}
                  {w.entryCount ?? 0} indmeldinger
                </p>
              </div>
              <div className="flex items-center gap-2 shrink-0">
                <button
                  onClick={() => toggleOpen(w)}
                  disabled={updateMutation.isPending}
                  className="text-xs text-gray-500 hover:text-gray-900 border border-gray-200 rounded-md px-2 py-1 transition-colors"
                >
                  {w.isOpen ? 'Luk' : 'Åbn'}
                </button>
                <button
                  onClick={() => navigate(`/ferieindmelding/${w.id}`)}
                  className="text-xs text-brand-600 hover:text-brand-800 border border-brand-200 rounded-md px-2 py-1 transition-colors"
                >
                  Se svar
                </button>
                <button
                  onClick={() => setDeleteConfirm(w.id ?? null)}
                  className="text-xs text-gray-400 hover:text-red-600 transition-colors"
                >
                  Slet
                </button>
              </div>
            </div>

            {deleteConfirm === w.id && w.id != null && (
              <div className="mt-3 pt-3 border-t border-gray-100 flex items-center gap-3">
                <p className="text-xs text-gray-600">Slet denne indmelding og alle svar?</p>
                <button
                  onClick={() => {
                    if (w.id == null) return
                    deleteMutation.mutate({ path: { id: w.id } })
                  }}
                  disabled={deleteMutation.isPending}
                  className="text-xs text-red-600 font-medium hover:text-red-800"
                >
                  Ja, slet
                </button>
                <button
                  onClick={() => setDeleteConfirm(null)}
                  className="text-xs text-gray-500 hover:text-gray-900"
                >
                  Annuller
                </button>
              </div>
            )}
          </div>
        ))}
      </div>

      <Modal
        isOpen={showModal}
        onClose={() => {
          setShowModal(false)
          setForm(emptyForm)
        }}
        title="Opret ferietilmelding"
      >
        <form onSubmit={handleSubmit} className="px-6 py-5 space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Titel</label>
            <input
              type="text"
              required
              placeholder="f.eks. Sommerferie 2026"
              value={form.title}
              onChange={(e) => setForm((f) => ({ ...f, title: e.target.value }))}
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Granularitet</label>
            <div className="flex gap-3">
              {(['Weeks', 'Days'] as VacationRegistrationGranularity[]).map((g) => (
                <label key={g} className="flex items-center gap-2 cursor-pointer">
                  <input
                    type="radio"
                    name="granularity"
                    value={g}
                    checked={form.granularity === g}
                    onChange={() => setForm((f) => ({ ...f, granularity: g }))}
                    className="accent-brand-600"
                  />
                  <span className="text-sm text-gray-700">{g === 'Weeks' ? 'Uger' : 'Dage'}</span>
                </label>
              ))}
            </div>
          </div>

          <div className="flex gap-3">
            <div className="flex-1 min-w-0">
              <p className="block text-sm font-medium text-gray-700 mb-1">Pasning start</p>
              <DatePicker
                value={form.careStartDate}
                onChange={(v) => setForm((f) => ({ ...f, careStartDate: v }))}
                placeholder="Vælg startdato"
                align="left"
              />
            </div>
            <div className="flex-1 min-w-0">
              <p className="block text-sm font-medium text-gray-700 mb-1">Pasning slut</p>
              <DatePicker
                value={form.careEndDate}
                onChange={(v) => setForm((f) => ({ ...f, careEndDate: v }))}
                min={form.careStartDate || undefined}
                placeholder="Vælg slutdato"
                align="right"
              />
            </div>
          </div>

          <div>
            <p className="block text-sm font-medium text-gray-700 mb-1">Indmeldelsesfrist</p>
            <DatePicker
              value={form.registrationDeadline}
              onChange={(v) => setForm((f) => ({ ...f, registrationDeadline: v }))}
              placeholder="Vælg fristdato"
              align="left"
            />
          </div>

          <label className="flex items-center gap-2 cursor-pointer">
            <input
              type="checkbox"
              checked={form.isOpen}
              onChange={(e) => setForm((f) => ({ ...f, isOpen: e.target.checked }))}
              className="accent-brand-600"
            />
            <span className="text-sm text-gray-700">Åbn for tilmeldinger med det samme</span>
          </label>

          {createMutation.isError && (
            <p className="text-sm text-red-600">Der opstod en fejl. Prøv igen.</p>
          )}

          <div className="flex gap-3 pt-1 pb-1">
            <button
              type="submit"
              disabled={createMutation.isPending}
              className="flex-1 px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors disabled:opacity-50"
            >
              {createMutation.isPending ? 'Opretter…' : 'Opret'}
            </button>
            <button
              type="button"
              onClick={() => {
                setShowModal(false)
                setForm(emptyForm)
              }}
              className="px-4 py-2 text-sm text-gray-600 hover:text-gray-900 transition-colors"
            >
              Annuller
            </button>
          </div>
        </form>
      </Modal>
    </div>
  )
}
