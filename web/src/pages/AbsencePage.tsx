import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { usePageTitle } from '../hooks/usePageTitle'
import { useAuth } from '../auth/useAuth'
import { getApiV1AbsenceLeaveRequestsOptions } from '../api/generated/@tanstack/react-query.gen'
import { getApiV1AbsenceExport } from '../api/generated/sdk.gen'
import { AttendanceTab } from '../components/absence/AttendanceTab'
import { AbsenceRegisterTab } from '../components/absence/AbsenceRegisterTab'
import { LeaveRequestsTab } from '../components/absence/LeaveRequestsTab'
import { AbsenceStatsTab } from '../components/absence/AbsenceStatsTab'
import { saveBlob, schoolYearLabel, schoolYearStart, todayIso } from '../lib/absence'

type Tab = 'fremmoede' | 'register' | 'fri' | 'statistik'

/** Retention rule, the next deletion date, and (for admins) the school-year Excel download. */
function RetentionNote({ isAdmin }: { isAdmin: boolean }) {
  const current = schoolYearStart(todayIso())
  const [year, setYear] = useState(current)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function download() {
    setError(null)
    setBusy(true)
    try {
      const { data } = await getApiV1AbsenceExport({
        query: { schoolYear: year },
        parseAs: 'blob',
        throwOnError: true,
      })
      saveBlob(data as Blob, `fravaer-${schoolYearLabel(year).replace('/', '-')}.xlsx`)
    } catch {
      setError('Filen kunne ikke hentes. Prøv igen om lidt.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div
      className="mt-10 flex flex-wrap items-center gap-3 border-t border-gray-200 pt-4 text-xs text-gray-500"
      data-testid="absence-retention-note"
    >
      <p className="flex-1 min-w-60">
        Fraværsdata gemmes i indeværende og forrige skoleår. Skoleåret{' '}
        {schoolYearLabel(current - 1)} slettes automatisk 1. august {current + 1}.
      </p>
      {isAdmin && (
        <div className="flex items-center gap-2">
          <select
            value={year}
            onChange={(e) => setYear(Number(e.target.value))}
            className="px-2 py-1 border border-gray-300 rounded-lg text-xs text-gray-700"
            aria-label="Skoleår"
          >
            {[current, current - 1].map((y) => (
              <option key={y} value={y}>
                {schoolYearLabel(y)}
              </option>
            ))}
          </select>
          <button
            type="button"
            onClick={download}
            disabled={busy}
            data-testid="absence-download-school-year"
            className="px-3 py-1.5 text-xs font-medium border border-gray-300 text-gray-700 rounded-lg hover:bg-gray-50 disabled:opacity-50"
          >
            {busy ? 'Henter…' : 'Download skoleår'}
          </button>
        </div>
      )}
      {error && <p className="w-full text-red-600">{error}</p>}
    </div>
  )
}

export default function AbsencePage() {
  usePageTitle('Fravær')
  const { isAdmin } = useAuth()
  const [searchParams, setSearchParams] = useSearchParams()
  const requested = searchParams.get('fane') as Tab | null
  const tab: Tab = requested === 'fri' && !isAdmin ? 'fremmoede' : (requested ?? 'fremmoede')

  const { data: leaveRequests = [] } = useQuery({
    ...getApiV1AbsenceLeaveRequestsOptions(),
    enabled: isAdmin,
  })
  const pendingLeave = leaveRequests.filter((r) => r.leaveStatus === 'Pending').length

  const tabs: { key: Tab; label: string; badge?: number }[] = [
    { key: 'fremmoede', label: 'Fremmøde' },
    { key: 'register', label: 'Fravær' },
    ...(isAdmin ? [{ key: 'fri' as const, label: 'Anmodninger om fri', badge: pendingLeave }] : []),
    { key: 'statistik', label: 'Statistik' },
  ]

  return (
    <div className="max-w-4xl mx-auto px-4 py-8">
      <h1 className="font-display text-2xl font-semibold text-gray-900 mb-4">Fravær</h1>

      <div className="flex gap-1 border-b border-gray-200 mb-6 overflow-x-auto" role="tablist">
        {tabs.map((t) => (
          <button
            key={t.key}
            type="button"
            role="tab"
            aria-selected={tab === t.key}
            onClick={() => setSearchParams({ fane: t.key }, { replace: true })}
            data-testid={`absence-tab-${t.key}`}
            className={`px-3 py-2 text-sm font-medium whitespace-nowrap border-b-2 -mb-px transition-colors ${
              tab === t.key
                ? 'border-brand-600 text-brand-700'
                : 'border-transparent text-gray-500 hover:text-gray-800'
            }`}
          >
            {t.label}
            {t.badge ? (
              <span className="ml-1.5 inline-flex items-center justify-center min-w-5 h-5 px-1 rounded-full bg-amber-100 text-amber-900 text-xs">
                {t.badge}
              </span>
            ) : null}
          </button>
        ))}
      </div>

      {tab === 'fremmoede' && <AttendanceTab isAdmin={isAdmin} />}
      {tab === 'register' && <AbsenceRegisterTab />}
      {tab === 'fri' && isAdmin && <LeaveRequestsTab />}
      {tab === 'statistik' && <AbsenceStatsTab isAdmin={isAdmin} />}

      <RetentionNote isAdmin={isAdmin} />
    </div>
  )
}
