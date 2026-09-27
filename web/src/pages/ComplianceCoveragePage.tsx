import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { usePageTitle } from '../hooks/usePageTitle'
import { useAuth } from '../auth/useAuth'
import { Modal } from '../components/Modal'
import {
  getApiV1ComplianceCoverageCoverageOptions,
  getApiV1ComplianceCoverageSnapshotsOptions,
  getApiV1ComplianceCoverageSnapshotsByIdOptions,
  postApiV1ComplianceCoverageSnapshotsMutation,
} from '../api/generated/@tanstack/react-query.gen'
import type {
  ComplianceCoverageControllerSubjectCoverageDto,
  ComplianceCoverageControllerCoverageResponseDto,
  ComplianceCoverageControllerClassCoverageDto,
} from '../api/generated/types.gen'

const SNAPSHOTS_QUERY_KEY = [{ _id: 'getApiV1ComplianceCoverageSnapshots' }] as const

const CATEGORY_LABELS: Record<string, string> = {
  Dansk: 'Dansk',
  Matematik: 'Matematik',
  Engelsk: 'Engelsk',
  Kristendomskundskab: 'Kristendom',
  Historie: 'Historie',
  Idraet: 'Idræt',
  Musik: 'Musik',
  Billedkunst: 'Billedkunst',
  HaandvaerkOgDesign: 'Håndværk',
  Naturfag: 'Naturfag',
  Geografi: 'Geografi',
  Biologi: 'Biologi',
  FysikKemi: 'Fysik/Kemi',
  Samfundsfag: 'Samfundsfag',
  Tysk: 'Tysk',
  Fransk: 'Fransk',
  Madkundskab: 'Madkundskab',
}

function formatDateTime(d: string | undefined) {
  if (!d) return ''
  return new Date(d).toLocaleString('da-DK', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function StatusDot({ subject }: { subject: ComplianceCoverageControllerSubjectCoverageDto }) {
  const status = subject.status ?? 'missing'
  const colors: Record<string, string> = {
    green: 'bg-green-400',
    yellow: 'bg-yellow-400',
    red: 'bg-red-400',
    missing: 'bg-gray-200',
  }
  const statusLabels: Record<string, string> = {
    green: 'Opfyldt',
    yellow: '75–99%',
    red: 'Under 75%',
    missing: 'Ikke planlagt',
  }

  const hoursSuffix =
    status !== 'missing' ? ` · ${subject.annualHours}t / ${subject.vejledendeAnnualHours}t` : ''

  return (
    <span
      className={`inline-block w-3 h-3 rounded-full ${colors[status] ?? 'bg-gray-200'}`}
      title={`${statusLabels[status] ?? status}${hoursSuffix}`}
    />
  )
}

function CoverageTable({
  data,
}: {
  data: ComplianceCoverageControllerCoverageResponseDto | undefined
}) {
  const allCategories = [
    ...new Set(
      (data?.classes ?? []).flatMap((c: ComplianceCoverageControllerClassCoverageDto) =>
        (c.subjects ?? []).map(
          (s: ComplianceCoverageControllerSubjectCoverageDto) => s.category ?? ''
        )
      )
    ),
  ].sort()

  if ((data?.classes ?? []).length === 0) {
    const missingGrade = data?.classesMissingGradeLevel ?? 0
    const activeSchemas = data?.activeSchemaCount ?? 0

    return (
      <div className="rounded-xl border border-gray-200 bg-gray-50 px-6 py-10 text-center">
        <p className="text-sm font-medium text-gray-700">Ingen faglig dækning at vise endnu</p>
        {missingGrade > 0 ? (
          <p className="mt-2 text-sm text-gray-500">
            {missingGrade === 1
              ? '1 klasse mangler klassetrin'
              : `${missingGrade} klasser mangler klassetrin`}
            . Faglig dækning måles mod UVM's vejledende timetal pr. klassetrin, så en klasse uden
            klassetrin kan ikke vises her.
          </p>
        ) : activeSchemas === 0 ? (
          <p className="mt-2 text-sm text-gray-500">
            Ingen skemaer er aktive i dag. Dækningen beregnes ud fra aktive skemaer — opret eller
            aktivér et skema for at komme i gang.
          </p>
        ) : (
          <p className="mt-2 text-sm text-gray-500">
            Ingen klasser med klassetrin og aktive skemaer fundet.
          </p>
        )}
        <Link
          to="/klasser"
          className="mt-4 inline-block text-sm font-medium text-brand-600 hover:text-brand-800 transition-colors"
        >
          Gå til Klasser →
        </Link>
      </div>
    )
  }

  return (
    <div className="overflow-x-auto">
      <table className="min-w-full text-sm border-separate border-spacing-0">
        <thead>
          <tr>
            <th className="sticky left-0 bg-white px-4 py-2.5 text-left font-semibold text-gray-700 border-b border-gray-200 whitespace-nowrap">
              Klasse
            </th>
            {allCategories.map((cat) => (
              <th
                key={cat}
                className="px-3 py-2.5 text-center font-medium text-gray-600 border-b border-gray-200 whitespace-nowrap text-xs"
              >
                {CATEGORY_LABELS[cat] ?? cat}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {(data?.classes ?? []).map((cls) => {
            const subjectMap = Object.fromEntries(
              (cls.subjects ?? []).map((s) => [s.category ?? '', s])
            )
            const unexpected = cls.unexpectedGradeCategories ?? []
            return (
              <tr key={cls.classId} className="hover:bg-gray-50">
                <td className="sticky left-0 bg-white px-4 py-2.5 font-medium text-gray-900 border-b border-gray-100 whitespace-nowrap">
                  <div>{cls.className}</div>
                  {unexpected.length > 0 && (
                    <div
                      className="mt-0.5 text-xs text-amber-600"
                      title={`${unexpected.map((c) => CATEGORY_LABELS[c] ?? c).join(', ')} er ikke en del af UVM's fagrække på dette klassetrin`}
                    >
                      ⚠ Uventet fag: {unexpected.map((c) => CATEGORY_LABELS[c] ?? c).join(', ')}
                    </div>
                  )}
                </td>
                {allCategories.map((cat) => {
                  const subject = subjectMap[cat]
                  return (
                    <td key={cat} className="px-3 py-2.5 text-center border-b border-gray-100">
                      {subject ? (
                        <StatusDot subject={subject} />
                      ) : (
                        <span className="text-gray-200">—</span>
                      )}
                    </td>
                  )
                })}
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

function CoverageLegend() {
  return (
    <div className="flex flex-wrap gap-4 text-sm text-gray-600">
      <span className="flex items-center gap-1.5">
        <span className="inline-block w-3 h-3 rounded-full bg-green-400" /> Opfyldt
      </span>
      <span className="flex items-center gap-1.5">
        <span className="inline-block w-3 h-3 rounded-full bg-yellow-400" /> 75–99%
      </span>
      <span className="flex items-center gap-1.5">
        <span className="inline-block w-3 h-3 rounded-full bg-red-400" /> Under 75%
      </span>
      <span className="flex items-center gap-1.5">
        <span className="inline-block w-3 h-3 rounded-full bg-gray-200" /> Ikke planlagt
      </span>
    </div>
  )
}

function LiveCoverageTab() {
  const { isAdmin } = useAuth()
  const qc = useQueryClient()
  const [showModal, setShowModal] = useState(false)
  const [reason, setReason] = useState('')
  const [savedMessage, setSavedMessage] = useState(false)

  const { data, isLoading, isError } = useQuery(getApiV1ComplianceCoverageCoverageOptions())

  const createSnapshot = useMutation({
    ...postApiV1ComplianceCoverageSnapshotsMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: SNAPSHOTS_QUERY_KEY })
      setShowModal(false)
      setReason('')
      setSavedMessage(true)
      setTimeout(() => setSavedMessage(false), 4000)
    },
  })

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <CoverageLegend />
        {isAdmin && (
          <button
            onClick={() => setShowModal(true)}
            className="px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors shrink-0"
          >
            Gem version
          </button>
        )}
      </div>

      {savedMessage && (
        <div className="rounded-xl border border-green-200 bg-green-50 px-5 py-3 text-sm text-green-700">
          Version gemt.
        </div>
      )}

      {isLoading ? (
        <div className="animate-pulse space-y-2">
          {[...Array(5)].map((_, i) => (
            <div key={i} className="h-10 bg-gray-100 rounded" />
          ))}
        </div>
      ) : isError ? (
        <div className="rounded-xl border border-red-200 bg-red-50 px-5 py-4 text-sm text-red-700">
          Kunne ikke hente data. Prøv at genindlæse siden.
        </div>
      ) : (
        <CoverageTable data={data} />
      )}

      <Modal isOpen={showModal} onClose={() => setShowModal(false)} title="Gem version" size="sm">
        <form
          onSubmit={(e) => {
            e.preventDefault()
            createSnapshot.mutate({ body: { reason: reason || undefined } })
          }}
          className="px-6 py-5 space-y-4"
        >
          <p className="text-sm text-gray-600">
            Gemmer en version af den nuværende faglige dækning, så den kan tilgås senere — f.eks.
            før et tilsynsbesøg.
          </p>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Begrundelse (valgfri)
            </label>
            <input
              type="text"
              placeholder="f.eks. Før tilsynsbesøg"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
            />
          </div>

          {createSnapshot.isError && (
            <p className="text-sm text-red-600">Der opstod en fejl. Prøv igen.</p>
          )}

          <div className="flex gap-3 pt-1 pb-1">
            <button
              type="submit"
              disabled={createSnapshot.isPending}
              className="flex-1 px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors disabled:opacity-50"
            >
              {createSnapshot.isPending ? 'Gemmer…' : 'Gem'}
            </button>
            <button
              type="button"
              onClick={() => setShowModal(false)}
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

function SnapshotDetail({ id, onBack }: { id: string; onBack: () => void }) {
  const { data, isLoading, isError } = useQuery(
    getApiV1ComplianceCoverageSnapshotsByIdOptions({ path: { id } })
  )

  return (
    <div className="space-y-6">
      <button
        onClick={onBack}
        className="text-sm text-brand-600 hover:text-brand-800 transition-colors"
      >
        ← Tilbage til versioner
      </button>

      {isLoading ? (
        <div className="animate-pulse space-y-2">
          {[...Array(5)].map((_, i) => (
            <div key={i} className="h-10 bg-gray-100 rounded" />
          ))}
        </div>
      ) : isError || !data ? (
        <div className="rounded-xl border border-red-200 bg-red-50 px-5 py-4 text-sm text-red-700">
          Kunne ikke hente versionen.
        </div>
      ) : (
        <>
          <div className="bg-gray-50 border border-gray-200 rounded-xl px-4 py-3 text-sm text-gray-600">
            <p>
              <span className="font-medium text-gray-900">Skoleår:</span> {data.schoolYear}
              {' · '}
              <span className="font-medium text-gray-900">Gemt:</span>{' '}
              {formatDateTime(data.createdAt)}
              {' · '}
              <span className="font-medium text-gray-900">Af:</span> {data.createdByStaffName}
            </p>
            {data.reason && (
              <p className="mt-1">
                <span className="font-medium text-gray-900">Begrundelse:</span> {data.reason}
              </p>
            )}
          </div>
          <CoverageLegend />
          <CoverageTable data={data.data} />
        </>
      )}
    </div>
  )
}

function SnapshotsListTab({ onSelect }: { onSelect: (id: string) => void }) {
  const {
    data: snapshots = [],
    isLoading,
    isError,
  } = useQuery(getApiV1ComplianceCoverageSnapshotsOptions())

  if (isLoading) {
    return (
      <div className="animate-pulse space-y-2">
        {[...Array(3)].map((_, i) => (
          <div key={i} className="h-14 bg-gray-100 rounded-xl" />
        ))}
      </div>
    )
  }

  if (isError) {
    return (
      <div className="rounded-xl border border-red-200 bg-red-50 px-5 py-4 text-sm text-red-700">
        Kunne ikke hente versioner. Prøv at genindlæse siden.
      </div>
    )
  }

  if (snapshots.length === 0) {
    return (
      <div className="text-center py-12 text-gray-400">
        <p>Ingen versioner gemt endnu</p>
      </div>
    )
  }

  return (
    <div className="space-y-3">
      {snapshots.map((s) => (
        <button
          key={s.id}
          onClick={() => s.id && onSelect(s.id)}
          className="w-full text-left bg-white border border-gray-200 rounded-xl p-4 hover:border-brand-300 transition-colors"
        >
          <div className="flex items-center gap-2 flex-wrap">
            <p className="font-medium text-gray-900 text-sm">Skoleår {s.schoolYear}</p>
            <span className="text-xs text-gray-400">{formatDateTime(s.createdAt)}</span>
          </div>
          <p className="text-xs text-gray-500 mt-1">
            Gemt af {s.createdByStaffName}
            {s.reason ? ` · ${s.reason}` : ''}
          </p>
        </button>
      ))}
    </div>
  )
}

export default function ComplianceCoveragePage() {
  usePageTitle('Stå mål med')
  const [tab, setTab] = useState<'live' | 'snapshots'>('live')
  const [selectedSnapshotId, setSelectedSnapshotId] = useState<string | null>(null)

  return (
    <div className="p-6 lg:p-8 max-w-full mx-auto space-y-6">
      <div>
        <h1 className="font-display text-2xl font-semibold text-gray-900">Stå mål med</h1>
        <p className="mt-1 text-sm text-gray-500">
          Faglig dækning pr. klasse baseret på aktive skemaer
        </p>
      </div>

      <div className="flex gap-1 border-b border-gray-200">
        <button
          onClick={() => {
            setTab('live')
            setSelectedSnapshotId(null)
          }}
          className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors ${
            tab === 'live'
              ? 'border-brand-600 text-brand-700'
              : 'border-transparent text-gray-500 hover:text-gray-700'
          }`}
        >
          Nuværende dækning
        </button>
        <button
          onClick={() => {
            setTab('snapshots')
            setSelectedSnapshotId(null)
          }}
          className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors ${
            tab === 'snapshots'
              ? 'border-brand-600 text-brand-700'
              : 'border-transparent text-gray-500 hover:text-gray-700'
          }`}
        >
          Versioner
        </button>
      </div>

      {tab === 'live' ? (
        <LiveCoverageTab />
      ) : selectedSnapshotId ? (
        <SnapshotDetail id={selectedSnapshotId} onBack={() => setSelectedSnapshotId(null)} />
      ) : (
        <SnapshotsListTab onSelect={setSelectedSnapshotId} />
      )}
    </div>
  )
}
