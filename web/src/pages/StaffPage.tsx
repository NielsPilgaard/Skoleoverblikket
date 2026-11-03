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
import { type Badge, PersonList, type RowAction } from '../components/ui/list'
import { Select } from '../components/ui/select'

const ROLE_OPTIONS: { value: StaffRole; label: string }[] = [
  { value: 'Teacher', label: 'Lærer' },
  { value: 'Aide', label: 'Pædagog' },
  { value: 'Substitute', label: 'Vikar' },
]

const ROLE_PLURAL: Record<StaffRole, string> = {
  Teacher: 'Lærere',
  Aide: 'Pædagoger',
  Substitute: 'Vikarer',
}

function roleLabel(role: StaffRole): string {
  return ROLE_OPTIONS.find((r) => r.value === role)?.label ?? role
}

function byName(a: StaffDto, b: StaffDto) {
  return a.name.localeCompare(b.name, 'da')
}

/** Login state for the list: no badge once the person has logged in. */
function loginStatus(
  s: StaffDto,
  invite: InvitationDto | undefined
): { text: string; badge?: Badge } {
  if (s.keycloakSubject) return { text: 'Aktiv' }
  if (!invite) return { text: 'Ikke inviteret', badge: { label: 'Ikke inviteret', tone: 'gray' } }
  if (invite.status === 'Expired')
    return { text: 'Invitationen er udløbet', badge: { label: 'Invitation udløbet', tone: 'red' } }
  const sent = invite.createdAt ? ` ${new Date(invite.createdAt).toLocaleDateString('da-DK')}` : ''
  return {
    text: `Inviteret${sent}, ikke logget ind endnu`,
    badge: { label: 'Inviteret', tone: 'amber' },
  }
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
          <Select
            label="Rolle"
            options={ROLE_OPTIONS.map((r) => ({ value: r.value, label: r.label }))}
            value={role}
            onChange={(value) => setRole(value as StaffRole)}
          />
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

  const staffQuery = useQuery(getApiV1StaffOptions())
  const invitationsQuery = useQuery(getApiV1StaffInvitationsOptions())
  const staff = staffQuery.data
  const allInvitations = invitationsQuery.data
  // Login status comes from the invitations, so the list waits for both: without them everyone
  // would look not invited and offer "Send invitation".
  const isLoading = staffQuery.isLoading || invitationsQuery.isLoading
  const isError = staffQuery.isError
  const invitationsFailed = invitationsQuery.isError && !allInvitations
  const refetch = () => void staffQuery.refetch()

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

      {invitationsFailed && (
        <div className="bg-amber-50 border border-amber-200 rounded-xl p-4 flex items-center justify-between gap-3">
          <p className="text-amber-900 text-sm font-medium">
            Kunne ikke hente invitationer, så vi kan ikke vise hvem der er inviteret.
          </p>
          <button
            onClick={() => void invitationsQuery.refetch()}
            className="h-11 px-3 text-sm font-semibold bg-amber-100 text-amber-900 rounded-lg shrink-0"
          >
            Prøv igen
          </button>
        </div>
      )}

      <PersonList
        items={staff}
        isLoading={isLoading}
        testId="staff-list"
        noun={{ one: 'medarbejder', other: 'medarbejdere' }}
        searchText={(s) => `${s.name} ${s.email ?? ''} ${s.phone ?? ''}`}
        searchPlaceholder="Søg efter navn eller e-mail"
        filterLabel="Filtrer efter rolle"
        filters={[
          { value: '', label: 'Alle' },
          ...ROLE_OPTIONS.map((r) => ({
            value: r.value,
            label: ROLE_PLURAL[r.value],
            match: (s: StaffDto) => s.role === r.value,
          })),
        ]}
        sorts={[
          { value: 'name', label: 'Navn A–Å', compare: byName },
          {
            value: 'role',
            label: 'Rolle',
            compare: (a, b) =>
              roleLabel(a.role ?? 'Teacher').localeCompare(roleLabel(b.role ?? 'Teacher'), 'da') ||
              byName(a, b),
          },
          {
            value: 'inactive',
            label: 'Ikke logget ind først',
            compare: (a, b) =>
              Number(!!a.keycloakSubject) - Number(!!b.keycloakSubject) || byName(a, b),
          },
        ]}
        empty={
          <>
            <p className="font-semibold text-gray-700">Ingen medarbejdere oprettet endnu</p>
            <p className="text-sm mt-1">Opret din første medarbejder for at komme i gang</p>
          </>
        }
        toPerson={(s) => {
          const invite = getLatestInvite(s.id ?? '')
          const isSelf =
            s.keycloakSubject != null && s.keycloakSubject === currentUserKeycloakSubject
          const status =
            invitationsFailed && !s.keycloakSubject
              ? { text: 'Ukendt, invitationer kunne ikke hentes', badge: undefined }
              : loginStatus(s, invite)
          const actions: RowAction[] = []
          if (!s.keycloakSubject && !isSelf && !invitationsFailed) {
            actions.push({
              label: invite ? 'Send invitation igen' : 'Send invitation',
              onClick: () => setInvitingStaff(s),
            })
          }
          actions.push(
            { label: 'Rediger', onClick: () => setEditingStaff(s) },
            { label: 'Se skema', onClick: () => navigate(`/medarbejdere/${s.id}/skema`) },
            {
              label: 'Slet',
              variant: 'danger',
              disabled: isSelf,
              disabledReason: isSelf ? 'Du kan ikke slette dig selv.' : undefined,
              onClick: () => {
                if (confirm(`Slet "${s.name}"?`))
                  deleteMutation.mutate({ path: { id: s.id ?? '' } })
              },
            }
          )
          return {
            key: s.id ?? s.name,
            name: s.name,
            subtitle: [roleLabel(s.role ?? 'Teacher'), s.isAdmin ? 'Administrator' : null]
              .filter(Boolean)
              .join(' · '),
            badge: status.badge,
            details: [
              { label: 'E-mail', value: s.email ?? '—' },
              { label: 'Telefon', value: s.phone ?? '—' },
              { label: 'Login', value: status.text },
            ],
            actions,
          }
        }}
      />

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
