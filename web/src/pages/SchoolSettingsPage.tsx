import { useState, useRef, useEffect } from 'react'
import { Modal } from '../components/Modal'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1SchoolsSettingsOptions,
  getApiV1SchoolsSettingsQueryKey,
  putApiV1SchoolsSettingsMutation,
  postApiV1SchoolsLogoMutation,
  getApiV1TimeSlotTemplateOptions,
  getApiV1TimeSlotTemplateQueryKey,
  putApiV1TimeSlotTemplateMutation,
  getApiV1BoardMembersOptions,
  getApiV1BoardMembersQueryKey,
  deleteApiV1BoardMembersByIdMutation,
  patchApiV1BoardMembersByIdTeacherDataAccessMutation,
  postApiV1BoardMembersInviteMutation,
} from '../api/generated/@tanstack/react-query.gen'
import { TimeInput } from '../components/TimeInput'
import { LessonDurationSlider } from '../components/LessonDurationSlider'
import { usePageTitle } from '../hooks/usePageTitle'

interface TimeSlotTemplateDto {
  id: string
  lessonDurationMinutes: number
  dayStartTime: string
  dayEndTime: string
  activeDays: string
  breaks: { id: string; startTime: string; durationMinutes: number }[]
}

interface BreakEntry {
  startTime: string
  durationMinutes: number
}

export default function SchoolSettingsPage() {
  usePageTitle('Indstillinger')
  const qc = useQueryClient()
  const fileRef = useRef<HTMLInputElement>(null)

  const { data, isLoading } = useQuery(getApiV1SchoolsSettingsOptions())

  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [phone, setPhone] = useState('')
  const [initialized, setInitialized] = useState(false)

  useEffect(() => {
    if (data && !initialized) {
      setName(data.name ?? '')
      setEmail(data.contactEmail ?? '')
      setPhone(data.contactPhone ?? '')
      setInitialized(true)
    }
  }, [data, initialized])

  const [saveError, setSaveError] = useState<string | null>(null)
  const [saveSuccess, setSaveSuccess] = useState(false)
  const [logoError, setLogoError] = useState<string | null>(null)

  const saveMutation = useMutation({
    ...putApiV1SchoolsSettingsMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: getApiV1SchoolsSettingsQueryKey() })
      setInitialized(false)
      setSaveError(null)
      setSaveSuccess(true)
      setTimeout(() => setSaveSuccess(false), 3000)
    },
    onError: (err) => {
      setSaveError(err instanceof Error ? err.message : 'Der opstod en fejl.')
      setSaveSuccess(false)
    },
  })

  const logoMutation = useMutation({
    ...postApiV1SchoolsLogoMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: getApiV1SchoolsSettingsQueryKey() })
      setLogoError(null)
      if (fileRef.current) fileRef.current.value = ''
    },
    onError: (err) => {
      setLogoError(err instanceof Error ? err.message : 'Logoet kunne ikke uploades.')
    },
  })

  if (isLoading) {
    return (
      <div className="p-6 lg:p-8 max-w-2xl mx-auto space-y-6">
        <div className="h-8 w-48 bg-gray-200 rounded animate-pulse" />
        <div className="h-40 bg-gray-100 rounded-xl animate-pulse" />
      </div>
    )
  }

  return (
    <div className="p-6 lg:p-8 max-w-2xl mx-auto space-y-8">
      <div>
        <h1 className="font-display text-2xl font-semibold text-gray-900">Skoleindstillinger</h1>
        <p className="mt-1 text-sm text-gray-500">Navn, kontaktoplysninger og logo</p>
      </div>

      {/* General settings */}
      <div className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100">
        <div className="px-6 py-5">
          <h2 className="text-sm font-semibold text-gray-700">Generelt</h2>
        </div>
        <div className="px-6 py-5 space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Skolens navn *</label>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  e.preventDefault()
                  if (name.trim())
                    saveMutation.mutate({
                      body: { name, contactEmail: email || null, contactPhone: phone || null },
                    })
                }
              }}
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Kontakt-e-mail</label>
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  e.preventDefault()
                  if (name.trim())
                    saveMutation.mutate({
                      body: { name, contactEmail: email || null, contactPhone: phone || null },
                    })
                }
              }}
              placeholder="kontakt@skolen.dk"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Kontakttelefon</label>
            <input
              type="tel"
              value={phone}
              onChange={(e) => setPhone(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  e.preventDefault()
                  if (name.trim())
                    saveMutation.mutate({
                      body: { name, contactEmail: email || null, contactPhone: phone || null },
                    })
                }
              }}
              placeholder="+45 12 34 56 78"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
            />
          </div>
          {saveError && <p className="text-sm text-red-600">{saveError}</p>}
          {saveSuccess && <p className="text-sm text-green-600">Ændringer gemt.</p>}
        </div>
        <div className="px-6 py-4 flex justify-end">
          <button
            onClick={() =>
              saveMutation.mutate({
                body: { name, contactEmail: email || null, contactPhone: phone || null },
              })
            }
            disabled={!name.trim() || saveMutation.isPending}
            className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {saveMutation.isPending ? 'Gemmer...' : 'Gem ændringer'}
          </button>
        </div>
      </div>

      {/* Logo upload */}
      <div className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100">
        <div className="px-6 py-5">
          <h2 className="text-sm font-semibold text-gray-700">Skolelogo</h2>
        </div>
        <div className="px-6 py-5 space-y-4">
          {data?.logoUrl && (
            <img
              src={data.logoUrl}
              alt="Skolelogo"
              className="h-16 w-auto object-contain rounded border border-gray-100"
            />
          )}
          <p className="text-sm text-gray-500">PNG, JPG eller WebP · maks. 2 MB</p>
          <input
            ref={fileRef}
            type="file"
            accept=".png,.jpg,.jpeg,.webp"
            data-testid="logo-upload"
            onChange={(e) => {
              const file = e.target.files?.[0]
              if (file) logoMutation.mutate({ body: { file } })
            }}
            className="block w-full text-sm text-gray-500 file:mr-4 file:py-2 file:px-4 file:rounded-lg file:border-0 file:text-sm file:font-medium file:bg-brand-50 file:text-brand-700 hover:file:bg-brand-100"
          />
          {logoMutation.isPending && <p className="text-sm text-gray-500">Uploader...</p>}
          {logoError && <p className="text-sm text-red-600">{logoError}</p>}
        </div>
      </div>

      {/* Skoledag */}
      <SkoledagCard />

      {/* Bestyrelsesmedlemmer */}
      <BoardMembersCard />
    </div>
  )
}

function BoardMembersCard() {
  const qc = useQueryClient()
  const [showInviteModal, setShowInviteModal] = useState(false)
  const [inviteName, setInviteName] = useState('')
  const [inviteEmail, setInviteEmail] = useState('')
  const [inviteError, setInviteError] = useState<string | null>(null)
  const [inviting, setInviting] = useState(false)

  const { data: members, isLoading, isError, error } = useQuery(getApiV1BoardMembersOptions())

  const toggleMutation = useMutation({
    ...patchApiV1BoardMembersByIdTeacherDataAccessMutation(),
    onSuccess: () => void qc.invalidateQueries({ queryKey: getApiV1BoardMembersQueryKey() }),
  })

  const deleteMutation = useMutation({
    ...deleteApiV1BoardMembersByIdMutation(),
    onSuccess: () => void qc.invalidateQueries({ queryKey: getApiV1BoardMembersQueryKey() }),
  })

  const inviteMutation = useMutation({
    ...postApiV1BoardMembersInviteMutation(),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: getApiV1BoardMembersQueryKey() })
      setShowInviteModal(false)
      setInviteName('')
      setInviteEmail('')
    },
    onError: (err) => {
      const detail = (err as { detail?: string; title?: string })?.detail
      const title = (err as { detail?: string; title?: string })?.title
      setInviteError(detail ?? title ?? 'Invitation mislykkedes')
    },
  })

  async function handleInvite() {
    setInviteError(null)
    setInviting(true)
    try {
      await inviteMutation.mutateAsync({
        body: { name: inviteName.trim(), email: inviteEmail.trim() },
      })
    } catch {
      // error handled in onError
    } finally {
      setInviting(false)
    }
  }

  return (
    <>
      <div className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100">
        <div className="px-6 py-5 flex items-center justify-between">
          <div>
            <h2 className="text-sm font-semibold text-gray-700">Bestyrelsesmedlemmer</h2>
            <p className="mt-0.5 text-xs text-gray-400">
              Administrer adgang til bestyrelsesmodulet
            </p>
          </div>
          <button
            onClick={() => setShowInviteModal(true)}
            className="flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium bg-brand-600 text-white rounded-lg hover:bg-brand-700 transition-colors"
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
            Inviter
          </button>
        </div>

        {isLoading ? (
          <div className="px-6 py-4 animate-pulse space-y-3">
            {[...Array(2)].map((_, i) => (
              <div key={i} className="h-10 bg-gray-100 rounded" />
            ))}
          </div>
        ) : isError ? (
          <div className="px-6 py-8 text-center text-sm text-red-600">
            {error instanceof Error ? error.message : 'Kunne ikke hente bestyrelsesmedlemmer'}
          </div>
        ) : members?.length === 0 ? (
          <div className="px-6 py-8 text-center text-sm text-gray-400">
            Ingen bestyrelsesmedlemmer endnu
          </div>
        ) : (
          members?.map((member) => (
            <div key={member.id} className="px-6 py-4 flex items-center gap-4">
              <div className="flex-1 min-w-0">
                <p className="text-sm font-medium text-gray-900 truncate">{member.name}</p>
                <p className="text-xs text-gray-500 truncate">{member.email}</p>
              </div>
              {member.hasAccount ? (
                <span className="shrink-0 px-2 py-0.5 text-xs font-medium rounded-full bg-green-100 text-green-700">
                  Konto oprettet
                </span>
              ) : (
                <span className="shrink-0 px-2 py-0.5 text-xs font-medium rounded-full bg-gray-100 text-gray-500">
                  Afventer
                </span>
              )}
              <label className="shrink-0 flex items-center gap-2 text-xs text-gray-600 cursor-pointer select-none">
                <input
                  type="checkbox"
                  checked={member.canAccessTeacherData}
                  onChange={(e) =>
                    toggleMutation.mutate({
                      path: { id: member.id! },
                      body: { canAccessTeacherData: e.target.checked },
                    })
                  }
                  className="rounded border-gray-300 text-brand-600 focus:ring-brand-500"
                />
                Læreradgang
              </label>
              <button
                onClick={() => deleteMutation.mutate({ path: { id: member.id! } })}
                disabled={deleteMutation.isPending}
                className="shrink-0 p-1.5 text-gray-400 hover:text-red-500 rounded-md hover:bg-red-50 transition-colors disabled:opacity-50"
                title="Fjern bestyrelsesmedlem"
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
                  <path d="M10 11v6" />
                  <path d="M14 11v6" />
                  <path d="M9 6V4h6v2" />
                </svg>
              </button>
            </div>
          ))
        )}
      </div>

      <Modal
        isOpen={showInviteModal}
        onClose={() => setShowInviteModal(false)}
        title="Inviter bestyrelsesmedlem"
      >
        <div className="px-6 py-5 space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Navn *</label>
            <input
              value={inviteName}
              onChange={(e) => setInviteName(e.target.value)}
              placeholder="Fornavn Efternavn"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">E-mail *</label>
            <input
              type="email"
              value={inviteEmail}
              onChange={(e) => setInviteEmail(e.target.value)}
              placeholder="bestyrelse@skolen.dk"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
            />
          </div>
          {inviteError && <p className="text-sm text-red-600">{inviteError}</p>}
          <div className="flex justify-end gap-3 pt-2">
            <button
              type="button"
              onClick={() => setShowInviteModal(false)}
              className="px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors"
            >
              Annuller
            </button>
            <button
              type="button"
              onClick={handleInvite}
              disabled={!inviteName.trim() || !inviteEmail.trim() || inviting}
              className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {inviting ? 'Sender...' : 'Send invitation'}
            </button>
          </div>
        </div>
      </Modal>
    </>
  )
}

function SkoledagCard() {
  const qc = useQueryClient()

  const { data: template } = useQuery({
    ...getApiV1TimeSlotTemplateOptions(),
    retry: (failureCount, err: unknown) => {
      if ((err as { status?: number })?.status === 404) return false
      return failureCount < 3
    },
    select: (d) => d as TimeSlotTemplateDto | null,
  })

  const [lessonDuration, setLessonDuration] = useState(45)
  const [dayStart, setDayStart] = useState('08:00')
  const [dayEnd, setDayEnd] = useState('14:00')
  const [breaks, setBreaks] = useState<BreakEntry[]>([
    { startTime: '09:30', durationMinutes: 15 },
    { startTime: '12:00', durationMinutes: 30 },
  ])
  const [initialized, setInitialized] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [saveSuccess, setSaveSuccess] = useState(false)
  const [showConfirmModal, setShowConfirmModal] = useState(false)
  const [restoreError, setRestoreError] = useState<string | null>(null)
  const [restoreSuccess, setRestoreSuccess] = useState(false)

  useEffect(() => {
    if (template !== undefined && !initialized) {
      if (template) {
        setLessonDuration(template.lessonDurationMinutes)
        setDayStart(template.dayStartTime.slice(0, 5))
        setDayEnd(template.dayEndTime.slice(0, 5))
        setBreaks(
          template.breaks.map((b) => ({
            startTime: b.startTime.slice(0, 5),
            durationMinutes: b.durationMinutes,
          }))
        )
      }
      setInitialized(true)
    }
  }, [template, initialized])

  const saveMutation = useMutation({
    ...putApiV1TimeSlotTemplateMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: getApiV1TimeSlotTemplateQueryKey() })
      setSaveError(null)
      setSaveSuccess(true)
      setShowConfirmModal(false)
      setTimeout(() => setSaveSuccess(false), 3000)
    },
    onError: (err) => {
      const detail =
        err && typeof err === 'object' && 'detail' in err
          ? (err as { detail: string }).detail
          : 'Kunne ikke gemme skoledag. Prøv igen.'
      setSaveError(detail)
      setSaveSuccess(false)
      setShowConfirmModal(false)
    },
  })

  const restoreMutation = useMutation({
    mutationFn: async () => {
      const { api } = await import('../api/client')
      return api.post<void>('/time-slot-template/restore', null)
    },
    onSuccess: async () => {
      setRestoreError(null)
      setInitialized(false)
      await qc.refetchQueries({ queryKey: getApiV1TimeSlotTemplateQueryKey() })
      setRestoreSuccess(true)
      setTimeout(() => setRestoreSuccess(false), 4000)
    },
    onError: (err) => {
      const detail =
        err && typeof err === 'object' && 'detail' in err
          ? (err as { detail: string }).detail
          : err instanceof Error
            ? err.message
            : 'Gendannelse mislykkedes. Prøv igen.'
      setRestoreError(detail)
      setRestoreSuccess(false)
    },
  })

  function addBreak() {
    setBreaks((prev) => [...prev, { startTime: '10:00', durationMinutes: 15 }])
  }
  function updateBreak(i: number, field: keyof BreakEntry, value: string | number) {
    setBreaks((prev) => prev.map((b, idx) => (idx === i ? { ...b, [field]: value } : b)))
  }
  function removeBreak(i: number) {
    setBreaks((prev) => prev.filter((_, idx) => idx !== i))
  }

  function handleSaveConfirmed() {
    saveMutation.mutate({
      body: {
        lessonDurationMinutes: lessonDuration,
        dayStartTime: `${dayStart}:00`,
        dayEndTime: `${dayEnd}:00`,
        activeDays: '1,2,3,4,5',
        breaks: breaks.map((b) => ({
          startTime: `${b.startTime}:00`,
          durationMinutes: b.durationMinutes,
        })),
      },
    })
  }

  return (
    <>
      <div className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100">
        <div className="px-6 py-5">
          <h2 className="text-sm font-semibold text-gray-700">Skoledag</h2>
          <p className="mt-0.5 text-xs text-gray-400">
            Lektionslængde og pauser for en normal skoledag
          </p>
        </div>
        <div className="px-6 py-5 space-y-5">
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Skoledag starter
              </label>
              <TimeInput value={dayStart} onChange={setDayStart} />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Skoledag slutter
              </label>
              <TimeInput value={dayEnd} onChange={setDayEnd} />
            </div>
          </div>

          <LessonDurationSlider value={lessonDuration} onChange={setLessonDuration} />

          <div>
            <div className="flex items-center justify-between mb-2">
              <label className="text-sm font-medium text-gray-700">Pauser</label>
              <button
                onClick={addBreak}
                className="flex items-center gap-1 text-sm text-brand-600 hover:text-brand-700"
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
                Tilføj pause
              </button>
            </div>
            {breaks.length === 0 && (
              <p className="text-sm text-gray-400 italic">Ingen faste pauser.</p>
            )}
            <div className="space-y-2">
              {breaks.map((b, i) => (
                <div key={i} className="flex items-end gap-2">
                  <div className="flex-1 grid grid-cols-2 gap-2">
                    <div>
                      <label className="block text-xs text-gray-500 mb-0.5">Starttidspunkt</label>
                      <TimeInput
                        value={b.startTime}
                        onChange={(v) => updateBreak(i, 'startTime', v)}
                      />
                    </div>
                    <div>
                      <label className="block text-xs text-gray-500 mb-0.5">Varighed (min)</label>
                      <input
                        type="number"
                        min={5}
                        max={60}
                        value={b.durationMinutes}
                        onFocus={(e) => e.target.select()}
                        onChange={(e) => updateBreak(i, 'durationMinutes', Number(e.target.value))}
                        className="w-full px-2 py-1.5 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
                      />
                    </div>
                  </div>
                  <button
                    onClick={() => removeBreak(i)}
                    className="p-1.5 text-gray-400 hover:text-red-500 rounded-md hover:bg-red-50 transition-colors mb-0.5"
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
              ))}
            </div>
          </div>

          {saveError && <p className="text-sm text-red-600">{saveError}</p>}
          {saveSuccess && <p className="text-sm text-green-600">Ændringer gemt.</p>}
        </div>
        <div className="px-6 py-4 space-y-3">
          <div className="flex items-start gap-3 rounded-lg border border-amber-300 bg-amber-50 px-4 py-3">
            <svg
              className="mt-0.5 shrink-0 text-amber-500"
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.5"
            >
              <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
              <line x1="12" y1="9" x2="12" y2="13" />
              <line x1="12" y1="17" x2="12.01" y2="17" />
            </svg>
            <p className="text-sm text-amber-800">
              <span className="font-semibold">Advarsel:</span> Ændringer i skoledag-indstillingerne
              gør eksisterende skemaer ubrugelige. Du kan fortryde ændringen bagefter.
            </p>
          </div>
          <div className="flex flex-col items-end gap-2">
            <button
              onClick={() => setShowConfirmModal(true)}
              disabled={saveMutation.isPending}
              className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {saveMutation.isPending ? 'Gemmer...' : 'Gem ændringer'}
            </button>
            <button
              onClick={() => restoreMutation.mutate()}
              disabled={restoreMutation.isPending}
              className="px-4 py-2 text-sm text-gray-600 border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {restoreMutation.isPending ? 'Gendanner...' : 'Fortryd seneste ændring'}
            </button>
            {restoreError && <p className="text-sm text-red-600">{restoreError}</p>}
            {restoreSuccess && <p className="text-sm text-green-600">Ændringen er fortrydet.</p>}
          </div>
        </div>
      </div>

      <Modal
        isOpen={showConfirmModal}
        onClose={() => setShowConfirmModal(false)}
        title="Er du sikker?"
      >
        <div className="px-6 py-5 space-y-4">
          <p className="text-sm text-gray-600">
            Ændringer i skoledag-indstillingerne gør eksisterende skemaer ubrugelige. En
            sikkerhedskopi gemmes automatisk, og du kan fortryde ændringen bagefter fra denne side.
          </p>
          <div className="flex justify-end gap-3 pt-2">
            <button
              type="button"
              onClick={() => setShowConfirmModal(false)}
              className="px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors"
            >
              Annuller
            </button>
            <button
              type="button"
              onClick={handleSaveConfirmed}
              disabled={saveMutation.isPending}
              className="px-4 py-2 text-sm bg-red-600 text-white rounded-lg hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {saveMutation.isPending ? 'Gemmer...' : 'Gem alligevel'}
            </button>
          </div>
        </div>
      </Modal>
    </>
  )
}
