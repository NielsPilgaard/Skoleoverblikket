import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Modal } from '../components/Modal'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1BoardMembersOptions,
  getApiV1BoardMembersQueryKey,
  deleteApiV1BoardMembersByIdMutation,
  patchApiV1BoardMembersByIdTeacherDataAccessMutation,
  postApiV1BoardMembersInviteMutation,
} from '../api/generated/@tanstack/react-query.gen'
import { usePageTitle } from '../hooks/usePageTitle'
import { useSubscription } from '../hooks/useSubscription'

export default function BoardMembersPage() {
  usePageTitle('Bestyrelse')
  const qc = useQueryClient()
  const { hasBoardModule, isLoading: modulesLoading } = useSubscription()
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

      {!modulesLoading && !hasBoardModule && (
        <div
          data-testid="board-module-inactive"
          className="rounded-xl border border-amber-300 bg-amber-50 px-5 py-4 text-sm text-amber-800"
        >
          Bestyrelsesmodulet er ikke aktivt, så du kan ikke invitere nye bestyrelsesmedlemmer.{' '}
          <Link to="/abonnement" className="font-medium underline hover:text-amber-900">
            Aktivér det under Abonnement
          </Link>
          .
        </div>
      )}

      <div className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100">
        {isLoading ? (
          <div className="px-5 py-4 animate-pulse space-y-3">
            {[...Array(3)].map((_, i) => (
              <div key={i} className="h-10 bg-gray-100 rounded" />
            ))}
          </div>
        ) : isError ? (
          <div className="px-5 py-8 text-center text-sm text-red-600">
            {error instanceof Error ? error.message : 'Kunne ikke hente bestyrelsesmedlemmer'}
          </div>
        ) : !members || members.length === 0 ? (
          <div className="px-5 py-12 flex flex-col items-center gap-4 text-center">
            <p className="text-gray-400 font-medium">Ingen bestyrelsesmedlemmer endnu</p>
            {inviteButton}
          </div>
        ) : (
          members.map((member) => (
            <div
              key={member.id}
              data-testid="board-member-row"
              className="px-5 py-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:gap-4"
            >
              <div className="flex-1 min-w-0">
                <p className="text-sm font-medium text-gray-900 truncate">{member.name}</p>
                <p className="text-xs text-gray-500 truncate">{member.email}</p>
              </div>
              <div className="flex items-center gap-4">
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
                    data-testid="board-member-teacher-access"
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
                  type="button"
                  data-testid="board-member-remove"
                  onClick={() => {
                    if (confirm(`Fjern "${member.name}" fra bestyrelsen? Adgangen fjernes.`))
                      deleteMutation.mutate({ path: { id: member.id! } })
                  }}
                  disabled={deleteMutation.isPending}
                  className="shrink-0 ml-auto sm:ml-0 p-1.5 text-gray-400 hover:text-red-500 rounded-md hover:bg-red-50 transition-colors disabled:opacity-50"
                  title="Fjern bestyrelsesmedlem"
                  aria-label="Fjern bestyrelsesmedlem"
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
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
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
