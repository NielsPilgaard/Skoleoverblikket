import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  getApiV1Notifications,
  postApiV1NotificationsByIdRead,
  postApiV1NotificationsReadAll,
} from '../api/generated/sdk.gen'
import type { NotificationType } from '../api/client'
import { useAuth } from '../auth/useAuth'

interface NotificationItem {
  id: string
  type: NotificationType
  body: string
  createdAt: string
  readAt: string | null
  referenceId: string | null
}

function typeIcon(type: NotificationType): string {
  switch (type) {
    case 'LeaveApproved':
      return '✅'
    case 'LeaveRejected':
      return '❌'
    case 'UnauthorizedAbsence':
    case 'AbsenceThreshold':
      return '⚠️'
    case 'LeaveRequested':
    case 'AbsenceRetentionWarning':
      return '🗂️'
    case 'StaffAbsenceReported':
    case 'SubstituteAssigned':
      return '🧑‍🏫'
    case 'NewMessage':
      return '💬'
    case 'NewContactMessage':
      return '📖'
    case 'WeekPlanChanged':
      return '📅'
    default:
      return '🔔'
  }
}

function relativeTime(dateStr: string): string {
  const now = Date.now()
  const then = new Date(dateStr).getTime()
  const diff = Math.floor((now - then) / 1000)
  if (diff < 60) return 'Lige nu'
  if (diff < 3600) return `${Math.floor(diff / 60)} min. siden`
  if (diff < 86400) return `${Math.floor(diff / 3600)} t. siden`
  return `${Math.floor(diff / 86400)} dage siden`
}

interface NotificationBellProps {
  variant?: 'light' | 'dark'
}

export default function NotificationBell({ variant = 'light' }: NotificationBellProps) {
  const [notifications, setNotifications] = useState<NotificationItem[]>([])
  const [open, setOpen] = useState(false)
  const dropdownRef = useRef<HTMLDivElement>(null)
  const navigate = useNavigate()
  const { isAdmin, isParent } = useAuth()

  const fetchNotifications = useCallback(async () => {
    try {
      const { data } = await getApiV1Notifications({ throwOnError: false })
      if (data) {
        setNotifications(data as NotificationItem[])
      }
    } catch {
      // silently ignore fetch errors — bell is non-critical
    }
  }, [])

  useEffect(() => {
    void fetchNotifications()
    const interval = setInterval(() => void fetchNotifications(), 60_000)
    return () => clearInterval(interval)
  }, [fetchNotifications])

  // Close dropdown when clicking outside
  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node)) {
        setOpen(false)
      }
    }
    if (open) {
      document.addEventListener('mousedown', handleClickOutside)
    }
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [open])

  const unreadCount = notifications.filter((n) => n.readAt === null).length
  const displayed = notifications.slice(0, 10)

  async function markRead(id: string) {
    try {
      await postApiV1NotificationsByIdRead({ path: { id }, throwOnError: false })
      setNotifications((prev) =>
        prev.map((n) => (n.id === id ? { ...n, readAt: new Date().toISOString() } : n))
      )
    } catch {
      // ignore
    }
  }

  function routeFor(n: NotificationItem): string {
    switch (n.type) {
      case 'NewContactMessage':
        return isParent
          ? `/foraeldrevisning/kontaktbog?threadId=${n.referenceId ?? ''}`
          : `/kontaktbog?threadId=${n.referenceId ?? ''}`
      case 'NewMessage':
      case 'GroupMessage':
        return '/beskeder'
      case 'WeekPlanChanged':
        return isParent ? '/foraeldrevisning/ugeplan' : '/mig/skema'
      case 'LeaveApproved':
      case 'LeaveRejected':
      case 'UnauthorizedAbsence':
        return isParent ? '/foraeldrevisning/fravaer' : '/fravaer?fane=register'
      case 'LeaveRequested':
        return '/fravaer?fane=fri'
      case 'AbsenceThreshold':
        return '/fravaer?fane=statistik'
      case 'AbsenceRetentionWarning':
        return '/fravaer'
      case 'StaffAbsenceReported':
        return n.referenceId ? `/vikardaekning/${n.referenceId}` : '/vikardaekning'
      case 'SubstituteAssigned':
        return isAdmin ? '/vikardaekning' : '/mig/fravaer'
      case 'VacationRegistrationOpened':
        return '/foraeldrevisning/ferieindmelding'
      default:
        if (isAdmin) return '/dashboard'
        return isParent ? '/foraeldrevisning/skema' : '/mig/skema'
    }
  }

  function handleNotificationClick(n: NotificationItem) {
    void markRead(n.id)
    setOpen(false)
    navigate(routeFor(n))
  }

  async function markAllRead() {
    try {
      await postApiV1NotificationsReadAll({ throwOnError: false })
      const now = new Date().toISOString()
      setNotifications((prev) => prev.map((n) => ({ ...n, readAt: n.readAt ?? now })))
    } catch {
      // ignore
    }
  }

  return (
    <div className="relative" ref={dropdownRef}>
      <button
        onClick={() => setOpen((o) => !o)}
        className={`relative p-1.5 rounded transition-colors ${
          variant === 'dark'
            ? 'text-brand-100 hover:text-white hover:bg-brand-800'
            : 'text-gray-500 hover:text-gray-700 hover:bg-gray-100'
        }`}
        aria-label="Notifikationer"
        data-testid="notification-bell"
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
          <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9" />
          <path d="M13.73 21a2 2 0 0 1-3.46 0" />
        </svg>
        {unreadCount > 0 && (
          <span className="absolute -top-0.5 -right-0.5 flex items-center justify-center h-4 min-w-4 px-1 rounded-full bg-red-500 text-white text-[10px] font-bold leading-none">
            {unreadCount > 99 ? '99+' : unreadCount}
          </span>
        )}
      </button>

      {open && (
        // The dark bell sits in the sidebar (opens rightwards); the light one sits at the right
        // edge of the mobile top bar, so it must open leftwards to stay on screen.
        <div
          className={`absolute ${variant === 'dark' ? 'left-0' : 'right-0'} top-full mt-2 w-80 max-w-[calc(100vw-2rem)] bg-white rounded-lg shadow-lg border border-gray-200 z-50 flex flex-col max-h-[min(70vh,28rem)]`}
        >
          {/* Header */}
          <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 shrink-0">
            <h3 className="text-sm font-semibold text-gray-800">Notifikationer</h3>
            {unreadCount > 0 && (
              <button
                onClick={() => void markAllRead()}
                className="text-xs text-brand-600 hover:text-brand-800 font-medium transition-colors"
                data-testid="mark-all-read"
              >
                Marker alle som læst
              </button>
            )}
          </div>

          {/* List */}
          <div className="overflow-y-auto flex-1">
            {displayed.length === 0 ? (
              <p className="px-4 py-6 text-sm text-gray-500 text-center">Ingen notifikationer</p>
            ) : (
              displayed.map((n) => (
                <button
                  key={n.id}
                  onClick={() => handleNotificationClick(n)}
                  className={`w-full text-left px-4 py-3 border-b border-gray-50 hover:bg-gray-50 transition-colors flex gap-3 items-start ${
                    n.readAt === null ? 'bg-blue-50/60' : ''
                  }`}
                  data-testid="notification-item"
                >
                  <span className="text-lg shrink-0 mt-0.5">{typeIcon(n.type)}</span>
                  <div className="flex-1 min-w-0">
                    <p
                      className={`text-sm leading-snug break-words ${n.readAt === null ? 'font-medium text-gray-900' : 'text-gray-700'}`}
                    >
                      {n.body}
                    </p>
                    <p className="text-xs text-gray-400 mt-1">{relativeTime(n.createdAt)}</p>
                  </div>
                  {n.readAt === null && (
                    <span className="h-2 w-2 rounded-full bg-blue-500 shrink-0 mt-1.5" />
                  )}
                </button>
              ))
            )}
          </div>
        </div>
      )}
    </div>
  )
}
