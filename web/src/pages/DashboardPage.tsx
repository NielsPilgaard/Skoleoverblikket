import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import {
  getApiV1StatsDashboardOptions,
  getApiV1SchoolsOnboardingStatusOptions,
} from '../api/generated/@tanstack/react-query.gen'
import type { StaffRole, OnboardingStatusDto } from '../api/client'
import type { StatsControllerDashboardStats } from '../api/generated/types.gen'
import LinksCard from '../components/LinksCard'
import { DataTable } from '../components/ui/list'
import { usePageTitle } from '../hooks/usePageTitle'

function OnboardingCard({ status }: { status: OnboardingStatusDto }) {
  const steps = [
    { label: 'Medarbejdere oprettet', done: (status.staffCount ?? 0) > 0 },
    { label: 'Klasser oprettet', done: (status.classCount ?? 0) > 0 },
    { label: 'Fag oprettet', done: (status.courseCount ?? 0) > 0 },
    { label: 'Lokaler oprettet', done: (status.roomCount ?? 0) > 0 },
  ]

  const stepsCompleted = steps.filter((s) => s.done).length
  const stepsTotal = steps.length
  const progressPercent = stepsTotal > 0 ? Math.round((stepsCompleted / stepsTotal) * 100) : 0

  if (stepsCompleted >= stepsTotal) return null

  return (
    <div className="bg-white rounded-xl border border-brand-200 p-6">
      <div className="flex items-start justify-between gap-4">
        <div className="flex-1 min-w-0">
          <h2 className="text-sm font-semibold text-gray-900">
            Kom godt i gang — {stepsCompleted} af {stepsTotal} trin fuldført
          </h2>
          <div className="mt-2 h-1.5 bg-gray-100 rounded-full overflow-hidden">
            <div
              className="h-full bg-brand-500 rounded-full transition-all"
              style={{ width: `${progressPercent}%` }}
            />
          </div>
          <ul className="mt-3 space-y-1.5">
            {steps.map((s) => (
              <li key={s.label} className="flex items-center gap-2 text-sm">
                {s.done ? (
                  <svg
                    width="14"
                    height="14"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2.5"
                    className="text-green-500 shrink-0"
                  >
                    <polyline points="20 6 9 17 4 12" />
                  </svg>
                ) : (
                  <svg
                    width="14"
                    height="14"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                    className="text-gray-300 shrink-0"
                  >
                    <circle cx="12" cy="12" r="9" />
                  </svg>
                )}
                <span className={s.done ? 'text-gray-400 line-through' : 'text-gray-700'}>
                  {s.label}
                </span>
              </li>
            ))}
          </ul>
        </div>
        <Link
          to="/setup"
          className="shrink-0 px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors whitespace-nowrap"
        >
          Fortsæt opsætning
        </Link>
      </div>
    </div>
  )
}

function AlertTile({
  to,
  label,
  detail,
  testId,
}: {
  to: string
  label: string
  detail: string
  testId: string
}) {
  return (
    <Link
      to={to}
      data-testid={testId}
      className="flex items-start gap-3 bg-amber-50 rounded-xl border border-amber-200 px-5 py-4 hover:bg-amber-100/70 transition-colors"
    >
      <span className="flex items-center justify-center w-8 h-8 rounded-lg bg-amber-100 text-amber-700 shrink-0">
        <svg
          width="16"
          height="16"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
          <line x1="12" y1="9" x2="12" y2="13" />
          <line x1="12" y1="17" x2="12.01" y2="17" />
        </svg>
      </span>
      <span className="min-w-0">
        <span className="block text-sm font-medium text-amber-900">{label}</span>
        <span className="block text-sm text-amber-700">{detail}</span>
      </span>
    </Link>
  )
}

/**
 * Attention alerts — each tile is omitted entirely when its count is zero,
 * so a school with nothing outstanding sees no section at all.
 */
function AlertsSection({ data }: { data: StatsControllerDashboardStats }) {
  const pendingAbsence = data.pendingAbsenceCount ?? 0
  const missingAttendance = data.missingAttendanceCount ?? 0
  const vacationWindow = data.openVacationWindow
  const unreadMessages = data.unreadMessageCount ?? 0
  const unreadKontaktbog = data.unreadKontaktbogCount ?? 0
  const uncoveredLessons = data.uncoveredLessonCount ?? 0

  const hasAny =
    uncoveredLessons > 0 ||
    pendingAbsence > 0 ||
    missingAttendance > 0 ||
    !!vacationWindow ||
    unreadMessages > 0 ||
    unreadKontaktbog > 0

  if (!hasAny) return null

  const deadline = vacationWindow
    ? new Intl.DateTimeFormat('da-DK', {
        day: 'numeric',
        month: 'long',
      }).format(new Date(vacationWindow.registrationDeadline))
    : null

  return (
    <div className="space-y-3" data-testid="dashboard-alerts">
      <h2 className="text-sm font-semibold text-gray-700">Kræver din opmærksomhed</h2>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        {uncoveredLessons > 0 && (
          <AlertTile
            to="/vikardaekning"
            testId="alert-uncovered-lessons"
            label="Lektioner uden vikar"
            detail={`${uncoveredLessons} ${uncoveredLessons === 1 ? 'lektion' : 'lektioner'} de næste 7 dage`}
          />
        )}
        {pendingAbsence > 0 && (
          <AlertTile
            to="/fravaer?fane=fri"
            testId="alert-pending-absence"
            label="Anmodninger om fri"
            detail={`${pendingAbsence} afventer svar`}
          />
        )}
        {missingAttendance > 0 && (
          <AlertTile
            to="/fravaer?fane=fremmoede"
            testId="alert-missing-attendance"
            label="Fremmøde mangler i dag"
            detail={`${missingAttendance} ${missingAttendance === 1 ? 'klasse' : 'klasser'}`}
          />
        )}
        {vacationWindow && (
          <AlertTile
            to={`/ferieindmelding/${vacationWindow.windowId}`}
            testId="alert-vacation-window"
            label={vacationWindow.title}
            detail={`Frist ${deadline} · ${vacationWindow.entryCount} svar`}
          />
        )}
        {unreadMessages > 0 && (
          <AlertTile
            to="/beskeder"
            testId="alert-unread-messages"
            label="Ulæste beskeder"
            detail={`${unreadMessages} ${unreadMessages === 1 ? 'besked' : 'beskeder'}`}
          />
        )}
        {unreadKontaktbog > 0 && (
          <AlertTile
            to="/kontaktbog"
            testId="alert-unread-kontaktbog"
            label="Ulæste i kontaktbogen"
            detail={`${unreadKontaktbog} ${unreadKontaktbog === 1 ? 'besked' : 'beskeder'}`}
          />
        )}
      </div>
    </div>
  )
}

const quickActions = [
  {
    to: '/klasser',
    label: 'Klasser',
    hint: 'Opret skema',
    icon: (
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
        <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
        <circle cx="9" cy="7" r="4" />
        <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
      </svg>
    ),
  },
  {
    to: '/medarbejdere',
    label: 'Medarbejdere',
    hint: 'Tilføj og rediger',
    icon: (
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
        <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" />
        <circle cx="12" cy="7" r="4" />
      </svg>
    ),
  },
  {
    to: '/kalender',
    label: 'Kalender',
    hint: 'Ferier og events',
    icon: (
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
        <rect x="3" y="4" width="18" height="18" rx="2" />
        <line x1="16" y1="2" x2="16" y2="6" />
        <line x1="8" y1="2" x2="8" y2="6" />
        <line x1="3" y1="10" x2="21" y2="10" />
      </svg>
    ),
  },
  {
    to: '/import',
    label: 'Importer data',
    hint: 'Elever og forældre',
    icon: (
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
        <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
        <polyline points="7 10 12 15 17 10" />
        <line x1="12" y1="15" x2="12" y2="3" />
      </svg>
    ),
  },
]

function QuickActionsGrid() {
  return (
    <div className="space-y-3" data-testid="dashboard-quick-actions">
      <h2 className="text-sm font-semibold text-gray-700">Genveje</h2>
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        {quickActions.map((action) => (
          <Link
            key={action.to}
            to={action.to}
            className="flex flex-col gap-2 bg-white rounded-xl border border-gray-200 px-5 py-4 hover:border-brand-300 hover:bg-brand-50/40 transition-colors"
          >
            <span className="flex items-center justify-center w-9 h-9 rounded-lg bg-brand-50 text-brand-600">
              {action.icon}
            </span>
            <span className="text-sm font-medium text-gray-800">{action.label}</span>
            <span className="text-sm text-gray-500">{action.hint}</span>
          </Link>
        ))}
      </div>
    </div>
  )
}

function StatCard({ label, value, sub }: { label: string; value: string | number; sub?: string }) {
  return (
    <div className="bg-white rounded-xl border border-gray-200 px-6 py-5">
      <p className="text-sm text-gray-500 font-medium">{label}</p>
      <p className="mt-1 text-3xl font-semibold text-gray-900 tabular-nums">{value}</p>
      {sub && <p className="mt-1 text-sm text-gray-400">{sub}</p>}
    </div>
  )
}

function roleLabel(role: StaffRole): string {
  if (role === 'Teacher') return 'Lærer'
  if (role === 'Aide') return 'Pædagog'
  return 'Vikar'
}

function SkeletonCard() {
  return (
    <div className="bg-white rounded-xl border border-gray-200 px-6 py-5 animate-pulse">
      <div className="h-4 w-24 bg-gray-200 rounded mb-3" />
      <div className="h-8 w-16 bg-gray-200 rounded" />
    </div>
  )
}

export default function DashboardPage() {
  usePageTitle('Oversigt')
  const { data, isError, refetch } = useQuery({
    ...getApiV1StatsDashboardOptions(),
    staleTime: 0,
  })

  const { data: onboarding } = useQuery({
    ...getApiV1SchoolsOnboardingStatusOptions(),
    retry: false,
    staleTime: 0,
  })

  if (isError) {
    return (
      <div className="p-8">
        <div className="bg-red-50 border border-red-200 rounded-xl p-6 text-center">
          <p className="text-red-700 font-medium">Kunne ikke hente statistik</p>
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

  const classesWithoutSchema = data?.unassignedClasses?.filter((c) => !c.hasSchema).length ?? 0

  return (
    <div className="p-6 lg:p-8 max-w-6xl mx-auto space-y-8" data-testid="admin-dashboard">
      {/* Header */}
      <div>
        <h1 className="font-display text-2xl font-semibold text-gray-900">Oversigt</h1>
        <p className="mt-1 text-sm text-gray-500">Status og nøgletal for din skole</p>
      </div>

      {/* Onboarding progress */}
      {onboarding && <OnboardingCard status={onboarding} />}

      {/* Attention alerts — hidden entirely when nothing needs action */}
      {data && <AlertsSection data={data} />}

      {/* Quick actions — always shown, not data-dependent */}
      <QuickActionsGrid />

      {/* The school's links — hidden until the admin adds one on /links */}
      <LinksCard allTo="/links" />

      {/* Stat cards */}
      <div className="grid grid-cols-2 lg:grid-cols-3 gap-4">
        {!data ? (
          Array.from({ length: 6 }).map((_, i) => <SkeletonCard key={i} />)
        ) : (
          <>
            <StatCard label="Klasser" value={data.classCount ?? 0} />
            <StatCard label="Medarbejdere" value={data.staffCount ?? 0} />
            <StatCard label="Fag" value={data.courseCount ?? 0} />
            <StatCard label="Lokaler" value={data.roomCount ?? 0} />
            <StatCard
              label="Færdige skemaer"
              value={
                data.schemasTotal === 0 ? '–' : `${data.schemasComplete} / ${data.schemasTotal}`
              }
              sub={data.schemasTotal === 0 ? 'Ingen skemaer oprettet' : 'af de oprettede skemaer'}
            />
            <StatCard
              label="Klasser u. skema"
              value={classesWithoutSchema}
              sub={classesWithoutSchema === 0 ? 'Alle klasser har et skema' : 'mangler skema'}
            />
          </>
        )}
      </div>

      {/* Tables */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <section className="space-y-2">
          <h2 className="text-sm font-semibold text-gray-700">Timer pr. medarbejder</h2>
          <DataTable
            items={data?.hoursPerStaff ?? undefined}
            isLoading={!data}
            caption="Timer pr. medarbejder"
            rowKey={(s) => s.staffId ?? s.staffName ?? ''}
            noun={{ one: 'medarbejder', other: 'medarbejdere' }}
            empty={<p>Ingen data endnu</p>}
            columns={[
              {
                key: 'name',
                header: 'Navn',
                cell: (s) => s.staffName,
                sortValue: (s) => s.staffName ?? '',
              },
              { key: 'role', header: 'Rolle', cell: (s) => (s.role ? roleLabel(s.role) : '–') },
              {
                key: 'hours',
                header: 'Timer',
                align: 'right',
                cell: (s) => <span className="tabular-nums">{s.hours}</span>,
                sortValue: (s) => s.hours ?? 0,
              },
            ]}
          />
        </section>

        <section className="space-y-2">
          <h2 className="text-sm font-semibold text-gray-700">Klasser med mangler</h2>
          <DataTable
            items={data?.unassignedClasses ?? undefined}
            isLoading={!data}
            caption="Klasser med mangler"
            rowKey={(c) => c.classId ?? c.className ?? ''}
            noun={{ one: 'klasse', other: 'klasser' }}
            empty={
              (data?.classCount ?? 0) > 0 ? (
                <p className="text-brand-800 font-semibold">Alle klasser har et færdigt skema</p>
              ) : (
                <p>Ingen klasser oprettet endnu</p>
              )
            }
            columns={[
              {
                key: 'name',
                header: 'Klasse',
                cell: (c) => c.className,
                sortValue: (c) => c.className ?? '',
              },
              {
                key: 'status',
                header: 'Status',
                align: 'right',
                cell: (c) =>
                  c.hasSchema ? (
                    <span className="tabular-nums">
                      {c.emptySlots === 1 ? '1 tom lektion' : `${c.emptySlots} tomme lektioner`}
                    </span>
                  ) : (
                    <Link
                      to={`/klasser?classId=${c.classId}&action=new-schema`}
                      className="inline-flex items-center min-h-[44px] font-semibold text-brand-700 underline"
                    >
                      Opret skema
                    </Link>
                  ),
              },
            ]}
          />
        </section>
      </div>
    </div>
  )
}
