import { useState } from 'react'
import { Modal } from '../components/Modal'
import { ResetTwoFactorDialog } from '../components/ResetTwoFactorDialog'
import ModuleInactiveNotice from '../components/ModuleInactiveNotice'
import { MailIcon } from '../components/ui/icons'
import { PersonList } from '../components/ui/list'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1BoardMembersOptions,
  getApiV1BoardMembersQueryKey,
  deleteApiV1BoardMembersByIdMutation,
  patchApiV1BoardMembersByIdTeacherDataAccessMutation,
  postApiV1BoardMembersInviteMutation,
  postApiV1BoardMembersByIdResetTwoFactorMutation,
} from '../api/generated/@tanstack/react-query.gen'
import { usePageTitle } from '../hooks/usePageTitle'
import { useSubscription } from '../hooks/useSubscription'

export default function BoardMembersPage() {
  usePageTitle('Bestyrelse')
  const qc = useQueryClient()
  const { hasBoardModule, isSuccess: modulesLoaded } = useSubscription()
  const [showInviteModal, setShowInviteModal] = useState(false)
  const [inviteName, setInviteName] = useState('')
  const [inviteEmail, setInviteEmail] = useState('')
  const [inviteError, setInviteError] = useState<string | null>(null)
  const [inviting, setInviting] = useState(false)
  const [rowError, setRowError] = useState<string | null>(null)
  const [resettingMember, setResettingMember] = useState<{ id: string; name: string } | null>(null)

  const { data: members, isLoading, isError, error } = useQuery(getApiV1BoardMembersOptions())

  const toggleMutation = useMutation({
    ...patchApiV1BoardMembersByIdTeacherDataAccessMutation(),
    onMutate: () => setRowError(null),
    onSuccess: () => void qc.invalidateQueries({ queryKey: getApiV1BoardMembersQueryKey() }),
    onError: () => setRowError('Læreradgangen kunne ikke ændres. Prøv igen.'),
  })

  const deleteMutation = useMutation({
    ...deleteApiV1BoardMembersByIdMutation(),
    onMutate: () => setRowError(null),
    onSuccess: () => void qc.invalidateQueries({ queryKey: getApiV1BoardMembersQueryKey() }),
    onError: () => setRowError('Bestyrelsesmedlemmet kunne ikke fjernes. Prøv igen.'),
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

  function openInvite() {
    setInviteError(null)
    setShowInviteModal(true)
  }

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

  const inviteButton = (
    <button
      type="button"
      onClick={openInvite}
      disabled={!hasBoardModule}
      title={!hasBoardModule ? 'Kræver bestyrelsesmodulet — aktivér under Abonnement' : undefined}
      data-testid="board-invite-button"
      className="flex items-center gap-2 px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
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
      Inviter bestyrelsesmedlem
    </button>
  )

  return (
    <div data-testid="board-members-page" className="p-6 lg:p-8 max-w-4xl mx-auto space-y-6">
      <div className="flex items-start justify-between flex-wrap gap-4">
        <div className="max-w-xl">
          <h1 className="font-display text-2xl font-semibold text-gray-900">Bestyrelse</h1>
          <p className="mt-1 text-sm text-gray-500">
            Bestyrelsesmedlemmer får adgang til bestyrelsens filer, oversigt og Stå mål med. Slå{' '}
            <em>Læreradgang</em> til, hvis de også skal kunne se skemaer og medarbejdere.
          </p>
        </div>
        {inviteButton}
      </div>

      {modulesLoaded && !hasBoardModule && (
        <ModuleInactiveNotice
          testId="board-module-inactive"
          text="Bestyrelsesmodulet er ikke aktivt, så du kan ikke invitere eller ændre bestyrelsesmedlemmer. Du kan stadig fjerne dem."
        />
      )}

      {rowError && (
        <div
          role="alert"
          data-testid="board-members-error"
          className="rounded-xl border border-red-200 bg-red-50 px-5 py-4 text-sm text-red-700"
        >
          {rowError}
        </div>
      )}

      {isError && (
        <div className="rounded-xl border border-red-200 bg-red-50 px-5 py-4 text-sm text-red-700">
          {error instanceof Error ? error.message : 'Kunne ikke hente bestyrelsesmedlemmer'}
        </div>
      )}

      <PersonList
        items={members}
        isLoading={isLoading}
        testId="board-member-list"
        noun={{ one: 'bestyrelsesmedlem', other: 'bestyrelsesmedlemmer' }}
        empty={
          <>
            <p className="font-semibold text-gray-700">Ingen bestyrelsesmedlemmer endnu</p>
            <p className="text-sm mt-1">Inviter det første bestyrelsesmedlem for at give adgang</p>
          </>
        }
        toPerson={(member) => ({
          key: member.id ?? member.email,
          name: member.name,
          // Account status is in the badge.
          subtitle: member.canAccessTeacherData ? 'Med læreradgang' : undefined,
          testId: 'board-member-row',
          badge: member.hasAccount ? undefined : { label: 'Afventer', tone: 'amber' },
          details: [
            {
              icon: MailIcon,
              label: 'E-mail',
              value: member.email,
              href: `mailto:${member.email}`,
            },
          ],
          content: (
            <label className="mt-1 flex items-center gap-2 min-h-11 text-sm text-gray-800 cursor-pointer select-none">
              <input
                type="checkbox"
                data-testid="board-member-teacher-access"
                checked={member.canAccessTeacherData}
                disabled={!hasBoardModule || toggleMutation.isPending}
                onChange={(e) =>
                  toggleMutation.mutate({
                    path: { id: member.id! },
                    body: { canAccessTeacherData: e.target.checked },
                  })
                }
                className="w-5 h-5 rounded-sm border-gray-300 text-brand-600 focus:ring-brand-500 disabled:opacity-50"
              />
              Læreradgang: kan se skemaer og medarbejdere
            </label>
          ),
          actions: [
            ...(member.hasAccount && hasBoardModule
              ? [
                  {
                    label: 'Nulstil tofaktor',
                    testId: 'board-member-reset-tfa',
                    onClick: () => setResettingMember({ id: member.id!, name: member.name }),
                  },
                ]
              : []),
            {
              label: 'Fjern fra bestyrelsen',
              variant: 'danger',
              testId: 'board-member-remove',
              disabled: deleteMutation.isPending,
              onClick: () => {
                if (confirm(`Fjern "${member.name}" fra bestyrelsen? Adgangen fjernes.`))
                  deleteMutation.mutate({ path: { id: member.id! } })
              },
            },
          ],
        })}
      />

      <ResetTwoFactorDialog
        person={resettingMember}
        reset={postApiV1BoardMembersByIdResetTwoFactorMutation().mutationFn}
        onClose={() => setResettingMember(null)}
      />

      <Modal
        isOpen={showInviteModal}
        onClose={() => setShowInviteModal(false)}
        title="Inviter bestyrelsesmedlem"
      >
        <div className="px-6 py-5 space-y-4">
          <div>
            <label
              htmlFor="board-invite-name"
              className="block text-sm font-medium text-gray-700 mb-1"
            >
              Navn *
            </label>
            <input
              id="board-invite-name"
              data-testid="board-invite-name"
              value={inviteName}
              onChange={(e) => setInviteName(e.target.value)}
              placeholder="Fornavn Efternavn"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-hidden focus:ring-2 focus:ring-brand-500 focus:border-transparent"
            />
          </div>
          <div>
            <label
              htmlFor="board-invite-email"
              className="block text-sm font-medium text-gray-700 mb-1"
            >
              E-mail *
            </label>
            <input
              id="board-invite-email"
              data-testid="board-invite-email"
              type="email"
              value={inviteEmail}
              onChange={(e) => setInviteEmail(e.target.value)}
              placeholder="bestyrelse@skolen.dk"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-hidden focus:ring-2 focus:ring-brand-500 focus:border-transparent"
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
              data-testid="board-invite-submit"
              onClick={handleInvite}
              disabled={!inviteName.trim() || !inviteEmail.trim() || inviting}
              className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {inviting ? 'Sender...' : 'Send invitation'}
            </button>
          </div>
        </div>
      </Modal>
    </div>
  )
}
