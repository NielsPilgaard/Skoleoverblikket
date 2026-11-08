import { useState, useEffect } from 'react'
import { Modal } from '../components/Modal'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import {
  getApiV1StaffOptions,
  getApiV1StaffQueryKey,
  postApiV1StaffMutation,
  putApiV1StaffByIdMutation,
  deleteApiV1StaffByIdMutation,
  getApiV1StaffInvitationsOptions,
  getApiV1StaffInvitationsQueryKey,
  getApiV1StaffInvitationsByStaffByStaffIdOptions,
  getApiV1StaffInvitationsByStaffByStaffIdQueryKey,
  postApiV1StaffInvitationsInviteByStaffIdMutation,
} from '../api/generated/@tanstack/react-query.gen'
import type { StaffDto, StaffRole, InvitationDto } from '../api/client'
import { usePageTitle } from '../hooks/usePageTitle'
import keycloak from '../auth/keycloak'

const ROLE_OPTIONS: { value: StaffRole; label: string }[] = [
  { value: 'Teacher', label: 'Lærer' },
  { value: 'Aide', label: 'Pædagog' },
  { value: 'Substitute', label: 'Vikar' },
]

function roleLabel(role: StaffRole): string {
  return ROLE_OPTIONS.find((r) => r.value === role)?.label ?? role
}

function roleBadge(role: StaffRole): string {
  if (role === 'Teacher') return 'bg-blue-100 text-blue-700'
  if (role === 'Aide') return 'bg-teal-100 text-teal-700'
  return 'bg-gray-100 text-gray-600'
}

type InviteStatus = 'Pending' | 'Accepted' | 'Expired'

function inviteStatusBadge(status: InviteStatus): string {
  if (status === 'Accepted') return 'bg-green-100 text-green-700'
  if (status === 'Expired') return 'bg-red-100 text-red-700'
  return 'bg-amber-100 text-amber-700'
}

function inviteStatusLabel(status: InviteStatus): string {
  if (status === 'Accepted') return 'Accepteret'
  if (status === 'Expired') return 'Udløbet'
  return 'Afventer'
}

function AdminToggle({
  value,
  onChange,
  disabled,
  disabledTooltip,
}: {
  value: boolean
  onChange: (v: boolean) => void
  disabled?: boolean
  disabledTooltip?: string
}) {
  return (
    <div className="flex items-center gap-3">
      <span className="text-sm font-medium text-gray-700">Administratoradgang</span>
      <button
        type="button"
        role="switch"
        aria-checked={value}
        disabled={disabled}
        title={disabled ? disabledTooltip : undefined}
        onClick={() => !disabled && onChange(!value)}
        className={`relative inline-flex h-6 w-11 flex-shrink-0 rounded-full border-2 border-transparent transition-colors duration-200 focus:outline-none focus:ring-2 focus:ring-brand-500 focus:ring-offset-1 ${
          disabled ? 'cursor-not-allowed opacity-50' : 'cursor-pointer'
        } ${value ? 'bg-brand-600' : 'bg-gray-200'}`}
      >
        <span
          className={`pointer-events-none inline-block h-5 w-5 transform rounded-full bg-white shadow ring-0 transition duration-200 ${
            value ? 'translate-x-5' : 'translate-x-0'
          }`}
        />
      </button>
    </div>
  )
}

interface StaffModalProps {
  initial?: StaffDto
  onClose: () => void
  onSaved: (created?: StaffDto) => void
  currentUserKeycloakSubject?: string
}

function StaffModal({ initial, onClose, onSaved, currentUserKeycloakSubject }: StaffModalProps) {
  const qc = useQueryClient()
  const [name, setName] = useState(initial?.name ?? '')
  const [email, setEmail] = useState(initial?.email ?? '')
  const [phone, setPhone] = useState(initial?.phone ?? '')
  const [role, setRole] = useState<StaffRole>(initial?.role ?? 'Teacher')
  const [isAdmin, setIsAdmin] = useState(initial?.isAdmin ?? false)

  const isEditingSelf =
    initial?.keycloakSubject != null && initial.keycloakSubject === currentUserKeycloakSubject
  const inviteNotAccepted = initial !== undefined && !initial.keycloakSubject

  const createMutation = useMutation({
    ...postApiV1StaffMutation(),
    onSuccess: (created) => {
      qc.invalidateQueries({ queryKey: getApiV1StaffQueryKey() })
      onSaved(created)
    },
  })
  const updateMutation = useMutation({
    ...putApiV1StaffByIdMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: getApiV1StaffQueryKey() })
      onSaved()
    },
  })

  const isPending = createMutation.isPending || updateMutation.isPending
  const isError = createMutation.isError || updateMutation.isError

  function handleSave() {
    if (!name.trim() || isPending) return
    const body = { name, email: email || null, phone: phone || null, role, isAdmin }
    if (initial) {
      updateMutation.mutate({ path: { id: initial.id! }, body })
    } else {
      createMutation.mutate({ body })
    }
  }

  return (
    <Modal isOpen onClose={onClose} title={initial ? 'Rediger medarbejder' : 'Opret medarbejder'}>
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
            placeholder="Fuldt navn"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Rolle *</label>
          <select
            value={role}
            onChange={(e) => setRole(e.target.value as StaffRole)}
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent bg-white"
          >
            {ROLE_OPTIONS.map((r) => (
              <option key={r.value} value={r.value}>
                {r.label}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">E-mail</label>
          <input
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault()
                handleSave()
              }
            }}
            type="email"
            placeholder="navn@skole.dk"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Telefon</label>
          <input
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault()
                handleSave()
              }
            }}
            type="tel"
            placeholder="+45 12 34 56 78"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>
        <AdminToggle
          value={isAdmin}
          onChange={setIsAdmin}
          disabled={isEditingSelf || inviteNotAccepted}
          disabledTooltip={
            isEditingSelf
              ? 'Du kan ikke ændre din egen administratoradgang'
              : 'Medarbejderen skal acceptere invitationen først'
          }
        />
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

interface InviteModalProps {
  staff: StaffDto
  onClose: () => void
}

function InviteModal({ staff, onClose }: InviteModalProps) {
  const qc = useQueryClient()
  const [sent, setSent] = useState(false)
  const [errorMsg, setErrorMsg] = useState('')

  useEffect(() => {
    function handleKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKey)
    return () => document.removeEventListener('keydown', handleKey)
  }, [onClose])

  const { data: invitations, isLoading } = useQuery(
    getApiV1StaffInvitationsByStaffByStaffIdOptions({ path: { staffId: staff.id! } })
  )

  const sendMutation = useMutation({
    ...postApiV1StaffInvitationsInviteByStaffIdMutation(),
    onSuccess: () => {
      qc.invalidateQueries({
        queryKey: getApiV1StaffInvitationsByStaffByStaffIdQueryKey({
          path: { staffId: staff.id! },
        }),
      })
      qc.invalidateQueries({ queryKey: getApiV1StaffInvitationsQueryKey() })
      setSent(true)
    },
    onError: (err) => {
      const msg = err instanceof Error ? err.message : 'Ukendt fejl'
      setErrorMsg(msg)
    },
  })

  return (
    <Modal isOpen onClose={onClose} title={`Invitér ${staff.name}`}>
      <div className="px-6 py-5 space-y-4">
        {staff.email ? (
          <p className="text-sm text-gray-600">
            Der sendes en invitationsmail til <strong>{staff.email}</strong> med et link til at
            oprette en konto.
          </p>
        ) : (
          <div className="bg-amber-50 border border-amber-200 rounded-lg p-3">
            <p className="text-sm text-amber-800">
              Medarbejderen har ingen e-mailadresse. Tilføj en e-mail under "Rediger" og prøv igen.
            </p>
          </div>
        )}

        {!isLoading && invitations && invitations.length > 0 && (
          <div className="space-y-2">
            <p className="text-xs font-semibold text-gray-500 uppercase tracking-wide">
              Tidligere invitationer
            </p>
            <div className="space-y-1.5">
              {invitations.map((inv) => (
                <div
                  key={inv.id}
                  className="flex items-center justify-between text-sm py-1.5 px-3 bg-gray-50 rounded-lg"
                >
                  <span className="text-gray-600">
                    {inv.createdAt ? new Date(inv.createdAt).toLocaleDateString('da-DK') : '—'}
                  </span>
                  <span
                    className={`px-2 py-0.5 rounded-full text-xs font-medium ${inviteStatusBadge((inv.status ?? 'Pending') as InviteStatus)}`}
                  >
                    {inviteStatusLabel((inv.status ?? 'Pending') as InviteStatus)}
                  </span>
                </div>
              ))}
            </div>
          </div>
        )}

        {sent && (
          <div className="bg-green-50 border border-green-200 rounded-lg p-3">
            <p className="text-sm text-green-800">
              Invitation sendt! Medarbejderen modtager en e-mail inden for kort tid.
            </p>
          </div>
        )}

        {errorMsg && (
          <p className="text-sm text-red-600 bg-red-50 px-3 py-2 rounded-lg">{errorMsg}</p>
        )}
      </div>

      <div className="px-6 py-4 border-t border-gray-100 flex justify-end gap-3">
        <button
          type="button"
          onClick={onClose}
          className="px-4 py-2 text-sm text-gray-600 hover:text-gray-900"
        >
          Luk
        </button>
        {staff.email && !sent && (
          <button
            type="button"
            onClick={() => {
              setSent(false)
              sendMutation.mutate({ path: { staffId: staff.id! } })
            }}
            disabled={sendMutation.isPending}
            className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {sendMutation.isPending ? 'Sender…' : 'Send invitation'}
          </button>
        )}
      </div>
    </Modal>
  )
}

export default function StaffPage() {
  usePageTitle('Medarbejdere')
  const qc = useQueryClient()
  const navigate = useNavigate()
  const [showCreate, setShowCreate] = useState(false)
  const [editingStaff, setEditingStaff] = useState<StaffDto | null>(null)
  const [invitingStaff, setInvitingStaff] = useState<StaffDto | null>(null)

  const currentUserKeycloakSubject = (keycloak.tokenParsed as Record<string, string> | undefined)
    ?.sub

  const { data: staff, isLoading, isError, refetch } = useQuery(getApiV1StaffOptions())

  const { data: allInvitations } = useQuery(getApiV1StaffInvitationsOptions())

  const [deleteError, setDeleteError] = useState<string | null>(null)

  const deleteMutation = useMutation({
    mutationFn: deleteApiV1StaffByIdMutation().mutationFn,
    onSuccess: () => qc.invalidateQueries({ queryKey: getApiV1StaffQueryKey() }),
    onError: (err: unknown) => {
      const problem = err as { detail?: string; status?: number } | null
      if (problem?.status === 409) {
        setDeleteError(problem.detail ?? 'Medarbejderen kan ikke slettes.')
      } else {
        setDeleteError('Der opstod en fejl. Prøv igen.')
      }
    },
  })

  function getLatestInvite(staffId: string): InvitationDto | undefined {
    return allInvitations?.filter((i) => i.staffId === staffId)[0]
  }

  return (
    <div className="p-6 lg:p-8 max-w-6xl mx-auto space-y-6">
      <Modal
        isOpen={!!deleteError}
        onClose={() => setDeleteError(null)}
        title="Kan ikke slette medarbejder"
        size="sm"
      >
        <div className="px-6 py-5 space-y-4">
          <p className="text-sm text-gray-600">{deleteError}</p>
          <div className="flex justify-end">
            <button
              type="button"
              onClick={() => setDeleteError(null)}
              className="px-4 py-2 text-sm font-medium text-white bg-brand-600 rounded-lg hover:bg-brand-700 transition-colors"
            >
              OK
            </button>
          </div>
        </div>
      </Modal>
      <div className="flex items-center justify-between">
        <div>
          <h1 className="font-display text-2xl font-semibold text-gray-900">Medarbejdere</h1>
          <p className="mt-1 text-sm text-gray-500">Lærere, pædagoger og vikarer</p>
        </div>
        <button
          onClick={() => setShowCreate(true)}
          aria-label="Opret medarbejder"
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
          <span className="hidden sm:inline">Opret medarbejder</span>
        </button>
      </div>

      {isError && (
        <div className="bg-red-50 border border-red-200 rounded-xl p-5 flex items-center justify-between">
          <p className="text-red-700 text-sm font-medium">Kunne ikke hente medarbejdere</p>
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
                <th className="px-5 py-3 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide">
                  Rolle
                </th>
                <th className="px-5 py-3 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide hidden sm:table-cell">
                  E-mail
                </th>
                <th className="px-5 py-3 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide hidden lg:table-cell">
                  Invitation
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
                      <div className="h-4 w-28 bg-gray-200 rounded" />
                    </td>
                    <td className="px-5 py-3">
                      <div className="h-5 w-16 bg-gray-100 rounded-full" />
                    </td>
                    <td className="px-5 py-3 hidden sm:table-cell">
                      <div className="h-4 w-36 bg-gray-100 rounded" />
                    </td>
                    <td className="px-5 py-3 hidden lg:table-cell">
                      <div className="h-4 w-20 bg-gray-100 rounded-full" />
                    </td>
                    <td className="px-5 py-3" />
                  </tr>
                ))}
              {!isLoading && staff?.length === 0 && (
                <tr>
                  <td colSpan={5} className="px-5 py-12 text-center">
                    <p className="text-gray-400 font-medium">Ingen medarbejdere oprettet endnu</p>
                    <p className="text-gray-400 text-xs mt-1">
                      Opret din første medarbejder for at komme i gang
                    </p>
                  </td>
                </tr>
              )}
              {staff?.map((s) => {
                const invite = getLatestInvite(s.id ?? '')
                const isSelf =
                  s.keycloakSubject != null && s.keycloakSubject === currentUserKeycloakSubject
                return (
                  <tr key={s.id} className="hover:bg-gray-50 transition-colors">
                    <td className="px-5 py-3 font-medium text-gray-900">
                      <div className="flex items-center gap-2">
                        {s.name}
                        {s.isAdmin && (
                          <span title="Administrator" className="text-brand-600">
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
                              <path d="M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4z" />
                            </svg>
                          </span>
                        )}
                      </div>
                    </td>
                    <td className="px-5 py-3">
                      <span
                        className={`px-2 py-0.5 rounded-full text-xs font-medium ${roleBadge(s.role ?? 'Teacher')}`}
                      >
                        {roleLabel(s.role ?? 'Teacher')}
                      </span>
                    </td>
                    <td className="px-5 py-3 text-gray-500 hidden sm:table-cell">
                      {s.email ?? '—'}
                    </td>
                    <td className="px-5 py-3 hidden lg:table-cell">
                      {invite ? (
                        <span
                          className={`px-2 py-0.5 rounded-full text-xs font-medium ${inviteStatusBadge((invite.status ?? 'Pending') as InviteStatus)}`}
                        >
                          {inviteStatusLabel((invite.status ?? 'Pending') as InviteStatus)}
                        </span>
                      ) : (
                        <span className="text-xs text-gray-400">Ikke inviteret</span>
                      )}
                    </td>
                    <td className="px-5 py-3 text-right">
                      <div className="flex items-center justify-end gap-2">
                        {!isSelf && !s.keycloakSubject && (
                          <button
                            onClick={() => setInvitingStaff(s)}
                            className="p-1.5 text-gray-400 hover:text-brand-600 rounded-md hover:bg-brand-50 transition-colors"
                            title="Send invitation"
                          >
                            <svg
                              width="15"
                              height="15"
                              viewBox="0 0 24 24"
                              fill="none"
                              stroke="currentColor"
                              strokeWidth="2"
                            >
                              <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z" />
                              <polyline points="22,6 12,13 2,6" />
                            </svg>
                          </button>
                        )}
                        <button
                          onClick={() => navigate(`/medarbejdere/${s.id}/skema`)}
                          className="p-1.5 text-gray-400 hover:text-brand-600 rounded-md hover:bg-brand-50 transition-colors"
                          title="Se skema"
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
                        <button
                          onClick={() => setEditingStaff(s)}
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
                        <button
                          onClick={() => {
                            if (isSelf) return
                            if (confirm(`Slet "${s.name}"?`))
                              deleteMutation.mutate({ path: { id: s.id ?? '' } })
                          }}
                          disabled={isSelf}
                          title={isSelf ? 'Du kan ikke slette dig selv' : 'Slet'}
                          className="p-1.5 text-gray-400 hover:text-red-600 rounded-md hover:bg-red-50 transition-colors disabled:opacity-30 disabled:cursor-not-allowed disabled:hover:text-gray-400 disabled:hover:bg-transparent"
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
                )
              })}
            </tbody>
          </table>
        </div>
      </div>

      {showCreate && (
        <StaffModal
          onClose={() => setShowCreate(false)}
          onSaved={(created) => {
            setShowCreate(false)
            if (created?.email) setInvitingStaff(created)
          }}
          currentUserKeycloakSubject={currentUserKeycloakSubject}
        />
      )}
      {editingStaff && (
        <StaffModal
          initial={editingStaff}
          onClose={() => setEditingStaff(null)}
          onSaved={() => setEditingStaff(null)}
          currentUserKeycloakSubject={currentUserKeycloakSubject}
        />
      )}
      {invitingStaff && (
        <InviteModal staff={invitingStaff} onClose={() => setInvitingStaff(null)} />
      )}
    </div>
  )
}
