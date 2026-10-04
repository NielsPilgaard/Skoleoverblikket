import { useState, useEffect, useRef } from 'react'
import keycloak from '../../auth/keycloak'

type EmailType =
  | 'staff-invitation'
  | 'parent-invitation'
  | 'notification'
  | 'deletion-warning'
  | 'sub-processor-notice'

const EMAIL_TYPES: { value: EmailType; label: string }[] = [
  { value: 'staff-invitation', label: 'Medarbejder-invitation' },
  { value: 'parent-invitation', label: 'Forældre-invitation' },
  { value: 'notification', label: 'Notifikation' },
  { value: 'deletion-warning', label: 'Varsel om sletning (7 dage før)' },
  { value: 'sub-processor-notice', label: 'Varsel om underdatabehandlere' },
]

/** `?type=deletion-warning` opens that preview directly, so it can be linked to. */
function initialType(): EmailType {
  const t = new URLSearchParams(window.location.search).get('type')
  return EMAIL_TYPES.some((e) => e.value === t) ? (t as EmailType) : 'staff-invitation'
}

async function fetchEmailPreview(type: EmailType, params: Record<string, string>): Promise<string> {
  await keycloak.updateToken(30).catch(() => {
    keycloak.login()
  })
  const qs = new URLSearchParams(params).toString()
  // Raw fetch intentional: endpoint returns text/html, not JSON; SDK client cannot handle non-JSON responses.
  const res = await fetch(`/api/v1/admin/email-preview/${type}?${qs}`, {
    headers: { Authorization: `Bearer ${keycloak.token}` },
  })
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
  return res.text()
}

export default function BackofficeEmailPreviewPage() {
  const [type, setType] = useState<EmailType>(initialType)
  const [name, setName] = useState('Mette Hansen')
  const [school, setSchool] = useState('Testskolen')
  const [withPassword, setWithPassword] = useState(true)
  const [notificationBody, setNotificationBody] = useState(
    'Dit barns skema er blevet opdateret for uge 22.'
  )
  const [change, setChange] = useState(
    'Vi tilføjer Alexandra Instituttet (Danmark) til AI-forslag til skemaer.'
  )
  const [html, setHtml] = useState('')
  const [error, setError] = useState<string | null>(null)
  const iframeRef = useRef<HTMLIFrameElement>(null)

  useEffect(() => {
    const params: Record<string, string> = {}
    if (type === 'notification') {
      params.body = notificationBody
    } else if (type === 'deletion-warning') {
      params.school = school
    } else if (type === 'sub-processor-notice') {
      params.change = change
    } else {
      params.name = name
      params.school = school
      params.withPassword = String(withPassword)
    }

    fetchEmailPreview(type, params)
      .then((h) => {
        setHtml(h)
        setError(null)
      })
      .catch((e) => setError(String(e)))
  }, [type, name, school, withPassword, notificationBody, change])

  const isInvitation = type === 'staff-invitation' || type === 'parent-invitation'

  useEffect(() => {
    if (iframeRef.current) {
      iframeRef.current.srcdoc = html
    }
  }, [html])

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-gray-900">E-mail forhåndsvisning</h1>
        <p className="text-sm text-gray-500 mt-1">Preview af udgående e-mails med testdata.</p>
      </div>

      <div className="bg-white border border-gray-200 rounded-lg p-4 space-y-4">
        <div className="flex flex-wrap gap-4 items-end">
          <div>
            <label className="block text-xs font-medium text-gray-700 mb-1">E-mailtype</label>
            <select
              value={type}
              onChange={(e) => setType(e.target.value as EmailType)}
              className="border border-gray-300 rounded-md px-3 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
            >
              {EMAIL_TYPES.map((t) => (
                <option key={t.value} value={t.value}>
                  {t.label}
                </option>
              ))}
            </select>
          </div>

          {isInvitation && (
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">Navn</label>
              <input
                type="text"
                value={name}
                onChange={(e) => setName(e.target.value)}
                className="border border-gray-300 rounded-md px-3 py-1.5 text-sm w-44 focus:outline-none focus:ring-2 focus:ring-indigo-500"
              />
            </div>
          )}

          {(isInvitation || type === 'deletion-warning') && (
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">Skole</label>
              <input
                type="text"
                value={school}
                onChange={(e) => setSchool(e.target.value)}
                className="border border-gray-300 rounded-md px-3 py-1.5 text-sm w-44 focus:outline-none focus:ring-2 focus:ring-indigo-500"
              />
            </div>
          )}

          {isInvitation && (
            <label className="flex items-center gap-2 text-sm text-gray-700 pb-1.5">
              <input
                type="checkbox"
                checked={withPassword}
                onChange={(e) => setWithPassword(e.target.checked)}
                className="rounded"
              />
              Med midlertidig adgangskode
            </label>
          )}

          {type === 'sub-processor-notice' && (
            <div className="flex-1 min-w-64">
              <label className="block text-xs font-medium text-gray-700 mb-1">Ændringen</label>
              <input
                type="text"
                value={change}
                onChange={(e) => setChange(e.target.value)}
                className="border border-gray-300 rounded-md px-3 py-1.5 text-sm w-full focus:outline-none focus:ring-2 focus:ring-indigo-500"
              />
            </div>
          )}

          {type === 'notification' && (
            <div className="flex-1 min-w-64">
              <label className="block text-xs font-medium text-gray-700 mb-1">Beskedtekst</label>
              <input
                type="text"
                value={notificationBody}
                onChange={(e) => setNotificationBody(e.target.value)}
                className="border border-gray-300 rounded-md px-3 py-1.5 text-sm w-full focus:outline-none focus:ring-2 focus:ring-indigo-500"
              />
            </div>
          )}
        </div>
      </div>

      {error && (
        <div className="bg-red-50 border border-red-200 rounded-md px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      <div className="bg-white border border-gray-200 rounded-lg overflow-hidden">
        <iframe
          ref={iframeRef}
          title="E-mail forhåndsvisning"
          className="w-full"
          style={{ height: '640px', border: 'none' }}
          sandbox="allow-same-origin"
        />
      </div>
    </div>
  )
}
