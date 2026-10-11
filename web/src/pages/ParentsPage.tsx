import { useState } from 'react'
import { Modal } from '../components/Modal'
import ModuleInactiveNotice from '../components/ModuleInactiveNotice'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1ParentsOptions,
  getApiV1ParentsQueryKey,
  getApiV1ParentsByIdOptions,
  postApiV1ParentsInviteMutation,
  deleteApiV1ParentsByIdMutation,
  postApiV1ParentInvitationsByParentIdResendMutation,
  patchApiV1ParentsByIdContactMutation,
  getApiV1StudentsOptions,
} from '../api/generated/@tanstack/react-query.gen'
import type { ParentSummaryDto, StudentDto } from '../api/client'
import { usePageTitle } from '../hooks/usePageTitle'
import { useSubscription } from '../hooks/useSubscription'
import {
  isValidPhone,
  isValidPostalCode,
  normalizePhone,
  PHONE_ERROR_MESSAGE,
  POSTAL_CODE_ERROR_MESSAGE,
} from '../lib/validation'

interface InviteModalProps {
  students: StudentDto[]
  onClose: () => void
}

function InviteModal({ students, onClose }: InviteModalProps) {
  const qc = useQueryClient()
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [selectedStudents, setSelectedStudents] = useState<string[]>([])
  const [sent, setSent] = useState(false)

  const inviteMutation = useMutation({
    ...postApiV1ParentsInviteMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: getApiV1ParentsQueryKey() })
      setSent(true)
    },
  })

  function toggleStudent(id: string) {
    setSelectedStudents((prev) =>
      prev.includes(id) ? prev.filter((s) => s !== id) : [...prev, id]
    )
  }

  function handleSend() {
    if (!name.trim() || !email.trim() || selectedStudents.length === 0 || inviteMutation.isPending)
      return
    inviteMutation.mutate({ body: { name, email, studentIds: selectedStudents } })
  }

  return (
    <Modal isOpen onClose={onClose} title="Inviter forældrene">
      {sent ? (
        <div className="px-6 py-8 text-center space-y-3">
          <div className="mx-auto h-12 w-12 rounded-full bg-green-100 flex items-center justify-center">
            <svg
              width="20"
              height="20"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.5"
              className="text-green-600"
            >
              <polyline points="20 6 9 17 4 12" />
            </svg>
          </div>
          <p className="text-sm text-gray-700 font-medium">Invitation sendt!</p>
          <p className="text-xs text-gray-500">
            Forældrene modtager en e-mail med link til at oprette adgang.
          </p>
          <button
            type="button"
            onClick={onClose}
            className="mt-4 px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 transition-colors"
          >
            Luk
          </button>
        </div>
      ) : (
        <>
          <div className="px-6 py-5 space-y-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Navn *</label>
              <input
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Forælder navn"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">E-mail *</label>
              <input
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                type="email"
                placeholder="forældrene@eksempel.dk"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Tilknyt elever *
              </label>
              <div className="border border-gray-200 rounded-lg divide-y divide-gray-100 max-h-48 overflow-y-auto">
                {students.length === 0 && (
                  <p className="px-3 py-3 text-sm text-gray-400">Ingen elever oprettet endnu</p>
                )}
                {students.map((s) => (
                  <label
                    key={s.id}
                    className="flex items-center gap-3 px-3 py-2.5 hover:bg-gray-50 cursor-pointer"
                  >
                    <input
                      type="checkbox"
                      checked={selectedStudents.includes(s.id ?? '')}
                      onChange={() => toggleStudent(s.id ?? '')}
                      className="rounded border-gray-300 text-brand-600 focus:ring-brand-500"
                    />
                    <span className="text-sm text-gray-900">{s.name}</span>
                    <span className="ml-auto text-xs text-gray-400">{s.className}</span>
                  </label>
                ))}
              </div>
            </div>
            {inviteMutation.isError && (
              <p className="text-sm text-red-600">Der opstod en fejl. Prøv igen.</p>
            )}
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
              onClick={handleSend}
              disabled={
                !name.trim() ||
                !email.trim() ||
                selectedStudents.length === 0 ||
                inviteMutation.isPending
              }
              className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {inviteMutation.isPending ? 'Sender...' : 'Send invitation'}
            </button>
          </div>
        </>
      )}
    </Modal>
  )
}

interface EditContactModalProps {
  parent: ParentSummaryDto
  onClose: () => void
}

function EditContactModal({ parent, onClose }: EditContactModalProps) {
  const qc = useQueryClient()
  const [name, setName] = useState(parent.name ?? '')
  const [phone, setPhone] = useState(parent.phone ?? '')
  const [address, setAddress] = useState(parent.address ?? '')
  const [postalCode, setPostalCode] = useState(parent.postalCode ?? '')
  const [city, setCity] = useState(parent.city ?? '')

  const phoneValid = isValidPhone(phone)
  const postalCodeValid = isValidPostalCode(postalCode)

  const updateMutation = useMutation({
    ...patchApiV1ParentsByIdContactMutation(),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: getApiV1ParentsQueryKey() })
      onClose()
    },
  })

  function handleSave() {
    if (!name.trim() || !phoneValid || !postalCodeValid) return
    updateMutation.mutate({
      path: { id: parent.id! },
      body: {
        name: name.trim(),
        phone: phone.trim() ? normalizePhone(phone.trim()) : null,
        address: address.trim() || null,
        postalCode: postalCode.trim() || null,
        city: city.trim() || null,
      },
    })
  }

  return (
    <Modal isOpen onClose={onClose} title="Rediger kontaktoplysninger">
      <div className="px-6 py-5 space-y-4">
        <div>
          <label
            htmlFor="parent-edit-name"
            className="block text-sm font-medium text-gray-700 mb-1"
          >
            Navn *
          </label>
          <input
            id="parent-edit-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>
        <div>
          <label
            htmlFor="parent-edit-phone"
            className="block text-sm font-medium text-gray-700 mb-1"
          >
            Telefon
          </label>
          <input
            id="parent-edit-phone"
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
            type="tel"
            placeholder="+45 12 34 56 78"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
          {phone.trim() !== '' && !phoneValid && (
            <p className="text-xs text-red-600 mt-1">{PHONE_ERROR_MESSAGE}</p>
          )}
        </div>
        <div>
          <label
            htmlFor="parent-edit-address"
            className="block text-sm font-medium text-gray-700 mb-1"
          >
            Adresse
          </label>
          <input
            id="parent-edit-address"
            value={address}
            onChange={(e) => setAddress(e.target.value)}
            placeholder="Vejnavn og nummer"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>
        <div className="flex gap-3">
          <div className="w-24">
            <label
              htmlFor="parent-edit-postal-code"
              className="block text-sm font-medium text-gray-700 mb-1"
            >
              Postnr.
            </label>
            <input
              id="parent-edit-postal-code"
              value={postalCode}
              onChange={(e) => setPostalCode(e.target.value)}
              placeholder="0000"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
            />
            {postalCode.trim() !== '' && !postalCodeValid && (
              <p className="text-xs text-red-600 mt-1">{POSTAL_CODE_ERROR_MESSAGE}</p>
            )}
          </div>
          <div className="flex-1">
            <label
              htmlFor="parent-edit-city"
              className="block text-sm font-medium text-gray-700 mb-1"
            >
              By
            </label>
            <input
              id="parent-edit-city"
              value={city}
              onChange={(e) => setCity(e.target.value)}
              placeholder="By"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
            />
          </div>
        </div>
        {updateMutation.isError && (
          <p className="text-sm text-red-600">Der opstod en fejl. Prøv igen.</p>
        )}
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
          disabled={!name.trim() || !phoneValid || !postalCodeValid || updateMutation.isPending}
          className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          {updateMutation.isPending ? 'Gemmer...' : 'Gem'}
        </button>
      </div>
    </Modal>
  )
}

interface DetailsModalProps {
  parentId: string
  parentName: string
  onOpenCoParent: (id: string, name: string) => void
  onClose: () => void
}

function DetailsModal({ parentId, parentName, onOpenCoParent, onClose }: DetailsModalProps) {
  const {
    data: detail,
    isLoading,
    isError,
  } = useQuery(getApiV1ParentsByIdOptions({ path: { id: parentId } }))

  return (
    <Modal isOpen onClose={onClose} title={`Detaljer — ${parentName}`}>
      <div className="px-6 py-5 space-y-4">
        {isLoading && <p className="text-sm text-gray-400">Indlæser...</p>}
        {isError && <p className="text-sm text-red-600">Kunne ikke hente detaljer.</p>}
        {detail && (
          <>
            <div>
              <span className="block text-xs font-semibold text-gray-500 uppercase tracking-wide mb-1">
                Konto
              </span>
              {detail.hasAccount ? (
                <span className="px-2 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-700">
                  Aktiv
                </span>
              ) : (
                <span className="px-2 py-0.5 rounded-full text-xs font-medium bg-amber-100 text-amber-700">
                  Afventer
                </span>
              )}
            </div>
            <div>
              <span className="block text-xs font-semibold text-gray-500 uppercase tracking-wide mb-1">
                E-mail
              </span>
              <a
                href={`mailto:${detail.email}`}
                className="text-sm text-brand-600 hover:underline break-all"
              >
                {detail.email}
              </a>
            </div>
            {detail.coParent && (
              <div>
                <span className="block text-xs font-semibold text-gray-500 uppercase tracking-wide mb-1">
                  Meforælder
                </span>
                <button
                  type="button"
                  onClick={() => onOpenCoParent(detail.coParent!.id!, detail.coParent!.name!)}
                  className="text-sm text-brand-600 hover:underline"
                >
                  {detail.coParent.name}
                </button>
              </div>
            )}
          </>
        )}
      </div>
      <div className="px-6 py-4 border-t border-gray-100 flex justify-end">
        <button
          type="button"
          onClick={onClose}
          className="px-4 py-2 text-sm text-gray-600 hover:text-gray-900"
        >
          Luk
        </button>
      </div>
    </Modal>
  )
}

export default function ParentsPage() {
  usePageTitle('Forældre')
  const qc = useQueryClient()
  const [showInvite, setShowInvite] = useState(false)
  const [editingParent, setEditingParent] = useState<ParentSummaryDto | null>(null)
  const [detailsTarget, setDetailsTarget] = useState<{ id: string; name: string } | null>(null)
  const { hasParentModule, isSuccess: modulesLoaded } = useSubscription()

  const { data: parents, isLoading, isError, refetch } = useQuery(getApiV1ParentsOptions())
  const { data: students } = useQuery(getApiV1StudentsOptions())

  const deleteMutation = useMutation({
    mutationFn: deleteApiV1ParentsByIdMutation().mutationFn,
    onSuccess: () => qc.invalidateQueries({ queryKey: getApiV1ParentsQueryKey() }),
  })

  const resendMutation = useMutation({
    mutationFn: postApiV1ParentInvitationsByParentIdResendMutation().mutationFn,
  })

  return (
    <div className="p-6 lg:p-8 max-w-6xl mx-auto space-y-6">
      <div className="flex items-center justify-between flex-wrap gap-4">
        <div>
          <h1 className="font-display text-2xl font-semibold text-gray-900">Forældre</h1>
          <p className="mt-1 text-sm text-gray-500">
            Inviter og administrer forældre med adgang til klasseskema
          </p>
        </div>
        <button
          onClick={() => hasParentModule && setShowInvite(true)}
          disabled={!hasParentModule}
          aria-label="Inviter forældrene"
          title={!hasParentModule ? 'Kræver forældremodulet — aktivér under Abonnement' : undefined}
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
          <span className="hidden sm:inline">Inviter forælder</span>
        </button>
      </div>

      {modulesLoaded && !hasParentModule && (
        <ModuleInactiveNotice
          testId="parent-module-inactive"
          text="Forældremodulet er ikke aktivt, så du kan ikke invitere eller ændre forældre. Du kan stadig se og slette dem."
        />
      )}

      {isError && (
        <div className="bg-red-50 border border-red-200 rounded-xl p-5 flex items-center justify-between">
          <p className="text-red-700 text-sm font-medium">Kunne ikke hente forældre</p>
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
                <th className="px-5 py-3 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide hidden md:table-cell">
                  Telefon
                </th>
                <th className="px-5 py-3 text-left text-xs font-semibold text-gray-500 uppercase tracking-wide">
                  Elever
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
                    <td className="px-5 py-3 hidden md:table-cell">
                      <div className="h-4 w-24 bg-gray-100 rounded" />
                    </td>
                    <td className="px-5 py-3">
                      <div className="h-4 w-20 bg-gray-100 rounded" />
                    </td>
                    <td className="px-5 py-3" />
                  </tr>
                ))}
              {!isLoading && (parents ?? []).length === 0 && (
                <tr>
                  <td colSpan={4} className="px-5 py-12 text-center">
                    <p className="text-gray-400 font-medium">Ingen forældre inviteret endnu</p>
                    <p className="text-gray-400 text-xs mt-1">
                      Inviter den første forældrene for at give adgang til klasseskemaet
                    </p>
                  </td>
                </tr>
              )}
              {(parents ?? []).map((p: ParentSummaryDto) => (
                <tr key={p.id} className="hover:bg-gray-50 transition-colors">
                  <td className="px-5 py-3 font-medium text-gray-900">{p.name}</td>
                  <td className="px-5 py-3 text-gray-500 hidden md:table-cell">{p.phone ?? '—'}</td>
                  <td className="px-5 py-3">
                    <div className="flex flex-wrap gap-1">
                      {(p.students ?? []).map((s) => (
                        <span
                          key={s.id}
                          className="px-2 py-0.5 rounded-full text-xs font-medium bg-blue-100 text-blue-700"
                        >
                          {s.name}
                        </span>
                      ))}
                      {(p.students ?? []).length === 0 && (
                        <span className="text-xs text-gray-400">—</span>
                      )}
                    </div>
                  </td>
                  <td className="px-5 py-3 text-right">
                    <div className="flex items-center justify-end gap-2">
                      <button
                        onClick={() => setDetailsTarget({ id: p.id!, name: p.name })}
                        className="p-1.5 text-gray-400 hover:text-brand-600 rounded-md hover:bg-brand-50 transition-colors"
                        title="Detaljer"
                      >
                        <svg
                          width="15"
                          height="15"
                          viewBox="0 0 24 24"
                          fill="none"
                          stroke="currentColor"
                          strokeWidth="2"
                        >
                          <circle cx="12" cy="12" r="10" />
                          <line x1="12" y1="16" x2="12" y2="12" />
                          <line x1="12" y1="8" x2="12.01" y2="8" />
                        </svg>
                      </button>
                      {!p.hasAccount && hasParentModule && (
                        <button
                          onClick={() => resendMutation.mutate({ path: { parentId: p.id! } })}
                          disabled={resendMutation.isPending}
                          className="p-1.5 text-gray-400 hover:text-brand-600 rounded-md hover:bg-brand-50 transition-colors"
                          title="Gensend invitation"
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
                      {hasParentModule && (
                        <button
                          onClick={() => setEditingParent(p)}
                          className="p-1.5 text-gray-400 hover:text-brand-600 rounded-md hover:bg-brand-50 transition-colors"
                          title="Rediger kontaktoplysninger"
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
                          if (confirm(`Slet "${p.name}"? Adgangen fjernes.`))
                            deleteMutation.mutate({ path: { id: p.id! } })
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

      {showInvite && students && (
        <InviteModal students={students} onClose={() => setShowInvite(false)} />
      )}
      {editingParent && (
        <EditContactModal parent={editingParent} onClose={() => setEditingParent(null)} />
      )}
      {detailsTarget && (
        <DetailsModal
          parentId={detailsTarget.id}
          parentName={detailsTarget.name}
          onOpenCoParent={(id, name) => setDetailsTarget({ id, name })}
          onClose={() => setDetailsTarget(null)}
        />
      )}
    </div>
  )
}
