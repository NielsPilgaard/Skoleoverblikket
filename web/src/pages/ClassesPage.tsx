import { useState, useEffect, useMemo } from 'react'
import { Modal } from '../components/Modal'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate, useSearchParams } from 'react-router-dom'
import {
  getApiV1ClassesOptions,
  getApiV1ClassesQueryKey,
  postApiV1ClassesMutation,
  putApiV1ClassesByIdMutation,
  deleteApiV1ClassesByIdMutation,
  getApiV1ClassesByClassIdSchemasOptions,
  getApiV1ClassesByClassIdSchemasQueryKey,
  postApiV1ClassesByClassIdSchemasMutation,
  deleteApiV1ClassesByClassIdSchemasBySchemaIdMutation,
  putApiV1ClassesByClassIdSchemasBySchemaIdDaterangeMutation,
  postApiV1ClassesByClassIdSchemasBySchemaIdCopyMutation,
  postApiV1ClassesByClassIdSchemasBySchemaIdCopyToByTargetClassIdMutation,
  getApiV1ClassesByClassIdPermissionsOptions,
  getApiV1ClassesByClassIdPermissionsQueryKey,
  postApiV1ClassesByClassIdPermissionsMutation,
  deleteApiV1ClassesByClassIdPermissionsByStaffIdMutation,
  getApiV1StaffOptions,
} from '../api/generated/@tanstack/react-query.gen'
import type { ClassDto, SchemaDto, StaffDto, ClassPermissionDto } from '../api/client'
import { usePageTitle } from '../hooks/usePageTitle'
import { DatePicker } from '../components/DatePicker'
import { detectGradeLevel, GRADE_LEVEL_LABELS } from '../utils/gradeLevel'
import { useAuth } from '../auth/useAuth'
import { useSubscription } from '../hooks/useSubscription'

interface CopySchemaModalProps {
  classId: string
  schemaId: string
  sourceName: string
  onClose: () => void
  onSaved: () => void
}

function CopySchemaModal({
  classId,
  schemaId,
  sourceName,
  onClose,
  onSaved,
}: CopySchemaModalProps) {
  const qc = useQueryClient()
  const [name, setName] = useState(`Kopi af ${sourceName}`)
  const [targetClassId, setTargetClassId] = useState(classId)

  const { data: allClasses } = useQuery({
    ...getApiV1ClassesOptions(),
    select: (d) => (d ?? []) as ClassDto[],
  })

  const mutation = useMutation({
    mutationFn: () => {
      if (targetClassId === classId) {
        const { mutationFn } = postApiV1ClassesByClassIdSchemasBySchemaIdCopyMutation()
        return mutationFn!({ path: { classId, schemaId }, body: { name } }, undefined as never)
      }
      const { mutationFn } =
        postApiV1ClassesByClassIdSchemasBySchemaIdCopyToByTargetClassIdMutation()
      return mutationFn!(
        { path: { classId, schemaId, targetClassId }, body: { name } },
        undefined as never
      )
    },
    onSuccess: () => {
      qc.invalidateQueries({
        queryKey: getApiV1ClassesByClassIdSchemasQueryKey({ path: { classId } }),
      })
      qc.invalidateQueries({
        queryKey: getApiV1ClassesByClassIdSchemasQueryKey({ path: { classId: targetClassId } }),
      })
      onSaved()
    },
  })

  function handleSave() {
    if (!name.trim() || mutation.isPending) return
    mutation.mutate()
  }

  return (
    <Modal isOpen onClose={onClose} title="Kopiér skema">
      <div className="px-6 py-5 space-y-4">
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Kopiér til klasse</label>
          <select
            value={targetClassId}
            onChange={(e) => setTargetClassId(e.target.value)}
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent bg-white"
          >
            {allClasses?.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
                {c.id === classId ? ' (samme klasse)' : ''}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Navn på kopi *</label>
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault()
                handleSave()
              }
            }}
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>
        {mutation.isError && <p className="text-sm text-red-600">Der opstod en fejl. Prøv igen.</p>}
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
          disabled={!name.trim() || mutation.isPending}
          className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          {mutation.isPending ? 'Kopierer...' : 'Kopiér'}
        </button>
      </div>
    </Modal>
  )
}

interface ClassModalProps {
  initial?: ClassDto
  onClose: () => void
  onSaved: () => void
}

function ClassModal({ initial, onClose, onSaved }: ClassModalProps) {
  const qc = useQueryClient()
  const [name, setName] = useState(initial?.name ?? '')
  const [description, setDescription] = useState(initial?.description ?? '')
  const [gradeLevel, setGradeLevel] = useState<number | null>(initial?.gradeLevel ?? null)

  function handleNameChange(v: string) {
    setName(v)
    if (!initial) {
      const detected = detectGradeLevel(v)
      if (detected !== null) setGradeLevel(detected)
    }
  }

  const createMutation = useMutation({
    ...postApiV1ClassesMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: getApiV1ClassesQueryKey() })
      onSaved()
    },
  })
  const updateMutation = useMutation({
    ...putApiV1ClassesByIdMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: getApiV1ClassesQueryKey() })
      onSaved()
    },
  })
  const isPending = createMutation.isPending || updateMutation.isPending
  const isError = createMutation.isError || updateMutation.isError

  function handleSave() {
    if (!name.trim() || isPending) return
    const body = { name, description: description || null, gradeLevel }
    if (initial) {
      updateMutation.mutate({ path: { id: initial.id! }, body })
    } else {
      createMutation.mutate({ body })
    }
  }

  return (
    <Modal isOpen onClose={onClose} title={initial ? 'Rediger klasse' : 'Opret klasse'}>
      <div className="px-6 py-5 space-y-4">
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Navn *</label>
          <input
            value={name}
            onChange={(e) => handleNameChange(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault()
                handleSave()
              }
            }}
            placeholder="fx 5.a"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Klassetrin</label>
          <select
            value={gradeLevel ?? ''}
            onChange={(e) => setGradeLevel(e.target.value === '' ? null : Number(e.target.value))}
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent bg-white"
          >
            <option value="">— ukendt —</option>
            {Object.entries(GRADE_LEVEL_LABELS).map(([val, label]) => (
              <option key={val} value={val}>
                {label}
              </option>
            ))}
          </select>
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
            placeholder="Valgfri beskrivelse"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>
        {isError && <p className="text-sm text-red-600">Der opstod en fejl. Prøv igen.</p>}
      </div>
      <div className="px-6 py-4 border-t border-gray-100 flex justify-end gap-3">
        <button
          type="button"
          onClick={onClose}
          className="px-4 py-2 text-sm text-gray-600 hover:text-gray-900 transition-colors"
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

interface SchemaModalProps {
  classId: string
  onClose: () => void
  onSaved: () => void
}

function isoWeekMonday(year: number, week: number): string {
  // Jan 4 is always in week 1 (ISO 8601)
  const jan4 = new Date(year, 0, 4)
  const dayOfWeek = jan4.getDay() || 7
  const week1Monday = new Date(jan4)
  week1Monday.setDate(jan4.getDate() - (dayOfWeek - 1))
  const monday = new Date(week1Monday)
  monday.setDate(week1Monday.getDate() + (week - 1) * 7)
  const pad = (n: number) => n.toString().padStart(2, '0')
  return `${monday.getFullYear()}-${pad(monday.getMonth() + 1)}-${pad(monday.getDate())}`
}

function isoWeekSunday(year: number, week: number): string {
  const monday = isoWeekMonday(year, week)
  const d = new Date(`${monday}T00:00:00`)
  d.setDate(d.getDate() + 6)
  const pad = (n: number) => n.toString().padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

function getSchoolYearPresets(): { label: string; start: string; end: string }[] {
  const today = new Date()
  const year = today.getFullYear()
  // School year starts week 32 — if we're past that, current school year started this year
  const schoolYearStart = new Date(`${isoWeekMonday(year, 32)}T00:00:00`)
  const baseYear = today >= schoolYearStart ? year : year - 1
  return [
    {
      label: `Hele skoleåret ${baseYear}/${baseYear + 1}`,
      start: isoWeekMonday(baseYear, 32),
      end: isoWeekSunday(baseYear + 1, 26),
    },
    {
      label: `Efterår ${baseYear} (uge 32–52)`,
      start: isoWeekMonday(baseYear, 32),
      end: isoWeekSunday(baseYear, 52),
    },
    {
      label: `Forår ${baseYear + 1} (uge 1–26)`,
      start: isoWeekMonday(baseYear + 1, 1),
      end: isoWeekSunday(baseYear + 1, 26),
    },
  ]
}

function SchemaModal({ classId, onClose, onSaved }: SchemaModalProps) {
  const qc = useQueryClient()
  const presets = useMemo(() => getSchoolYearPresets(), [])
  const [name, setName] = useState('')
  const [startDate, setStartDate] = useState(presets[0].start)
  const [endDate, setEndDate] = useState(presets[0].end)

  const dateInvalid = !!startDate && !!endDate && startDate > endDate

  const mutation = useMutation({
    mutationFn: async () => {
      const { mutationFn: createSchema } = postApiV1ClassesByClassIdSchemasMutation()
      const created: SchemaDto = await createSchema!(
        { path: { classId }, body: { name } },
        undefined as never
      )
      if ((startDate || endDate) && created.id) {
        const { mutationFn: setDaterange } =
          putApiV1ClassesByClassIdSchemasBySchemaIdDaterangeMutation()
        await setDaterange!(
          {
            path: { classId, schemaId: created.id },
            body: { startDate: startDate || null, endDate: endDate || null },
          },
          undefined as never
        )
      }
    },
    onSuccess: () => {
      qc.invalidateQueries({
        queryKey: getApiV1ClassesByClassIdSchemasQueryKey({ path: { classId } }),
      })
      onSaved()
    },
  })

  function handleSave() {
    if (!name.trim() || mutation.isPending || dateInvalid) return
    mutation.mutate()
  }

  return (
    <Modal isOpen onClose={onClose} title="Opret skema">
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
            placeholder="fx Efterår 2025"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Periode</label>
          <div className="flex flex-wrap gap-1.5">
            {presets.map((p) => {
              const active = startDate === p.start && endDate === p.end
              return (
                <button
                  key={p.label}
                  type="button"
                  onClick={() => {
                    setStartDate(p.start)
                    setEndDate(p.end)
                    if (!name.trim()) setName(p.label)
                  }}
                  className={`px-2.5 py-1 rounded-md text-xs font-medium border transition-colors ${active ? 'bg-brand-600 text-white border-brand-600' : 'bg-white text-gray-600 border-gray-300 hover:border-brand-400 hover:text-brand-700'}`}
                >
                  {p.label}
                </button>
              )
            })}
          </div>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Startdato</label>
            <DatePicker value={startDate} onChange={setStartDate} />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Slutdato</label>
            <DatePicker
              value={endDate}
              onChange={setEndDate}
              min={startDate || undefined}
              align="right"
            />
          </div>
        </div>
        {dateInvalid && <p className="text-sm text-red-600">Startdato skal være før slutdato.</p>}
        {mutation.isError && <p className="text-sm text-red-600">Der opstod en fejl. Prøv igen.</p>}
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
          disabled={!name.trim() || mutation.isPending || dateInvalid}
          className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          {mutation.isPending ? 'Opretter...' : 'Opret'}
        </button>
      </div>
    </Modal>
  )
}

function formatDateRange(startDate?: string | null, endDate?: string | null): string | null {
  if (!startDate && !endDate) return null
  const fmt = (d: string) => {
    const [, m] = d.split('-')
    const months = [
      'jan',
      'feb',
      'mar',
      'apr',
      'maj',
      'jun',
      'jul',
      'aug',
      'sep',
      'okt',
      'nov',
      'dec',
    ]
    return `${months[parseInt(m, 10) - 1]} ${d.slice(0, 4)}`
  }
  if (startDate && endDate) return `${fmt(startDate)} – ${fmt(endDate)}`
  if (startDate) return `fra ${fmt(startDate)}`
  return `til ${fmt(endDate!)}`
}

function isActiveNow(startDate?: string | null, endDate?: string | null): boolean {
  if (!startDate || !endDate) return false
  const today = new Date().toISOString().slice(0, 10)
  return startDate <= today && endDate >= today
}

function ClassPermissionsTab({ classId }: { classId: string }) {
  const qc = useQueryClient()
  const [selectedStaffId, setSelectedStaffId] = useState('')

  const { data: rawPermissions, isLoading } = useQuery(
    getApiV1ClassesByClassIdPermissionsOptions({ path: { classId } })
  )
  const permissions = (rawPermissions ?? []) as ClassPermissionDto[]

  const { data: rawStaff } = useQuery({
    ...getApiV1StaffOptions(),
    select: (d) => (d ?? []) as StaffDto[],
  })
  const staff = rawStaff ?? []

  const grantedIds = new Set(permissions.map((p) => p.staffId))
  const ungrantedStaff = staff.filter((s) => !grantedIds.has(s.id))

  const allSuperadmin = permissions.length === 0

  const grantMutation = useMutation({
    ...postApiV1ClassesByClassIdPermissionsMutation(),
    onSuccess: () => {
      qc.invalidateQueries({
        queryKey: getApiV1ClassesByClassIdPermissionsQueryKey({ path: { classId } }),
      })
      setSelectedStaffId('')
    },
  })

  const revokeMutation = useMutation({
    ...deleteApiV1ClassesByClassIdPermissionsByStaffIdMutation(),
    onSuccess: () =>
      qc.invalidateQueries({
        queryKey: getApiV1ClassesByClassIdPermissionsQueryKey({ path: { classId } }),
      }),
  })

  if (isLoading) {
    return (
      <div className="px-6 py-4 animate-pulse space-y-2">
        {[1, 2].map((i) => (
          <div key={i} className="h-8 bg-gray-100 rounded-lg" />
        ))}
      </div>
    )
  }

  return (
    <div className="border-t border-gray-100 bg-brand-50/40 px-6 py-4 space-y-4">
      {allSuperadmin && (
        <div className="flex items-start gap-3 px-4 py-3 bg-blue-50 border border-blue-200 rounded-lg text-sm text-blue-800">
          <svg
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            className="shrink-0 mt-0.5"
          >
            <circle cx="12" cy="12" r="10" />
            <line x1="12" y1="8" x2="12" y2="12" />
            <line x1="12" y1="16" x2="12.01" y2="16" />
          </svg>
          Ingen adgangsbegrænsninger. Alle medarbejdere kan redigere denne klasse.
        </div>
      )}

      {permissions.length > 0 && (
        <div className="space-y-2">
          <p className="text-xs font-semibold text-gray-400 uppercase tracking-wider">
            Tildelt adgang
          </p>
          {permissions.map((p) => (
            <div
              key={p.staffId}
              className="flex items-center justify-between px-4 py-2.5 bg-white border border-gray-200 rounded-lg"
            >
              <span className="text-sm text-gray-800">{p.staffName}</span>
              <button
                data-testid={`revoke-permission-${p.staffId}`}
                onClick={() => revokeMutation.mutate({ path: { classId, staffId: p.staffId! } })}
                disabled={revokeMutation.isPending}
                className="text-xs text-red-600 hover:text-red-800 transition-colors disabled:opacity-50"
              >
                Fjern
              </button>
            </div>
          ))}
        </div>
      )}

      {ungrantedStaff.length > 0 && (
        <div className="space-y-2">
          <p className="text-xs font-semibold text-gray-400 uppercase tracking-wider">
            Tilføj medarbejder
          </p>
          <div className="flex flex-wrap gap-2">
            <select
              value={selectedStaffId}
              onChange={(e) => setSelectedStaffId(e.target.value)}
              className="flex-1 min-w-0 px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent bg-white"
            >
              <option value="">Vælg medarbejder…</option>
              {ungrantedStaff.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </select>
            <button
              data-testid="grant-permission-btn"
              disabled={!selectedStaffId || grantMutation.isPending}
              onClick={() =>
                grantMutation.mutate({ path: { classId }, body: { staffId: selectedStaffId } })
              }
              className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors shrink-0"
            >
              Tilføj
            </button>
          </div>
          {grantMutation.isError && (
            <p className="text-sm text-red-600">Der opstod en fejl. Prøv igen.</p>
          )}
        </div>
      )}

      {staff.length === 0 && (
        <p className="text-sm text-gray-400">Ingen medarbejdere at tildele adgang til.</p>
      )}
      {staff.length > 0 && ungrantedStaff.length === 0 && permissions.length > 0 && (
        <p className="text-sm text-gray-400">Alle medarbejdere har fået tildelt adgang.</p>
      )}
    </div>
  )
}

function ExpandedClassPanel({
  classId,
  autoOpenCreate,
  onAutoOpenHandled,
}: {
  classId: string
  autoOpenCreate?: boolean
  onAutoOpenHandled?: () => void
}) {
  const { isAdmin } = useAuth()
  const [tab, setTab] = useState<'skemaer' | 'adgang'>('skemaer')

  return (
    <div>
      {isAdmin && (
        <div className="flex border-t border-gray-100 bg-gray-50">
          <button
            onClick={() => setTab('skemaer')}
            className={`px-5 py-2.5 text-xs font-semibold transition-colors ${tab === 'skemaer' ? 'text-brand-700 border-b-2 border-brand-600 bg-white' : 'text-gray-500 hover:text-gray-700'}`}
          >
            Skemaer
          </button>
          <button
            onClick={() => setTab('adgang')}
            className={`px-5 py-2.5 text-xs font-semibold transition-colors ${tab === 'adgang' ? 'text-brand-700 border-b-2 border-brand-600 bg-white' : 'text-gray-500 hover:text-gray-700'}`}
          >
            Adgang
          </button>
        </div>
      )}
      {tab === 'skemaer' && (
        <SchemaList
          classId={classId}
          autoOpenCreate={autoOpenCreate}
          onAutoOpenHandled={onAutoOpenHandled}
        />
      )}
      {tab === 'adgang' && isAdmin && <ClassPermissionsTab classId={classId} />}
    </div>
  )
}

function SchemaList({
  classId,
  autoOpenCreate,
  onAutoOpenHandled,
}: {
  classId: string
  autoOpenCreate?: boolean
  onAutoOpenHandled?: () => void
}) {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const { isAdmin, staffId } = useAuth()
  const { hasParentModule } = useSubscription()
  const [showCreate, setShowCreate] = useState(false)

  const { data: rawPermissions } = useQuery(
    getApiV1ClassesByClassIdPermissionsOptions({ path: { classId } })
  )
  const permissions = (rawPermissions ?? []) as ClassPermissionDto[]
  const canEditSchema =
    isAdmin || permissions.length === 0 || permissions.some((p) => p.staffId === staffId)
  const [copyingSchema, setCopyingSchema] = useState<SchemaDto | null>(null)

  useEffect(() => {
    if (autoOpenCreate) {
      setShowCreate(true)
      onAutoOpenHandled?.()
    }
  }, [autoOpenCreate, onAutoOpenHandled])

  const { data: rawSchemas, isLoading } = useQuery(
    getApiV1ClassesByClassIdSchemasOptions({ path: { classId } })
  )
  const schemas = (rawSchemas ?? []) as SchemaDto[]

  const deleteSchemaMutation = useMutation({
    ...deleteApiV1ClassesByClassIdSchemasBySchemaIdMutation(),
    onSuccess: () =>
      qc.invalidateQueries({
        queryKey: getApiV1ClassesByClassIdSchemasQueryKey({ path: { classId } }),
      }),
  })

  if (isLoading) {
    return (
      <div className="px-6 py-4 animate-pulse space-y-2">
        {[1, 2].map((i) => (
          <div key={i} className="h-10 bg-gray-100 rounded-lg" />
        ))}
      </div>
    )
  }

  return (
    <div className="border-t border-gray-100 bg-brand-50/40">
      <div className="px-6 py-3 flex items-center justify-between">
        <div className="flex items-center gap-3">
          <p className="text-xs font-semibold text-gray-400 uppercase tracking-wider">Skemaer</p>
        </div>
        {canEditSchema && hasParentModule && (
          <button
            type="button"
            onClick={() => navigate(`/fravaer/fremmoede/${classId}`)}
            data-testid={`class-attendance-${classId}`}
            className="ml-auto mr-4 text-xs font-medium text-brand-600 hover:text-brand-800 transition-colors"
          >
            Fremmøde
          </button>
        )}
        {canEditSchema && (
          <button
            onClick={() => setShowCreate(true)}
            className="flex items-center gap-1.5 text-xs font-medium text-brand-600 hover:text-brand-800 transition-colors"
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
            Nyt skema
          </button>
        )}
      </div>

      {schemas && schemas.length === 0 && (
        <p className="px-6 pb-4 text-sm text-gray-400">Ingen skemaer endnu</p>
      )}

      <div className="px-4 pb-4 space-y-2">
        {schemas?.map((s) => {
          const active = isActiveNow(s.startDate, s.endDate)
          const dateRange = formatDateRange(s.startDate, s.endDate)
          return (
            <button
              key={s.id}
              type="button"
              onClick={() => navigate(`/klasser/${classId}/skema/${s.id}`)}
              className="w-full text-left flex flex-col gap-2 px-4 py-3 bg-white rounded-lg border border-gray-200 hover:border-brand-300 hover:bg-brand-50/30 transition-colors group cursor-pointer"
            >
              <div className="flex items-center gap-2 min-w-0">
                <span className="font-medium text-sm text-gray-800 group-hover:text-brand-700 transition-colors truncate flex-1">
                  {s.name}
                </span>
                {active && (
                  <span className="shrink-0 flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-700">
                    <span className="w-1.5 h-1.5 rounded-full bg-green-500 inline-block" />
                    Aktiv nu
                  </span>
                )}
              </div>
              {dateRange && <span className="text-xs text-gray-400">{dateRange}</span>}
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                <button
                  data-testid={`class-ugeplan-${classId}-${s.id}`}
                  onClick={(e) => {
                    e.stopPropagation()
                    navigate(`/klasser/${classId}/ugeplan?schemaId=${s.id}`)
                  }}
                  className="flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-semibold text-brand-700 bg-brand-50 border border-brand-200 rounded-lg hover:bg-brand-100 transition-colors"
                >
                  <svg
                    width="12"
                    height="12"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2.5"
                  >
                    <rect x="3" y="4" width="18" height="18" rx="2" ry="2" />
                    <line x1="16" y1="2" x2="16" y2="6" />
                    <line x1="8" y1="2" x2="8" y2="6" />
                    <line x1="3" y1="10" x2="21" y2="10" />
                  </svg>
                  Ugeplan
                </button>
                {canEditSchema && (
                  <button
                    onClick={(e) => {
                      e.stopPropagation()
                      navigate(`/klasser/${classId}/skema/${s.id}`)
                    }}
                    className="flex items-center justify-center gap-1.5 px-3 py-1.5 text-xs font-medium text-gray-600 bg-gray-100 rounded-lg hover:bg-gray-200 transition-colors"
                  >
                    <svg
                      width="12"
                      height="12"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2"
                    >
                      <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
                      <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
                    </svg>
                    Rediger
                  </button>
                )}
                {canEditSchema && (
                  <>
                    <button
                      onClick={(e) => {
                        e.stopPropagation()
                        setCopyingSchema(s)
                      }}
                      className="flex items-center justify-center gap-1.5 px-3 py-1.5 text-xs font-medium text-gray-600 bg-gray-100 rounded-lg hover:bg-gray-200 transition-colors"
                    >
                      <svg
                        width="12"
                        height="12"
                        viewBox="0 0 24 24"
                        fill="none"
                        stroke="currentColor"
                        strokeWidth="2"
                      >
                        <rect x="9" y="9" width="13" height="13" rx="2" ry="2" />
                        <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
                      </svg>
                      Kopiér
                    </button>
                    <button
                      onClick={(e) => {
                        e.stopPropagation()
                        if (
                          confirm(
                            `Slet skemaet "${s.name}"? Alle lektioner i skemaet slettes også.`
                          )
                        ) {
                          deleteSchemaMutation.mutate({ path: { classId, schemaId: s.id! } })
                        }
                      }}
                      className="flex items-center justify-center gap-1.5 px-3 py-1.5 text-xs font-medium text-red-600 bg-red-50 rounded-lg hover:bg-red-100 transition-colors"
                    >
                      <svg
                        width="12"
                        height="12"
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
                      Slet
                    </button>
                  </>
                )}
              </div>
            </button>
          )
        })}
      </div>

      {showCreate && (
        <SchemaModal
          classId={classId}
          onClose={() => setShowCreate(false)}
          onSaved={() => setShowCreate(false)}
        />
      )}
      {copyingSchema && (
        <CopySchemaModal
          classId={classId}
          schemaId={copyingSchema.id!}
          sourceName={copyingSchema.name!}
          onClose={() => setCopyingSchema(null)}
          onSaved={() => setCopyingSchema(null)}
        />
      )}
    </div>
  )
}

export default function ClassesPage() {
  usePageTitle('Klasser')
  const { isAdmin } = useAuth()
  const [searchParams, setSearchParams] = useSearchParams()
  const qc = useQueryClient()
  const [showCreate, setShowCreate] = useState(false)
  const [editingClass, setEditingClass] = useState<ClassDto | null>(null)
  const [expandedClass, setExpandedClass] = useState<string | null>(() => {
    return localStorage.getItem('classes-expanded') ?? null
  })
  const [newSchemaForClass, setNewSchemaForClass] = useState<string | null>(null)
  const [openMenuId, setOpenMenuId] = useState<string | null>(null)

  useEffect(() => {
    if (!openMenuId) return
    const close = () => setOpenMenuId(null)
    document.addEventListener('click', close)
    return () => document.removeEventListener('click', close)
  }, [openMenuId])

  const { data: rawClasses, isLoading, isError, refetch } = useQuery(getApiV1ClassesOptions())
  const classes = useMemo(() => (rawClasses ?? []) as ClassDto[], [rawClasses])

  useEffect(() => {
    const action = searchParams.get('action')
    const classId = searchParams.get('classId')
    if (action === 'new-schema' && classId && classes) {
      const match = classes.find((c) => c.id === classId)
      if (match) {
        setExpandedClass(classId)
        setNewSchemaForClass(classId)
        setSearchParams({}, { replace: true })
      }
    }
  }, [searchParams, classes, setSearchParams])

  const deleteMutation = useMutation({
    ...deleteApiV1ClassesByIdMutation(),
    onSuccess: () => qc.invalidateQueries({ queryKey: getApiV1ClassesQueryKey() }),
  })

  const toggleExpand = (id: string) => {
    setExpandedClass((prev) => {
      const next = prev === id ? null : id
      if (next) localStorage.setItem('classes-expanded', next)
      else localStorage.removeItem('classes-expanded')
      return next
    })
  }

  return (
    <div className="p-6 lg:p-8 max-w-4xl mx-auto space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="font-display text-2xl font-semibold text-gray-900">Klasser</h1>
          <p className="mt-1 text-sm text-gray-500">Administrer klasser og deres skemaer</p>
        </div>
        {isAdmin && (
          <button
            onClick={() => setShowCreate(true)}
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
            <span className="hidden sm:inline">Opret klasse</span>
          </button>
        )}
      </div>

      {/* Error */}
      {isError && (
        <div className="bg-red-50 border border-red-200 rounded-xl p-5 flex items-center justify-between">
          <p className="text-red-700 text-sm font-medium">Kunne ikke hente klasser</p>
          <button
            onClick={() => refetch()}
            className="text-sm px-3 py-1.5 bg-red-100 text-red-700 rounded-lg hover:bg-red-200 transition-colors"
          >
            Prøv igen
          </button>
        </div>
      )}

      {/* List */}
      <div className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100">
        {isLoading &&
          Array.from({ length: 4 }).map((_, i) => (
            <div key={i} className="px-5 py-4 animate-pulse flex justify-between">
              <div className="h-5 w-20 bg-gray-200 rounded" />
              <div className="h-5 w-32 bg-gray-100 rounded" />
            </div>
          ))}

        {!isLoading && classes?.length === 0 && (
          <div className="px-5 py-10 text-center text-gray-400 text-sm">
            Ingen klasser oprettet endnu
          </div>
        )}

        {classes?.map((cls) => (
          <div key={cls.id}>
            <div
              className="flex items-center justify-between px-5 py-4"
              data-testid={`class-row-${cls.id}`}
            >
              <button
                onClick={() => toggleExpand(cls.id!)}
                className="flex items-center gap-3 min-w-0 text-left group"
              >
                <svg
                  width="16"
                  height="16"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2"
                  className={`shrink-0 text-gray-400 transition-transform ${expandedClass === cls.id ? 'rotate-90' : ''}`}
                >
                  <polyline points="9 18 15 12 9 6" />
                </svg>
                <span className="font-semibold text-gray-900 group-hover:text-brand-700 transition-colors">
                  {cls.name}
                </span>
                {cls.description && (
                  <span className="text-sm text-gray-400 truncate">{cls.description}</span>
                )}
              </button>
              {isAdmin && (
                <div className="relative shrink-0 ml-4">
                  <button
                    data-testid={`class-menu-${cls.id}`}
                    onClick={(e) => {
                      e.stopPropagation()
                      setOpenMenuId(openMenuId === cls.id ? null : cls.id!)
                    }}
                    className="p-1.5 text-gray-400 hover:text-gray-700 rounded-md hover:bg-gray-100 transition-colors"
                    title="Flere handlinger"
                  >
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor">
                      <circle cx="5" cy="12" r="1.5" />
                      <circle cx="12" cy="12" r="1.5" />
                      <circle cx="19" cy="12" r="1.5" />
                    </svg>
                  </button>
                  {openMenuId === cls.id && (
                    <div className="absolute right-0 top-full mt-1 w-36 bg-white border border-gray-200 rounded-lg shadow-lg z-10 py-1">
                      <button
                        data-testid={`class-edit-${cls.id}`}
                        onClick={() => {
                          setEditingClass(cls)
                          setOpenMenuId(null)
                        }}
                        className="w-full flex items-center gap-2 px-3 py-2 text-sm text-gray-700 hover:bg-gray-50 transition-colors"
                      >
                        <svg
                          width="14"
                          height="14"
                          viewBox="0 0 24 24"
                          fill="none"
                          stroke="currentColor"
                          strokeWidth="2"
                        >
                          <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
                          <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
                        </svg>
                        Rediger
                      </button>
                      <button
                        data-testid={`class-delete-${cls.id}`}
                        onClick={() => {
                          setOpenMenuId(null)
                          if (confirm(`Slet klassen "${cls.name}"?`))
                            deleteMutation.mutate({ path: { id: cls.id! } })
                        }}
                        className="w-full flex items-center gap-2 px-3 py-2 text-sm text-red-600 hover:bg-red-50 transition-colors"
                      >
                        <svg
                          width="14"
                          height="14"
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
                        Slet
                      </button>
                    </div>
                  )}
                </div>
              )}
            </div>
            {expandedClass === cls.id && (
              <ExpandedClassPanel
                classId={cls.id}
                autoOpenCreate={newSchemaForClass === cls.id}
                onAutoOpenHandled={() => setNewSchemaForClass(null)}
              />
            )}
          </div>
        ))}
      </div>

      {/* Modals */}
      {showCreate && (
        <ClassModal onClose={() => setShowCreate(false)} onSaved={() => setShowCreate(false)} />
      )}
      {editingClass && (
        <ClassModal
          initial={editingClass}
          onClose={() => setEditingClass(null)}
          onSaved={() => setEditingClass(null)}
        />
      )}
    </div>
  )
}
