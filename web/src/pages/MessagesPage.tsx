import { useCallback, useEffect, useRef, useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1MessagesInbox,
  getApiV1MessagesRecipients,
  getApiV1MessagesSent,
  getApiV1MessagesByIdThread,
  postApiV1Messages,
  postApiV1MessagesByIdRead,
  postApiV1MessagesGroupPreview,
  postApiV1MessagesGroup,
} from '../api/generated/sdk.gen'
import {
  getApiV1ClassesOptions,
  getApiV1ParentsMeOptions,
} from '../api/generated/@tanstack/react-query.gen'
import type {
  BroadcastAudience,
  StaffRole,
  ClassesControllerClassDto,
  MessagesControllerThreadMessageDto,
} from '../api/generated/types.gen'
import { Modal } from '../components/Modal'
import { useAuth } from '../auth/useAuth'
import { usePageTitle } from '../hooks/usePageTitle'

type RecipientType = 'Parent' | 'Staff'

interface InboxMessageDto {
  id: string
  senderId: string
  senderType: RecipientType
  senderName: string
  subject: string
  body: string
  sentAt: string
  readAt?: string
  inReplyToId?: string
  isGroup?: boolean
}

interface SentMessageDto {
  id: string
  recipientId: string
  recipientType: RecipientType
  recipientName: string
  subject: string
  body: string
  sentAt: string
  readAt?: string
  isGroup?: boolean
  audienceLabel?: string
  groupRecipientCount?: number
  inReplyToId?: string
}

type ThreadMessageDto = MessagesControllerThreadMessageDto

interface RecipientDto {
  id: string
  name: string
  type: RecipientType
  avatarUrl?: string
}

function formatRelativeTime(iso: string): string {
  const now = new Date()
  const date = new Date(iso)
  const diffMs = now.getTime() - date.getTime()
  const diffMin = Math.floor(diffMs / 60000)
  const diffH = Math.floor(diffMin / 60)
  const diffD = Math.floor(diffH / 24)

  if (diffMin < 1) {
    return 'Lige nu'
  }
  if (diffMin < 60) {
    return `${diffMin} min siden`
  }
  if (diffH < 24) {
    return `${diffH} t siden`
  }
  if (diffD === 1) {
    return 'I går'
  }
  if (diffD < 7) {
    return `${diffD} dage siden`
  }
  const dd = date.getDate().toString().padStart(2, '0')
  const mo = (date.getMonth() + 1).toString().padStart(2, '0')
  return `${dd}/${mo}`
}

function getInitials(name: string): string {
  return name
    .split(' ')
    .map((n) => n.charAt(0))
    .slice(0, 2)
    .join('')
    .toUpperCase()
}

// Messages are only ever between Parent and Staff — the generated RecipientType also includes
// Board (board members), which never participates in this messaging flow.
function toMessageRecipientType(type: string | undefined): RecipientType {
  return type === 'Staff' ? 'Staff' : 'Parent'
}

function truncate(text: string, max: number): string {
  if (text.length <= max) {
    return text
  }
  return `${text.slice(0, max)}…`
}

interface ThreadLinkable {
  id: string
  inReplyToId?: string
  sentAt: string
  isGroup?: boolean
  readAt?: string
}

// InReplyToId only ever points at the previous message, and a reply to my
// inbox message lands in my *sent* list (and vice versa) — so the chain must
// be walked across inbox+sent combined, not per-tab, or it breaks as soon as
// a thread crosses tabs. Returns id -> root id for every message.
function buildThreadRoots(allMessages: ThreadLinkable[]): Map<string, string> {
  const byId = new Map(allMessages.map((m) => [m.id, m]))
  const roots = new Map<string, string>()

  function rootIdOf(msg: ThreadLinkable): string {
    const cached = roots.get(msg.id)
    if (cached) return cached

    let current = msg
    const visited = new Set<string>()
    while (current.inReplyToId && byId.has(current.inReplyToId) && !visited.has(current.id)) {
      visited.add(current.id)
      current = byId.get(current.inReplyToId)!
    }
    roots.set(msg.id, current.id)
    return current.id
  }

  for (const msg of allMessages) rootIdOf(msg)
  return roots
}

// Collapses a flat message list into one row per thread (latest message wins),
// so replies don't show up as separate list entries. Unread state is OR'd across every
// message in the thread, not just the retained latest one, so an unread earlier reply
// still marks the collapsed row unread even if the newest message was already read.
function groupByThread<T extends ThreadLinkable>(
  messages: T[],
  threadRoots: Map<string, string>
): (T & { hasUnread: boolean })[] {
  const latestByRoot = new Map<string, T>()
  const unreadByRoot = new Map<string, boolean>()
  for (const msg of messages) {
    const rootId = msg.isGroup ? msg.id : (threadRoots.get(msg.id) ?? msg.id)
    const existing = latestByRoot.get(rootId)
    if (!existing || new Date(msg.sentAt).getTime() > new Date(existing.sentAt).getTime()) {
      latestByRoot.set(rootId, msg)
    }
    if (!msg.readAt) {
      unreadByRoot.set(rootId, true)
    }
  }

  return [...latestByRoot.entries()]
    .map(([rootId, msg]) => ({ ...msg, hasUnread: unreadByRoot.get(rootId) ?? false }))
    .sort((a, b) => new Date(b.sentAt).getTime() - new Date(a.sentAt).getTime())
}

// ── GroupComposeModal ────────────────────────────────────────────────────────

type GroupStep = 'audience' | 'compose' | 'confirm' | 'done'

interface GroupComposeModalProps {
  isAdmin: boolean
  isParent: boolean
  parentClasses: { classId: string; className: string }[]
  allClasses: ClassesControllerClassDto[]
  onClose: () => void
  onSent: () => void
}

function GroupComposeModal({
  isAdmin,
  isParent,
  parentClasses,
  allClasses,
  onClose,
  onSent,
}: GroupComposeModalProps) {
  const [step, setStep] = useState<GroupStep>('audience')
  const [audience, setAudience] = useState<BroadcastAudience>('ClassParents')
  const [classId, setClassId] = useState<string>(
    isParent ? (parentClasses[0]?.classId ?? '') : (allClasses[0]?.id ?? '')
  )
  const [staffRole, setStaffRole] = useState<StaffRole>('Teacher')
  const [subject, setSubject] = useState('')
  const [body, setBody] = useState('')
  const [previewCount, setPreviewCount] = useState<number | null>(null)
  const [previewLoading, setPreviewLoading] = useState(false)
  const previewDebounceRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const classOptions = isParent
    ? parentClasses.map((c) => ({ id: c.classId, name: c.className }))
    : allClasses.map((c) => ({ id: c.id ?? '', name: c.name ?? '' }))

  const sendMutation = useMutation({
    mutationFn: async () => {
      const sendBody =
        audience === 'ClassParents'
          ? { audience, classId, subject, body }
          : audience === 'StaffByRole'
            ? { audience, staffRole, subject, body }
            : { audience, subject, body }
      await postApiV1MessagesGroup({
        body: sendBody,
        throwOnError: true,
      })
    },
    onSuccess: () => {
      setStep('done')
    },
  })

  function triggerPreview(aud: BroadcastAudience, cid: string, role: StaffRole) {
    if (previewDebounceRef.current) clearTimeout(previewDebounceRef.current)
    setPreviewLoading(true)
    setPreviewCount(null)
    previewDebounceRef.current = setTimeout(async () => {
      try {
        const res = await postApiV1MessagesGroupPreview({
          body:
            aud === 'ClassParents'
              ? { audience: aud, classId: cid }
              : aud === 'StaffByRole'
                ? { audience: aud, staffRole: role }
                : { audience: aud },
          throwOnError: false,
        })
        setPreviewCount(res.data?.recipientCount ?? 0)
      } finally {
        setPreviewLoading(false)
      }
    }, 400)
  }

  function handleAudienceChange(aud: BroadcastAudience) {
    setAudience(aud)
    triggerPreview(aud, classId, staffRole)
  }

  function handleClassChange(cid: string) {
    setClassId(cid)
    triggerPreview(audience, cid, staffRole)
  }

  function handleRoleChange(role: StaffRole) {
    setStaffRole(role)
    triggerPreview(audience, classId, role)
  }

  useEffect(() => {
    triggerPreview(audience, classId, staffRole)
    return () => {
      if (previewDebounceRef.current) clearTimeout(previewDebounceRef.current)
    }
    // Only on mount
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const audienceLabel = (() => {
    switch (audience) {
      case 'AllParents':
        return 'Alle forældre'
      case 'SfoParents':
        return 'SFO-forældre'
      case 'AllStaff':
        return 'Alt personale'
      case 'StaffByRole':
        return staffRole === 'Teacher' ? 'Lærere' : staffRole === 'Aide' ? 'Pædagoger' : 'Vikarer'
      case 'ClassParents':
        return `Forældre i ${classOptions.find((c) => c.id === classId)?.name ?? 'klassen'}`
    }
  })()

  return (
    <Modal
      isOpen
      onClose={onClose}
      size="lg"
      contentClassName="bg-white rounded-xl p-6 shadow-xl w-full max-w-lg"
    >
      <div className="flex items-center justify-between mb-5">
        <h2 className="text-base font-semibold text-gray-900">Gruppebesked</h2>
        <button
          type="button"
          aria-label="Luk"
          onClick={onClose}
          className="p-1 rounded text-gray-400 hover:text-gray-600 transition-colors"
        >
          <svg
            aria-hidden="true"
            width="18"
            height="18"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
          >
            <line x1="18" y1="6" x2="6" y2="18" />
            <line x1="6" y1="6" x2="18" y2="18" />
          </svg>
        </button>
      </div>

      {step === 'audience' && (
        <div>
          <p className="text-sm font-medium text-gray-700 mb-3">Modtagere</p>
          <div className="space-y-2 mb-4">
            {isAdmin && (
              <label className="flex items-center gap-3 p-3 border rounded-lg cursor-pointer hover:bg-gray-50 transition-colors">
                <input
                  type="radio"
                  name="audience"
                  value="AllParents"
                  checked={audience === 'AllParents'}
                  onChange={() => handleAudienceChange('AllParents')}
                  className="text-brand-600 focus:ring-brand-500"
                />
                <span className="text-sm text-gray-800">Alle forældre</span>
              </label>
            )}
            <label className="flex items-center gap-3 p-3 border rounded-lg cursor-pointer hover:bg-gray-50 transition-colors">
              <input
                type="radio"
                name="audience"
                value="ClassParents"
                checked={audience === 'ClassParents'}
                onChange={() => handleAudienceChange('ClassParents')}
                className="text-brand-600 focus:ring-brand-500"
              />
              <span className="text-sm text-gray-800">Forældre i en klasse</span>
            </label>
            {audience === 'ClassParents' && classOptions.length > 0 && (
              <select
                value={classId}
                onChange={(e) => handleClassChange(e.target.value)}
                className="ml-8 w-[calc(100%-2rem)] px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
              >
                {classOptions.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </select>
            )}
            {isAdmin && (
              <label className="flex items-center gap-3 p-3 border rounded-lg cursor-pointer hover:bg-gray-50 transition-colors">
                <input
                  type="radio"
                  name="audience"
                  value="SfoParents"
                  checked={audience === 'SfoParents'}
                  onChange={() => handleAudienceChange('SfoParents')}
                  className="text-brand-600 focus:ring-brand-500"
                />
                <span className="text-sm text-gray-800">SFO-forældre</span>
              </label>
            )}
            {!isParent && (
              <>
                <label className="flex items-center gap-3 p-3 border rounded-lg cursor-pointer hover:bg-gray-50 transition-colors">
                  <input
                    type="radio"
                    name="audience"
                    value="AllStaff"
                    checked={audience === 'AllStaff'}
                    onChange={() => handleAudienceChange('AllStaff')}
                    className="text-brand-600 focus:ring-brand-500"
                  />
                  <span className="text-sm text-gray-800">Alt personale</span>
                </label>
                <label className="flex items-center gap-3 p-3 border rounded-lg cursor-pointer hover:bg-gray-50 transition-colors">
                  <input
                    type="radio"
                    name="audience"
                    value="StaffByRole"
                    checked={audience === 'StaffByRole'}
                    onChange={() => handleAudienceChange('StaffByRole')}
                    className="text-brand-600 focus:ring-brand-500"
                  />
                  <span className="text-sm text-gray-800">Personale (rolle)</span>
                </label>
                {audience === 'StaffByRole' && (
                  <select
                    value={staffRole}
                    onChange={(e) => handleRoleChange(e.target.value as StaffRole)}
                    className="ml-8 w-[calc(100%-2rem)] px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
                  >
                    <option value="Teacher">Lærere</option>
                    <option value="Aide">Pædagoger</option>
                    <option value="Substitute">Vikarer</option>
                  </select>
                )}
              </>
            )}
          </div>

          <div className="mb-5 text-sm text-gray-500">
            {previewLoading && 'Henter modtagere…'}
            {!previewLoading && previewCount !== null && (
              <span>
                <span className="font-semibold text-gray-800">{previewCount}</span> modtager
                {previewCount !== 1 ? 'e' : ''}
              </span>
            )}
          </div>

          <div className="flex justify-end gap-3">
            <button
              type="button"
              onClick={onClose}
              className="px-4 py-2 text-sm font-medium text-gray-700 hover:text-gray-900 transition-colors"
            >
              Annuller
            </button>
            <button
              type="button"
              onClick={() => setStep('compose')}
              disabled={audience === 'ClassParents' && !classId}
              className="px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors disabled:opacity-50"
            >
              Fortsæt
            </button>
          </div>
        </div>
      )}

      {step === 'compose' && (
        <div>
          <div className="mb-4">
            <label htmlFor="group-subject" className="block text-sm font-medium text-gray-700 mb-1">
              Emne
            </label>
            <input
              id="group-subject"
              type="text"
              value={subject}
              onChange={(e) => setSubject(e.target.value)}
              maxLength={200}
              placeholder="Emne"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
            />
          </div>
          <div className="mb-5">
            <label htmlFor="group-body" className="block text-sm font-medium text-gray-700 mb-1">
              Besked
            </label>
            <textarea
              id="group-body"
              value={body}
              onChange={(e) => setBody(e.target.value)}
              rows={6}
              maxLength={10000}
              placeholder="Skriv din besked her…"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 resize-none"
            />
          </div>
          <div className="flex justify-end gap-3">
            <button
              type="button"
              onClick={() => setStep('audience')}
              className="px-4 py-2 text-sm font-medium text-gray-700 border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors"
            >
              Tilbage
            </button>
            <button
              type="button"
              onClick={() => setStep('confirm')}
              disabled={!subject.trim() || !body.trim()}
              className="px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors disabled:opacity-50"
            >
              Fortsæt
            </button>
          </div>
        </div>
      )}

      {step === 'confirm' && (
        <div>
          <div className="bg-gray-50 rounded-lg p-4 space-y-2 text-sm text-gray-700 mb-5">
            <div>
              <span className="font-medium">Modtagere: </span>
              {audienceLabel}
            </div>
            <div>
              <span className="font-medium">Antal: </span>
              <span className="font-semibold text-brand-700">{previewCount ?? '…'}</span>
            </div>
            <div>
              <span className="font-medium">Emne: </span>
              {subject}
            </div>
          </div>
          {previewCount === 0 && (
            <p className="text-sm text-yellow-700 bg-yellow-50 border border-yellow-200 rounded-lg px-4 py-3 mb-4">
              Ingen modtagere fundet. Beskeden sendes ikke.
            </p>
          )}
          {sendMutation.isError && (
            <p className="text-sm text-red-600 mb-3">Der opstod en fejl. Prøv igen.</p>
          )}
          <div className="flex justify-end gap-3">
            <button
              type="button"
              onClick={() => setStep('compose')}
              disabled={sendMutation.isPending}
              className="px-4 py-2 text-sm font-medium text-gray-700 border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors disabled:opacity-50"
            >
              Tilbage
            </button>
            <button
              type="button"
              onClick={() => sendMutation.mutate()}
              disabled={sendMutation.isPending || previewCount === 0}
              className="px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors disabled:opacity-50"
            >
              {sendMutation.isPending ? 'Sender…' : 'Send'}
            </button>
          </div>
        </div>
      )}

      {step === 'done' && (
        <div className="text-center py-4">
          <div className="mx-auto h-12 w-12 rounded-full bg-green-100 flex items-center justify-center mb-3">
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
          <p className="text-sm font-medium text-gray-900 mb-4">Gruppebesked sendt!</p>
          <button
            type="button"
            onClick={() => {
              onSent()
              onClose()
            }}
            className="px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors"
          >
            Luk
          </button>
        </div>
      )}
    </Modal>
  )
}

// ── MessagesPage ─────────────────────────────────────────────────────────────

export default function MessagesPage() {
  usePageTitle('Beskeder')
  const { isAdmin, isParent } = useAuth()

  const qc = useQueryClient()
  const [tab, setTab] = useState<'inbox' | 'sent'>('inbox')
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [composeOpen, setComposeOpen] = useState(false)
  const [groupComposeOpen, setGroupComposeOpen] = useState(false)

  const [inbox, setInbox] = useState<InboxMessageDto[]>([])
  const [sent, setSent] = useState<SentMessageDto[]>([])
  const [loading, setLoading] = useState(true)

  // Directory state
  const [directorySearch, setDirectorySearch] = useState('')
  const [allRecipients, setAllRecipients] = useState<RecipientDto[]>([])
  const [filteredRecipients, setFilteredRecipients] = useState<RecipientDto[]>([])
  const [directoryLoading, setDirectoryLoading] = useState(true)
  const [mobilePanel, setMobilePanel] = useState<'directory' | 'list' | 'detail'>('directory')

  // Compose state
  const [recipientSearch, setRecipientSearch] = useState('')
  const [recipientResults, setRecipientResults] = useState<RecipientDto[]>([])
  const [selectedRecipient, setSelectedRecipient] = useState<RecipientDto | null>(null)
  const [subject, setSubject] = useState('')
  const [body, setBody] = useState('')
  const [sending, setSending] = useState(false)
  const [sendError, setSendError] = useState<string | null>(null)
  const [showDropdown, setShowDropdown] = useState(false)
  const [inReplyToId, setInReplyToId] = useState<string | null>(null)

  const searchDebounceRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const directoryDebounceRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  // Data for GroupComposeModal
  const { data: allClasses = [] } = useQuery({
    ...getApiV1ClassesOptions(),
    enabled: !isParent,
  })
  const { data: parentMe } = useQuery({
    ...getApiV1ParentsMeOptions(),
    enabled: isParent,
  })

  const parentClasses = (parentMe?.classes ?? []).map((c) => ({
    classId: c.classId ?? '',
    className: c.className ?? '',
  }))

  const threadQueryKey = [{ _id: 'getApiV1MessagesByIdThread', path: { id: selectedId } }] as const

  const {
    data: thread = [],
    isLoading: threadLoading,
    isError: threadError,
  } = useQuery({
    queryKey: threadQueryKey,
    queryFn: async () => {
      const { data } = await getApiV1MessagesByIdThread({
        path: { id: selectedId! },
        throwOnError: true,
      })
      return (data ?? []) as ThreadMessageDto[]
    },
    enabled: selectedId !== null,
  })

  useEffect(() => {
    return () => {
      if (searchDebounceRef.current) clearTimeout(searchDebounceRef.current)
      if (directoryDebounceRef.current) clearTimeout(directoryDebounceRef.current)
    }
  }, [])

  const fetchMessages = useCallback(async () => {
    setLoading(true)
    try {
      const [inboxRes, sentRes] = await Promise.all([
        getApiV1MessagesInbox({ throwOnError: false }),
        getApiV1MessagesSent({ throwOnError: false }),
      ])
      if (inboxRes.data) setInbox(inboxRes.data as InboxMessageDto[])
      if (sentRes.data) setSent(sentRes.data as SentMessageDto[])
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    fetchMessages()
  }, [fetchMessages])

  useEffect(() => {
    async function loadDirectory() {
      setDirectoryLoading(true)
      try {
        const { data } = await getApiV1MessagesRecipients({ query: { q: '' }, throwOnError: false })
        const recipients = (data ?? []) as RecipientDto[]
        setAllRecipients(recipients)
        setFilteredRecipients(recipients)
      } finally {
        setDirectoryLoading(false)
      }
    }
    loadDirectory()
  }, [])

  function handleDirectorySearch(value: string) {
    setDirectorySearch(value)
    if (directoryDebounceRef.current) clearTimeout(directoryDebounceRef.current)
    const searchAtCall = value
    directoryDebounceRef.current = setTimeout(async () => {
      if (searchAtCall.length === 0) {
        setFilteredRecipients(allRecipients)
        return
      }
      const { data } = await getApiV1MessagesRecipients({
        query: { q: searchAtCall },
        throwOnError: false,
      })
      setDirectorySearch((current) => {
        if (current === searchAtCall) setFilteredRecipients((data ?? []) as RecipientDto[])
        return current
      })
    }, 300)
  }

  async function handleSelectInbox(id: string) {
    setSelectedId(id)
    setMobilePanel('detail')

    // The clicked row is the latest message in its thread (per groupByThread), but earlier
    // replies in the same thread can still be unread — mark every unread message sharing
    // that thread root as read, not just the clicked one.
    const rootId = threadRoots.get(id) ?? id
    const unreadInThread = inbox.filter(
      (m) => !m.readAt && (threadRoots.get(m.id) ?? m.id) === rootId
    )
    if (unreadInThread.length === 0) {
      return
    }

    await Promise.all(
      unreadInThread.map((m) =>
        postApiV1MessagesByIdRead({ path: { id: m.id }, throwOnError: false })
      )
    )
    const now = new Date().toISOString()
    const unreadIds = new Set(unreadInThread.map((m) => m.id))
    setInbox((prev) => prev.map((m) => (unreadIds.has(m.id) ? { ...m, readAt: now } : m)))
  }

  function handleRecipientSearchChange(value: string) {
    setRecipientSearch(value)
    if (searchDebounceRef.current) clearTimeout(searchDebounceRef.current)
    if (value.length < 2) {
      setRecipientResults([])
      setShowDropdown(false)
      return
    }
    searchDebounceRef.current = setTimeout(async () => {
      const { data } = await getApiV1MessagesRecipients({
        query: { q: value },
        throwOnError: false,
      })
      if (data) {
        const results = data as RecipientDto[]
        setRecipientResults(results)
        setShowDropdown(results.length > 0)
      }
    }, 300)
  }

  function handleSelectRecipient(recipient: RecipientDto) {
    setSelectedRecipient(recipient)
    setRecipientSearch('')
    setRecipientResults([])
    setShowDropdown(false)
  }

  function handleOpenCompose(
    prefilledRecipient?: RecipientDto,
    prefilled?: { subject: string; body: string; inReplyToId: string }
  ) {
    setSelectedRecipient(prefilledRecipient ?? null)
    setRecipientSearch('')
    setRecipientResults([])
    setShowDropdown(false)
    setSubject(prefilled?.subject ?? '')
    setBody(prefilled?.body ?? '')
    setInReplyToId(prefilled?.inReplyToId ?? null)
    setSendError(null)
    setComposeOpen(true)
  }

  function handleDirectoryContactClick(recipient: RecipientDto) {
    handleOpenCompose(recipient)
  }

  function handleReply() {
    if (!selectedMsg || threadLoading) return

    // Reply to the last message in the thread, so multi-hop conversations
    // always append to the end regardless of which row was clicked.
    const lastMsg = thread.length > 0 ? thread[thread.length - 1] : null

    const recipient: RecipientDto = lastMsg
      ? lastMsg.isOwn
        ? {
            id: lastMsg.recipientId,
            name: lastMsg.recipientName,
            type: toMessageRecipientType(lastMsg.recipientType),
          }
        : {
            id: lastMsg.senderId,
            name: lastMsg.senderName,
            type: toMessageRecipientType(lastMsg.senderType),
          }
      : tab === 'inbox'
        ? {
            id: (selectedMsg as InboxMessageDto).senderId,
            name: (selectedMsg as InboxMessageDto).senderName,
            type: (selectedMsg as InboxMessageDto).senderType,
          }
        : {
            id: (selectedMsg as SentMessageDto).recipientId,
            name: (selectedMsg as SentMessageDto).recipientName,
            type: (selectedMsg as SentMessageDto).recipientType,
          }

    handleOpenCompose(recipient, {
      subject: selectedMsg.subject,
      body: '',
      inReplyToId: lastMsg?.id ?? selectedMsg.id,
    })
  }

  async function handleSend() {
    if (!selectedRecipient || !subject.trim() || !body.trim()) return
    setSending(true)
    setSendError(null)
    try {
      await postApiV1Messages({
        body: {
          recipientId: selectedRecipient.id,
          recipientType: selectedRecipient.type,
          subject: subject.trim(),
          body: body.trim(),
          inReplyToId: inReplyToId ?? undefined,
        },
        throwOnError: true,
      })
      setComposeOpen(false)
      setInReplyToId(null)
      await fetchMessages()
      if (selectedId !== null) {
        await qc.invalidateQueries({
          queryKey: [{ _id: 'getApiV1MessagesByIdThread', path: { id: selectedId } }],
        })
      }
    } catch {
      setSendError('Der opstod en fejl. Prøv igen.')
    } finally {
      setSending(false)
    }
  }

  const threadRoots = buildThreadRoots([...inbox, ...sent])
  const groupedInbox = groupByThread(inbox, threadRoots)
  const groupedSent = groupByThread(sent, threadRoots)
  const currentMessages = tab === 'inbox' ? groupedInbox : groupedSent
  const selectedInboxMsg = tab === 'inbox' ? inbox.find((m) => m.id === selectedId) : null
  const selectedSentMsg = tab === 'sent' ? sent.find((m) => m.id === selectedId) : null
  const selectedMsg = selectedInboxMsg ?? selectedSentMsg

  return (
    <div className="flex flex-col h-full min-h-0">
      {/* Header */}
      <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200 bg-white shrink-0">
        <h1 className="font-display text-xl font-semibold text-gray-900">Beskeder</h1>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => setGroupComposeOpen(true)}
            className="flex items-center gap-2 px-4 py-2 bg-white border border-gray-300 text-gray-700 text-sm font-medium rounded-lg hover:bg-gray-50 transition-colors"
          >
            <svg
              aria-hidden="true"
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
              <circle cx="9" cy="7" r="4" />
              <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
              <path d="M16 3.13a4 4 0 0 1 0 7.75" />
            </svg>
            Gruppebesked
          </button>
          <button
            type="button"
            onClick={() => handleOpenCompose()}
            className="flex items-center gap-2 px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors"
          >
            <svg
              aria-hidden="true"
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <line x1="12" y1="5" x2="12" y2="19" />
              <line x1="5" y1="12" x2="19" y2="12" />
            </svg>
            Ny besked
          </button>
        </div>
      </div>

      {/* Content */}
      <div className="flex flex-1 min-h-0 overflow-hidden">
        {/* Directory panel */}
        <div
          className={`w-full lg:w-64 shrink-0 border-r border-gray-200 bg-white flex flex-col ${mobilePanel === 'directory' ? 'flex' : 'hidden lg:flex'}`}
        >
          <div className="px-3 py-3 border-b border-gray-100 shrink-0">
            <p className="text-xs font-semibold uppercase tracking-wider text-gray-400 mb-2">
              Kontakter
            </p>
            <div className="relative">
              <svg
                aria-hidden="true"
                className="absolute left-2.5 top-1/2 -translate-y-1/2 text-gray-400"
                width="14"
                height="14"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
              >
                <circle cx="11" cy="11" r="8" />
                <line x1="21" y1="21" x2="16.65" y2="16.65" />
              </svg>
              <input
                type="text"
                aria-label="Søg i kontakter"
                value={directorySearch}
                onChange={(e) => handleDirectorySearch(e.target.value)}
                placeholder="Søg…"
                className="w-full pl-8 pr-3 py-1.5 border border-gray-200 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
              />
            </div>
          </div>
          <div className="flex-1 overflow-y-auto divide-y divide-gray-50">
            {directoryLoading && (
              <div className="flex items-center justify-center py-8 text-sm text-gray-400">
                Indlæser…
              </div>
            )}
            {!directoryLoading && filteredRecipients.length === 0 && (
              <div className="flex items-center justify-center py-8 text-sm text-gray-400">
                Ingen kontakter
              </div>
            )}
            {filteredRecipients.map((r) => (
              <button
                type="button"
                key={r.id}
                onClick={() => handleDirectoryContactClick(r)}
                className="w-full text-left px-3 py-2.5 hover:bg-gray-50 transition-colors flex items-center gap-2.5"
              >
                <div className="flex items-center justify-center h-7 w-7 rounded-full bg-brand-100 text-brand-700 text-xs font-semibold shrink-0 overflow-hidden">
                  {r.avatarUrl ? (
                    <img src={r.avatarUrl} alt="" className="h-7 w-7 rounded-full object-cover" />
                  ) : (
                    getInitials(r.name)
                  )}
                </div>
                <div className="min-w-0">
                  <p className="text-sm font-medium text-gray-900 truncate">{r.name}</p>
                  <p className="text-xs text-gray-400">
                    {r.type === 'Parent' ? 'Forælder' : 'Medarbejder'}
                  </p>
                </div>
              </button>
            ))}
          </div>
          <div className="lg:hidden px-3 py-2 border-t border-gray-100">
            <button
              type="button"
              onClick={() => setMobilePanel('list')}
              className="flex items-center gap-1 text-sm text-brand-600 font-medium"
            >
              Indbakke
              <svg
                aria-hidden="true"
                width="14"
                height="14"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
              >
                <polyline points="9 18 15 12 9 6" />
              </svg>
            </button>
          </div>
        </div>

        {/* Message list */}
        <div
          className={`w-full lg:w-72 shrink-0 border-r border-gray-200 bg-white flex flex-col ${mobilePanel === 'list' ? 'flex' : 'hidden lg:flex'}`}
        >
          <div className="flex border-b border-gray-200 shrink-0">
            <button
              type="button"
              onClick={() => {
                setTab('inbox')
                setSelectedId(null)
              }}
              className={`flex-1 px-4 py-3 text-sm font-medium border-b-2 transition-colors ${tab === 'inbox' ? 'border-brand-600 text-brand-600' : 'border-transparent text-gray-500 hover:text-gray-700'}`}
            >
              Indbakke
            </button>
            <button
              type="button"
              onClick={() => {
                setTab('sent')
                setSelectedId(null)
              }}
              className={`flex-1 px-4 py-3 text-sm font-medium border-b-2 transition-colors ${tab === 'sent' ? 'border-brand-600 text-brand-600' : 'border-transparent text-gray-500 hover:text-gray-700'}`}
            >
              Sendt
            </button>
          </div>

          <div className="flex-1 overflow-y-auto">
            {loading && (
              <div className="flex items-center justify-center py-12 text-sm text-gray-500">
                Indlæser…
              </div>
            )}
            {!loading && currentMessages.length === 0 && (
              <div className="flex items-center justify-center py-12 px-4">
                <p className="text-sm text-gray-400 text-center">Ingen beskeder</p>
              </div>
            )}
            <div className="divide-y divide-gray-100">
              {tab === 'inbox' &&
                groupedInbox.map((msg) => {
                  const isUnread = msg.hasUnread
                  return (
                    <button
                      type="button"
                      key={msg.id}
                      onClick={() => handleSelectInbox(msg.id)}
                      className={`w-full text-left px-4 py-3 hover:bg-gray-50 transition-colors ${selectedId === msg.id ? 'bg-brand-50' : ''}`}
                    >
                      <div className="flex items-center justify-between mb-0.5">
                        <span
                          className={`text-sm truncate ${isUnread ? 'font-semibold text-gray-900' : 'font-medium text-gray-700'}`}
                        >
                          {msg.senderName}
                        </span>
                        <span className="text-xs text-gray-400 shrink-0 ml-2">
                          {formatRelativeTime(msg.sentAt)}
                        </span>
                      </div>
                      <p
                        className={`text-sm truncate ${isUnread ? 'font-medium text-gray-800' : 'text-gray-600'}`}
                      >
                        {msg.subject}
                      </p>
                      <p className="text-xs text-gray-400 truncate mt-0.5">
                        {truncate(msg.body, 80)}
                      </p>
                    </button>
                  )
                })}
              {tab === 'sent' &&
                groupedSent.map((msg) => (
                  <button
                    type="button"
                    key={msg.id}
                    onClick={() => {
                      setSelectedId(msg.id)
                      setMobilePanel('detail')
                    }}
                    className={`w-full text-left px-4 py-3 hover:bg-gray-50 transition-colors ${selectedId === msg.id ? 'bg-brand-50' : ''}`}
                  >
                    <div className="flex items-center justify-between mb-0.5">
                      <div className="flex items-center gap-1.5 min-w-0">
                        {msg.isGroup && (
                          <span className="shrink-0 inline-flex items-center px-1.5 py-0.5 rounded text-xs font-medium bg-brand-100 text-brand-700">
                            Gruppe
                          </span>
                        )}
                        <span className="text-sm font-medium text-gray-700 truncate">
                          {msg.isGroup ? msg.audienceLabel : `Til: ${msg.recipientName}`}
                        </span>
                        {msg.isGroup && msg.groupRecipientCount != null && (
                          <span className="shrink-0 text-xs text-gray-400">
                            ({msg.groupRecipientCount})
                          </span>
                        )}
                      </div>
                      <span className="text-xs text-gray-400 shrink-0 ml-2">
                        {formatRelativeTime(msg.sentAt)}
                      </span>
                    </div>
                    <p className="text-sm text-gray-600 truncate">{msg.subject}</p>
                    <p className="text-xs text-gray-400 truncate mt-0.5">
                      {truncate(msg.body, 80)}
                    </p>
                  </button>
                ))}
            </div>
          </div>

          <div className="lg:hidden px-3 py-2 border-t border-gray-100">
            <button
              type="button"
              onClick={() => setMobilePanel('directory')}
              className="flex items-center gap-1 text-sm text-gray-500"
            >
              <svg
                aria-hidden="true"
                width="14"
                height="14"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
              >
                <polyline points="15 18 9 12 15 6" />
              </svg>
              Kontakter
            </button>
          </div>
        </div>

        {/* Detail panel */}
        <div
          className={`flex-1 bg-gray-50 min-w-0 flex flex-col ${mobilePanel === 'detail' ? 'flex' : 'hidden lg:flex'}`}
        >
          {selectedMsg ? (
            <div className="flex flex-col h-full">
              <div className="lg:hidden px-4 pt-3">
                <button
                  type="button"
                  onClick={() => setMobilePanel('list')}
                  className="flex items-center gap-1 text-sm text-gray-500 hover:text-gray-700"
                >
                  <svg
                    aria-hidden="true"
                    width="16"
                    height="16"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  >
                    <polyline points="15 18 9 12 15 6" />
                  </svg>
                  Tilbage
                </button>
              </div>
              {(() => {
                const isGroup =
                  (tab === 'sent' && (selectedMsg as SentMessageDto).isGroup) ||
                  (tab === 'inbox' && (selectedMsg as InboxMessageDto).isGroup)

                return (
                  <div className="flex-1 overflow-y-auto px-6 py-6">
                    <div className="max-w-2xl">
                      <div className="flex items-center justify-between gap-3 mb-4">
                        <h2 className="text-lg font-semibold text-gray-900">
                          {selectedMsg.subject}
                        </h2>
                        {!isGroup && (
                          <button
                            type="button"
                            onClick={handleReply}
                            disabled={threadLoading}
                            className="shrink-0 flex items-center gap-1.5 px-3 py-1.5 border border-gray-300 text-gray-700 text-sm font-medium rounded-lg hover:bg-gray-50 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                          >
                            <svg
                              aria-hidden="true"
                              width="14"
                              height="14"
                              viewBox="0 0 24 24"
                              fill="none"
                              stroke="currentColor"
                              strokeWidth="2"
                              strokeLinecap="round"
                              strokeLinejoin="round"
                            >
                              <polyline points="9 17 4 12 9 7" />
                              <path d="M20 18v-2a4 4 0 0 0-4-4H4" />
                            </svg>
                            Svar
                          </button>
                        )}
                      </div>

                      {isGroup ? (
                        <>
                          <div className="flex items-start gap-3 mb-6 pb-4 border-b border-gray-200">
                            <div className="flex items-center justify-center h-9 w-9 rounded-full bg-brand-100 text-brand-700 text-sm font-semibold shrink-0">
                              {tab === 'inbox'
                                ? getInitials((selectedMsg as InboxMessageDto).senderName)
                                : getInitials((selectedMsg as SentMessageDto).recipientName)}
                            </div>
                            <div className="min-w-0">
                              {tab === 'inbox' ? (
                                <>
                                  <p className="text-sm font-medium text-gray-900">
                                    {(selectedMsg as InboxMessageDto).senderName}
                                  </p>
                                  <p className="text-xs text-gray-500">
                                    {(selectedMsg as InboxMessageDto).senderType === 'Parent'
                                      ? 'Forælder'
                                      : 'Medarbejder'}
                                    {' · '}
                                    {formatRelativeTime(selectedMsg.sentAt)}
                                  </p>
                                </>
                              ) : (
                                <>
                                  <div className="flex items-center gap-1.5">
                                    <span className="inline-flex items-center px-1.5 py-0.5 rounded text-xs font-medium bg-brand-100 text-brand-700">
                                      Gruppe
                                    </span>
                                    <p className="text-sm font-medium text-gray-900">
                                      {(selectedMsg as SentMessageDto).audienceLabel}
                                    </p>
                                    {(selectedMsg as SentMessageDto).groupRecipientCount !=
                                      null && (
                                      <span className="text-xs text-gray-400">
                                        ({(selectedMsg as SentMessageDto).groupRecipientCount}{' '}
                                        modtagere)
                                      </span>
                                    )}
                                  </div>
                                  <p className="text-xs text-gray-500">
                                    {formatRelativeTime(selectedMsg.sentAt)}
                                  </p>
                                </>
                              )}
                            </div>
                          </div>
                          <div className="text-sm text-gray-800 whitespace-pre-wrap leading-relaxed">
                            {selectedMsg.body}
                          </div>
                        </>
                      ) : (
                        <div className="space-y-4">
                          {threadLoading && (
                            <p className="text-sm text-gray-400">Indlæser samtale…</p>
                          )}
                          {!threadLoading && threadError && (
                            <p className="text-sm text-red-600">
                              Kunne ikke hente samtalen. Prøv igen.
                            </p>
                          )}
                          {!threadLoading && !threadError && thread.length === 0 && (
                            <div className="flex items-start gap-3 pb-4 border-b border-gray-200">
                              <div className="flex items-center justify-center h-9 w-9 rounded-full bg-brand-100 text-brand-700 text-sm font-semibold shrink-0">
                                {tab === 'inbox'
                                  ? getInitials((selectedMsg as InboxMessageDto).senderName)
                                  : getInitials((selectedMsg as SentMessageDto).recipientName)}
                              </div>
                              <div className="min-w-0">
                                <p className="text-sm font-medium text-gray-900">
                                  {tab === 'inbox'
                                    ? (selectedMsg as InboxMessageDto).senderName
                                    : `Til: ${(selectedMsg as SentMessageDto).recipientName}`}
                                </p>
                                <p className="text-xs text-gray-500">
                                  {formatRelativeTime(selectedMsg.sentAt)}
                                </p>
                                <p className="text-sm text-gray-800 whitespace-pre-wrap leading-relaxed mt-2">
                                  {selectedMsg.body}
                                </p>
                              </div>
                            </div>
                          )}
                          {[...thread].reverse().map((msg) => (
                            <div
                              key={msg.id}
                              className={`flex items-start gap-3 pb-4 border-b border-gray-100 last:border-0 ${msg.isOwn ? 'flex-row-reverse text-right' : ''}`}
                            >
                              <div
                                className={`flex items-center justify-center h-9 w-9 rounded-full text-sm font-semibold shrink-0 ${msg.isOwn ? 'bg-blue-100 text-blue-700' : 'bg-brand-100 text-brand-700'}`}
                              >
                                {getInitials(msg.senderName)}
                              </div>
                              <div className="min-w-0 flex-1">
                                <p className="text-sm font-medium text-gray-900">
                                  {msg.isOwn ? 'Dig' : msg.senderName}
                                </p>
                                <p className="text-xs text-gray-500">
                                  {msg.senderType === 'Parent' ? 'Forælder' : 'Medarbejder'}
                                  {' · '}
                                  {formatRelativeTime(msg.sentAt)}
                                </p>
                                <div
                                  className={`inline-block mt-2 max-w-lg px-3 py-2 rounded-xl text-sm text-left whitespace-pre-wrap leading-relaxed ${msg.isOwn ? 'bg-blue-100 text-gray-900' : 'bg-white border border-gray-200 text-gray-900'}`}
                                >
                                  {msg.body}
                                </div>
                              </div>
                            </div>
                          ))}
                        </div>
                      )}
                    </div>
                  </div>
                )
              })()}
            </div>
          ) : (
            <div className="flex-1 flex items-center justify-center">
              <div className="text-center">
                <svg
                  aria-hidden="true"
                  className="mx-auto mb-3 text-gray-300"
                  width="40"
                  height="40"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.5"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                >
                  <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z" />
                  <polyline points="22,6 12,13 2,6" />
                </svg>
                <p className="text-sm text-gray-400">Vælg en besked for at læse den</p>
              </div>
            </div>
          )}
        </div>
      </div>

      {/* 1:1 compose modal */}
      <Modal
        isOpen={composeOpen}
        onClose={() => {
          setComposeOpen(false)
          setInReplyToId(null)
        }}
        size="lg"
        contentClassName="bg-white rounded-xl p-6 shadow-xl w-full max-w-lg"
      >
        <div className="flex items-center justify-between mb-5">
          <h2 className="text-base font-semibold text-gray-900">
            {inReplyToId ? 'Svar' : 'Ny besked'}
          </h2>
          <button
            type="button"
            aria-label="Luk"
            onClick={() => {
              setComposeOpen(false)
              setInReplyToId(null)
            }}
            className="p-1 rounded text-gray-400 hover:text-gray-600 transition-colors"
          >
            <svg
              aria-hidden="true"
              width="18"
              height="18"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <line x1="18" y1="6" x2="6" y2="18" />
              <line x1="6" y1="6" x2="18" y2="18" />
            </svg>
          </button>
        </div>

        <div className="mb-4">
          <label
            htmlFor="compose-recipient"
            className="block text-sm font-medium text-gray-700 mb-1"
          >
            Modtager
          </label>
          {selectedRecipient ? (
            <div className="flex items-center gap-2 px-3 py-2 border border-gray-300 rounded-lg">
              <div className="flex items-center justify-center h-6 w-6 rounded-full bg-brand-100 text-brand-700 text-xs font-semibold shrink-0">
                {getInitials(selectedRecipient.name)}
              </div>
              <span className="text-sm text-gray-900 flex-1">{selectedRecipient.name}</span>
              <span className="text-xs text-gray-400">
                {selectedRecipient.type === 'Parent' ? 'Forælder' : 'Medarbejder'}
              </span>
              <button
                type="button"
                aria-label="Fjern modtager"
                onClick={() => setSelectedRecipient(null)}
                className="ml-1 text-gray-400 hover:text-gray-600"
              >
                <svg
                  aria-hidden="true"
                  width="14"
                  height="14"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                >
                  <line x1="18" y1="6" x2="6" y2="18" />
                  <line x1="6" y1="6" x2="18" y2="18" />
                </svg>
              </button>
            </div>
          ) : (
            <div className="relative">
              <input
                id="compose-recipient"
                type="text"
                value={recipientSearch}
                onChange={(e) => handleRecipientSearchChange(e.target.value)}
                placeholder="Søg efter navn (min. 2 tegn)…"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
                onFocus={() => {
                  if (recipientResults.length > 0) setShowDropdown(true)
                }}
                onBlur={() => setTimeout(() => setShowDropdown(false), 150)}
              />
              {showDropdown && (
                <div className="absolute z-10 mt-1 w-full bg-white border border-gray-200 rounded-lg shadow-lg max-h-48 overflow-y-auto">
                  {recipientResults.map((r) => (
                    <button
                      type="button"
                      key={r.id}
                      onMouseDown={() => handleSelectRecipient(r)}
                      className="flex items-center gap-3 w-full text-left px-3 py-2.5 hover:bg-gray-50 transition-colors"
                    >
                      <div className="flex items-center justify-center h-7 w-7 rounded-full bg-brand-100 text-brand-700 text-xs font-semibold shrink-0">
                        {r.avatarUrl ? (
                          <img
                            src={r.avatarUrl}
                            alt=""
                            className="h-7 w-7 rounded-full object-cover"
                          />
                        ) : (
                          getInitials(r.name)
                        )}
                      </div>
                      <div className="min-w-0">
                        <p className="text-sm font-medium text-gray-900 truncate">{r.name}</p>
                        <p className="text-xs text-gray-500">
                          {r.type === 'Parent' ? 'Forælder' : 'Medarbejder'}
                        </p>
                      </div>
                    </button>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>

        <div className="mb-4">
          <label htmlFor="compose-subject" className="block text-sm font-medium text-gray-700 mb-1">
            Emne
          </label>
          <input
            id="compose-subject"
            type="text"
            value={subject}
            onChange={(e) => setSubject(e.target.value)}
            placeholder="Emne"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
          />
        </div>

        <div className="mb-5">
          <label htmlFor="compose-body" className="block text-sm font-medium text-gray-700 mb-1">
            Besked
          </label>
          <textarea
            id="compose-body"
            value={body}
            onChange={(e) => setBody(e.target.value)}
            rows={6}
            placeholder="Skriv din besked her…"
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 resize-none"
          />
        </div>

        {sendError && <p className="text-sm text-red-600 mb-3">{sendError}</p>}

        <div className="flex items-center justify-end gap-3">
          <button
            type="button"
            onClick={() => {
              setComposeOpen(false)
              setInReplyToId(null)
            }}
            className="px-4 py-2 text-sm font-medium text-gray-700 hover:text-gray-900 transition-colors"
          >
            Annuller
          </button>
          <button
            type="button"
            onClick={handleSend}
            disabled={sending || !selectedRecipient || !subject.trim() || !body.trim()}
            className="px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors disabled:opacity-50"
          >
            {sending ? 'Sender…' : 'Send'}
          </button>
        </div>
      </Modal>

      {/* Group compose modal */}
      {groupComposeOpen && (
        <GroupComposeModal
          isAdmin={isAdmin}
          isParent={isParent}
          parentClasses={parentClasses}
          allClasses={allClasses as ClassesControllerClassDto[]}
          onClose={() => setGroupComposeOpen(false)}
          onSent={fetchMessages}
        />
      )}
    </div>
  )
}
