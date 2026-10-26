import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useParams, Link } from 'react-router-dom'
import {
  deleteApiV1AdminTenantsBySchoolIdFreeAccessMutation,
  deleteApiV1AdminTenantsBySchoolIdModulesByModuleMutation,
  getApiV1AdminTenantsBySchoolIdOptions,
  getApiV1AdminTenantsBySchoolIdQueryKey,
  postApiV1AdminTenantsBySchoolIdModulesMutation,
  putApiV1AdminTenantsBySchoolIdFreeAccessMutation,
} from '../../api/generated/@tanstack/react-query.gen'
import type { SubscriptionModule, TenantDetailDto } from '../../api/generated/types.gen'
import type { SubscriptionStatus } from '../../api/client'
import { problemDetail } from '../../lib/problem'

type ModuleItem = { module: SubscriptionModule }

const ALL_MODULES: ModuleItem['module'][] = ['ParentModule', 'BoardModule']

const moduleLabels: Record<ModuleItem['module'], string> = {
  ParentModule: 'Forældremodul',
  BoardModule: 'Bestyrelse og tilsyn',
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

function Field({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div>
      <dt className="text-xs font-medium text-gray-500 uppercase tracking-wide">{label}</dt>
      <dd className="mt-0.5 text-sm text-gray-900">{value ?? '—'}</dd>
    </div>
  )
}

export default function BackofficeTenantDetailPage() {
  const { schoolId = '' } = useParams<{ schoolId: string }>()
  const qc = useQueryClient()
  const path = { schoolId }
  const invalidate = () =>
    qc.invalidateQueries({ queryKey: getApiV1AdminTenantsBySchoolIdQueryKey({ path }) })

  const {
    data: tenant,
    isLoading,
    isError,
  } = useQuery({
    ...getApiV1AdminTenantsBySchoolIdOptions({ path }),
    enabled: !!schoolId,
  })

  const grantMutation = useMutation({
    ...postApiV1AdminTenantsBySchoolIdModulesMutation(),
    onSuccess: invalidate,
  })

  const revokeMutation = useMutation({
    ...deleteApiV1AdminTenantsBySchoolIdModulesByModuleMutation(),
    onSuccess: invalidate,
  })

  if (isLoading) return <div className="text-gray-500 text-sm">Henter skole…</div>
  if (isError || !tenant) return <div className="text-red-600 text-sm">Kunne ikke hente skole.</div>

  const modules = tenant.modules ?? []
  const activeModules = new Set(modules.map((m) => m.module))

  return (
    <div className="space-y-6 max-w-3xl">
      <div className="flex items-center gap-3">
        <Link to="/backoffice/tenants" className="text-sm text-gray-400 hover:text-gray-600">
          ← Alle skoler
        </Link>
        <span className="text-gray-300">/</span>
        <span className="text-sm font-medium text-gray-900">{tenant.name}</span>
      </div>

      {/* School info */}
      <div className="bg-white border border-gray-200 rounded-xl p-5">
        <h2 className="text-base font-semibold text-gray-900 mb-4">Skoleoplysninger</h2>
        <dl className="grid grid-cols-2 gap-4">
          <Field label="Navn" value={tenant.name} />
          <Field label="Oprettet" value={formatDate(tenant.createdAt)} />
          <Field label="E-mail" value={tenant.contactEmail} />
          <Field label="Telefon" value={tenant.contactPhone} />
        </dl>
      </div>

      {/* Subscription */}
      <div className="bg-white border border-gray-200 rounded-xl p-5">
        <h2 className="text-base font-semibold text-gray-900 mb-4">Abonnement</h2>
        <dl className="grid grid-cols-2 gap-4">
          <div>
            <dt className="text-xs font-medium text-gray-500 uppercase tracking-wide">Status</dt>
            <dd className="mt-0.5">
              <span
                className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${statusColors[tenant.subscriptionStatus]}`}
              >
                {statusLabel[tenant.subscriptionStatus]}
              </span>
            </dd>
          </div>
          <Field label="Prøveperiode slut" value={formatDate(tenant.trialEnd)} />
          <Field
            label="Faktureringsperiode slut"
            value={tenant.currentPeriodEnd ? formatDate(tenant.currentPeriodEnd) : null}
          />
          <div />
          <div className="col-span-2">
            <dt className="text-xs font-medium text-gray-500 uppercase tracking-wide">
              Stripe kunde-ID
            </dt>
            <dd className="mt-0.5 text-sm font-mono text-gray-700">
              {tenant.stripeCustomerId ?? '—'}
            </dd>
          </div>
          <div className="col-span-2">
            <dt className="text-xs font-medium text-gray-500 uppercase tracking-wide">
              Stripe abonnements-ID
            </dt>
            <dd className="mt-0.5 text-sm font-mono text-gray-700">
              {tenant.stripeSubscriptionId ?? '—'}
            </dd>
          </div>
        </dl>
      </div>

      <FreeAccessCard tenant={tenant} onChanged={invalidate} />

      {/* Modules */}
      <div className="bg-white border border-gray-200 rounded-xl p-5">
        <h2 className="text-base font-semibold text-gray-900 mb-4">Moduler</h2>
        <div className="space-y-3">
          {ALL_MODULES.map((mod) => {
            const active = activeModules.has(mod)
            const item = modules.find((m) => m.module === mod)
            const isPending = grantMutation.isPending || revokeMutation.isPending

            return (
              <div
                key={mod}
                className="flex items-center justify-between py-2 border-b border-gray-100 last:border-0"
              >
                <div className="flex items-center gap-3">
                  <span
                    className={`w-2 h-2 rounded-full ${active ? 'bg-green-500' : 'bg-gray-300'}`}
                  />
                  <span className="text-sm font-medium text-gray-900">{moduleLabels[mod]}</span>
                  {item && (
                    <span
                      className={`text-xs px-1.5 py-0.5 rounded font-medium ${
                        item.isAdminOverride
                          ? 'bg-purple-100 text-purple-700'
                          : 'bg-indigo-100 text-indigo-700'
                      }`}
                    >
                      {item.isAdminOverride ? 'Admin override' : 'Stripe'}
                    </span>
                  )}
                </div>
                <button
                  disabled={isPending}
                  onClick={() => {
                    if (active) {
                      revokeMutation.mutate({ path: { schoolId, module: mod } })
                    } else {
                      grantMutation.mutate({ path, body: { module: mod } })
                    }
                  }}
                  className={`text-sm px-3 py-1 rounded-lg font-medium transition-colors disabled:opacity-50 ${
                    active
                      ? 'bg-red-50 text-red-600 hover:bg-red-100'
                      : 'bg-indigo-50 text-indigo-700 hover:bg-indigo-100'
                  }`}
                >
                  {active ? 'Fjern' : 'Tildel'}
                </button>
              </div>
            )
          })}
        </div>
      </div>
    </div>
  )
}

// en-CA formats as YYYY-MM-DD, which the date input and the API expect.
const danishDate = new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Copenhagen' })

function danishDateInMonths(months: number) {
  const date = new Date()
  date.setMonth(date.getMonth() + months)
  return danishDate.format(date)
}

/** Free base access without Stripe, e.g. after the trial expired. Modules are granted below. */
function FreeAccessCard({ tenant, onChanged }: { tenant: TenantDetailDto; onChanged: () => void }) {
  const today = danishDate.format(new Date())
  const [lastDay, setLastDay] = useState(tenant.freeAccessLastDay ?? danishDateInMonths(3))
  const [noEndDate, setNoEndDate] = useState(
    tenant.freeAccessGrantedAt != null && tenant.freeAccessLastDay == null
  )
  const grant = useMutation({
    ...putApiV1AdminTenantsBySchoolIdFreeAccessMutation(),
    onSuccess: onChanged,
  })
  const revoke = useMutation({
    ...deleteApiV1AdminTenantsBySchoolIdFreeAccessMutation(),
    onSuccess: onChanged,
  })

  const path = { schoolId: tenant.id }
  const granted = tenant.freeAccessGrantedAt != null
  const expired = granted && tenant.freeAccessLastDay != null && tenant.freeAccessLastDay < today
  const isPaying = tenant.subscriptionStatus === 'Active'
  const isPending = grant.isPending || revoke.isPending
  const error = grant.error ?? revoke.error

  const status = !granted
    ? 'Ingen gratis adgang'
    : tenant.freeAccessLastDay == null
      ? 'Gratis adgang uden slutdato'
      : expired
        ? `Gratis adgang udløb ${formatDate(tenant.freeAccessLastDay)}`
        : `Gratis adgang til og med ${formatDate(tenant.freeAccessLastDay)}`

  function submit(e: React.FormEvent) {
    e.preventDefault()
    grant.mutate({ path, body: { lastDay: noEndDate ? null : lastDay } })
  }

  return (
    <div data-testid="free-access-card" className="bg-white border border-gray-200 rounded-xl p-5">
      <h2 className="text-base font-semibold text-gray-900">Gratis adgang</h2>
      <p className="mt-1 text-sm text-gray-500">
        Skolen kan bruge Basis uden at betale. Moduler tildeles separat nedenfor.
      </p>
      <p data-testid="free-access-status" className="mt-3 text-sm font-medium text-gray-900">
        {status}
      </p>

      {isPaying ? (
        <p className="mt-3 text-sm text-gray-500">
          Skolen betaler via Stripe, så gratis adgang kan ikke gives.
        </p>
      ) : (
        <form onSubmit={submit} className="mt-4 flex flex-wrap items-end gap-4">
          <label className="block">
            <span className="text-sm font-medium text-gray-700">Sidste dag</span>
            <input
              type="date"
              value={lastDay}
              min={today}
              onChange={(e) => setLastDay(e.target.value)}
              disabled={noEndDate}
              required={!noEndDate}
              data-testid="free-access-last-day"
              className="mt-1 block px-3 py-2 border border-gray-300 rounded-lg text-sm disabled:bg-gray-50 disabled:text-gray-400"
            />
          </label>
          <label className="flex items-center gap-2 py-2 text-sm text-gray-700">
            <input
              type="checkbox"
              checked={noEndDate}
              onChange={(e) => setNoEndDate(e.target.checked)}
              data-testid="free-access-no-end-date"
            />
            Uden slutdato
          </label>
          <button
            type="submit"
            disabled={isPending}
            data-testid="free-access-grant"
            className="text-sm px-3 py-2 rounded-lg font-medium bg-indigo-50 text-indigo-700 hover:bg-indigo-100 transition-colors disabled:opacity-50"
          >
            {granted && !expired ? 'Opdater' : 'Giv gratis adgang'}
          </button>
          {granted && (
            <button
              type="button"
              disabled={isPending}
              onClick={() => revoke.mutate({ path })}
              data-testid="free-access-revoke"
              className="text-sm px-3 py-2 rounded-lg font-medium bg-red-50 text-red-600 hover:bg-red-100 transition-colors disabled:opacity-50"
            >
              Fjern
            </button>
          )}
        </form>
      )}

      {error && (
        <p className="mt-3 text-sm text-red-600">{problemDetail(error) ?? 'Det lykkedes ikke.'}</p>
      )}
    </div>
  )
}
