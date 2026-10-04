import { useState } from 'react'
import { Modal } from '../components/Modal'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1BillingSubscriptionOptions,
  getApiV1ModulesOptions,
  postApiV1BillingCheckoutMutation,
  postApiV1BillingPortalMutation,
  postApiV1BillingModulesMutation,
  postApiV1BillingIntervalMutation,
  deleteApiV1BillingModulesByModuleMutation,
} from '../api/generated/@tanstack/react-query.gen'
import type { SubscriptionDto, BillingInterval } from '../api/client'
import { usePageTitle } from '../hooks/usePageTitle'
import { BILLING_INTERVAL_STORAGE_KEY, parseBillingInterval } from '../lib/billingInterval'

function initialBillingInterval(): BillingInterval {
  return parseBillingInterval(localStorage.getItem(BILLING_INTERVAL_STORAGE_KEY)) ?? 'Monthly'
}

const SELF_SERVE_ENABLED = true
// Keep in sync with web/src/pages/LandingPage.tsx — same prices, must match Stripe Price IDs in StripeOptions.
const MONTHLY_PRICE_KR = 300
const YEARLY_PRICE_KR = 3000
const YEARLY_EFFECTIVE_MONTHLY_KR = Math.round(YEARLY_PRICE_KR / 12)
const YEARLY_SAVINGS_KR = MONTHLY_PRICE_KR * 12 - YEARLY_PRICE_KR

const PARENT_MODULE_MONTHLY_KR = 300
const PARENT_MODULE_YEARLY_KR = 3000
const BOARD_MODULE_MONTHLY_KR = 300
const BOARD_MODULE_YEARLY_KR = 3000

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('da-DK', {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  })
}

function SkeletonStatusCard() {
  return (
    <div className="bg-white rounded-xl border border-gray-200 p-6 animate-pulse">
      <div className="h-5 w-48 bg-gray-200 rounded mb-3" />
      <div className="h-4 w-64 bg-gray-100 rounded mb-2" />
      <div className="h-4 w-40 bg-gray-100 rounded mb-5" />
      <div className="h-9 w-44 bg-gray-200 rounded-lg" />
    </div>
  )
}

function CheckIcon() {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2.5"
      className="text-brand-500 shrink-0 mt-0.5"
    >
      <polyline points="20 6 9 17 4 12" />
    </svg>
  )
}

export default function BillingPage() {
  usePageTitle('Abonnement')
  const queryClient = useQueryClient()
  const { data, isLoading, isError, refetch } = useQuery(getApiV1BillingSubscriptionOptions())
  const [selectedInterval, setSelectedInterval] = useState<BillingInterval>(initialBillingInterval)

  const checkoutMutation = useMutation({
    ...postApiV1BillingCheckoutMutation(),
    onSuccess: (result) => {
      localStorage.removeItem(BILLING_INTERVAL_STORAGE_KEY)
      if (result?.url) window.location.href = result.url
    },
    onError: (error) => {
      const errorMessage =
        error instanceof Error ? error.message : 'Kunne ikke oprette checkoutsession'
      console.error('Checkout error:', error)
      alert(errorMessage)
    },
  })

  const portalMutation = useMutation({
    ...postApiV1BillingPortalMutation(),
    onSuccess: (result) => {
      if (result?.url) window.location.href = result.url
    },
    onError: (error) => {
      const errorMessage =
        error instanceof Error ? error.message : 'Kunne ikke åbne administrationsportal'
      console.error('Portal error:', error)
      alert(errorMessage)
    },
  })

  const switchIntervalMutation = useMutation({
    ...postApiV1BillingIntervalMutation(),
    onSuccess: () => {
      void queryClient.invalidateQueries(getApiV1BillingSubscriptionOptions())
    },
    onError: (error) => {
      const errorMessage =
        error instanceof Error ? error.message : 'Kunne ikke skifte betalingsinterval'
      console.error('Switch interval error:', error)
      alert(errorMessage)
    },
  })

  const addModuleMutation = useMutation({
    ...postApiV1BillingModulesMutation(),
    onSuccess: () => {
      void queryClient.invalidateQueries(getApiV1BillingSubscriptionOptions())
      void queryClient.invalidateQueries(getApiV1ModulesOptions())
    },
    onError: (error) => {
      const errorMessage = error instanceof Error ? error.message : 'Kunne ikke aktivere modul'
      console.error('Add module error:', error)
      alert(errorMessage)
    },
  })

  const removeModuleMutation = useMutation({
    ...deleteApiV1BillingModulesByModuleMutation(),
    onSuccess: () => {
      void queryClient.invalidateQueries(getApiV1BillingSubscriptionOptions())
      void queryClient.invalidateQueries(getApiV1ModulesOptions())
    },
    onError: (error) => {
      const errorMessage = error instanceof Error ? error.message : 'Kunne ikke deaktivere modul'
      console.error('Remove module error:', error)
      alert(errorMessage)
    },
  })

  const isRedirecting = checkoutMutation.isPending || portalMutation.isPending
  const activeModules = data?.activeModules ?? []
  // Module pricing follows the subscription's committed interval once active (that's what
  // AddModuleAsync actually charges); before activation, preview the interval being chosen.
  const moduleIsYearly = (data?.isActive ? data.interval : selectedInterval) === 'Yearly'

  return (
    <div className="p-6 pb-12 lg:p-8 max-w-2xl mx-auto space-y-8">
      {/* Page header */}
      <div>
        <h1 className="font-display text-2xl font-semibold text-gray-900">Abonnement</h1>
        <p className="mt-1 text-sm text-gray-500">
          Administrer dit abonnement og betalingsoplysninger
        </p>
      </div>

      {/* Status card */}
      {isLoading ? (
        <SkeletonStatusCard />
      ) : isError ? (
        <div className="bg-red-50 border border-red-200 rounded-xl p-6 text-center">
          <p className="text-red-700 font-medium">Kunne ikke hente abonnementsoplysninger</p>
          <button
            onClick={() => refetch()}
            className="mt-3 px-4 py-2 text-sm bg-red-100 text-red-700 rounded-lg hover:bg-red-200 transition-colors"
          >
            Prøv igen
          </button>
        </div>
      ) : data ? (
        <StatusCard
          data={data}
          selectedInterval={selectedInterval}
          activeModules={activeModules}
          onCheckout={() => checkoutMutation.mutate({ body: { interval: selectedInterval } })}
          onPortal={() => portalMutation.mutate({})}
          onSwitchInterval={(interval) => switchIntervalMutation.mutate({ body: { interval } })}
          isRedirecting={isRedirecting}
          isSwitchingInterval={switchIntervalMutation.isPending}
        />
      ) : null}

      {/* Pricing info card */}
      <PricingCard
        isActive={data?.isActive ?? false}
        isTrialing={data?.isTrialing ?? false}
        trialEnd={data?.trialEnd}
        selectedInterval={data?.isActive ? (data.interval ?? selectedInterval) : selectedInterval}
        onIntervalChange={setSelectedInterval}
        onCheckout={() =>
          checkoutMutation.mutate({
            body: { interval: data?.isActive ? (data.interval ?? 'Monthly') : selectedInterval },
          })
        }
        isRedirecting={isRedirecting}
      />

      {/* Add-on modules */}
      <div>
        <h2 className="font-display text-lg font-semibold text-gray-900 mb-3">Tilkøb</h2>
        <ModuleCard
          name="Forældremodul"
          description="Giv forældre adgang til at se klassernes skema, kalender og ugeplan. Inviter forældre via e-mail og knyt dem til deres barns klasse."
          monthlyKr={PARENT_MODULE_MONTHLY_KR}
          yearlyKr={PARENT_MODULE_YEARLY_KR}
          isYearly={moduleIsYearly}
          isActive={activeModules.includes('ParentModule')}
          canToggle={data?.isActive ?? false}
          isPending={addModuleMutation.isPending || removeModuleMutation.isPending}
          onActivate={() => addModuleMutation.mutate({ body: { module: 'ParentModule' } })}
          onDeactivate={() => removeModuleMutation.mutate({ path: { module: 'ParentModule' } })}
          blockedReason={!data?.isActive ? 'Kræver aktivt basis abonnement' : undefined}
        />
        <div className="mt-3">
          <ModuleCard
            name="Bestyrelse og tilsyn"
            description="Giv bestyrelsesmedlemmer en dedikeret adgang med aggregerede statistikker og bestyrelsesdokumenter. Admin styrer adgangsniveau pr. bestyrelsesmedlem."
            monthlyKr={BOARD_MODULE_MONTHLY_KR}
            yearlyKr={BOARD_MODULE_YEARLY_KR}
            isYearly={moduleIsYearly}
            isActive={activeModules.includes('BoardModule')}
            canToggle={data?.isActive ?? false}
            isPending={addModuleMutation.isPending || removeModuleMutation.isPending}
            onActivate={() => addModuleMutation.mutate({ body: { module: 'BoardModule' } })}
            onDeactivate={() => removeModuleMutation.mutate({ path: { module: 'BoardModule' } })}
            blockedReason={!data?.isActive ? 'Kræver aktivt basis abonnement' : undefined}
          />
        </div>
      </div>
    </div>
  )
}

const MODULE_PRICING: Record<string, { monthly: number; yearly: number; label: string }> = {
  ParentModule: {
    label: 'Forældremodul',
    monthly: PARENT_MODULE_MONTHLY_KR,
    yearly: PARENT_MODULE_YEARLY_KR,
  },
  BoardModule: {
    label: 'Bestyrelse og tilsyn',
    monthly: BOARD_MODULE_MONTHLY_KR,
    yearly: BOARD_MODULE_YEARLY_KR,
  },
}

function intervalLabel(interval: BillingInterval): string {
  return interval === 'Yearly' ? 'årlig' : 'månedlig'
}

function SwitchIntervalModal({
  fromInterval,
  toInterval,
  activeModules,
  onConfirm,
  onCancel,
  isPending,
}: {
  fromInterval: BillingInterval
  toInterval: BillingInterval
  activeModules: string[]
  onConfirm: () => void
  onCancel: () => void
  isPending: boolean
}) {
  const toYearly = toInterval === 'Yearly'
  const basisFrom =
    fromInterval === 'Yearly' ? `${YEARLY_PRICE_KR} kr/år` : `${MONTHLY_PRICE_KR} kr/md`
  const basisTo = toYearly ? `${YEARLY_PRICE_KR} kr/år` : `${MONTHLY_PRICE_KR} kr/md`

  return (
    <Modal isOpen onClose={onCancel} size="sm">
      <div className="p-6">
        <div className="flex items-center gap-3 mb-4">
          <div className="w-10 h-10 rounded-full bg-amber-100 flex items-center justify-center shrink-0">
            <svg
              width="20"
              height="20"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              className="text-amber-600"
            >
              <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
              <line x1="12" y1="9" x2="12" y2="13" />
              <line x1="12" y1="17" x2="12.01" y2="17" />
            </svg>
          </div>
          <h2 className="text-base font-semibold text-gray-900">
            Skift til {intervalLabel(toInterval)} betaling?
          </h2>
        </div>
        <p className="text-sm text-gray-600 mb-3">
          Dette ændrer betalingsintervallet for hele abonnementet, inklusive tilkøbte moduler:
        </p>
        <div className="bg-gray-50 rounded-lg px-4 py-3 mb-4 space-y-2">
          <div className="flex items-center justify-between text-sm">
            <span className="font-medium text-gray-800">Basis</span>
            <span className="text-gray-500 tabular-nums">
              {basisFrom} <span aria-hidden="true">→</span>{' '}
              <b className="text-gray-900">{basisTo}</b>
            </span>
          </div>
          {activeModules.map((moduleKey) => {
            const pricing = MODULE_PRICING[moduleKey]
            if (!pricing) return null
            const from =
              fromInterval === 'Yearly' ? `${pricing.yearly} kr/år` : `${pricing.monthly} kr/md`
            const to = toYearly ? `${pricing.yearly} kr/år` : `${pricing.monthly} kr/md`
            return (
              <div key={moduleKey} className="flex items-center justify-between text-sm">
                <span className="font-medium text-gray-800">{pricing.label}</span>
                <span className="text-gray-500 tabular-nums">
                  {from} <span aria-hidden="true">→</span> <b className="text-gray-900">{to}</b>
                </span>
              </div>
            )
          })}
          <p className="pt-1 text-xs text-gray-500">
            Du opkræves med det samme — beløbet modregnes for den ubrugte tid på dit nuværende{' '}
            {fromInterval === 'Yearly' ? 'årlige' : 'månedlige'} abonnement.
          </p>
        </div>
        <div className="flex gap-3">
          <button
            type="button"
            onClick={onCancel}
            disabled={isPending}
            className="flex-1 px-4 py-2.5 text-sm font-medium border border-gray-300 text-gray-700 rounded-lg hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            Annuller
          </button>
          <button
            type="button"
            onClick={onConfirm}
            disabled={isPending}
            className="flex-1 px-4 py-2.5 text-sm font-medium bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {isPending ? 'Skifter...' : 'Ja, skift'}
          </button>
        </div>
      </div>
    </Modal>
  )
}

function StatusCard({
  data,
  selectedInterval,
  activeModules,
  onCheckout,
  onPortal,
  onSwitchInterval,
  isRedirecting,
  isSwitchingInterval,
}: {
  data: SubscriptionDto
  selectedInterval: BillingInterval
  activeModules: string[]
  onCheckout: () => void
  onPortal: () => void
  onSwitchInterval: (interval: BillingInterval) => void
  isRedirecting: boolean
  isSwitchingInterval: boolean
}) {
  const [showIntervalConfirm, setShowIntervalConfirm] = useState(false)
  if (data.isTrialing) {
    const trialCadenceLabel =
      selectedInterval === 'Yearly' ? `${YEARLY_PRICE_KR} kr/år` : `${MONTHLY_PRICE_KR} kr/md`
    return (
      <div className="bg-brand-50 border border-brand-200 rounded-xl p-6">
        <div className="flex items-start gap-3">
          <div className="mt-0.5 flex-shrink-0 w-8 h-8 rounded-full bg-brand-100 flex items-center justify-center">
            <svg
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              className="text-brand-600"
            >
              <circle cx="12" cy="12" r="10" />
              <polyline points="12 6 12 12 16 14" />
            </svg>
          </div>
          <div className="flex-1 min-w-0">
            <h2 className="text-base font-semibold text-brand-900">Gratis prøveperiode</h2>
            <p className="mt-1 text-sm text-brand-700">
              {(data.trialDaysLeft ?? 0) > 0
                ? `${data.trialDaysLeft} ${data.trialDaysLeft === 1 ? 'dag' : 'dage'} tilbage — ingen betaling endnu`
                : 'Prøveperioden udløber i dag'}
            </p>
            <p className="mt-0.5 text-sm text-brand-600">
              Du kan bruge alle funktioner gratis frem til den {formatDate(data.trialEnd ?? '')}.
              Herefter koster det {trialCadenceLabel}.
            </p>
          </div>
        </div>
      </div>
    )
  }

  if (data.isActive) {
    const targetInterval: BillingInterval = data.interval === 'Yearly' ? 'Monthly' : 'Yearly'
    return (
      <>
        {showIntervalConfirm && (
          <SwitchIntervalModal
            fromInterval={data.interval ?? 'Monthly'}
            toInterval={targetInterval}
            activeModules={activeModules}
            onConfirm={() => {
              onSwitchInterval(targetInterval)
              setShowIntervalConfirm(false)
            }}
            onCancel={() => setShowIntervalConfirm(false)}
            isPending={isSwitchingInterval}
          />
        )}
        <div className="bg-green-50 border border-green-200 rounded-xl p-6">
          <div className="flex items-start gap-3">
            <div className="mt-0.5 flex-shrink-0 w-8 h-8 rounded-full bg-green-100 flex items-center justify-center">
              <svg
                width="16"
                height="16"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2.5"
                className="text-green-600"
              >
                <polyline points="20 6 9 17 4 12" />
              </svg>
            </div>
            <div className="flex-1 min-w-0">
              <h2 className="text-base font-semibold text-green-900">
                Aktivt abonnement — Basis (
                {data.interval === 'Yearly'
                  ? `${YEARLY_PRICE_KR} kr/år`
                  : `${MONTHLY_PRICE_KR} kr/md`}
                )
              </h2>
              {data.currentPeriodEnd && (
                <p className="mt-1 text-sm text-green-700">
                  Næste betaling den {formatDate(data.currentPeriodEnd)}
                </p>
              )}
              {activeModules.length > 0 && (
                <p className="mt-1 text-sm text-green-700">
                  Tilkøb:{' '}
                  {activeModules
                    .map((moduleKey) => MODULE_PRICING[moduleKey]?.label ?? moduleKey)
                    .join(', ')}
                </p>
              )}
              <div className="mt-4 flex flex-wrap items-center gap-3">
                <button
                  onClick={onPortal}
                  disabled={isRedirecting}
                  className="px-4 py-2 text-sm font-medium bg-green-700 text-white rounded-lg hover:bg-green-800 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                >
                  {isRedirecting ? 'Vent...' : 'Administrer abonnement'}
                </button>
                <button
                  onClick={() => setShowIntervalConfirm(true)}
                  disabled={isSwitchingInterval}
                  className="px-4 py-2 text-sm font-medium border border-green-300 text-green-800 rounded-lg hover:bg-green-100 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                >
                  {isSwitchingInterval
                    ? 'Skifter...'
                    : data.interval === 'Yearly'
                      ? 'Skift til månedlig'
                      : 'Skift til årlig'}
                </button>
              </div>
              <p className="mt-2 text-xs text-green-600">
                Du kan opsige abonnementet via administreringsportalen. Skift af betalingsinterval
                sker her — modultilkøb følger automatisk med.
              </p>
            </div>
          </div>
        </div>
      </>
    )
  }

  if (data.status === 'PastDue') {
    return (
      <div className="bg-amber-50 border border-amber-200 rounded-xl p-6">
        <div className="flex items-start gap-3">
          <div className="mt-0.5 flex-shrink-0 w-8 h-8 rounded-full bg-amber-100 flex items-center justify-center">
            <svg
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              className="text-amber-600"
            >
              <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
              <line x1="12" y1="9" x2="12" y2="13" />
              <line x1="12" y1="17" x2="12.01" y2="17" />
            </svg>
          </div>
          <div className="flex-1 min-w-0">
            <h2 className="text-base font-semibold text-amber-900">Betaling mislykkedes</h2>
            <p className="mt-1 text-sm text-amber-700">
              Vi kunne ikke gennemføre betalingen. Opdater dine betalingsoplysninger for at
              fortsætte.
            </p>
            <button
              onClick={onPortal}
              disabled={isRedirecting}
              className="mt-4 px-4 py-2 text-sm font-medium bg-amber-600 text-white rounded-lg hover:bg-amber-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {isRedirecting ? 'Vent...' : 'Opdater betalingskort'}
            </button>
          </div>
        </div>
      </div>
    )
  }

  // Canceled or Unpaid
  return (
    <div className="bg-red-50 border border-red-200 rounded-xl p-6">
      <div className="flex items-start gap-3">
        <div className="mt-0.5 flex-shrink-0 w-8 h-8 rounded-full bg-red-100 flex items-center justify-center">
          <svg
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            className="text-red-600"
          >
            <circle cx="12" cy="12" r="10" />
            <line x1="15" y1="9" x2="9" y2="15" />
            <line x1="9" y1="9" x2="15" y2="15" />
          </svg>
        </div>
        <div className="flex-1 min-w-0">
          <h2 className="text-base font-semibold text-red-900">Abonnement afsluttet</h2>
          <p className="mt-1 text-sm text-red-700">
            Dit abonnement er ikke aktivt. Forny for at få fuld adgang igen.
          </p>
          {SELF_SERVE_ENABLED && (
            <button
              onClick={onCheckout}
              disabled={isRedirecting}
              className="mt-4 px-4 py-2 text-sm font-medium bg-red-700 text-white rounded-lg hover:bg-red-800 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {isRedirecting ? 'Vent...' : 'Forny abonnement'}
            </button>
          )}
        </div>
      </div>
    </div>
  )
}

function ActivateModuleModal({
  name,
  price,
  onConfirm,
  onCancel,
  isPending,
}: {
  name: string
  price: string
  onConfirm: () => void
  onCancel: () => void
  isPending: boolean
}) {
  return (
    <Modal isOpen onClose={onCancel} size="sm">
      <div className="p-6">
        <div className="flex items-center gap-3 mb-4">
          <div className="w-10 h-10 rounded-full bg-amber-100 flex items-center justify-center shrink-0">
            <svg
              width="20"
              height="20"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              className="text-amber-600"
            >
              <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
              <line x1="12" y1="9" x2="12" y2="13" />
              <line x1="12" y1="17" x2="12.01" y2="17" />
            </svg>
          </div>
          <h2 className="text-base font-semibold text-gray-900">Aktiver {name}?</h2>
        </div>
        <p className="text-sm text-gray-600 mb-1">
          Du er ved at tilføje et tilkøb til dit abonnement:
        </p>
        <div className="bg-gray-50 rounded-lg px-4 py-3 mb-4">
          <div className="flex items-center justify-between">
            <span className="text-sm font-medium text-gray-800">{name}</span>
            <span className="text-sm font-semibold text-gray-900 tabular-nums">+{price}</span>
          </div>
          <p className="mt-1 text-xs text-gray-500">
            Beløbet lægges til din næste faktura og fortsætter{' '}
            {price.endsWith('kr/år') ? 'årligt' : 'månedligt'}, indtil du deaktiverer modulet.
          </p>
        </div>
        <div className="flex gap-3">
          <button
            type="button"
            onClick={onCancel}
            disabled={isPending}
            className="flex-1 px-4 py-2.5 text-sm font-medium border border-gray-300 text-gray-700 rounded-lg hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            Annuller
          </button>
          <button
            type="button"
            onClick={onConfirm}
            disabled={isPending}
            className="flex-1 px-4 py-2.5 text-sm font-medium bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {isPending ? 'Aktiverer...' : 'Ja, aktiver'}
          </button>
        </div>
      </div>
    </Modal>
  )
}

function ModuleCard({
  name,
  description,
  monthlyKr,
  yearlyKr,
  isYearly,
  isActive,
  canToggle,
  isPending,
  onActivate,
  onDeactivate,
  blockedReason,
}: {
  name: string
  description: string
  monthlyKr: number
  yearlyKr: number
  isYearly: boolean
  isActive: boolean
  canToggle: boolean
  isPending: boolean
  onActivate: () => void
  onDeactivate: () => void
  blockedReason?: string
}) {
  const [showConfirm, setShowConfirm] = useState(false)
  const priceLabel = isYearly ? `${yearlyKr} kr/år` : `${monthlyKr} kr/md`

  function handleActivateClick() {
    setShowConfirm(true)
  }

  function handleConfirm() {
    onActivate()
    setShowConfirm(false)
  }

  return (
    <>
      {showConfirm && (
        <ActivateModuleModal
          name={name}
          price={priceLabel}
          onConfirm={handleConfirm}
          onCancel={() => setShowConfirm(false)}
          isPending={isPending}
        />
      )}
      <div
        className={`bg-white rounded-xl border ${isActive ? 'border-brand-300 ring-1 ring-brand-200' : 'border-gray-200'}`}
      >
        <div className="px-6 py-5 flex items-start justify-between gap-4">
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2">
              <h3 className="text-sm font-semibold text-gray-700">{name}</h3>
              {isActive && (
                <span className="px-2 py-0.5 text-xs font-medium rounded-full bg-green-100 text-green-700">
                  Aktiv
                </span>
              )}
            </div>
            <p className="mt-1 text-xs text-gray-500">{description}</p>
          </div>
          <div className="text-right shrink-0">
            {isYearly ? (
              <>
                <span className="text-lg font-semibold text-gray-900 tabular-nums">
                  {Math.round(yearlyKr / 12)}
                </span>
                <span className="text-sm text-gray-500"> kr/md</span>
                <p className="text-xs text-gray-400 tabular-nums">{yearlyKr} kr/år</p>
              </>
            ) : (
              <>
                <span className="text-lg font-semibold text-gray-900 tabular-nums">
                  {monthlyKr}
                </span>
                <span className="text-sm text-gray-500"> kr/md</span>
              </>
            )}
          </div>
        </div>
        <div className="px-6 pb-5">
          {blockedReason ? (
            <p className="text-xs text-gray-400 italic">{blockedReason}</p>
          ) : isActive ? (
            <button
              onClick={onDeactivate}
              disabled={isPending}
              className="px-4 py-2 text-sm font-medium border border-gray-300 text-gray-700 rounded-lg hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {isPending ? 'Vent...' : 'Deaktiver'}
            </button>
          ) : (
            <button
              onClick={handleActivateClick}
              disabled={isPending || !canToggle}
              className="px-4 py-2 text-sm font-medium bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              Aktiver
            </button>
          )}
        </div>
      </div>
    </>
  )
}

function IntervalToggle({
  selected,
  onChange,
}: {
  selected: BillingInterval
  onChange: (interval: BillingInterval) => void
}) {
  return (
    <fieldset className="inline-flex items-center rounded-lg border border-gray-200 bg-gray-50 p-1 m-0">
      <legend className="sr-only">Betalingsinterval</legend>
      {(['Monthly', 'Yearly'] as const).map((interval) => (
        <button
          key={interval}
          type="button"
          aria-pressed={selected === interval}
          onClick={() => onChange(interval)}
          className={`px-3 py-1.5 text-sm font-medium rounded-md transition-colors ${
            selected === interval
              ? 'bg-white text-gray-900 shadow-sm'
              : 'text-gray-500 hover:text-gray-700'
          }`}
        >
          {interval === 'Monthly' ? 'Månedligt' : 'Årligt'}
        </button>
      ))}
    </fieldset>
  )
}

function PricingCard({
  isActive,
  isTrialing,
  trialEnd,
  selectedInterval,
  onIntervalChange,
  onCheckout,
  isRedirecting,
}: {
  isActive?: boolean
  isTrialing?: boolean
  trialEnd?: string
  selectedInterval: BillingInterval
  onIntervalChange: (interval: BillingInterval) => void
  onCheckout: () => void
  isRedirecting: boolean
}) {
  const features = [
    'Ubegrænsede skemaer',
    'Op til 100 GB filer',
    'Konfliktkontrol – ingen dobbeltbookede lærere eller lokaler',
    'Support via e-mail',
    'Ingen binding',
  ]
  const isYearly = selectedInterval === 'Yearly'

  return (
    <div
      className={`bg-white rounded-xl border divide-y divide-gray-100 ${isActive || isTrialing ? 'border-brand-300 ring-1 ring-brand-200' : 'border-gray-200'}`}
    >
      <div className="px-6 py-5">
        <div className="flex items-center justify-between gap-4">
          <div>
            <div className="flex items-center gap-2">
              <h2 className="text-sm font-semibold text-gray-700">Basis</h2>
              {isActive && (
                <span className="px-2 py-0.5 text-xs font-medium rounded-full bg-green-100 text-green-700">
                  Aktiv
                </span>
              )}
              {isTrialing && !isActive && (
                <span className="px-2 py-0.5 text-xs font-medium rounded-full bg-brand-100 text-brand-700">
                  Prøveperiode
                </span>
              )}
            </div>
            <p className="mt-0.5 text-xs text-gray-400">Alt hvad din skole behøver</p>
          </div>
          <div className="text-right shrink-0">
            {isYearly ? (
              <>
                <span className="text-2xl font-semibold text-gray-900 tabular-nums">
                  {YEARLY_EFFECTIVE_MONTHLY_KR}
                </span>
                <span className="text-sm text-gray-500"> kr/md</span>
                <p className="text-xs text-gray-400 tabular-nums">{YEARLY_PRICE_KR} kr/år</p>
              </>
            ) : (
              <>
                <span className="text-2xl font-semibold text-gray-900 tabular-nums">
                  {MONTHLY_PRICE_KR}
                </span>
                <span className="text-sm text-gray-500"> kr/md</span>
              </>
            )}
          </div>
        </div>
        {!isActive && SELF_SERVE_ENABLED && (
          <div className="mt-4 flex items-center gap-3">
            <IntervalToggle selected={selectedInterval} onChange={onIntervalChange} />
            {isYearly && (
              <>
                <span className="px-2 py-0.5 text-xs font-medium rounded-full bg-green-100 text-green-700">
                  Spar {YEARLY_SAVINGS_KR} kr/år
                </span>
              </>
            )}
          </div>
        )}
      </div>
      <div className="px-6 py-5">
        <ul className="space-y-2.5">
          {features.map((feature) => (
            <li key={feature} className="flex items-start gap-2.5 text-sm text-gray-700">
              <CheckIcon />
              {feature}
            </li>
          ))}
        </ul>
      </div>
      {!isActive && SELF_SERVE_ENABLED && (
        <div className="px-6 py-4">
          <button
            onClick={onCheckout}
            disabled={isRedirecting}
            className="w-full px-4 py-2.5 text-sm font-medium bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {isRedirecting
              ? 'Vent...'
              : isTrialing && trialEnd
                ? `Abonner nu — første betaling den ${formatDate(trialEnd)}`
                : 'Køb abonnement'}
          </button>
        </div>
      )}
    </div>
  )
}
