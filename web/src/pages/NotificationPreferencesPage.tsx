import { useCallback, useEffect, useState } from 'react'
import {
  getApiV1NotificationPreferences,
  putApiV1NotificationPreferences,
} from '../api/generated/sdk.gen'
import type { NotificationType } from '../api/client'

interface PreferenceState {
  type: NotificationType
  inApp: boolean
  email: boolean
}

const ALL_TYPES: NotificationType[] = [
  'NewMessage',
  'NewContactMessage',
  'GroupMessage',
  'WeekPlanChanged',
  'AbsenceConfirmed',
  'AbsenceDismissed',
  'VacationRegistrationOpened',
  'ClassChatMessage',
]

const TYPE_LABELS: Record<NotificationType, string> = {
  NewMessage: 'Ny besked',
  NewContactMessage: 'Ny besked i kontaktbog',
  GroupMessage: 'Gruppebesked',
  WeekPlanChanged: 'Ugeplanen opdateret',
  AbsenceConfirmed: 'Fravær bekræftet',
  AbsenceDismissed: 'Fravær afvist',
  VacationRegistrationOpened: 'Ferietilmelding åbnet',
  ClassChatMessage: 'Ny besked i klassechat',
}

/**
 * Mirrors NotificationDefaults on the API: class chat is high-volume, so its email
 * notifications are opt-in while every other type stays opt-out.
 */
const EMAIL_ON_BY_DEFAULT: Record<NotificationType, boolean> = {
  NewMessage: true,
  NewContactMessage: true,
  GroupMessage: true,
  WeekPlanChanged: true,
  AbsenceConfirmed: true,
  AbsenceDismissed: true,
  VacationRegistrationOpened: true,
  ClassChatMessage: false,
}

function buildDefaultPreferences(): PreferenceState[] {
  return ALL_TYPES.map((type) => ({ type, inApp: true, email: EMAIL_ON_BY_DEFAULT[type] }))
}

function mergeWithDefaults(
  loaded: Array<{ type: NotificationType; inApp: boolean; email: boolean }>
): PreferenceState[] {
  const map = new Map(loaded.map((p) => [p.type, p]))
  return ALL_TYPES.map((type) => {
    const p = map.get(type)
    return p
      ? { type, inApp: p.inApp, email: p.email }
      : { type, inApp: true, email: EMAIL_ON_BY_DEFAULT[type] }
  })
}

export default function NotificationPreferencesPage() {
  const [prefs, setPrefs] = useState<PreferenceState[]>(buildDefaultPreferences)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  const fetchPrefs = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const { data } = await getApiV1NotificationPreferences({ throwOnError: true })
      if (data) {
        setPrefs(
          mergeWithDefaults(
            data as Array<{ type: NotificationType; inApp: boolean; email: boolean }>
          )
        )
      }
    } catch {
      setError('Kunne ikke indlæse notifikationspræferencer.')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void fetchPrefs()
  }, [fetchPrefs])

  async function handleSave() {
    setSaving(true)
    setError(null)
    setSaved(false)
    try {
      await putApiV1NotificationPreferences({ body: prefs, throwOnError: true })
      setSaved(true)
      setTimeout(() => setSaved(false), 3000)
    } catch {
      setError('Kunne ikke gemme notifikationspræferencer.')
    } finally {
      setSaving(false)
    }
  }

  function toggle(type: NotificationType, field: 'inApp' | 'email') {
    setPrefs((prev) => prev.map((p) => (p.type === type ? { ...p, [field]: !p[field] } : p)))
    setSaved(false)
  }

  return (
    <div className="max-w-2xl mx-auto px-4 py-8">
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Notifikationer</h1>
        <p className="mt-1 text-sm text-gray-600">
          Vælg hvordan du vil modtage notifikationer for hver hændelsestype.
        </p>
      </div>

      {error && (
        <div className="mb-4 rounded-md bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      {loading ? (
        <div className="text-sm text-gray-500">Indlæser...</div>
      ) : (
        <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
          {/* Table header */}
          <div className="grid grid-cols-[1fr_auto_auto] gap-4 px-5 py-3 bg-gray-50 border-b border-gray-200">
            <span className="text-xs font-semibold uppercase tracking-wide text-gray-500">
              Hændelse
            </span>
            <span className="text-xs font-semibold uppercase tracking-wide text-gray-500 w-16 text-center">
              I appen
            </span>
            <span className="text-xs font-semibold uppercase tracking-wide text-gray-500 w-16 text-center">
              E-mail
            </span>
          </div>

          {/* Rows */}
          {prefs.map((pref, idx) => (
            <div
              key={pref.type}
              className={`grid grid-cols-[1fr_auto_auto] gap-4 items-center px-5 py-4 ${
                idx < prefs.length - 1 ? 'border-b border-gray-100' : ''
              }`}
            >
              <span className="text-sm text-gray-800">{TYPE_LABELS[pref.type]}</span>
              <div className="w-16 flex justify-center">
                <Toggle
                  checked={pref.inApp}
                  onChange={() => toggle(pref.type, 'inApp')}
                  label={`${TYPE_LABELS[pref.type]} i appen`}
                />
              </div>
              <div className="w-16 flex justify-center">
                <Toggle
                  checked={pref.email}
                  onChange={() => toggle(pref.type, 'email')}
                  label={`${TYPE_LABELS[pref.type]} e-mail`}
                />
              </div>
            </div>
          ))}
        </div>
      )}

      <div className="mt-6 flex items-center gap-3">
        <button
          onClick={() => void handleSave()}
          disabled={saving || loading}
          className="px-5 py-2 rounded-lg bg-brand-600 text-white text-sm font-medium hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          data-testid="save-preferences"
        >
          {saving ? 'Gemmer...' : 'Gem præferencer'}
        </button>
        {saved && <span className="text-sm text-green-600 font-medium">Gemt!</span>}
      </div>
    </div>
  )
}

interface ToggleProps {
  checked: boolean
  onChange: () => void
  label: string
}

function Toggle({ checked, onChange, label }: ToggleProps) {
  return (
    <button
      role="switch"
      aria-checked={checked}
      aria-label={label}
      onClick={onChange}
      className={`relative inline-flex h-5 w-9 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-offset-2 ${
        checked ? 'bg-brand-600' : 'bg-gray-200'
      }`}
    >
      <span
        className={`pointer-events-none inline-block h-4 w-4 transform rounded-full bg-white shadow-sm ring-0 transition duration-200 ${
          checked ? 'translate-x-4' : 'translate-x-0'
        }`}
      />
    </button>
  )
}
