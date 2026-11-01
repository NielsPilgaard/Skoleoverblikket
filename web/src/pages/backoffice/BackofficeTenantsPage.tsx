import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { getApiV1AdminTenantsOptions } from '../../api/generated/@tanstack/react-query.gen'
import type { SubscriptionStatus } from '../../api/client'
import { DataTable } from '../../components/ui/list'
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
  const navigate = useNavigate()
  const { data: tenants, isLoading, isError } = useQuery(getApiV1AdminTenantsOptions())
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
      <h1 className="mb-4 text-2xl font-semibold text-gray-900">Skoler</h1>

      <DataTable
        items={tenants}
        caption="Skoler"
        rowKey={(t) => t.id}
        noun={{ one: 'skole', other: 'skoler' }}
        searchText={(t) => `${t.name} ${t.contactEmail ?? ''}`}
        searchPlaceholder="Søg efter skole eller e-mail"
        filterLabel="Filtrer efter status"
        filters={[
          { value: '', label: 'Alle' },
          {
            value: 'Trialing',
            label: 'Prøveperiode',
            match: (t) => t.subscriptionStatus === 'Trialing',
          },
          { value: 'Active', label: 'Aktiv', match: (t) => t.subscriptionStatus === 'Active' },
          {
            value: 'Unpaid',
            label: 'Mangler betaling',
            match: (t) => t.subscriptionStatus === 'PastDue' || t.subscriptionStatus === 'Unpaid',
          },
          {
            value: 'Canceled',
            label: 'Annulleret',
            match: (t) => t.subscriptionStatus === 'Canceled',
          },
        ]}
        empty={<p>Ingen skoler endnu</p>}
        columns={[
          { key: 'name', header: 'Navn', cell: (t) => t.name, sortValue: (t) => t.name },
          { key: 'email', header: 'E-mail', cell: (t) => t.contactEmail ?? '—' },
          {
            key: 'created',
            header: 'Oprettet',
            cell: (t) => formatDate(t.createdAt),
            sortValue: (t) => t.createdAt,
          },
          {
            key: 'status',
            header: 'Status',
            sortValue: (t) => statusLabel[t.subscriptionStatus],
            cell: (t) => (
              <span className="inline-flex flex-wrap gap-1.5">
                <span
                  className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-bold ${statusColors[t.subscriptionStatus]}`}
                >
                  {statusLabel[t.subscriptionStatus]}
                </span>
                {t.freeAccessGrantedAt != null &&
                  (t.freeAccessLastDay == null || t.freeAccessLastDay >= today) && (
                    <span className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-bold bg-purple-100 text-purple-800">
                      Gratis
                    </span>
                  )}
              </span>
            ),
          },
          {
            key: 'trialEnd',
            header: 'Prøveperiode slut',
            cell: (t) => formatDate(t.trialEnd),
            sortValue: (t) => t.trialEnd,
          },
          {
            key: 'modules',
            header: 'Moduler',
            cell: (t) => t.activeModuleCount,
            sortValue: (t) => t.activeModuleCount,
          },
        ]}
        actions={(t) => [
          { label: 'Se detaljer', onClick: () => navigate(`/backoffice/tenants/${t.id}`) },
        ]}
      />
    </div>
  )
}
