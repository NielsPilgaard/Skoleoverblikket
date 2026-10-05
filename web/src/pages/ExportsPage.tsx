import { useState } from 'react'
import { usePageTitle } from '../hooks/usePageTitle'
import keycloak from '../auth/keycloak'
import { postApiV1ExportsSchoolZipLink } from '../api/generated/sdk.gen'

interface ExportCard {
  title: string
  description: string
  path: string
  filename: string
}

const EXPORTS: ExportCard[] = [
  {
    title: 'Timer pr. medarbejder',
    description: 'Oversigt over undervisningstimer pr. lærer og pædagog',
    path: '/reports/hours/staff.xlsx',
    filename: 'timer-medarbejdere.xlsx',
  },
  {
    title: 'Timer pr. fag',
    description: 'Ugentlige timer fordelt på fag og klasse',
    path: '/reports/hours/courses.xlsx',
    filename: 'timer-fag.xlsx',
  },
  {
    title: 'Komplet skema',
    description: 'Alle aktive lektioner med dag, tid, fag, lærer og lokale',
    path: '/reports/schema.xlsx',
    filename: 'skema.xlsx',
  },
  {
    title: 'UVM minimumstimetal',
    description:
      'Sammenligning af planlagte timer med UVM minimumstimetal for dansk, matematik og historie',
    path: '/reports/uvm-minimumstimetal.xlsx',
    filename: 'uvm-minimumstimetal.xlsx',
  },
]

const FULL_EXPORT_KEY = 'full-export'

function DownloadIcon() {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
      <polyline points="7 10 12 15 17 10" />
      <line x1="12" y1="15" x2="12" y2="3" />
    </svg>
  )
}

function Spinner() {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      className="animate-spin"
      aria-hidden="true"
    >
      <path d="M21 12a9 9 0 1 1-6.219-8.56" />
    </svg>
  )
}

export default function ExportsPage() {
  usePageTitle('Eksporter')
  const [downloading, setDownloading] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function download(path: string, filename: string) {
    setError(null)
    setDownloading(filename)
    try {
      await keycloak.updateToken(30).catch(() => keycloak.login())
      // Raw fetch intentional: SDK client cannot return Blob responses (typed as unknown).
      const res = await fetch(`/api/v1${path}`, {
        headers: { Authorization: `Bearer ${keycloak.token}` },
      })
      if (!res.ok) throw new Error('Download mislykkedes')
      const blob = await res.blob()
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = filename
      a.click()
      URL.revokeObjectURL(url)
    } catch {
      setError('Filen kunne ikke hentes. Prøv igen om lidt.')
    } finally {
      setDownloading(null)
    }
  }

  // The ZIP can be large, so the browser downloads it itself from a one-minute, single-use link
  // instead of holding it all in memory like the small exports above.
  async function downloadAll() {
    setError(null)
    setDownloading(FULL_EXPORT_KEY)
    try {
      const { data } = await postApiV1ExportsSchoolZipLink({ throwOnError: true })
      window.location.assign(data.url)
    } catch {
      setError('Filen kunne ikke hentes. Prøv igen om lidt.')
    } finally {
      setDownloading(null)
    }
  }

  return (
    <div className="p-6 lg:p-8 max-w-3xl mx-auto space-y-8">
      {/* Header */}
      <div>
        <h1 className="font-display text-2xl font-semibold text-gray-900">Eksporter</h1>
        <p className="mt-1 text-sm text-gray-500">
          Download data til videre bearbejdning i Excel, eller hent alt på én gang
        </p>
      </div>

      {/* Error banner */}
      {error && (
        <div className="rounded-xl border border-red-200 bg-red-50 px-5 py-4 text-sm text-red-700">
          {error}
        </div>
      )}

      {/* Everything in one ZIP */}
      <div
        data-testid="full-export-card"
        className="flex flex-col gap-4 rounded-xl border border-brand-200 bg-brand-50 px-6 py-5 sm:flex-row sm:items-center sm:justify-between"
      >
        <div className="min-w-0">
          <p className="text-sm font-semibold text-gray-900">Alle skolens data</p>
          <p className="mt-0.5 text-sm text-gray-600">
            Én ZIP-fil med alt: klasser, elever, forældre, medarbejdere, skemaer, fravær, beskeder
            og alle uploadede filer. Gem den, hvis I stopper med Skoleoverblikket. Det kan tage et
            par minutter.
          </p>
        </div>
        <button
          type="button"
          data-testid="full-export-download"
          disabled={downloading !== null}
          onClick={downloadAll}
          className="shrink-0 inline-flex items-center justify-center gap-2 rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {downloading === FULL_EXPORT_KEY ? <Spinner /> : <DownloadIcon />}
          {downloading === FULL_EXPORT_KEY ? 'Henter…' : 'Hent alt'}
        </button>
      </div>

      {/* Export cards */}
      <div className="space-y-4">
        {EXPORTS.map((item) => {
          const isDownloading = downloading === item.filename
          return (
            <div
              key={item.filename}
              className="flex items-center justify-between gap-4 rounded-xl border border-gray-200 bg-white px-6 py-5"
            >
              <div className="min-w-0">
                <p className="text-sm font-semibold text-gray-900">{item.title}</p>
                <p className="mt-0.5 text-sm text-gray-500">{item.description}</p>
              </div>
              <button
                type="button"
                disabled={downloading !== null}
                onClick={() => download(item.path, item.filename)}
                className="shrink-0 inline-flex items-center gap-2 rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
              >
                {isDownloading ? <Spinner /> : <DownloadIcon />}
                {isDownloading ? 'Henter…' : 'Download'}
              </button>
            </div>
          )
        })}
      </div>
    </div>
  )
}
