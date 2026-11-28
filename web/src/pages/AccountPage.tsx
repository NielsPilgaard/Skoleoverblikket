import { useEffect, useRef, useState } from 'react'
import type React from 'react'
import { useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1AccountSecurityOptions,
  getApiV1AccountSecurityQueryKey,
  postApiV1AccountLogoutEverywhereMutation,
  postApiV1AccountSecurityCleanupRecoveryCodesMutation,
} from '../api/generated/@tanstack/react-query.gen'
import keycloak, { logout, takeActionResult } from '../auth/keycloak'
import { Modal } from '../components/Modal'
import { usePageTitle } from '../hooks/usePageTitle'

type Done = 'password' | 'totp' | 'passkey' | 'second-codes' | 'codes' | 'app-off' | 'passkey-off'

const DONE_MESSAGES: Record<Done, string> = {
  password: 'Din adgangskode er skiftet.',
  totp: 'Godkendelses-appen er tilføjet.',
  passkey: 'Adgangsnøglen er tilføjet.',
  'second-codes': 'Tofaktor er slået til.',
  codes: 'Dine nye gendannelseskoder er gemt. De gamle virker ikke længere.',
  'app-off': 'Godkendelses-appen er fjernet.',
  'passkey-off': 'Adgangsnøglen er fjernet.',
}

const PROVIDER_NAMES: Record<string, string> = { microsoft: 'Microsoft', google: 'Google' }

/** Sends the browser to the Keycloak page for action; Keycloak returns to /min-konto?done=<done>. */
function startAction(action: string, done: Done) {
  keycloak.login({ action, redirectUri: `${window.location.origin}/min-konto?done=${done}` })
}

function formatDate(value: string | null | undefined) {
  return value
    ? new Date(value).toLocaleDateString('da-DK', {
        day: 'numeric',
        month: 'long',
        year: 'numeric',
      })
    : null
}

export default function AccountPage() {
  usePageTitle('Min konto')
  const qc = useQueryClient()
  const [searchParams, setSearchParams] = useSearchParams()
  const [notice, setNotice] = useState<{ text: string; error?: boolean } | null>(null)
  const [confirmLogout, setConfirmLogout] = useState(false)

  const { data, isLoading, isError, refetch } = useQuery(getApiV1AccountSecurityOptions())

  const cleanup = useMutation({
    ...postApiV1AccountSecurityCleanupRecoveryCodesMutation(),
    onSuccess: () => qc.invalidateQueries({ queryKey: getApiV1AccountSecurityQueryKey() }),
  })
  const logoutEverywhere = useMutation({
    ...postApiV1AccountLogoutEverywhereMutation(),
    onSuccess: () => logout({ redirectUri: window.location.origin }),
  })
  // Once sent, the logout goes through, so the dialog can't be dismissed while it runs.
  const closeLogout = () => {
    if (!logoutEverywhere.isPending) setConfirmLogout(false)
  }

  // Read the result of the Keycloak page we came back from, once per return.
  const handledReturn = useRef(false)
  useEffect(() => {
    if (handledReturn.current || !data) return
    const done = searchParams.get('done') as Done | null
    if (!done) return
    handledReturn.current = true
    setSearchParams({}, { replace: true })
    const status = takeActionResult()
    if (status === 'cancelled') return setNotice({ text: 'Intet blev ændret.' })
    if (status === 'error') return setNotice({ text: 'Noget gik galt. Prøv igen.', error: true })
    // No result means the URL was opened directly, not returned to from Keycloak.
    if (status !== 'success' || !DONE_MESSAGES[done]) return
    const hasSecondFactor = !!data.otp || !!data.passkeys?.length
    // A first app or passkey also needs recovery codes, or a lost phone becomes a support case.
    if ((done === 'totp' || done === 'passkey') && hasSecondFactor && !data.recoveryCodes) {
      return startAction('CONFIGURE_RECOVERY_AUTHN_CODES', 'second-codes')
    }
    if ((done === 'app-off' || done === 'passkey-off') && !hasSecondFactor) {
      return setNotice({ text: 'Tofaktor er slået fra.' })
    }
    setNotice({ text: DONE_MESSAGES[done] })
  }, [data, searchParams, setSearchParams])

  // Recovery codes without a second factor would still be offered at login, so remove them.
  // Runs on every visit, so a closed tab after "Slå tofaktor fra" is fixed next time.
  const cleanedUp = useRef(false)
  const cleanupMutate = cleanup.mutate
  useEffect(() => {
    if (!data) return
    // A factor added later and removed again needs a new cleanup.
    if (data.otp || data.passkeys?.length) cleanedUp.current = false
    else if (!cleanedUp.current && data.recoveryCodes) {
      cleanedUp.current = true
      cleanupMutate({})
    }
  }, [data, cleanupMutate])

  if (isLoading) {
    return <div className="p-4 md:p-6 text-sm text-gray-500">Indlæser din konto...</div>
  }

  if (isError || !data) {
    return (
      <div className="p-4 md:p-6 max-w-lg space-y-3">
        <p className="text-sm text-red-600">Kunne ikke hente din konto.</p>
        <button
          type="button"
          onClick={() => refetch()}
          className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 transition-colors"
        >
          Prøv igen
        </button>
      </div>
    )
  }

  const external = data.externalLogin
    ? (PROVIDER_NAMES[data.externalLogin] ?? data.externalLogin)
    : null
  const otpSince = formatDate(data.otp?.createdAt)
  const remaining = data.recoveryCodes?.remaining
  const passkeys = data.passkeys ?? []
  const apps = data.authenticatorApps ?? []
  const hasSecondFactor = !!data.otp || passkeys.length > 0

  return (
    <div className="p-4 md:p-6 max-w-xl space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-gray-900">Min konto</h1>
        <p className="text-sm text-gray-500 mt-1">
          Din adgangskode, tofaktor og de enheder, du er logget ind på.
        </p>
      </div>

      {cleanup.isError && (
        <div
          role="alert"
          data-testid="account-cleanup-error"
          className="rounded-lg px-4 py-3 text-sm bg-red-50 text-red-700 border border-red-200 space-y-2"
        >
          <p>Dine gendannelseskoder kunne ikke fjernes. De virker stadig, når du logger ind.</p>
          <button
            type="button"
            onClick={() => cleanup.mutate({})}
            disabled={cleanup.isPending}
            className="px-3 py-1.5 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 transition-colors"
          >
            Prøv igen
          </button>
        </div>
      )}

      {notice && (
        <p
          role="status"
          data-testid="account-notice"
          className={`rounded-lg px-4 py-3 text-sm ${
            notice.error
              ? 'bg-red-50 text-red-700 border border-red-200'
              : 'bg-brand-50 text-brand-800 border border-brand-200'
          }`}
        >
          {notice.text}
        </p>
      )}

      {external ? (
        <section
          data-testid="account-status-external"
          className="rounded-xl border border-gray-200 bg-white p-5"
        >
          <h2 className="font-semibold text-gray-900">Du logger ind med din {external}-konto</h2>
          <p className="text-sm text-gray-600 mt-1">
            Adgangskode og tofaktor styrer du hos {external}.
          </p>
        </section>
      ) : hasSecondFactor ? (
        <section
          data-testid="account-status-on"
          className="rounded-xl border border-brand-200 bg-brand-50 p-5 space-y-2"
        >
          <h2 className="font-semibold text-brand-900">Din konto er godt beskyttet</h2>
          <ul className="text-sm text-brand-900 space-y-1">
            <li>✓ Adgangskode</li>
            {data.otp && <li>✓ Tofaktor med app{otpSince && `, siden ${otpSince}`}</li>}
            {passkeys.length > 0 && (
              <li>
                ✓ {passkeys.length === 1 ? 'Adgangsnøgle' : `${passkeys.length} adgangsnøgler`}
              </li>
            )}
            {data.recoveryCodes && (
              <li>
                ✓ Gendannelseskoder
                {remaining != null && ` (${remaining} ubrugte)`}
              </li>
            )}
          </ul>
        </section>
      ) : data.recoveryCodes ? (
        // The last factor is gone, but the recovery codes still work until cleanup removes them.
        <section
          data-testid="account-status-cleanup"
          className="rounded-xl border border-gray-200 bg-white p-5"
        >
          <h2 className="font-semibold text-gray-900">Slår tofaktor fra …</h2>
          <p className="text-sm text-gray-600 mt-1">Dine gendannelseskoder bliver fjernet.</p>
        </section>
      ) : (
        <section
          data-testid="account-status-off"
          className="rounded-xl border border-amber-200 bg-amber-50 p-5 space-y-3"
        >
          <h2 className="font-semibold text-amber-900">Beskyt din konto bedre</h2>
          <p className="text-sm text-amber-900">
            Du logger ind med kun en adgangskode. Med tofaktor skal man også have din telefon eller
            computer for at komme ind. Det tager 2 minutter.
          </p>
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              data-testid="account-passkey-on"
              onClick={() => startAction('webauthn-register', 'passkey')}
              className="px-4 py-2 text-sm font-medium bg-brand-600 text-white rounded-lg hover:bg-brand-700 transition-colors"
            >
              Brug Face ID eller fingeraftryk
            </button>
            <button
              type="button"
              data-testid="account-tfa-on"
              onClick={() => startAction('CONFIGURE_TOTP', 'totp')}
              className="px-4 py-2 text-sm font-medium text-brand-800 bg-white border border-brand-200 rounded-lg hover:bg-brand-50 transition-colors"
            >
              Brug en app i stedet
            </button>
          </div>
        </section>
      )}

      <Group label="Konto">
        <div className="px-4 py-3.5">
          <p className="text-sm font-medium text-gray-900">E-mail</p>
          <p className="text-sm text-gray-500 break-all" data-testid="account-email">
            {data.email}
          </p>
        </div>
      </Group>
      <p className="-mt-4 px-1 text-xs text-gray-500">
        Kontakt skolens kontor for at ændre e-mail.
      </p>

      {!external && (
        <Group label="Sikkerhed">
          <Row
            testId="account-password"
            title={data.hasPassword ? 'Skift adgangskode' : 'Vælg en adgangskode'}
            onClick={() => startAction('UPDATE_PASSWORD', 'password')}
          />
          {apps.length ? (
            apps.map((app) => {
              const since = formatDate(app.createdAt)
              const name = app.label || 'Godkendelses-app'
              return (
                <Row
                  key={app.id}
                  testId="account-tfa-status"
                  title={name}
                  detail={since ? `Slået til siden ${since}` : 'Slået til'}
                  remove={{
                    testId: 'account-tfa-off',
                    label: app.label
                      ? `Fjern godkendelses-appen ${app.label}`
                      : 'Fjern godkendelses-appen',
                    onClick: () => startAction(`delete_credential:${app.id}`, 'app-off'),
                  }}
                />
              )
            })
          ) : (
            <Row
              testId="account-tfa-row"
              title="Godkendelses-app"
              detail="Kode fra en app på telefonen, når du logger ind"
              badge={<Badge>Fra</Badge>}
              onClick={() => startAction('CONFIGURE_TOTP', 'totp')}
            />
          )}
          {passkeys.map((passkey) => {
            const added = formatDate(passkey.createdAt)
            const name = passkey.label || 'Adgangsnøgle'
            return (
              <Row
                key={passkey.id}
                testId="account-passkey"
                title={name}
                detail={added ? `Adgangsnøgle · tilføjet ${added}` : 'Adgangsnøgle'}
                remove={{
                  testId: 'account-passkey-remove',
                  label: `Fjern adgangsnøglen ${name}`,
                  onClick: () => startAction(`delete_credential:${passkey.id}`, 'passkey-off'),
                }}
              />
            )
          })}
          <Row
            testId="account-passkey-add"
            title={passkeys.length ? 'Tilføj endnu en adgangsnøgle' : 'Tilføj adgangsnøgle'}
            detail="Log ind med Face ID, fingeraftryk eller enhedens kode"
            onClick={() => startAction('webauthn-register', 'passkey')}
          />
          {hasSecondFactor && (
            <Row
              testId="account-new-codes"
              title="Gendannelseskoder"
              detail={
                remaining != null
                  ? `${remaining} ubrugte · lav nye`
                  : data.recoveryCodes
                    ? 'Lav nye'
                    : 'Lav koder, hvis du mister telefonen'
              }
              onClick={() => startAction('CONFIGURE_RECOVERY_AUTHN_CODES', 'codes')}
            />
          )}
        </Group>
      )}

      <Group label="Enheder">
        <Row
          testId="account-logout-everywhere"
          title="Log ud på alle enheder"
          detail="Mistet telefonen? Også denne"
          onClick={() => setConfirmLogout(true)}
        />
      </Group>

      <Modal isOpen={confirmLogout} onClose={closeLogout} title="Log ud på alle enheder?" size="sm">
        <div className="px-6 py-5 space-y-4">
          <p className="text-sm text-gray-600">
            Du bliver logget ud på alle telefoner og computere, også denne. Du skal logge ind igen
            bagefter.
          </p>
          {logoutEverywhere.isError && (
            <p className="text-sm text-red-600">Det lykkedes ikke. Prøv igen.</p>
          )}
          <div className="flex justify-end gap-3 pt-2">
            <button
              type="button"
              disabled={logoutEverywhere.isPending}
              onClick={closeLogout}
              className="px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50 transition-colors"
            >
              Annuller
            </button>
            <button
              type="button"
              data-testid="account-logout-everywhere-confirm"
              disabled={logoutEverywhere.isPending}
              onClick={() => logoutEverywhere.mutate({})}
              className="px-4 py-2 text-sm font-medium bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 transition-colors"
            >
              Log ud overalt
            </button>
          </div>
        </div>
      </Modal>
    </div>
  )
}

function Group({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <section>
      <h2 className="px-1 mb-1.5 text-xs font-medium uppercase tracking-wide text-gray-500">
        {label}
      </h2>
      <div className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-200 overflow-hidden">
        {children}
      </div>
    </section>
  )
}

function Row({
  title,
  detail,
  badge,
  remove,
  onClick,
  testId,
}: {
  title: string
  detail?: string
  badge?: React.ReactNode
  /** A "Fjern" button at the end of the row; label is its accessible name. */
  remove?: { label: string; onClick: () => void; testId: string }
  onClick?: () => void
  testId: string
}) {
  const content = (
    <>
      <span className="flex-1 min-w-0">
        <span className="block text-sm font-medium text-gray-900 break-words">{title}</span>
        {detail && <span className="block text-sm text-gray-500">{detail}</span>}
      </span>
      {badge}
      {remove && (
        <button
          type="button"
          data-testid={remove.testId}
          aria-label={remove.label}
          onClick={remove.onClick}
          className="shrink-0 px-3 py-1.5 text-sm font-medium text-red-700 rounded-lg hover:bg-red-50 focus-visible:outline-2 focus-visible:outline-red-600 transition-colors"
        >
          Fjern
        </button>
      )}
      {onClick && (
        <span aria-hidden="true" className="text-lg text-gray-400">
          ›
        </span>
      )}
    </>
  )
  return onClick ? (
    <button
      type="button"
      data-testid={testId}
      onClick={onClick}
      className="w-full flex items-center gap-3 px-4 py-3.5 text-left hover:bg-brand-50 focus-visible:outline-2 focus-visible:outline-brand-600 transition-colors"
    >
      {content}
    </button>
  ) : (
    <div data-testid={testId} className="flex items-center gap-3 px-4 py-3.5">
      {content}
    </div>
  )
}

function Badge({ children }: { children: React.ReactNode }) {
  return (
    <span className="rounded-full px-2 py-0.5 text-xs font-semibold border bg-gray-50 text-gray-500 border-gray-200">
      {children}
    </span>
  )
}
