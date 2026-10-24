import { useEffect, useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { Helmet } from 'react-helmet-async'
import Logo from '../components/Logo'
import { storeActingAs } from '../auth/actingAs'
import { seedPostSignupToken } from '../auth/keycloak'
import {
  getApiV1BoardInvitationsPreview,
  postApiV1BoardInvitationsByTokenAccept,
  postApiV1BoardInvitationsPassword,
} from '../api/generated/sdk.gen'

interface InvitationPreview {
  boardMemberName: string
  email: string
  schoolName: string
  expiresAt: string
  needsPassword: boolean
}

const MIN_PASSWORD_LENGTH = 8

export default function BoardInvitationPage() {
  const { token } = useParams<{ token: string }>()
  const navigate = useNavigate()
  const [preview, setPreview] = useState<InvitationPreview | null>(null)
  const [loading, setLoading] = useState(true)
  const [accepting, setAccepting] = useState(false)
  const [error, setError] = useState<{ title: string; message: string } | null>(null)
  const [accepted, setAccepted] = useState(false)
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)

  useEffect(() => {
    if (!token) {
      setLoading(false)
      setError({ title: 'Ugyldigt link', message: 'Ugyldigt invitationslink' })
      return
    }
    getApiV1BoardInvitationsPreview({ query: { token } })
      .then((res) => {
        // 503: the login service is down. The link is fine, so don't call it invalid.
        if (res.response?.status === 503) {
          setError({
            title: 'Prøv igen om lidt',
            message: 'Vi kan ikke hente invitationen lige nu. Vent et øjeblik, og genindlæs siden.',
          })
          return
        }
        if (res.error) throw new Error('Ugyldigt eller udløbet link')
        setPreview((res.data as InvitationPreview) ?? null)
      })
      .catch((err: Error) => setError({ title: 'Ugyldigt link', message: err.message }))
      .finally(() => setLoading(false))
  }, [token])

  async function handleAccept() {
    if (!token || !preview) return
    setAccepting(true)
    setError(null)
    try {
      // A new invitee chooses their password first; the API logs them in.
      let login: { accessToken: string; refreshToken?: string | null } | undefined
      if (preview.needsPassword) {
        const pw = await postApiV1BoardInvitationsPassword({ body: { token, password } })
        if (pw.error) {
          if (pw.response.status === 409) {
            setPreview({ ...preview, needsPassword: false })
          } else {
            setError({
              title: 'Adgangskoden blev ikke gemt',
              message:
                pw.response.status === 400
                  ? `Adgangskoden skal være mindst ${MIN_PASSWORD_LENGTH} tegn.`
                  : 'Linket er ugyldigt eller udløbet.',
            })
          }
          return
        }
        login = pw.data
      }

      const res = await postApiV1BoardInvitationsByTokenAccept({ path: { token } })
      if (res.error) {
        // 403: the school no longer has the board module, and the detail says so.
        const detail = (res.error as { detail?: string }).detail
        setError(
          res.response.status === 403
            ? { title: 'Modulet er ikke aktivt', message: detail ?? 'Kontakt skolens kontor.' }
            : { title: 'Ugyldigt link', message: detail ?? 'Invitation kunne ikke accepteres' }
        )
        return
      }
      storeActingAs('board')
      if (login) {
        seedPostSignupToken(login.accessToken, login.refreshToken ?? '')
        window.location.assign('/bestyrelse/oversigt')
        return
      }
      setAccepted(true)
    } catch {
      setError({ title: 'Ugyldigt link', message: 'Der opstod en fejl' })
    } finally {
      setAccepting(false)
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50 px-4">
      <Helmet>
        <meta name="robots" content="noindex,nofollow" />
      </Helmet>
      <div className="max-w-md w-full bg-white rounded-2xl shadow-sm border border-gray-200 p-8 space-y-6">
        <div className="flex justify-center">
          <Logo variant="light" size={40} />
        </div>

        {loading ? (
          <div className="animate-pulse space-y-3">
            <div className="h-5 bg-gray-200 rounded w-3/4 mx-auto" />
            <div className="h-4 bg-gray-100 rounded w-1/2 mx-auto" />
          </div>
        ) : error ? (
          <div className="text-center">
            <p className="font-semibold text-gray-900" data-testid="board-invitation-error-title">
              {error.title}
            </p>
            <p className="mt-1 text-sm text-gray-500">{error.message}</p>
            <button
              onClick={() => navigate('/')}
              className="mt-4 px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 transition-colors"
            >
              Gå til forsiden
            </button>
          </div>
        ) : accepted ? (
          <div className="text-center" data-testid="board-invitation-accepted">
            <p className="font-semibold text-gray-900">Invitation accepteret</p>
            <p className="mt-1 text-sm text-gray-500">Log ind med din e-mail og din adgangskode.</p>
            <button
              onClick={() => navigate('/login')}
              className="mt-4 px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 transition-colors"
            >
              Log ind
            </button>
          </div>
        ) : preview ? (
          <div className="text-center space-y-4" data-testid="board-invitation-preview">
            <div>
              <p className="font-semibold text-gray-900">Du er inviteret til bestyrelsen</p>
              <p className="mt-1 text-sm text-gray-500">
                {preview.schoolName} har inviteret {preview.boardMemberName} til bestyrelsesmodulet
                på Skoleoverblikket.
              </p>
            </div>
            <div className="bg-gray-50 rounded-xl p-4 text-left text-sm space-y-1">
              <p className="text-gray-500">
                E-mail: <span className="text-gray-900">{preview.email}</span>
              </p>
              <p className="text-gray-500">
                Skole: <span className="text-gray-900">{preview.schoolName}</span>
              </p>
            </div>
            <p className="text-xs text-gray-400">
              Linket udløber {new Date(preview.expiresAt).toLocaleDateString('da-DK')}
            </p>
            {preview.needsPassword && (
              <div className="text-left">
                {/* Lets the browser's password manager save the new password under the right email. */}
                <input
                  type="email"
                  name="username"
                  autoComplete="username"
                  value={preview.email}
                  readOnly
                  hidden
                />
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
                    data-testid="board-invitation-password"
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
            )}
            <button
              data-testid="board-invitation-accept"
              onClick={handleAccept}
              disabled={
                accepting || (preview.needsPassword && password.length < MIN_PASSWORD_LENGTH)
              }
              className="w-full px-4 py-2.5 text-sm font-medium bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {accepting ? 'Accepterer...' : 'Acceptér invitation'}
            </button>
          </div>
        ) : null}
      </div>
    </div>
  )
}
