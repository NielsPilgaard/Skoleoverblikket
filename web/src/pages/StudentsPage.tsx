import { useState } from 'react'
import { Modal } from '../components/Modal'
import ModuleInactiveNotice from '../components/ModuleInactiveNotice'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1StudentsOptions,
  getApiV1StudentsQueryKey,
  postApiV1StudentsMutation,
  putApiV1StudentsByIdMutation,
  deleteApiV1StudentsByIdMutation,
  getApiV1ClassesOptions,
} from '../api/generated/@tanstack/react-query.gen'
import type { StudentDto, ClassDto } from '../api/client'
import { usePageTitle } from '../hooks/usePageTitle'
import { useSubscription } from '../hooks/useSubscription'

interface StudentModalProps {
  initial?: StudentDto
  classes: ClassDto[]
  onClose: () => void
}

function StudentModal({ initial, classes, onClose }: StudentModalProps) {
  const qc = useQueryClient()
  const [name, setName] = useState(initial?.name ?? '')
  const [classId, setClassId] = useState(initial?.classId ?? classes[0]?.id ?? '')
  const [isEnrolledInSfo, setIsEnrolledInSfo] = useState(initial?.isEnrolledInSfo ?? false)

  const createMutation = useMutation({
    ...postApiV1StudentsMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: getApiV1StudentsQueryKey() })
      onClose()
    },
  })
  const updateMutation = useMutation({
    ...putApiV1StudentsByIdMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: getApiV1StudentsQueryKey() })
      onClose()
    },
  })

  const isPending = createMutation.isPending || updateMutation.isPending
  const isError = createMutation.isError || updateMutation.isError

  function handleSave() {
    if (!name.trim() || !classId || isPending) return
    const body = { name, classId, isEnrolledInSfo }
    if (initial) {
      updateMutation.mutate({ path: { id: initial.id! }, body })
    } else {
      createMutation.mutate({ body })
    }
  }

  return (
    <Modal isOpen onClose={onClose} title={initial ? 'Rediger elev' : 'Opret elev'}>
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
            placeholder="Elevens fulde navn"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>
        <div>
          <label className="flex items-center gap-2 text-sm cursor-pointer">
            <input
              type="checkbox"
              checked={isEnrolledInSfo}
              onChange={(e) => setIsEnrolledInSfo(e.target.checked)}
              className="rounded border-gray-300 text-brand-600 focus:ring-brand-500"
            />
            <span className="font-medium text-gray-700">Indmeldt i SFO</span>
          </label>
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Klasse *</label>
          <select
            value={classId}
            onChange={(e) => setClassId(e.target.value)}
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent bg-white"
          >
            {classes.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </div>
        {isError && <p className="text-sm text-red-600">Der opstod en fejl. Prov igen.</p>}
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
          disabled={!name.trim() || !classId || isPending}
          className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          {isPending ? 'Gemmer...' : 'Gem'}
        </button>
      </div>
    </Modal>
  )
}

export default function StudentsPage() {
  usePageTitle('Elever')
  const qc = useQueryClient()
  const [showCreate, setShowCreate] = useState(false)
  const [editingStudent, setEditingStudent] = useState<StudentDto | null>(null)
  const [filterClassId, setFilterClassId] = useState<string>('')
  const { hasParentModule, isLoading: modulesLoading } = useSubscription()

  const { data: students, isLoading, isError, refetch } = useQuery(getApiV1StudentsOptions())
  const { data: classes } = useQuery(getApiV1ClassesOptions())

  const deleteMutation = useMutation({
    mutationFn: deleteApiV1StudentsByIdMutation().mutationFn,
    onSuccess: () => qc.invalidateQueries({ queryKey: getApiV1StudentsQueryKey() }),
  })

  const filtered = filterClassId
    ? (students ?? []).filter((s) => s.classId === filterClassId)
    : (students ?? [])

  return (
    <div className="p-6 lg:p-8 max-w-6xl mx-auto space-y-6">
      <div className="flex items-center justify-between flex-wrap gap-4">
        <div>
          <h1 className="font-display text-2xl font-semibold text-gray-900">Elever</h1>
          <p className="mt-1 text-sm text-gray-500">Administrer skolens elever og deres klasser</p>
        </div>
        <div className="flex flex-col items-end gap-1">
          <button
            onClick={() => setShowCreate(true)}
            disabled={!classes || classes.length === 0 || !hasParentModule}
            aria-label="Opret elev"
            title={
              !hasParentModule ? 'Kræver forældremodulet — aktivér under Abonnement' : undefined
            }
            className="flex items-center gap-2 px-3 py-2 sm:px-4 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
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
            <span className="hidden sm:inline">Opret elev</span>
          </button>
          {classes && classes.length === 0 && (
            <p className="text-xs text-gray-500">Opret en klasse først</p>
          )}
        </div>
      </div>

      {!modulesLoading && !hasParentModule && (
        <ModuleInactiveNotice
          testId="students-module-inactive"
          text="Forældremodulet er ikke aktivt, så du kan ikke oprette eller ændre elever. Du kan stadig se og slette dem."
        />
      )}

      {classes && classes.length > 0 && (
        <div>
          <select
            value={filterClassId}
            onChange={(e) => setFilterClassId(e.target.value)}
            className="px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 bg-white"
          >
            <option value="">Alle klasser</option>
            {classes.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </div>
      )}

      {isError && (
        <div className="bg-red-50 border border-red-200 rounded-xl p-5 flex items-center justify-between">
          <p className="text-red-700 text-sm font-medium">Kunne ikke hente elever</p>
          <button
            onClick={() => refetch()}
            className="text-sm px-3 py-1.5 bg-red-100 text-red-700 rounded-lg"
          >
            Prov igen
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
                <th className="px-5 py-3 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide">
                  Klasse
                </th>
                <th className="px-5 py-3 text-right text-xs font-semibold text-gray-500 uppercase tracking-wide">
                  Handlinger
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-50">
              {isLoading &&
                Array.from({ length: 5 }).map((_, i) => (
                  <tr key={i} className="animate-pulse">
                    <td className="px-5 py-3">
                      <div className="h-4 w-32 bg-gray-200 rounded" />
                    </td>
                    <td className="px-5 py-3">
                      <div className="h-5 w-16 bg-gray-100 rounded-full" />
                    </td>
                    <td className="px-5 py-3" />
                  </tr>
                ))}
              {!isLoading && filtered.length === 0 && (
                <tr>
                  <td colSpan={3} className="px-5 py-12 text-center">
                    <p className="text-gray-400 font-medium">Ingen elever fundet</p>
                  </td>
                </tr>
              )}
              {filtered.map((s) => (
                <tr key={s.id} className="hover:bg-gray-50 transition-colors">
                  <td className="px-5 py-3 font-medium text-gray-900">{s.name}</td>
                  <td className="px-5 py-3">
                    <span className="px-2 py-0.5 rounded-full text-xs font-medium bg-blue-100 text-blue-700">
                      {s.className ?? '—'}
                    </span>
                  </td>
                  <td className="px-5 py-3 text-right">
                    <div className="flex items-center justify-end gap-2">
                      {hasParentModule && (
                        <button
                          onClick={() => setEditingStudent(s)}
                          className="p-1.5 text-gray-400 hover:text-gray-700 rounded-md hover:bg-gray-100 transition-colors"
                          title="Rediger"
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
                      )}
                      <button
                        onClick={() => {
                          if (confirm(`Slet "${s.name}"?`))
                            deleteMutation.mutate({ path: { id: s.id! } })
                        }}
                        className="p-1.5 text-gray-400 hover:text-red-600 rounded-md hover:bg-red-50 transition-colors"
                        title="Slet"
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
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {showCreate && classes && classes.length > 0 && (
        <StudentModal classes={classes} onClose={() => setShowCreate(false)} />
      )}
      {editingStudent && classes && (
        <StudentModal
          initial={editingStudent}
          classes={classes}
          onClose={() => setEditingStudent(null)}
        />
      )}
    </div>
  )
}
