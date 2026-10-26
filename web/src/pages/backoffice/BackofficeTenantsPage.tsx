import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { getApiV1AdminTenantsOptions } from '../../api/generated/@tanstack/react-query.gen'
import type { SubscriptionStatus } from '../../api/client'
import BackofficeBackupStatusCard from './BackofficeBackupStatusCard'

// en-CA formats as YYYY-MM-DD, like the API's dates.
const danishDate = new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Copenhagen' })

const statusLabel: Record<SubscriptionStatus, string> = {
  Trialing: 'Prøveperiode',
  Active: 'Aktiv',
  PastDue: 'Forfaldent',
  Canceled: 'Annulleret',
  Unpaid: 'Ubetalt',
}

const statusColors: Record<SubscriptionStatus, string> = {
  Trialing: 'bg-blue-100 text-blue-700',
  Active: 'bg-green-100 text-green-700',
  PastDue: 'bg-yellow-100 text-yellow-800',
  Canceled: 'bg-red-100 text-red-700',
  Unpaid: 'bg-red-100 text-red-700',
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString('da-DK', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  })
}

export default function BackofficeTenantsPage() {
  // Internal schools (the CD smoke test's) aren't customers, so they're hidden unless asked for.
  const [includeInternal, setIncludeInternal] = useState(false)
  const {
    data: tenants,
    isLoading,
    isError,
  } = useQuery({
    ...getApiV1AdminTenantsOptions({ query: { includeInternal } }),
    // Keep the table on screen while the toggle refetches.
    placeholderData: keepPreviousData,
  })
  const today = danishDate.format(new Date())

  // The backup card has its own endpoint, so it shows even when the school list fails.
  if (isLoading) {
    return (
      <div>
        <BackofficeBackupStatusCard />
        <div className="text-gray-500 text-sm">Henter skoler…</div>
      </div>
    )
  }

  if (isError || !tenants) {
    return (
      <div>
        <BackofficeBackupStatusCard />
        <div className="text-red-600 text-sm">Kunne ikke hente skoler.</div>
      </div>
    )
  }

  return (
    <div>
      <BackofficeBackupStatusCard />
      <div className="mb-6 flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold text-gray-900">Skoler</h1>
          <p className="text-sm text-gray-500 mt-1">{tenants.length} skoler i alt</p>
        </div>
        <label className="flex items-center gap-2 text-sm text-gray-700">
          <input
            type="checkbox"
            checked={includeInternal}
            onChange={(e) => setIncludeInternal(e.target.checked)}
            className="h-4 w-4 rounded border-gray-300"
          />
          Vis interne skoler
        </label>
      </div>

      <div className="bg-white border border-gray-200 rounded-xl overflow-hidden">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-100 bg-gray-50 text-left text-xs font-medium text-gray-500 uppercase tracking-wide">
              <th className="px-4 py-3">Navn</th>
              <th className="px-4 py-3">E-mail</th>
              <th className="px-4 py-3">Oprettet</th>
              <th className="px-4 py-3">Status</th>
              <th className="px-4 py-3">Prøveperiode slut</th>
              <th className="px-4 py-3">Moduler</th>
              <th className="px-4 py-3"></th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {tenants.map((t) => (
              <tr key={t.id} className="hover:bg-gray-50 transition-colors">
                <td className="px-4 py-3 font-medium text-gray-900">
                  {t.name}
                  {t.isInternal && (
                    <span className="ml-1.5 inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-600">
                      Intern
                    </span>
                  )}
                </td>
                <td className="px-4 py-3 text-gray-500">{t.contactEmail ?? '—'}</td>
                <td className="px-4 py-3 text-gray-500">{formatDate(t.createdAt)}</td>
                <td className="px-4 py-3">
                  <span
                    className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${statusColors[t.subscriptionStatus]}`}
                  >
                    {statusLabel[t.subscriptionStatus]}
                  </span>
                  {t.freeAccessGrantedAt != null &&
                    (t.freeAccessLastDay == null || t.freeAccessLastDay >= today) && (
                      <span className="ml-1.5 inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-purple-100 text-purple-700">
                        Gratis
                      </span>
                    )}
                </td>
                <td className="px-4 py-3 text-gray-500">{formatDate(t.trialEnd)}</td>
                <td className="px-4 py-3 text-gray-500">{t.activeModuleCount}</td>
                <td className="px-4 py-3">
                  <Link
                    to={`/backoffice/tenants/${t.id}`}
                    className="text-indigo-600 hover:text-indigo-800 font-medium"
                  >
                    Se detaljer →
                  </Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
