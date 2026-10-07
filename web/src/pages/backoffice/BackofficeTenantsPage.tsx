import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { api } from '../../api/client'
import type { SubscriptionStatus } from '../../api/client'
import BackofficeBackupStatusCard from './BackofficeBackupStatusCard'

type TenantListItem = {
  id: string
  name: string
  contactEmail: string | null
  createdAt: string
  subscriptionStatus: SubscriptionStatus
  trialEnd: string
  currentPeriodEnd: string | null
  activeModuleCount: number
}

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
  const {
    data: tenants,
    isLoading,
    isError,
  } = useQuery({
    queryKey: ['admin', 'tenants'],
    queryFn: () => api.get<TenantListItem[]>('/admin/tenants'),
  })

  if (isLoading) {
    return <div className="text-gray-500 text-sm">Henter skoler…</div>
  }

  if (isError || !tenants) {
    return <div className="text-red-600 text-sm">Kunne ikke hente skoler.</div>
  }

  return (
    <div>
      <BackofficeBackupStatusCard />
      <div className="mb-6">
        <h1 className="text-2xl font-semibold text-gray-900">Skoler</h1>
        <p className="text-sm text-gray-500 mt-1">{tenants.length} skoler i alt</p>
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
                <td className="px-4 py-3 font-medium text-gray-900">{t.name}</td>
                <td className="px-4 py-3 text-gray-500">{t.contactEmail ?? '—'}</td>
                <td className="px-4 py-3 text-gray-500">{formatDate(t.createdAt)}</td>
                <td className="px-4 py-3">
                  <span
                    className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${statusColors[t.subscriptionStatus]}`}
                  >
                    {statusLabel[t.subscriptionStatus]}
                  </span>
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
