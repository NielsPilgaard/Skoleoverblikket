import { useEffect, useState } from 'react'
import { useParams, useSearchParams } from 'react-router-dom'
import { Helmet } from 'react-helmet-async'
import { setActingAs, storeActingAs } from '../auth/actingAs'
import keycloak, { seedPostSignupToken } from '../auth/keycloak'
import { useAuth } from '../auth/useAuth'
import CookieBanner from '../components/CookieBanner'
import {
  postApiV1StaffInvitationsAccept,
  postApiV1ParentInvitationsAccept,
  patchApiV1ParentsMeContact,
} from '../api/generated/sdk.gen'

type InvitationType = 'staff' | 'parent'

interface InvitationPreview {
  name: string
  email: string
  schoolName: string
  expiresAt: string
  needsPassword: boolean
  type: InvitationType
}

interface StaffInvitationPreviewResponse {
  staffName: string
  email: string
  schoolName: string
  expiresAt: string
  needsPassword: boolean
}

interface ParentInvitationPreviewResponse {
  parentName: string
  email: string
  schoolName: string
  expiresAt: string
  needsPassword: boolean
}

const MIN_PASSWORD_LENGTH = 8

type PageState =
  | 'loading'
  | 'invalid'
  | 'ready'
  | 'accepting'
  | 'success'
  | 'contact-info'
  | 'error'

export default function InvitationAcceptPage() {
  const { token } = useParams<{ token: string }>()
  const { isAdmin } = useAuth()
  const [searchParams] = useSearchParams()
  const [state, setState] = useState<PageState>('loading')
  const [preview, setPreview] = useState<InvitationPreview | null>(null)
  const [errorMsg, setErrorMsg] = useState('')
  const [phone, setPhone] = useState('')
  const [address, setAddress] = useState('')
  const [postalCode, setPostalCode] = useState('')
  const [city, setCity] = useState('')
  const [shareContactInfo, setShareContactInfo] = useState(false)
  const [submittingContact, setSubmittingContact] = useState(false)
  const [contactError, setContactError] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [savingPassword, setSavingPassword] = useState(false)
  const [passwordError, setPasswordError] = useState('')

  // 'accept=1' is appended to redirectUri so we know the user just came back
  // from a fresh Keycloak login and explicitly authenticated for this invitation.
  const returningFromLogin = searchParams.get('accept') === '1'

  useEffect(() => {
    if (!token) {
      setState('invalid')
      return
    }
    const inviteToken = token

    const controller = new AbortController()

    async function loadPreview() {
      // Use plain fetch for these [AllowAnonymous] endpoints to avoid the SDK's
      // auth interceptor triggering a Keycloak login redirect for unauthenticated users.
      const staffRes = await fetch(
        `/api/v1/staff-invitations/preview?token=${encodeURIComponent(inviteToken)}`,
        { signal: controller.signal }
      )

      let normalized: InvitationPreview
      if (staffRes.status === 404) {
        const parentRes = await fetch(
          `/api/v1/parent-invitations/preview?token=${encodeURIComponent(inviteToken)}`,
          { signal: controller.signal }
        )
        if (!parentRes.ok) {
          setState('invalid')
          return
        }
        const data: ParentInvitationPreviewResponse = await parentRes.json()
        normalized = {
          name: data.parentName,
          email: data.email,
          schoolName: data.schoolName,
          expiresAt: data.expiresAt,
          needsPassword: data.needsPassword,
          type: 'parent',
        }
      } else {
        if (!staffRes.ok) {
          setState('invalid')
          return
        }
        const data: StaffInvitationPreviewResponse = await staffRes.json()
        normalized = {
          name: data.staffName,
          email: data.email,
          schoolName: data.schoolName,
          expiresAt: data.expiresAt,
          needsPassword: data.needsPassword,
          type: 'staff',
        }
      }
      setPreview(normalized)

      if (returningFromLogin && keycloak.authenticated && keycloak.token) {
        setState('accepting')
        await acceptInvitation(inviteToken, keycloak.token, normalized.type)
      } else if (keycloak.authenticated) {
        keycloak.logout({ redirectUri: window.location.href })
      } else {
        setState('ready')
      }
    }

    loadPreview().catch((err) => {
      if (err.name === 'AbortError') return
      setState('invalid')
    })

    return () => controller.abort()
  }, [token, returningFromLogin])

  async function acceptInvitation(inviteToken: string, _bearerToken: string, type: InvitationType) {
    try {
      if (type === 'staff') {
        await postApiV1StaffInvitationsAccept({
          body: { token: inviteToken, keycloakSubject: '' },
          throwOnError: true,
        })
      } else {
        await postApiV1ParentInvitationsAccept({
          query: { token: inviteToken },
          throwOnError: true,
        })
      }
      // A login can hold several roles and defaults to staff. Act as the role just accepted, so the
      // contact step and the landing page are not refused.
      storeActingAs(type)
      setActingAs(type)
      setState(type === 'parent' ? 'contact-info' : 'success')
    } catch (err: unknown) {
      const detail = (err as { detail?: string })?.detail
      setErrorMsg(
        detail ?? 'Der opstod en fejl. Invitationen er muligvis allerede brugt eller udløbet.'
      )
      setState('error')
    }
  }

  function acceptUrl() {
    const basePath = preview?.type === 'parent' ? 'parent-invitation' : 'invitation'
    return `${window.location.origin}/${basePath}/${token}?accept=1`
  }

  function handleLogin() {
    if (!token) return
    keycloak.login({ loginHint: preview?.email, redirectUri: acceptUrl(), prompt: 'login' })
  }

  /** A new invitee chooses a password; the API logs them in, so the page reloads straight into accepting. */
  async function submitPassword() {
    if (!token || !preview) return
    setSavingPassword(true)
    setPasswordError('')
    try {
      // Plain fetch, like the preview: the SDK's auth interceptor would send the anonymous user to Keycloak.
      const api = preview.type === 'parent' ? 'parent-invitations' : 'staff-invitations'
      const res = await fetch(`/api/v1/${api}/password`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ token, password }),
      })
      if (res.status === 409) {
        setPreview({ ...preview, needsPassword: false })
        return
      }
      if (!res.ok) {
        setPasswordError(
          res.status === 400
            ? `Adgangskoden skal være mindst ${MIN_PASSWORD_LENGTH} tegn.`
            : 'Der opstod en fejl. Prøv igen.'
        )
        return
      }
      const login: { accessToken: string; refreshToken?: string | null } = await res.json()
      seedPostSignupToken(login.accessToken, login.refreshToken ?? '')
      window.location.assign(acceptUrl())
    } catch {
      setPasswordError('Der opstod en fejl. Prøv igen.')
    } finally {
      setSavingPassword(false)
    }
  }

  async function submitContactInfo() {
    if (!preview) return
    setSubmittingContact(true)
    setContactError('')
    try {
      await patchApiV1ParentsMeContact({
        body: {
          name: preview.name,
          phone: phone || null,
          address: address || null,
          postalCode: postalCode || null,
          city: city || null,
          shareContactInfo,
        },
        throwOnError: true,
      })
      window.location.href = '/foraeldrevisning/skema'
    } catch (err: unknown) {
      const detail = (err as { detail?: string })?.detail
      setContactError(detail ?? 'Der opstod en fejl. Prøv igen.')
    } finally {
      setSubmittingContact(false)
    }
  }

  if (state === 'loading' || state === 'accepting') {
    return (
      <div className="min-h-screen bg-brand-50 flex items-center justify-center p-4">
        <Helmet>
          <meta name="robots" content="noindex,nofollow" />
        </Helmet>
        <div
          className="bg-white rounded-2xl shadow-lg p-8 max-w-md w-full text-center"
          role="status"
          aria-live="polite"
          aria-busy="true"
        >
          <div className="w-8 h-8 border-2 border-brand-600 border-t-transparent rounded-full animate-spin mx-auto" />
          <p className="mt-4 text-sm text-gray-500">
            {state === 'accepting' ? 'Bekræfter invitation…' : 'Indlæser invitation…'}
          </p>
        </div>
      </div>
    )
  }

  if (state === 'invalid') {
    return (
      <div className="min-h-screen bg-brand-50 flex items-center justify-center p-4">
        <Helmet>
          <meta name="robots" content="noindex,nofollow" />
        </Helmet>
        <div className="bg-white rounded-2xl shadow-lg p-8 max-w-md w-full text-center space-y-4">
          <div className="w-12 h-12 bg-red-100 rounded-full flex items-center justify-center mx-auto">
            <svg
              width="24"
              height="24"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.5"
              className="text-red-600"
            >
              <line x1="18" y1="6" x2="6" y2="18" />
              <line x1="6" y1="6" x2="18" y2="18" />
            </svg>
          </div>
          <h1 className="font-display text-xl font-semibold text-gray-900">
            Invitation ugyldig eller udløbet
          </h1>
          <p className="text-sm text-gray-500">
            Dette invitationslink er ugyldigt eller er udløbet. Bed din administrator om at sende en
            ny invitation.
          </p>
        </div>
      </div>
    )
  }

  if (state === 'success') {
    const destination = isAdmin ? '/dashboard' : '/mig/skema'

    return (
      <div className="min-h-screen bg-brand-50 flex items-center justify-center p-4">
        <Helmet>
          <meta name="robots" content="noindex,nofollow" />
        </Helmet>
        <div className="bg-white rounded-2xl shadow-lg p-8 max-w-md w-full text-center space-y-4">
          <div className="w-12 h-12 bg-green-100 rounded-full flex items-center justify-center mx-auto">
            <svg
              width="24"
              height="24"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.5"
              className="text-green-600"
            >
              <polyline points="20 6 9 17 4 12" />
            </svg>
          </div>
          <h1 className="font-display text-xl font-semibold text-gray-900">
            Invitation accepteret!
          </h1>
          <p className="text-sm text-gray-500">
            Du er nu tilknyttet <strong>{preview?.schoolName}</strong>. Du kan gå til dit skema
            herunder.
          </p>
          <a
            href={destination}
            className="inline-block mt-2 px-5 py-2.5 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors"
          >
            Gå til mit skema
          </a>
        </div>
      </div>
    )
  }

  if (state === 'error') {
    return (
      <div className="min-h-screen bg-brand-50 flex items-center justify-center p-4">
        <Helmet>
          <meta name="robots" content="noindex,nofollow" />
        </Helmet>
        <div className="bg-white rounded-2xl shadow-lg p-8 max-w-md w-full text-center space-y-4">
          <div className="w-12 h-12 bg-red-100 rounded-full flex items-center justify-center mx-auto">
            <svg
              width="24"
              height="24"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.5"
              className="text-red-600"
            >
              <line x1="18" y1="6" x2="6" y2="18" />
              <line x1="6" y1="6" x2="18" y2="18" />
            </svg>
          </div>
          <h1 className="font-display text-xl font-semibold text-gray-900">Noget gik galt</h1>
          <p className="text-sm text-gray-500">{errorMsg}</p>
        </div>
      </div>
    )
  }

  if (state === 'contact-info') {
    return (
      <div className="min-h-screen bg-brand-50 flex items-center justify-center p-4">
        <Helmet>
          <meta name="robots" content="noindex,nofollow" />
        </Helmet>
        <div className="bg-white rounded-2xl shadow-lg w-full max-w-md">
          <div className="px-8 pt-8 pb-6 border-b border-gray-100 text-center">
            <div className="w-12 h-12 bg-green-100 rounded-full flex items-center justify-center mx-auto mb-4">
              <svg
                width="24"
                height="24"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2.5"
                className="text-green-600"
              >
                <polyline points="20 6 9 17 4 12" />
              </svg>
            </div>
            <h1 className="font-display text-xl font-semibold text-gray-900">
              Invitation accepteret!
            </h1>
            <p className="mt-1 text-sm text-gray-500">
              Udfyld dine kontaktoplysninger — du kan altid ændre dem senere.
            </p>
          </div>

          <form
            className="px-8 py-6 space-y-4"
            onSubmit={(e) => {
              e.preventDefault()
              submitContactInfo()
            }}
          >
            <div>
              <label htmlFor="phone" className="block text-sm font-medium text-gray-700 mb-1">
                Telefon
              </label>
              <input
                id="phone"
                name="tel"
                type="tel"
                autoComplete="tel"
                value={phone}
                onChange={(e) => setPhone(e.target.value)}
                placeholder="Valgfrit"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
              />
            </div>
            <div>
              <label htmlFor="address" className="block text-sm font-medium text-gray-700 mb-1">
                Adresse
              </label>
              <input
                id="address"
                name="street-address"
                type="text"
                autoComplete="street-address"
                value={address}
                onChange={(e) => setAddress(e.target.value)}
                placeholder="Valgfrit"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
              />
            </div>
            <div className="flex gap-3">
              <div className="w-28">
                <label
                  htmlFor="postalCode"
                  className="block text-sm font-medium text-gray-700 mb-1"
                >
                  Postnummer
                </label>
                <input
                  id="postalCode"
                  name="postal-code"
                  type="text"
                  autoComplete="postal-code"
                  value={postalCode}
                  onChange={(e) => setPostalCode(e.target.value)}
                  placeholder="Valgfrit"
                  maxLength={10}
                  className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
                />
              </div>
              <div className="flex-1">
                <label htmlFor="city" className="block text-sm font-medium text-gray-700 mb-1">
                  By
                </label>
                <input
                  id="city"
                  name="address-level2"
                  type="text"
                  autoComplete="address-level2"
                  value={city}
                  onChange={(e) => setCity(e.target.value)}
                  placeholder="Valgfrit"
                  className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
                />
              </div>
            </div>
            <label className="flex items-start gap-3 cursor-pointer">
              <input
                id="shareContactInfo"
                name="shareContactInfo"
                type="checkbox"
                checked={shareContactInfo}
                onChange={(e) => setShareContactInfo(e.target.checked)}
                className="mt-0.5 h-4 w-4 rounded border-gray-300 text-brand-600 focus:ring-brand-500"
              />
              <span className="text-sm text-gray-700">
                Tillad andre forældre at se mine kontaktoplysninger
              </span>
            </label>

            {contactError && <p className="text-sm text-red-600">{contactError}</p>}
            <button
              type="submit"
              data-testid="invitation-contact-submit"
              disabled={submittingContact}
              className="w-full py-2.5 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors disabled:opacity-50"
            >
              {submittingContact ? 'Gemmer…' : 'Gem og fortsæt'}
            </button>
            <button
              type="button"
              onClick={() => {
                window.location.href = '/foraeldrevisning/skema'
              }}
              className="w-full py-2 text-sm text-gray-500 hover:text-gray-700 transition-colors"
            >
              Spring over
            </button>
          </form>
        </div>
      </div>
    )
  }

  // state === 'ready' — show invitation details and login button
  return (
    <>
      <Helmet>
        <meta name="robots" content="noindex,nofollow" />
      </Helmet>
      <div className="min-h-screen bg-brand-50 flex items-center justify-center p-4">
        <div className="bg-white rounded-2xl shadow-lg w-full max-w-md">
          <div className="px-8 pt-8 pb-6 border-b border-gray-100 text-center">
            <span className="font-display text-2xl font-semibold text-brand-800">
              Skoleoverblikket
            </span>
            <h1 className="mt-3 text-lg font-semibold text-gray-900">Du er inviteret!</h1>
            <p className="mt-1 text-sm text-gray-500">
              {preview?.schoolName} har inviteret dig til at oprette en konto
            </p>
          </div>

          <div className="px-8 py-6 space-y-4">
            <div className="bg-brand-50 rounded-xl p-4 space-y-2">
              <div className="flex items-center gap-2 text-sm">
                <span className="text-gray-500 w-20 shrink-0">Navn</span>
                <span className="font-medium text-gray-900">{preview?.name}</span>
              </div>
              <div className="flex items-center gap-2 text-sm">
                <span className="text-gray-500 w-20 shrink-0">E-mail</span>
                <span className="font-medium text-gray-900">{preview?.email}</span>
              </div>
              <div className="flex items-center gap-2 text-sm">
                <span className="text-gray-500 w-20 shrink-0">Skole</span>
                <span className="font-medium text-gray-900">{preview?.schoolName}</span>
              </div>
            </div>

            {preview?.needsPassword ? (
              <form
                className="space-y-4"
                onSubmit={(e) => {
                  e.preventDefault()
                  submitPassword()
                }}
              >
                {/* Lets the browser's password manager save the new password under the right email. */}
                <input
                  type="email"
                  name="username"
                  autoComplete="username"
                  value={preview.email}
                  readOnly
                  hidden
                />
                <div>
                  <label
                    htmlFor="new-password"
                    className="block text-sm font-medium text-gray-700 mb-1"
                  >
                    Vælg din adgangskode
                  </label>
                  <div className="relative">
                    <input
                      id="new-password"
                      name="new-password"
                      type={showPassword ? 'text' : 'password'}
                      autoComplete="new-password"
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
                      data-testid="invitation-password"
                      className="w-full px-3 py-2 pr-16 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
                    />
                    <button
                      type="button"
                      onClick={() => setShowPassword((v) => !v)}
                      className="absolute inset-y-0 right-0 px-3 text-sm text-gray-500 hover:text-gray-700"
                    >
                      {showPassword ? 'Skjul' : 'Vis'}
                    </button>
                  </div>
                  <p className="mt-1 text-xs text-gray-500">
                    Mindst {MIN_PASSWORD_LENGTH} tegn. Du bruger den, når du logger ind.
                  </p>
                </div>
                {passwordError && <p className="text-sm text-red-600">{passwordError}</p>}
                <button
                  type="submit"
                  data-testid="invitation-password-submit"
                  disabled={savingPassword || password.length < MIN_PASSWORD_LENGTH}
                  className="w-full py-2.5 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  {savingPassword ? 'Opretter konto…' : 'Opret konto og acceptér'}
                </button>
              </form>
            ) : (
              <>
                <p className="text-sm text-gray-500">
                  Du har allerede en konto. Log ind med din adgangskode for at acceptere
                  invitationen.
                </p>
                <button
                  onClick={handleLogin}
                  data-testid="invitation-login"
                  className="w-full py-2.5 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors"
                >
                  Log ind og acceptér
                </button>
              </>
            )}
          </div>
        </div>
      </div>
      <CookieBanner />
    </>
  )
}
