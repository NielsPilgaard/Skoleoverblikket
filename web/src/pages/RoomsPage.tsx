import { useState } from 'react'
import { Modal } from '../components/Modal'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import {
  getApiV1RoomsOptions,
  getApiV1RoomsQueryKey,
  postApiV1RoomsMutation,
  putApiV1RoomsByIdMutation,
  deleteApiV1RoomsByIdMutation,
} from '../api/generated/@tanstack/react-query.gen'
import type { RoomDto } from '../api/client'
import { usePageTitle } from '../hooks/usePageTitle'
import { useAuth } from '../auth/useAuth'

interface RoomModalProps {
  initial?: RoomDto
  onClose: () => void
  onSaved: () => void
}

function RoomModal({ initial, onClose, onSaved }: RoomModalProps) {
  const qc = useQueryClient()
  const [name, setName] = useState(initial?.name ?? '')
  const [capacity, setCapacity] = useState(initial?.capacity?.toString() ?? '')
  const [description, setDescription] = useState(initial?.description ?? '')

  const createMutation = useMutation({
    ...postApiV1RoomsMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: getApiV1RoomsQueryKey() })
      onSaved()
    },
  })
  const updateMutation = useMutation({
    ...putApiV1RoomsByIdMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: getApiV1RoomsQueryKey() })
      onSaved()
    },
  })
  const isPending = createMutation.isPending || updateMutation.isPending
  const isError = createMutation.isError || updateMutation.isError

  function handleSave() {
    if (!name.trim() || isPending) return
    const body = {
      name,
      capacity: capacity ? parseInt(capacity, 10) : null,
      description: description || null,
    }
    if (initial) {
      updateMutation.mutate({ path: { id: initial.id! }, body })
    } else {
      createMutation.mutate({ body })
    }
  }

  return (
    <Modal isOpen onClose={onClose} title={initial ? 'Rediger lokale' : 'Opret lokale'}>
      <div className="px-6 py-5 space-y-4">
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Navn *</label>
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault()
                handleSave()
              }
            }}
            placeholder="fx Lokale 12"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Kapacitet</label>
          <input
            value={capacity}
            onChange={(e) => setCapacity(e.target.value)}
            onFocus={(e) => e.target.select()}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault()
                handleSave()
              }
            }}
            type="number"
            min="1"
            placeholder="fx 30"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Beskrivelse</label>
          <input
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault()
                handleSave()
              }
            }}
            placeholder="fx Musikundervisning"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>
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
          disabled={!name.trim() || isPending}
          className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          {isPending ? 'Gemmer...' : 'Gem'}
        </button>
      </div>
    </Modal>
  )
}

export default function RoomsPage() {
  usePageTitle('Lokaler')
  const { isAdmin } = useAuth()
  const qc = useQueryClient()
  const navigate = useNavigate()
  const [showCreate, setShowCreate] = useState(false)
  const [editingRoom, setEditingRoom] = useState<RoomDto | null>(null)

  const { data: rooms, isLoading, isError, refetch } = useQuery(getApiV1RoomsOptions())

  const deleteMutation = useMutation({
    ...deleteApiV1RoomsByIdMutation(),
    onSuccess: () => qc.invalidateQueries({ queryKey: getApiV1RoomsQueryKey() }),
  })

  return (
    <div className="p-6 lg:p-8 max-w-6xl mx-auto space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="font-display text-2xl font-semibold text-gray-900">Lokaler</h1>
          <p className="mt-1 text-sm text-gray-500">Administrer undervisningslokaler</p>
        </div>
        {isAdmin && (
          <button
            onClick={() => setShowCreate(true)}
            aria-label="Opret lokale"
            className="flex items-center gap-2 px-3 py-2 sm:px-4 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors"
          >
            <svg
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.5"
            >
              <line x1="12" y1="5" x2="12" y2="19" />
              <line x1="5" y1="12" x2="19" y2="12" />
            </svg>
            <span className="hidden sm:inline">Opret lokale</span>
          </button>
        )}
      </div>

      {isError && (
        <div className="bg-red-50 border border-red-200 rounded-xl p-5 flex items-center justify-between">
          <p className="text-red-700 text-sm font-medium">Kunne ikke hente lokaler</p>
          <button
            onClick={() => refetch()}
            className="text-sm px-3 py-1.5 bg-red-100 text-red-700 rounded-lg"
          >
            Prøv igen
          </button>
        </div>
      )}

      <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-gray-100 bg-gray-50">
                <th className="px-5 py-3 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide">
                  Navn
                </th>
                <th className="px-5 py-3 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide hidden sm:table-cell">
                  Kapacitet
                </th>
                <th className="px-5 py-3 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide hidden md:table-cell">
                  Beskrivelse
                </th>
                <th className="px-5 py-3 text-right text-xs font-semibold text-gray-500 uppercase tracking-wide">
                  Handlinger
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-50">
              {isLoading &&
                Array.from({ length: 4 }).map((_, i) => (
                  <tr key={i} className="animate-pulse">
                    <td className="px-5 py-3">
                      <div className="h-4 w-24 bg-gray-200 rounded" />
                    </td>
                    <td className="px-5 py-3 hidden sm:table-cell">
                      <div className="h-4 w-12 bg-gray-100 rounded" />
                    </td>
                    <td className="px-5 py-3 hidden md:table-cell">
                      <div className="h-4 w-40 bg-gray-100 rounded" />
                    </td>
                    <td className="px-5 py-3" />
                  </tr>
                ))}
              {!isLoading && rooms?.length === 0 && (
                <tr>
                  <td colSpan={4} className="px-5 py-10 text-center text-gray-400">
                    Ingen lokaler oprettet endnu
                  </td>
                </tr>
              )}
              {[...(rooms ?? [])]
                .sort((a, b) =>
                  new Intl.Collator('da', { numeric: true, sensitivity: 'base' }).compare(
                    a.name ?? '',
                    b.name ?? ''
                  )
                )
                .map((r) => (
                  <tr key={r.id} className="hover:bg-gray-50 transition-colors">
                    <td className="px-5 py-3 font-medium text-gray-900">{r.name}</td>
                    <td className="px-5 py-3 text-gray-500 hidden sm:table-cell tabular-nums">
                      {r.capacity != null ? `${r.capacity} pladser` : '—'}
                    </td>
                    <td className="px-5 py-3 text-gray-500 hidden md:table-cell">
                      {r.description ?? '—'}
                    </td>
                    <td className="px-5 py-3 text-right">
                      <div className="flex items-center justify-end gap-2">
                        <button
                          onClick={() => navigate(`/lokaler/${r.id}/skema`)}
                          className="p-1.5 text-gray-400 hover:text-brand-600 rounded-md hover:bg-brand-50 transition-colors"
                          title="Se lokaleplan"
                        >
                          <svg
                            width="15"
                            height="15"
                            viewBox="0 0 24 24"
                            fill="none"
                            stroke="currentColor"
                            strokeWidth="2"
                          >
                            <rect x="3" y="4" width="18" height="18" rx="2" ry="2" />
                            <line x1="16" y1="2" x2="16" y2="6" />
                            <line x1="8" y1="2" x2="8" y2="6" />
                            <line x1="3" y1="10" x2="21" y2="10" />
                          </svg>
                        </button>
                        {isAdmin && (
                          <>
                            <button
                              onClick={() => setEditingRoom(r)}
                              className="p-1.5 text-gray-400 hover:text-gray-700 rounded-md hover:bg-gray-100 transition-colors"
                            >
                              <svg
                                width="15"
                                height="15"
                                viewBox="0 0 24 24"
                                fill="none"
                                stroke="currentColor"
                                strokeWidth="2"
                              >
                                <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
                                <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
                              </svg>
                            </button>
                            <button
                              onClick={() => {
                                if (r.id && confirm(`Slet lokalet "${r.name ?? 'lokale'}"?`))
                                  deleteMutation.mutate({ path: { id: r.id } })
                              }}
                              className="p-1.5 text-gray-400 hover:text-red-600 rounded-md hover:bg-red-50 transition-colors"
                            >
                              <svg
                                width="15"
                                height="15"
                                viewBox="0 0 24 24"
                                fill="none"
                                stroke="currentColor"
                                strokeWidth="2"
                              >
                                <polyline points="3 6 5 6 21 6" />
                                <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" />
                                <path d="M10 11v6M14 11v6" />
                                <path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
                              </svg>
                            </button>
                          </>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
            </tbody>
          </table>
        </div>
      </div>

      {showCreate && (
        <RoomModal onClose={() => setShowCreate(false)} onSaved={() => setShowCreate(false)} />
      )}
      {editingRoom && (
        <RoomModal
          initial={editingRoom}
          onClose={() => setEditingRoom(null)}
          onSaved={() => setEditingRoom(null)}
        />
      )}
    </div>
  )
}
