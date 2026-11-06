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
import { DataTable } from '../components/ui/list'

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
            className="flex items-center min-h-11 min-w-11 justify-center sm:min-h-0 sm:min-w-0 gap-2 px-3 py-2 sm:px-4 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors"
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

      <DataTable
        items={rooms}
        isLoading={isLoading}
        caption="Lokaler"
        testId="room-list"
        rowKey={(r) => r.id ?? r.name ?? ''}
        noun={{ one: 'lokale', other: 'lokaler' }}
        searchText={(r) => `${r.name ?? ''} ${r.description ?? ''}`}
        searchPlaceholder="Søg efter lokale"
        empty={<p>Ingen lokaler oprettet endnu</p>}
        columns={[
          { key: 'name', header: 'Navn', cell: (r) => r.name, sortValue: (r) => r.name ?? '' },
          {
            key: 'capacity',
            header: 'Kapacitet',
            cell: (r) => (r.capacity != null ? `${r.capacity} pladser` : '—'),
            sortValue: (r) => r.capacity ?? 0,
          },
          { key: 'description', header: 'Beskrivelse', cell: (r) => r.description ?? '—' },
        ]}
        actions={(r) => [
          { label: 'Lokaleplan', onClick: () => navigate(`/lokaler/${r.id}/skema`) },
          ...(isAdmin
            ? [
                {
                  label: 'Rediger',
                  variant: 'secondary' as const,
                  onClick: () => setEditingRoom(r),
                },
                {
                  label: 'Slet',
                  variant: 'danger' as const,
                  onClick: () => {
                    if (r.id && confirm(`Slet lokalet "${r.name ?? 'lokale'}"?`))
                      deleteMutation.mutate({ path: { id: r.id } })
                  },
                },
              ]
            : []),
        ]}
      />

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
