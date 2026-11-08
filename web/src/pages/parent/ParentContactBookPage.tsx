import { useState, useEffect, useRef } from 'react'
import { Modal } from '../../components/Modal'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { usePageTitle } from '../../hooks/usePageTitle'
import {
  getApiV1ContactThreads,
  getApiV1ContactThreadsByThreadIdMessages,
  getApiV1ContactThreadsStaffOptionsByStudentId,
  postApiV1ContactThreadsByThreadIdRead,
  postApiV1ContactThreadsByThreadIdMessages,
  postApiV1ContactThreads,
  getApiV1ParentsMe,
} from '../../api/generated/sdk.gen'
import type { ContactThreadsControllerNotifyStaffOptionDto } from '../../api/generated/types.gen'

interface ContactThreadDto {
  id: string
  studentId: string
  studentName: string
  lastMessageBody?: string
  lastMessageSentAt?: string
  lastMessageSenderType?: 'Parent' | 'Staff'
  unreadCount: number
}

interface ContactMessageDto {
  id: string
  senderType: 'Parent' | 'Staff'
  senderId: string
  senderName: string
  body: string
  sentAt: string
  readAt?: string
}

interface PagedResult<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

interface ParentStudentDto {
  studentId?: string
  studentName?: string | null
}

function formatDateTime(iso: string): string {
  const d = new Date(iso)
  const hh = d.getHours().toString().padStart(2, '0')
  const mm = d.getMinutes().toString().padStart(2, '0')
  const dd = d.getDate().toString().padStart(2, '0')
  const mo = (d.getMonth() + 1).toString().padStart(2, '0')
  return `${hh}:${mm} ${dd}/${mo}`
}

function truncate(text: string, max: number): string {
  if (text.length <= max) {
    return text
  }
  return `${text.slice(0, max)}…`
}

interface NotifyStaffPickerProps {
  studentId: string | null
  selectedIds: string[]
  onChange: (ids: string[]) => void
}

function NotifyStaffPicker({ studentId, selectedIds, onChange }: NotifyStaffPickerProps) {
  const [search, setSearch] = useState('')

  const { data: options = [] } = useQuery({
    queryKey: [{ _id: 'getApiV1ContactThreadsStaffOptionsByStudentId', path: { studentId } }],
    queryFn: async () => {
      const { data } = await getApiV1ContactThreadsStaffOptionsByStudentId({
        path: { studentId: studentId! },
        throwOnError: false,
      })
      return data ?? ([] as ContactThreadsControllerNotifyStaffOptionDto[])
    },
    enabled: !!studentId,
  })

  const filtered = options.filter((o) => o.name.toLowerCase().includes(search.toLowerCase()))
  const relevant = filtered.filter((o) => o.isRelevant).sort((a, b) => a.name.localeCompare(b.name))
  const rest = filtered.filter((o) => !o.isRelevant).sort((a, b) => a.name.localeCompare(b.name))

  function toggle(id: string) {
    onChange(selectedIds.includes(id) ? selectedIds.filter((s) => s !== id) : [...selectedIds, id])
  }

  if (!studentId || options.length === 0) {
    return null
  }

  return (
    <div className="space-y-2">
      <input
        type="search"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Søg personale…"
        className="w-full px-3 py-1.5 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
      />
      <div className="max-h-48 overflow-y-auto border border-gray-200 rounded-lg divide-y divide-gray-100">
        {relevant.length > 0 && (
          <div>
            <p className="px-3 py-1.5 text-xs font-semibold text-gray-500 bg-gray-50">
              Klassens personale
            </p>
            {relevant.map((o) => (
              <label
                key={o.id}
                className="flex items-center gap-3 px-3 py-2.5 hover:bg-gray-50 cursor-pointer"
              >
                <input
                  type="checkbox"
                  checked={selectedIds.includes(o.id)}
                  onChange={() => toggle(o.id)}
                  className="rounded border-gray-300 text-brand-600 focus:ring-brand-500 h-4 w-4 shrink-0"
                />
                <span className="text-sm text-gray-900">{o.name}</span>
              </label>
            ))}
          </div>
        )}
        {rest.length > 0 && (
          <div>
            <p className="px-3 py-1.5 text-xs font-semibold text-gray-500 bg-gray-50">Øvrige</p>
            {rest.map((o) => (
              <label
                key={o.id}
                className="flex items-center gap-3 px-3 py-2.5 hover:bg-gray-50 cursor-pointer"
              >
                <input
                  type="checkbox"
                  checked={selectedIds.includes(o.id)}
                  onChange={() => toggle(o.id)}
                  className="rounded border-gray-300 text-brand-600 focus:ring-brand-500 h-4 w-4 shrink-0"
                />
                <span className="text-sm text-gray-900">{o.name}</span>
              </label>
            ))}
          </div>
        )}
        {filtered.length === 0 && (
          <p className="px-3 py-3 text-sm text-gray-400">Ingen resultater</p>
        )}
      </div>
    </div>
  )
}

export default function ParentContactBookPage() {
  usePageTitle('Kontaktbog')
  const qc = useQueryClient()
  const [selectedThreadId, setSelectedThreadId] = useState<string | null>(null)
  const [messageBody, setMessageBody] = useState('')
  const [replyNotifyStaffIds, setReplyNotifyStaffIds] = useState<string[]>([])
  const [newMessageBody, setNewMessageBody] = useState('')
  const [newNotifyStaffIds, setNewNotifyStaffIds] = useState<string[]>([])
  const [showMobileMessages, setShowMobileMessages] = useState(false)
  const messagesEndRef = useRef<HTMLDivElement>(null)

  // Student picker state (for starting a new thread when parent has multiple children)
  const [studentPickerOpen, setStudentPickerOpen] = useState(false)

  const threadsQueryKey = [{ _id: 'getApiV1ContactThreads' }] as const

  const { data: threads = [], isLoading: threadsLoading } = useQuery({
    queryKey: threadsQueryKey,
    queryFn: async () => {
      const { data } = await getApiV1ContactThreads({ throwOnError: false })
      return (data ?? []) as ContactThreadDto[]
    },
  })

  const { data: parentMe } = useQuery({
    queryKey: [{ _id: 'getApiV1ParentsMe' }],
    queryFn: async () => {
      const { data } = await getApiV1ParentsMe({ throwOnError: false })
      return data
    },
  })

  const students = (parentMe?.students ?? []) as ParentStudentDto[]

  const { data: messagesData } = useQuery({
    queryKey: [
      { _id: 'getApiV1ContactThreadsByThreadIdMessages', path: { threadId: selectedThreadId } },
    ],
    queryFn: async () => {
      const { data } = await getApiV1ContactThreadsByThreadIdMessages({
        path: { threadId: selectedThreadId! },
        query: { page: 1, pageSize: 50 },
        throwOnError: false,
      })
      return (data ?? {
        items: [],
        total: 0,
        page: 1,
        pageSize: 50,
      }) as PagedResult<ContactMessageDto>
    },
    enabled: selectedThreadId !== null,
  })

  const messages = messagesData?.items ?? []

  const readMutation = useMutation({
    mutationFn: async (threadId: string) => {
      await postApiV1ContactThreadsByThreadIdRead({ path: { threadId }, throwOnError: false })
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: threadsQueryKey })
    },
  })

  const sendMessageMutation = useMutation({
    mutationFn: async ({
      threadId,
      body,
      notifyStaffIds,
    }: {
      threadId: string
      body: string
      notifyStaffIds: string[]
    }) => {
      await postApiV1ContactThreadsByThreadIdMessages({
        path: { threadId },
        body: { body, notifyStaffIds },
        throwOnError: true,
      })
    },
    onSuccess: () => {
      qc.invalidateQueries({
        queryKey: [
          { _id: 'getApiV1ContactThreadsByThreadIdMessages', path: { threadId: selectedThreadId } },
        ],
      })
      qc.invalidateQueries({ queryKey: threadsQueryKey })
      setMessageBody('')
      setReplyNotifyStaffIds([])
    },
  })

  const createThreadMutation = useMutation({
    mutationFn: async ({
      studentId,
      body,
      notifyStaffIds,
    }: {
      studentId: string
      body: string
      notifyStaffIds: string[]
    }) => {
      const { data } = await postApiV1ContactThreads({
        body: { studentId, body, notifyStaffIds },
        throwOnError: true,
      })
      return data as { threadId: string }
    },
    onSuccess: (data) => {
      qc.invalidateQueries({ queryKey: threadsQueryKey })
      setSelectedThreadId(data.threadId)
      setNewMessageBody('')
      setNewNotifyStaffIds([])
      setShowMobileMessages(true)
    },
  })

  const validStudents = students.filter(
    (s): s is { studentId: string; studentName?: string | null } => !!s.studentId
  )

  function handleSelectThread(threadId: string) {
    setSelectedThreadId(threadId)
    setShowMobileMessages(true)
    setReplyNotifyStaffIds([])
    readMutation.mutate(threadId)
  }

  function handleSend() {
    if (!selectedThreadId || !messageBody.trim()) {
      return
    }
    sendMessageMutation.mutate({
      threadId: selectedThreadId,
      body: messageBody.trim(),
      notifyStaffIds: replyNotifyStaffIds,
    })
  }

  function handleStartNewThread() {
    if (!newMessageBody.trim()) {
      return
    }
    if (validStudents.length === 0) {
      return
    }
    if (validStudents.length === 1) {
      createThreadMutation.mutate({
        studentId: validStudents[0].studentId,
        body: newMessageBody.trim(),
        notifyStaffIds: newNotifyStaffIds,
      })
    } else {
      setStudentPickerOpen(true)
    }
  }

  function handleStudentPick(studentId: string) {
    setStudentPickerOpen(false)
    if (!newMessageBody.trim()) {
      return
    }
    createThreadMutation.mutate({
      studentId,
      body: newMessageBody.trim(),
      notifyStaffIds: newNotifyStaffIds,
    })
  }

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages.length])

  const selectedThread = threads.find((t) => t.id === selectedThreadId)

  return (
    <div className="flex h-full min-h-0 overflow-hidden" style={{ height: 'calc(100vh - 4rem)' }}>
      {/* Sidebar — hidden on mobile when message panel is open */}
      <div
        className={`w-full lg:w-80 shrink-0 border-r border-gray-200 bg-white flex flex-col ${showMobileMessages ? 'hidden lg:flex' : 'flex'}`}
      >
        {/* Sidebar header */}
        <div className="px-4 pt-4 pb-3 border-b border-gray-100 flex items-center justify-between gap-3">
          <h1 className="font-display text-xl font-semibold text-gray-900">Kontaktbog</h1>
          {threads.length > 0 && (
            <button
              type="button"
              onClick={() => {
                setSelectedThreadId(null)
                setShowMobileMessages(true)
              }}
              className="shrink-0 px-3 py-1.5 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors"
            >
              Ny besked
            </button>
          )}
        </div>

        {threadsLoading && (
          <div className="flex-1 flex items-center justify-center text-sm text-gray-500">
            Indlæser…
          </div>
        )}

        {!threadsLoading && threads.length === 0 && (
          <div className="flex-1 flex flex-col items-center justify-center px-6 text-center gap-4">
            <p className="text-sm text-gray-500">Ingen beskeder endnu</p>
          </div>
        )}

        <div className="flex-1 overflow-y-auto divide-y divide-gray-100">
          {threads.map((thread) => (
            <button
              key={thread.id}
              onClick={() => handleSelectThread(thread.id)}
              className={`w-full text-left px-4 py-3 hover:bg-gray-50 transition-colors ${selectedThreadId === thread.id ? 'bg-brand-50' : ''}`}
            >
              <div className="flex items-center justify-between mb-0.5">
                <span className="font-medium text-sm text-gray-900">{thread.studentName}</span>
                {thread.unreadCount > 0 && (
                  <span className="inline-flex items-center justify-center h-5 min-w-5 px-1.5 rounded-full bg-brand-600 text-white text-xs font-semibold">
                    {thread.unreadCount}
                  </span>
                )}
              </div>
              {thread.lastMessageBody && (
                <p className="text-xs text-gray-500 truncate">
                  {truncate(thread.lastMessageBody, 50)}
                </p>
              )}
              {thread.lastMessageSentAt && (
                <p className="text-xs text-gray-400 mt-0.5">
                  {formatDateTime(thread.lastMessageSentAt)}
                </p>
              )}
            </button>
          ))}
        </div>
      </div>

      {/* Message panel */}
      <div
        className={`flex-1 flex flex-col bg-gray-50 min-w-0 ${showMobileMessages ? 'flex' : 'hidden lg:flex'}`}
      >
        {selectedThread ? (
          <>
            {/* Header */}
            <div className="px-4 py-3 bg-white border-b border-gray-200 flex items-center gap-3">
              <button
                onClick={() => setShowMobileMessages(false)}
                className="lg:hidden p-1 rounded text-gray-500 hover:text-gray-700"
                aria-label="Tilbage"
              >
                <svg
                  width="18"
                  height="18"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                >
                  <polyline points="15 18 9 12 15 6" />
                </svg>
              </button>
              <div>
                <p className="font-semibold text-sm text-gray-900">{selectedThread.studentName}</p>
                <p className="text-xs text-gray-500">Besked til skolen</p>
              </div>
            </div>

            {/* Messages */}
            <div className="flex-1 overflow-y-auto px-4 py-4 space-y-3">
              {messages.map((msg) => {
                const isOwn = msg.senderType === 'Parent'
                return (
                  <div
                    key={msg.id}
                    className={`flex flex-col ${isOwn ? 'items-end' : 'items-start'}`}
                  >
                    <div
                      className={`max-w-sm px-3 py-2 rounded-xl text-sm ${isOwn ? 'bg-blue-100 text-gray-900' : 'bg-white border border-gray-200 text-gray-900'}`}
                    >
                      {!isOwn && (
                        <p className="text-xs font-medium text-gray-600 mb-1">{msg.senderName}</p>
                      )}
                      <p className="whitespace-pre-wrap">{msg.body}</p>
                    </div>
                    <p className="text-xs text-gray-400 mt-1 px-1">{formatDateTime(msg.sentAt)}</p>
                  </div>
                )
              })}
              <div ref={messagesEndRef} />
            </div>

            {/* Input */}
            <div className="px-4 py-3 bg-white border-t border-gray-200 space-y-2">
              <details className="group">
                <summary className="cursor-pointer text-xs font-medium text-gray-500 hover:text-brand-600 select-none list-none flex items-center gap-1">
                  <span>
                    Giv besked til personale
                    {replyNotifyStaffIds.length > 0 ? ` (${replyNotifyStaffIds.length})` : ''}
                  </span>
                </summary>
                <div className="mt-2">
                  <NotifyStaffPicker
                    studentId={selectedThread.studentId}
                    selectedIds={replyNotifyStaffIds}
                    onChange={setReplyNotifyStaffIds}
                  />
                </div>
              </details>
              <div className="flex gap-2 items-end">
                <textarea
                  value={messageBody}
                  onChange={(e) => setMessageBody(e.target.value)}
                  rows={3}
                  placeholder="Skriv en besked…"
                  className="flex-1 px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 resize-none"
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
                      e.preventDefault()
                      handleSend()
                    }
                  }}
                />
                <button
                  onClick={handleSend}
                  disabled={sendMessageMutation.isPending || !messageBody.trim()}
                  className="px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors disabled:opacity-50 shrink-0"
                >
                  {sendMessageMutation.isPending ? 'Sender…' : 'Send'}
                </button>
              </div>
              {sendMessageMutation.isError && (
                <p className="text-xs text-red-600 mt-1">Der opstod en fejl. Prøv igen.</p>
              )}
            </div>
          </>
        ) : (
          <div className="flex-1 flex flex-col items-center justify-center px-6 text-center gap-4">
            <p className="text-sm text-gray-500">
              {threads.length === 0 ? 'Ingen beskeder endnu' : 'Skriv en ny besked'}
            </p>
            <div className="w-full max-w-sm space-y-2">
              <textarea
                value={newMessageBody}
                onChange={(e) => setNewMessageBody(e.target.value)}
                rows={3}
                placeholder="Skriv din første besked til læreren…"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 resize-none"
              />
              {validStudents.length === 1 && (
                <details className="group text-left">
                  <summary className="cursor-pointer text-xs font-medium text-gray-500 hover:text-brand-600 select-none list-none">
                    Giv besked til personale
                    {newNotifyStaffIds.length > 0 ? ` (${newNotifyStaffIds.length})` : ''}
                  </summary>
                  <div className="mt-2">
                    <NotifyStaffPicker
                      studentId={validStudents[0].studentId}
                      selectedIds={newNotifyStaffIds}
                      onChange={setNewNotifyStaffIds}
                    />
                  </div>
                </details>
              )}
              <button
                onClick={handleStartNewThread}
                disabled={createThreadMutation.isPending || !newMessageBody.trim()}
                className="w-full px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors disabled:opacity-50"
              >
                {createThreadMutation.isPending ? 'Sender…' : 'Skriv til læreren'}
              </button>
              {createThreadMutation.isError && (
                <p className="text-xs text-red-600">Der opstod en fejl. Prøv igen.</p>
              )}
            </div>
          </div>
        )}
      </div>

      {/* Student picker modal */}
      <Modal
        isOpen={studentPickerOpen}
        onClose={() => setStudentPickerOpen(false)}
        title="Vælg barn"
        size="sm"
      >
        <div className="px-6 py-5 space-y-4">
          <p className="text-sm text-gray-500">Hvilket barn gælder beskeden?</p>
          <div className="space-y-2">
            {validStudents.map((s) => (
              <button
                type="button"
                key={s.studentId}
                onClick={() => handleStudentPick(s.studentId)}
                className="w-full text-left px-4 py-3 rounded-lg border border-gray-200 hover:border-brand-400 hover:bg-brand-50 transition-colors text-sm font-medium text-gray-900"
              >
                {s.studentName}
              </button>
            ))}
          </div>
          <button
            type="button"
            onClick={() => setStudentPickerOpen(false)}
            className="w-full text-sm text-gray-500 hover:text-gray-700"
          >
            Annuller
          </button>
        </div>
      </Modal>
    </div>
  )
}
