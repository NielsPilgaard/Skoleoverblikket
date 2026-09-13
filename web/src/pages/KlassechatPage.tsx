import { useState, useEffect, useRef } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { usePageTitle } from '../hooks/usePageTitle'
import { linkify } from '../utils/linkify'
import {
  postApiV1ClassChatsByClassIdMessages,
  deleteApiV1ClassChatsByClassIdMessagesByMessageId,
  postApiV1ClassChatsByClassIdAttachmentsPresign,
  postApiV1ClassChatsByClassIdAttachmentsConfirm,
} from '../api/generated/sdk.gen'
import {
  getApiV1ClassChatsOptions,
  getApiV1ClassChatsQueryKey,
  getApiV1ClassChatsByClassIdMessagesOptions,
  getApiV1ClassChatsByClassIdMessagesQueryKey,
} from '../api/generated/@tanstack/react-query.gen'
import type { ClassChatControllerClassChatMessageDto } from '../api/generated/types.gen'

/** Mirrors ClassChatAttachment.MaxFileSizeBytes on the API. */
const MAX_ATTACHMENT_BYTES = 25 * 1024 * 1024
/** Mirrors ClassChatMessage.MaxBodyLength on the API. */
const MAX_BODY_LENGTH = 4000
/** Mirrors ClassChatAttachment.MaxPerMessage on the API. */
const MAX_ATTACHMENTS = 10

/** An attachment picked in the composer, uploaded but not yet posted. */
interface PendingAttachment {
  id: string
  fileName: string
  sizeBytes: number
}

function formatDateTime(iso: string): string {
  const d = new Date(iso)
  const hh = d.getHours().toString().padStart(2, '0')
  const mm = d.getMinutes().toString().padStart(2, '0')
  const dd = d.getDate().toString().padStart(2, '0')
  const mo = (d.getMonth() + 1).toString().padStart(2, '0')
  return `${hh}:${mm} ${dd}/${mo}`
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`
  }
  if (bytes < 1024 * 1024) {
    return `${Math.round(bytes / 1024)} KB`
  }
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

function truncate(text: string, max: number): string {
  return text.length <= max ? text : `${text.slice(0, max)}…`
}

function initialsOf(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('')
}

function isImage(contentType: string): boolean {
  return contentType.startsWith('image/')
}

export default function KlassechatPage() {
  usePageTitle('Klassechat')
  const qc = useQueryClient()
  const [selectedClassId, setSelectedClassId] = useState<string | null>(null)
  const [showMobileMessages, setShowMobileMessages] = useState(false)
  const [messageBody, setMessageBody] = useState('')
  const [pending, setPending] = useState<PendingAttachment[]>([])
  const [uploading, setUploading] = useState(false)
  const [uploadError, setUploadError] = useState<string | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const messagesEndRef = useRef<HTMLDivElement>(null)

  const { data: threads = [], isLoading: threadsLoading } = useQuery({
    ...getApiV1ClassChatsOptions(),
    select: (d) => d ?? [],
    // No websocket infrastructure — the thread list refreshes on an interval instead.
    refetchInterval: 60_000,
  })

  const messagesOptions = getApiV1ClassChatsByClassIdMessagesOptions({
    path: { classId: selectedClassId! },
    query: { pageSize: 100 },
  })

  const { data: messagesData, isLoading: messagesLoading } = useQuery({
    ...messagesOptions,
    enabled: !!selectedClassId,
    refetchInterval: 30_000,
  })

  const messages: ClassChatControllerClassChatMessageDto[] = messagesData?.items ?? []

  function invalidateThread() {
    if (selectedClassId) {
      qc.invalidateQueries({
        queryKey: getApiV1ClassChatsByClassIdMessagesQueryKey({
          path: { classId: selectedClassId },
          query: { pageSize: 100 },
        }),
      })
    }
    qc.invalidateQueries({ queryKey: getApiV1ClassChatsQueryKey() })
  }

  const sendMutation = useMutation({
    mutationFn: async ({
      classId,
      body,
      attachmentIds,
    }: {
      classId: string
      body: string
      attachmentIds: string[]
    }) => {
      const { data, error } = await postApiV1ClassChatsByClassIdMessages({
        path: { classId },
        body: { body, attachmentIds },
        throwOnError: false,
      })
      if (error) {
        throw new Error('send_failed')
      }
      return data
    },
    onSuccess: () => {
      setMessageBody('')
      setPending([])
      invalidateThread()
    },
  })

  const deleteMutation = useMutation({
    mutationFn: async ({ classId, messageId }: { classId: string; messageId: string }) => {
      const { error } = await deleteApiV1ClassChatsByClassIdMessagesByMessageId({
        path: { classId, messageId },
        throwOnError: false,
      })
      if (error) {
        throw new Error('delete_failed')
      }
    },
    onSuccess: invalidateThread,
  })

  /** Presign → PUT to object storage → confirm, mirroring the file archive upload flow. */
  async function uploadAttachment(file: File) {
    if (!selectedClassId) {
      return
    }

    if (file.size > MAX_ATTACHMENT_BYTES) {
      setUploadError(`"${file.name}" er større end 25 MB.`)
      return
    }

    setUploading(true)
    setUploadError(null)

    try {
      const { data: presign, error: presignError } =
        await postApiV1ClassChatsByClassIdAttachmentsPresign({
          path: { classId: selectedClassId },
          body: { fileName: file.name, fileSizeBytes: file.size },
          throwOnError: false,
        })

      if (presignError || !presign) {
        setUploadError(`"${file.name}" kunne ikke vedhæftes. Filtypen er måske ikke understøttet.`)
        return
      }

      const putResponse = await fetch(presign.uploadUrl!, {
        method: 'PUT',
        body: file,
        headers: { 'Content-Type': presign.contentType! },
      })

      if (!putResponse.ok) {
        setUploadError(`"${file.name}" kunne ikke uploades. Prøv igen.`)
        return
      }

      const { data: confirmed, error: confirmError } =
        await postApiV1ClassChatsByClassIdAttachmentsConfirm({
          path: { classId: selectedClassId },
          body: { confirmToken: presign.confirmToken! },
          throwOnError: false,
        })

      if (confirmError || !confirmed) {
        setUploadError(`"${file.name}" kunne ikke gemmes. Prøv igen.`)
        return
      }

      setPending((prev) => [
        ...prev,
        { id: confirmed.id!, fileName: confirmed.fileName!, sizeBytes: confirmed.sizeBytes ?? 0 },
      ])
    } catch {
      setUploadError(`"${file.name}" kunne ikke uploades. Prøv igen.`)
    } finally {
      setUploading(false)
    }
  }

  async function handleFilesPicked(files: FileList | null) {
    if (!files) {
      return
    }
    const picked = Array.from(files)
    const room = Math.max(0, MAX_ATTACHMENTS - pending.length)
    for (const file of picked.slice(0, room)) {
      await uploadAttachment(file)
    }
    if (picked.length > room) {
      setUploadError(`Der kan højst vedhæftes ${MAX_ATTACHMENTS} filer.`)
    }
    if (fileInputRef.current) {
      fileInputRef.current.value = ''
    }
  }

  function handleSend() {
    const body = messageBody.trim()
    if (!selectedClassId || !body || sendMutation.isPending || uploading) {
      return
    }
    sendMutation.mutate({ classId: selectedClassId, body, attachmentIds: pending.map((a) => a.id) })
  }

  function handleSelectThread(classId: string) {
    setSelectedClassId(classId)
    setShowMobileMessages(true)
    setMessageBody('')
    setPending([])
    setUploadError(null)
  }

  function handleDelete(messageId: string) {
    if (!selectedClassId) {
      return
    }
    if (!window.confirm('Slet denne besked? Det kan ikke fortrydes.')) {
      return
    }
    deleteMutation.mutate({ classId: selectedClassId, messageId })
  }

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages.length, selectedClassId])

  const selectedThread = threads.find((t) => t.classId === selectedClassId)

  return (
    <div className="flex h-[calc(100vh-4rem)] min-h-0 overflow-hidden">
      {/* Thread list — hidden on mobile while the conversation is open */}
      <div
        className={`w-full lg:w-80 shrink-0 border-r border-gray-200 bg-white flex-col ${showMobileMessages ? 'hidden lg:flex' : 'flex'}`}
        data-testid="klassechat-thread-list"
      >
        <div className="px-4 py-4 border-b border-gray-100">
          <h1 className="font-display text-xl font-semibold text-gray-900">Klassechat</h1>
          <p className="mt-1 text-xs text-gray-500">Fælles beskeder for klassen</p>
        </div>

        {threadsLoading && (
          <div className="flex-1 flex items-center justify-center text-sm text-gray-500">
            Indlæser…
          </div>
        )}

        {!threadsLoading && threads.length === 0 && (
          <div className="flex-1 flex items-center justify-center px-4">
            <p className="text-sm text-gray-500 text-center">
              Du har ikke adgang til nogen klassechat endnu
            </p>
          </div>
        )}

        <div className="flex-1 overflow-y-auto divide-y divide-gray-100">
          {threads.map((thread) => (
            <button
              key={thread.classId}
              onClick={() => handleSelectThread(thread.classId)}
              data-testid="klassechat-thread-row"
              className={`w-full text-left px-4 py-3 hover:bg-gray-50 transition-colors ${selectedClassId === thread.classId ? 'bg-brand-50' : ''}`}
            >
              <div className="flex items-center justify-between mb-0.5">
                <span className="font-medium text-sm text-gray-900">{thread.className}</span>
                {thread.lastMessageSentAt && (
                  <span className="text-xs text-gray-400">
                    {formatDateTime(thread.lastMessageSentAt)}
                  </span>
                )}
              </div>
              {thread.lastMessageBody ? (
                <p className="text-xs text-gray-500 truncate">
                  {thread.lastMessageSenderName ? `${thread.lastMessageSenderName}: ` : ''}
                  {truncate(thread.lastMessageBody, 50)}
                </p>
              ) : (
                <p className="text-xs text-gray-400">Ingen beskeder endnu</p>
              )}
            </button>
          ))}
        </div>
      </div>

      {/* Conversation */}
      <div
        className={`flex-1 flex-col bg-gray-50 min-w-0 ${showMobileMessages ? 'flex' : 'hidden lg:flex'}`}
        data-testid="klassechat-conversation"
      >
        {selectedThread ? (
          <>
            <div className="px-4 py-3 bg-white border-b border-gray-200 flex items-center gap-3">
              <button
                onClick={() => setShowMobileMessages(false)}
                className="lg:hidden p-1 rounded text-gray-500 hover:text-gray-700"
                aria-label="Tilbage"
              >
                <svg
                  width="20"
                  height="20"
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
              <div className="min-w-0">
                <h2 className="font-medium text-gray-900 truncate">{selectedThread.className}</h2>
                <p className="text-xs text-gray-500">
                  Alle forældre og lærere i klassen kan se beskederne her
                </p>
              </div>
            </div>

            <div className="flex-1 overflow-y-auto px-4 py-4 space-y-4">
              {messagesLoading && <p className="text-sm text-gray-500 text-center">Indlæser…</p>}

              {!messagesLoading && messages.length === 0 && (
                <p className="text-sm text-gray-500 text-center">
                  Ingen beskeder endnu. Skriv den første herunder.
                </p>
              )}

              {messages.map((message) => (
                <div key={message.id} className="flex gap-3" data-testid="klassechat-message">
                  {message.senderAvatarUrl ? (
                    <img
                      src={message.senderAvatarUrl}
                      alt=""
                      className="h-9 w-9 rounded-full object-cover shrink-0"
                    />
                  ) : (
                    <div className="h-9 w-9 rounded-full bg-brand-100 text-brand-700 text-xs font-semibold flex items-center justify-center shrink-0">
                      {initialsOf(message.senderName)}
                    </div>
                  )}

                  <div className="min-w-0 flex-1">
                    <div className="flex items-baseline gap-2">
                      <span className="text-sm font-medium text-gray-900">
                        {message.senderName}
                      </span>
                      {message.senderType === 'Staff' && (
                        <span className="text-xs px-1.5 py-0.5 rounded bg-brand-50 text-brand-700">
                          Skolen
                        </span>
                      )}
                      <span className="text-xs text-gray-400">
                        {formatDateTime(message.sentAt)}
                      </span>
                      {message.canDelete && (
                        <button
                          onClick={() => handleDelete(message.id)}
                          data-testid="klassechat-delete-message"
                          className="ml-auto text-xs text-gray-400 hover:text-red-600 transition-colors"
                        >
                          Slet
                        </button>
                      )}
                    </div>

                    <p className="mt-1 text-sm text-gray-800 whitespace-pre-wrap break-words">
                      {linkify(message.body)}
                    </p>

                    {message.attachments && message.attachments.length > 0 && (
                      <div className="mt-2 space-y-2">
                        {message.attachments.map((attachment) => (
                          <div key={attachment.id}>
                            {isImage(attachment.contentType) ? (
                              <a href={attachment.url} target="_blank" rel="noopener noreferrer">
                                <img
                                  src={attachment.url}
                                  alt={attachment.fileName}
                                  className="max-h-64 rounded-lg border border-gray-200"
                                />
                              </a>
                            ) : (
                              <a
                                href={attachment.url}
                                target="_blank"
                                rel="noopener noreferrer"
                                className="inline-flex items-center gap-2 px-3 py-2 bg-white border border-gray-200 rounded-lg text-sm text-gray-700 hover:border-brand-300 transition-colors"
                              >
                                <svg
                                  width="16"
                                  height="16"
                                  viewBox="0 0 24 24"
                                  fill="none"
                                  stroke="currentColor"
                                  strokeWidth="2"
                                  strokeLinecap="round"
                                  strokeLinejoin="round"
                                  className="shrink-0"
                                >
                                  <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                                  <polyline points="14 2 14 8 20 8" />
                                </svg>
                                <span className="truncate max-w-[16rem]">
                                  {attachment.fileName}
                                </span>
                                <span className="text-xs text-gray-400">
                                  {formatFileSize(attachment.sizeBytes)}
                                </span>
                              </a>
                            )}
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                </div>
              ))}
              <div ref={messagesEndRef} />
            </div>

            {/* Composer */}
            <div className="px-4 py-3 bg-white border-t border-gray-200 space-y-2">
              {pending.length > 0 && (
                <div className="flex flex-wrap gap-2">
                  {pending.map((attachment) => (
                    <span
                      key={attachment.id}
                      className="inline-flex items-center gap-2 px-2 py-1 bg-gray-100 rounded text-xs text-gray-700"
                    >
                      <span className="truncate max-w-[12rem]">{attachment.fileName}</span>
                      <span className="text-gray-400">{formatFileSize(attachment.sizeBytes)}</span>
                      <button
                        onClick={() =>
                          setPending((prev) => prev.filter((a) => a.id !== attachment.id))
                        }
                        className="text-gray-400 hover:text-red-600"
                        aria-label={`Fjern ${attachment.fileName}`}
                      >
                        ×
                      </button>
                    </span>
                  ))}
                </div>
              )}

              {uploadError && <p className="text-xs text-red-600">{uploadError}</p>}
              {sendMutation.isError && (
                <p className="text-xs text-red-600">Beskeden kunne ikke sendes. Prøv igen.</p>
              )}

              <div className="flex gap-2 items-end">
                <input
                  ref={fileInputRef}
                  type="file"
                  multiple
                  className="hidden"
                  data-testid="klassechat-file-input"
                  accept="image/*,.pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.csv,.odt,.ods,.odp,.rtf"
                  onChange={(e) => handleFilesPicked(e.target.files)}
                />
                <button
                  onClick={() => fileInputRef.current?.click()}
                  disabled={uploading || pending.length >= MAX_ATTACHMENTS}
                  data-testid="klassechat-attach"
                  className="p-2 rounded-lg text-gray-500 hover:text-gray-700 hover:bg-gray-100 transition-colors disabled:opacity-50"
                  aria-label="Vedhæft fil"
                >
                  <svg
                    width="20"
                    height="20"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  >
                    <path d="M21.44 11.05l-9.19 9.19a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.83-2.83l8.49-8.48" />
                  </svg>
                </button>

                <textarea
                  value={messageBody}
                  onChange={(e) => setMessageBody(e.target.value.slice(0, MAX_BODY_LENGTH))}
                  rows={2}
                  placeholder="Skriv en besked til klassen…"
                  data-testid="klassechat-composer"
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
                  disabled={sendMutation.isPending || uploading || !messageBody.trim()}
                  data-testid="klassechat-send"
                  className="px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors disabled:opacity-50"
                >
                  {uploading ? 'Uploader…' : sendMutation.isPending ? 'Sender…' : 'Send'}
                </button>
              </div>
            </div>
          </>
        ) : (
          <div className="flex-1 flex items-center justify-center px-4">
            <p className="text-sm text-gray-500 text-center">Vælg en klasse for at se beskederne</p>
          </div>
        )}
      </div>
    </div>
  )
}
