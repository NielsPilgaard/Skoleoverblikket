import { useState, useEffect, useRef } from 'react'
import { Helmet } from 'react-helmet-async'
import SeoMeta from '../components/SeoMeta'
import keycloak, { seedPostSignupToken } from '../auth/keycloak'
import { BILLING_INTERVAL_STORAGE_KEY, parseBillingInterval } from '../lib/billingInterval'

const SELF_SERVE_ENABLED = true

interface ValidationErrors {
  name?: string
  adminEmail?: string
  adminFirstName?: string
  adminLastName?: string
  adminPassword?: string
  general?: string
}

export default function SignupPage() {
  const [name, setName] = useState('')
  const [adminFirstName, setAdminFirstName] = useState('')
  const [adminLastName, setAdminLastName] = useState('')
  const [adminEmail, setAdminEmail] = useState('')
  const [adminPassword, setAdminPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [pending, setPending] = useState(false)
  const [errors, setErrors] = useState<ValidationErrors>({})
  const redirectingRef = useRef(false)

  // If already logged in, go straight to setup
  useEffect(() => {
    if (keycloak.authenticated) {
      window.location.replace('/setup')
    }
  }, [])

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setErrors({})
    setPending(true)

    try {
      // Raw fetch intentional: response includes non-spec fields (accessToken, refreshToken) used
      // to seed Keycloak state immediately after signup, before any auth session exists.
      const res = await fetch('/api/v1/tenants', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, adminEmail, adminFirstName, adminLastName, adminPassword }),
      })

      if (res.ok) {
        const body = await res.json()
        redirectingRef.current = true
        seedPostSignupToken(body.accessToken, body.refreshToken)
        const interval = parseBillingInterval(
          new URLSearchParams(window.location.search).get('interval')
        )
        if (interval) {
          localStorage.setItem(BILLING_INTERVAL_STORAGE_KEY, interval)
        }
        window.location.href = `/setup?schoolName=${encodeURIComponent(name)}`
        return
      }

      if (res.status === 400) {
        const body = await res.json()
        const fieldErrors: ValidationErrors = {}
        for (const [field, msgs] of Object.entries(body.errors ?? {})) {
          const key = (field.charAt(0).toLowerCase() + field.slice(1)) as keyof ValidationErrors
          fieldErrors[key] = (msgs as string[])[0]
        }
        setErrors(fieldErrors)
      } else if (res.status === 502) {
        setErrors({ general: 'Der opstod en fejl ved oprettelse af brugerkonto. Prøv igen.' })
      } else {
        setErrors({ general: 'Der opstod en fejl. Prøv igen.' })
      }
    } catch {
      setErrors({ general: 'Kunne ikke oprette forbindelse til serveren.' })
    } finally {
      if (!redirectingRef.current) {
        setPending(false)
      }
    }
  }

  if (!SELF_SERVE_ENABLED) {
    return (
      <div className="min-h-screen bg-brand-50 flex items-center justify-center p-4">
        <Helmet>
          <meta name="robots" content="noindex,nofollow" />
        </Helmet>
        <div className="bg-white rounded-2xl shadow-lg w-full max-w-md text-center px-8 py-12">
          <span className="font-display text-2xl font-semibold text-brand-800">
            Skoleoverblikket
          </span>
          <h1 className="mt-4 text-lg font-semibold text-gray-900">Kommer snart</h1>
          <p className="mt-2 text-sm text-gray-500">
            Tilmelding er ikke åben endnu. Følg med for opdateringer.
          </p>
          <p className="mt-6 text-xs text-gray-400">
            Har du allerede en konto?{' '}
            <a href="/login" className="text-brand-600 hover:underline">
              Log ind
            </a>
          </p>
        </div>
      </div>
    )
  }

  return (
    <div className="min-h-screen bg-brand-50 flex items-center justify-center p-4">
      <SeoMeta
        title="Opret din skole — prøv gratis i 30 dage"
        description="Opret din skole i Skoleoverblikket på 2 minutter. 30 dages gratis prøveperiode, intet kreditkort og ingen binding."
        path="/signup"
      />
      <div className="bg-white rounded-2xl shadow-lg w-full max-w-md">
        {/* Header */}
        <div className="px-8 pt-8 pb-6 border-b border-gray-100 text-center">
          <span className="font-display text-2xl font-semibold text-brand-800">
            Skoleoverblikket
          </span>
          <h1 className="mt-3 text-lg font-semibold text-gray-900">Opret din skole</h1>
          <p className="mt-1 text-sm text-gray-500">
            30 dages gratis prøveperiode · Intet kreditkort
          </p>
        </div>

        <form onSubmit={handleSubmit} className="px-8 py-6 space-y-5">
          {/* School name */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Skolens navn *</label>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Vores Friskole"
              required
              autoFocus
              data-testid="signup-name"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
            />
            {errors.name && <p className="mt-1 text-xs text-red-600">{errors.name}</p>}
          </div>

          {/* Admin name */}
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Fornavn *</label>
              <input
                value={adminFirstName}
                onChange={(e) => setAdminFirstName(e.target.value)}
                placeholder="Anne"
                required
                data-testid="signup-first-name"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
              />
              {errors.adminFirstName && (
                <p className="mt-1 text-xs text-red-600">{errors.adminFirstName}</p>
              )}
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Efternavn *</label>
              <input
                value={adminLastName}
                onChange={(e) => setAdminLastName(e.target.value)}
                placeholder="Jensen"
                required
                data-testid="signup-last-name"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
              />
              {errors.adminLastName && (
                <p className="mt-1 text-xs text-red-600">{errors.adminLastName}</p>
              )}
            </div>
          </div>

          {/* Email */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">E-mail *</label>
            <input
              type="email"
              value={adminEmail}
              onChange={(e) => setAdminEmail(e.target.value)}
              placeholder="anne@vores-friskole.dk"
              required
              data-testid="signup-email"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
            />
            {errors.adminEmail && <p className="mt-1 text-xs text-red-600">{errors.adminEmail}</p>}
          </div>

          {/* Password */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Adgangskode *</label>
            <div className="relative">
              <input
                type={showPassword ? 'text' : 'password'}
                value={adminPassword}
                onChange={(e) => setAdminPassword(e.target.value)}
                placeholder="Mindst 8 tegn"
                required
                minLength={8}
                data-testid="signup-password"
                className="w-full px-3 py-2 pr-10 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
              />
              <button
                type="button"
                onClick={() => setShowPassword((v) => !v)}
                className="absolute right-2.5 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
                aria-label={showPassword ? 'Skjul adgangskode' : 'Vis adgangskode'}
              >
                {showPassword ? (
                  <svg
                    width="16"
                    height="16"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                  >
                    <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94" />
                    <path d="M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19" />
                    <line x1="1" y1="1" x2="23" y2="23" />
                  </svg>
                ) : (
                  <svg
                    width="16"
                    height="16"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                  >
                    <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" />
                    <circle cx="12" cy="12" r="3" />
                  </svg>
                )}
              </button>
            </div>
            {errors.adminPassword && (
              <p className="mt-1 text-xs text-red-600">{errors.adminPassword}</p>
            )}
          </div>

          {errors.general && (
            <p className="text-sm text-red-600 bg-red-50 px-3 py-2 rounded-lg">{errors.general}</p>
          )}

          <button
            type="submit"
            disabled={
              !name.trim() ||
              !adminEmail.trim() ||
              !adminFirstName.trim() ||
              !adminLastName.trim() ||
              !adminPassword ||
              pending
            }
            className="w-full py-2.5 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {pending ? 'Opretter skole…' : 'Opret skole'}
          </button>
        </form>

        <div className="px-8 pb-6 text-center">
          <p className="text-xs text-gray-400">
            Har du allerede en konto?{' '}
            <a href="/login" className="text-brand-600 hover:underline">
              Log ind
            </a>
          </p>
        </div>
      </div>
    </div>
  )
}
