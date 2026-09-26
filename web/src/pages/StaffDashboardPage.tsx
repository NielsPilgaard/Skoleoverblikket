import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import {
  getApiV1StatsMyDashboardOptions,
  getApiV1StaffMeOptions,
} from '../api/generated/@tanstack/react-query.gen'
import type { StatsControllerTodayLektion } from '../api/generated/types.gen'
import { usePageTitle } from '../hooks/usePageTitle'

/** "08:00:00" → "08:00" — the API sends TimeOnly with seconds. */
function formatTime(time: string): string {
  return time.slice(0, 5)
}

function UnreadTile({
  to,
  label,
  count,
  icon,
}: {
  to: string
  label: string
  count: number
  icon: React.ReactNode
}) {
  return (
    <Link
      to={to}
      data-testid={`staff-unread-${label.toLowerCase()}`}
      className="flex items-center gap-3 bg-white rounded-xl border border-gray-200 px-5 py-4 hover:border-brand-300 hover:bg-brand-50/40 transition-colors"
    >
      <span className="flex items-center justify-center w-9 h-9 rounded-lg bg-brand-50 text-brand-600 shrink-0">
        {icon}
      </span>
      <span className="min-w-0">
        <span className="block text-sm font-medium text-gray-800">{label}</span>
        <span className="block text-sm text-gray-500">
          {count} {count === 1 ? 'ulæst' : 'ulæste'}
        </span>
      </span>
    </Link>
  )
}

function ScheduleRow({ lektion }: { lektion: StatsControllerTodayLektion }) {
  return (
    <li className="flex items-baseline gap-4 px-5 py-3">
      <span className="w-24 shrink-0 text-sm tabular-nums text-gray-500">
        {formatTime(lektion.startTime)}–{formatTime(lektion.endTime)}
      </span>
      <span className="min-w-0 flex-1">
        <span className="block text-sm font-medium text-gray-800">{lektion.courseName}</span>
        <span className="block text-sm text-gray-500">
          {lektion.className}
          {lektion.roomName ? ` · ${lektion.roomName}` : ''}
        </span>
      </span>
    </li>
  )
}

function SkeletonList() {
  return (
    <div className="bg-white rounded-xl border border-gray-200 overflow-hidden animate-pulse">
      <div className="px-5 py-4 border-b border-gray-100">
        <div className="h-5 w-40 bg-gray-200 rounded" />
      </div>
      <div className="divide-y divide-gray-100">
        {Array.from({ length: 4 }).map((_, i) => (
          <div key={i} className="px-5 py-3 flex gap-4">
            <div className="h-4 w-20 bg-gray-100 rounded" />
            <div className="h-4 flex-1 bg-gray-100 rounded" />
          </div>
        ))}
      </div>
    </div>
  )
}

export default function StaffDashboardPage() {
  usePageTitle('Oversigt')

  const { data: me } = useQuery({ ...getApiV1StaffMeOptions(), retry: false })
  const { data, isLoading, isError, refetch } = useQuery({
    ...getApiV1StatsMyDashboardOptions(),
    staleTime: 0,
  })

  const todaySchedule = data?.todaySchedule ?? []
  const unreadMessages = data?.unreadMessageCount ?? 0
  const unreadKontaktbog = data?.unreadKontaktbogCount ?? 0

  const today = new Intl.DateTimeFormat('da-DK', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
  }).format(new Date())

  if (isError) {
    return (
      <div className="p-8">
        <div className="bg-red-50 border border-red-200 rounded-xl p-6 text-center">
          <p className="text-red-700 font-medium">Kunne ikke hente din oversigt</p>
          <button
            onClick={() => refetch()}
            className="mt-3 px-4 py-2 text-sm bg-red-100 text-red-700 rounded-lg hover:bg-red-200 transition-colors"
          >
            Prøv igen
          </button>
        </div>
      </div>
    )
  }

  return (
    <div className="p-6 lg:p-8 max-w-4xl mx-auto space-y-8" data-testid="staff-dashboard">
      <div>
        <h1 className="font-display text-2xl font-semibold text-gray-900">
          {me?.name ? `Hej ${me.name.split(' ')[0]}` : 'Oversigt'}
        </h1>
        <p className="mt-1 text-sm text-gray-500 first-letter:uppercase">{today}</p>
      </div>

      {/* Unread counts — omitted entirely at zero or when the parent module is inactive */}
      {(unreadMessages > 0 || unreadKontaktbog > 0) && (
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          {unreadMessages > 0 && (
            <UnreadTile
              to="/beskeder"
              label="Beskeder"
              count={unreadMessages}
              icon={
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
                  <path d="M4 4h16v12H5.17L4 17.17V4z" />
                </svg>
              }
            />
          )}
          {unreadKontaktbog > 0 && (
            <UnreadTile
              to="/kontaktbog"
              label="Kontaktbog"
              count={unreadKontaktbog}
              icon={
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
                  <path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20" />
                  <path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z" />
                </svg>
              }
            />
          )}
        </div>
      )}

      {/* Today's schedule */}
      {isLoading ? (
        <SkeletonList />
      ) : (
        <div
          className="bg-white rounded-xl border border-gray-200 overflow-hidden"
          data-testid="staff-today-schedule"
        >
          <div className="px-5 py-4 border-b border-gray-100 flex items-center justify-between gap-4">
            <h2 className="text-sm font-semibold text-gray-700">Dagens skema</h2>
            <Link
              to="/mig/skema"
              className="text-sm text-brand-600 hover:text-brand-700 font-medium whitespace-nowrap"
            >
              Hele ugen →
            </Link>
          </div>
          {todaySchedule.length === 0 ? (
            <p className="px-5 py-8 text-sm text-gray-400 text-center">
              Du har ingen lektioner i dag
            </p>
          ) : (
            <ul className="divide-y divide-gray-50">
              {todaySchedule.map((lektion) => (
                <ScheduleRow key={lektion.slotId} lektion={lektion} />
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  )
}
