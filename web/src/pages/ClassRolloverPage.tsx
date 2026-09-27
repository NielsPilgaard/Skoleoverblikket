import { useState } from 'react'
import { Modal } from '../components/Modal'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1ClassesOptions,
  getApiV1ClassesQueryKey,
  getApiV1ClassesArchivedOptions,
  getApiV1ClassesArchivedQueryKey,
  postApiV1ClassesYearRollMutation,
} from '../api/generated/@tanstack/react-query.gen'
import type { ClassDto, YearRollRenameEntry, YearRollCreateEntry } from '../api/client'
import { usePageTitle } from '../hooks/usePageTitle'

function suggestNextName(name: string): string {
  const m = name.match(/^(.*?)(\d+)(\D*)$/)
  if (!m) return ''
  const prefix = m[1]
  const num = parseInt(m[2], 10)
  const suffix = m[3]
  return `${prefix}${num + 1}${suffix}`
}

interface RenameRow {
  classId: string
  currentName: string
  newName: string
  archive: boolean
}

interface NewClassRow {
  id: string
  name: string
}

export default function ClassRolloverPage() {
  usePageTitle('Årsrul')
  const qc = useQueryClient()

  const { data: classes, isLoading } = useQuery(getApiV1ClassesOptions())
  const { data: archivedClasses } = useQuery(getApiV1ClassesArchivedOptions())

  const [rows, setRows] = useState<RenameRow[] | null>(null)
  const [newClasses, setNewClasses] = useState<NewClassRow[]>([])
  const [showConfirm, setShowConfirm] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState(false)
  const [activeTab, setActiveTab] = useState<'roll' | 'archived'>('roll')

  const rollMutation = useMutation({
    ...postApiV1ClassesYearRollMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: getApiV1ClassesQueryKey() })
      qc.invalidateQueries({ queryKey: getApiV1ClassesArchivedQueryKey() })
      setShowConfirm(false)
      setSuccess(true)
      setRows(null)
      setNewClasses([])
    },
    onError: (err) => {
      const detail =
        err && typeof err === 'object' && 'errors' in err
          ? Object.values((err as { errors: Record<string, string[]> }).errors)
              .flat()
              .join(' ')
          : err instanceof Error
            ? err.message
            : 'Der opstod en fejl.'
      setError(detail)
      setShowConfirm(false)
    },
  })

  function initRows(classList: ClassDto[]) {
    setRows(
      classList.map((c) => ({
        classId: c.id ?? '',
        currentName: c.name ?? '',
        newName: suggestNextName(c.name ?? ''),
        archive: false,
      }))
    )
    setNewClasses([])
    setSuccess(false)
    setError(null)
  }

  function updateRow(classId: string, field: keyof RenameRow, value: string | boolean) {
    setRows((prev) => prev!.map((r) => (r.classId === classId ? { ...r, [field]: value } : r)))
  }

  function addNewClass() {
    setNewClasses((prev) => [...prev, { id: crypto.randomUUID(), name: '' }])
  }

  function updateNewClass(id: string, name: string) {
    setNewClasses((prev) => prev.map((nc) => (nc.id === id ? { ...nc, name } : nc)))
  }

  function removeNewClass(id: string) {
    setNewClasses((prev) => prev.filter((nc) => nc.id !== id))
  }

  function getNameConflicts(): Set<string> {
    if (!rows) return new Set()
    const nameCounts = new Map<string, number>()
    for (const r of rows) {
      if (!r.archive && r.newName.trim()) {
        const key = r.newName.trim().toLowerCase()
        nameCounts.set(key, (nameCounts.get(key) ?? 0) + 1)
      }
    }
    for (const nc of newClasses) {
      if (nc.name.trim()) {
        const key = nc.name.trim().toLowerCase()
        nameCounts.set(key, (nameCounts.get(key) ?? 0) + 1)
      }
    }
    const conflicts = new Set<string>()
    for (const [name, count] of nameCounts) {
      if (count > 1) conflicts.add(name)
    }
    return conflicts
  }

  function handleConfirm() {
    if (!rows) return
    const conflicts = getNameConflicts()
    if (conflicts.size > 0) {
      setError('To eller flere klasser ville få det samme navn. Ret navnene inden du fortsætter.')
      return
    }
    setError(null)
    setShowConfirm(true)
  }

  function handleSubmit() {
    if (!rows) return
    const renames: YearRollRenameEntry[] = rows
      .filter((r) => !r.archive && r.newName.trim() !== r.currentName)
      .map((r) => ({ classId: r.classId, newName: r.newName.trim() }))
    const archive: string[] = rows.filter((r) => r.archive).map((r) => r.classId)
    const create: YearRollCreateEntry[] = newClasses
      .filter((nc) => nc.name.trim())
      .map((nc) => ({ name: nc.name.trim() }))

    rollMutation.mutate({ body: { renames, archive, create } })
  }

  const conflicts = getNameConflicts()
  const archiveCount = rows?.filter((r) => r.archive).length ?? 0
  const renameCount =
    rows?.filter((r) => !r.archive && r.newName.trim() !== r.currentName).length ?? 0

  if (isLoading) {
    return (
      <div className="p-6 lg:p-8 max-w-3xl mx-auto space-y-6">
        <div className="h-8 w-48 bg-gray-200 rounded animate-pulse" />
        <div className="h-64 bg-gray-100 rounded-xl animate-pulse" />
      </div>
    )
  }

  return (
    <div className="p-6 lg:p-8 max-w-3xl mx-auto space-y-6">
      <div>
        <h1 className="font-display text-2xl font-semibold text-gray-900">Årsrul</h1>
        <p className="mt-1 text-sm text-gray-500">
          Omdøb klasser til næste skoleår, arkiver afgangselever, og opret nye indskrivningsklasser.
        </p>
      </div>

      {/* Tabs */}
      <div className="flex border-b border-gray-200">
        <button
          onClick={() => setActiveTab('roll')}
          className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors ${
            activeTab === 'roll'
              ? 'border-brand-600 text-brand-700'
              : 'border-transparent text-gray-500 hover:text-gray-700'
          }`}
        >
          Årsrul
        </button>
        <button
          onClick={() => setActiveTab('archived')}
          className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors ${
            activeTab === 'archived'
              ? 'border-brand-600 text-brand-700'
              : 'border-transparent text-gray-500 hover:text-gray-700'
          }`}
        >
          Arkiverede klasser
          {archivedClasses && archivedClasses.length > 0 && (
            <span className="ml-1.5 px-1.5 py-0.5 text-xs bg-gray-100 text-gray-600 rounded-full">
              {archivedClasses.length}
            </span>
          )}
        </button>
      </div>

      {activeTab === 'archived' && <ArchivedClassesTab classes={archivedClasses ?? []} />}

      {activeTab === 'roll' && (
        <>
          {success && (
            <div className="rounded-lg bg-green-50 border border-green-200 px-4 py-3">
              <p className="text-sm text-green-800 font-medium">
                Årsrul gennemført. Klasserne er omdøbt.
              </p>
            </div>
          )}

          {rows === null ? (
            <div className="bg-white rounded-xl border border-gray-200 px-6 py-8 text-center space-y-4">
              <p className="text-sm text-gray-600">
                Klik på knappen herunder for at starte årsrullet. Du kan redigere navnene inden du
                bekræfter.
              </p>
              <button
                onClick={() => initRows(classes ?? [])}
                className="px-5 py-2.5 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 transition-colors"
              >
                Start årsrul
              </button>
            </div>
          ) : (
            <div className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100">
              <div className="px-4 py-3 grid grid-cols-12 gap-2 text-xs font-medium text-gray-500 uppercase tracking-wide">
                <div className="col-span-4">Nuværende navn</div>
                <div className="col-span-5">Nyt navn</div>
                <div className="col-span-3 text-center">Arkiver</div>
              </div>

              {rows.map((row) => {
                const isConflict = !row.archive && conflicts.has(row.newName.trim().toLowerCase())
                return (
                  <div
                    key={row.classId}
                    className={`px-4 py-2.5 grid grid-cols-12 gap-2 items-center ${row.archive ? 'opacity-50' : ''}`}
                  >
                    <div className="col-span-4 text-sm text-gray-700 font-medium">
                      {row.currentName}
                    </div>
                    <div className="col-span-5">
                      <input
                        value={row.newName}
                        onChange={(e) => updateRow(row.classId, 'newName', e.target.value)}
                        disabled={row.archive}
                        className={`w-full px-2.5 py-1.5 border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent disabled:bg-gray-50 disabled:text-gray-400 ${
                          isConflict ? 'border-red-400 bg-red-50' : 'border-gray-300'
                        }`}
                        data-testid={`rename-input-${row.currentName}`}
                      />
                      {isConflict && (
                        <p className="mt-0.5 text-xs text-red-600">Navn allerede brugt</p>
                      )}
                    </div>
                    <div className="col-span-3 flex justify-center">
                      <input
                        type="checkbox"
                        checked={row.archive}
                        onChange={(e) => updateRow(row.classId, 'archive', e.target.checked)}
                        className="h-4 w-4 text-brand-600 rounded border-gray-300 focus:ring-brand-500"
                        data-testid={`archive-checkbox-${row.currentName}`}
                      />
                    </div>
                  </div>
                )
              })}

              {newClasses.map((nc) => {
                const isConflict = !!nc.name.trim() && conflicts.has(nc.name.trim().toLowerCase())
                return (
                  <div
                    key={nc.id}
                    className="px-4 py-2.5 grid grid-cols-12 gap-2 items-center bg-green-50/40"
                  >
                    <div className="col-span-4 text-sm text-gray-400 italic">Ny klasse</div>
                    <div className="col-span-6">
                      <input
                        value={nc.name}
                        onChange={(e) => updateNewClass(nc.id, e.target.value)}
                        placeholder="f.eks. 0.a"
                        className={`w-full px-2.5 py-1.5 border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent ${
                          isConflict ? 'border-red-400 bg-red-50' : 'border-gray-300'
                        }`}
                      />
                      {isConflict && (
                        <p className="mt-0.5 text-xs text-red-600">Navn allerede brugt</p>
                      )}
                    </div>
                    <div className="col-span-2 flex justify-end">
                      <button
                        onClick={() => removeNewClass(nc.id)}
                        className="p-1 text-gray-400 hover:text-red-500 rounded-md hover:bg-red-50"
                      >
                        <svg
                          width="14"
                          height="14"
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
                )
              })}

              <div className="px-4 py-3">
                <button
                  onClick={addNewClass}
                  className="flex items-center gap-1.5 text-sm text-brand-600 hover:text-brand-700"
                >
                  <svg
                    width="13"
                    height="13"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2.5"
                  >
                    <line x1="12" y1="5" x2="12" y2="19" />
                    <line x1="5" y1="12" x2="19" y2="12" />
                  </svg>
                  Opret ny klasse
                </button>
              </div>
            </div>
          )}

          {error && <p className="text-sm text-red-600">{error}</p>}

          {rows !== null && (
            <div className="flex justify-end gap-3">
              <button
                onClick={() => {
                  setRows(null)
                  setError(null)
                }}
                className="px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors"
              >
                Annuller
              </button>
              <button
                onClick={handleConfirm}
                disabled={conflicts.size > 0}
                className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                data-testid="year-roll-submit"
              >
                Udfør årsrul
              </button>
            </div>
          )}
        </>
      )}

      <Modal
        isOpen={showConfirm && !!rows}
        onClose={() => setShowConfirm(false)}
        title="Bekræft årsrul"
      >
        <div className="px-6 py-5 space-y-4">
          <p className="text-sm text-gray-600">
            Dette omdøber{' '}
            <span className="font-semibold">
              {renameCount} {renameCount === 1 ? 'klasse' : 'klasser'}
            </span>
            {archiveCount > 0 && (
              <>
                , arkiverer{' '}
                <span className="font-semibold">
                  {archiveCount} {archiveCount === 1 ? 'klasse' : 'klasser'}
                </span>
              </>
            )}
            {newClasses.filter((nc) => nc.name.trim()).length > 0 && (
              <>
                {' '}
                og opretter{' '}
                <span className="font-semibold">
                  {newClasses.filter((nc) => nc.name.trim()).length === 1
                    ? '1 ny klasse'
                    : `${newClasses.filter((nc) => nc.name.trim()).length} nye klasser`}
                </span>
              </>
            )}
            . Arkiverede klasser skjules fra skemabyggeren.
          </p>
          <div className="flex justify-end gap-3 pt-2">
            <button
              type="button"
              onClick={() => setShowConfirm(false)}
              className="px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors"
            >
              Annuller
            </button>
            <button
              type="button"
              onClick={handleSubmit}
              disabled={rollMutation.isPending}
              className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 transition-colors"
              data-testid="year-roll-confirm"
            >
              {rollMutation.isPending ? 'Udfører...' : 'Bekræft årsrul'}
            </button>
          </div>
        </div>
      </Modal>
    </div>
  )
}

function ArchivedClassesTab({ classes }: { classes: ClassDto[] }) {
  if (classes.length === 0) {
    return (
      <div className="bg-white rounded-xl border border-gray-200 px-6 py-10 text-center">
        <p className="text-sm text-gray-500">Ingen arkiverede klasser.</p>
      </div>
    )
  }

  return (
    <div className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100">
      <div className="px-4 py-3">
        <h2 className="text-sm font-semibold text-gray-700">Arkiverede klasser</h2>
        <p className="mt-0.5 text-xs text-gray-400">
          Skjult fra skemabyggeren. Opret en ny klasse for at gendanne.
        </p>
      </div>
      {classes.map((c) => (
        <div key={c.id} className="px-4 py-3 flex items-center gap-3">
          <span className="text-sm text-gray-500">{c.name}</span>
          {c.description && <span className="text-xs text-gray-400">{c.description}</span>}
        </div>
      ))}
    </div>
  )
}
