import { useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { getApiV1AdminBackupStatusOptions } from '../../api/generated/@tanstack/react-query.gen'
import type { BackupHealth } from '../../api/generated/types.gen'

// Read-only on purpose (task 60 D6): every backup action lives in the agent's console,
// which is only reachable over an SSH tunnel. A stolen superadmin token gains nothing here.

const STALE_AFTER_MINUTES = 15

const healthLabel: Record<BackupHealth, string> = {
  Healthy: 'Alt i orden',
  Degraded: 'Advarsel',
  Unhealthy: 'Fejl',
}

const healthColors: Record<BackupHealth, string> = {
  Healthy: 'bg-green-100 text-green-700',
  Degraded: 'bg-yellow-100 text-yellow-800',
  Unhealthy: 'bg-red-100 text-red-700',
}

function minutesSince(iso: string, now: number) {
  return Math.max(0, Math.round((now - new Date(iso).getTime()) / 60_000))
}

function ago(iso: string | null | undefined, now: number) {
  if (!iso) return 'aldrig'
  const minutes = minutesSince(iso, now)
  if (minutes < 1) return 'lige nu'
  if (minutes < 60) return `for ${minutes} min siden`
  const hours = Math.round(minutes / 60)
  if (hours < 48) return `for ${hours} t siden`
  return `for ${Math.round(hours / 24)} dage siden`
}

function formatDateTime(iso: string | null | undefined) {
  if (!iso) return '—'
  return new Date(iso).toLocaleString('da-DK', {
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function problemTitle(error: unknown) {
  if (error && typeof error === 'object' && 'title' in error && typeof error.title === 'string') {
    return error.title
  }
  return 'Kunne ikke hente backup-status.'
}

export default function BackofficeBackupStatusCard() {
  const { data, isLoading, error } = useQuery({
    ...getApiV1AdminBackupStatusOptions(),
    refetchInterval: 60_000,
    retry: false,
  })

  // Re-render every 30 s so "opdateret for X min siden" keeps counting between fetches.
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 30_000)
    return () => clearInterval(id)
  }, [])

  if (isLoading) {
    return (
      <section className="mb-6 bg-white border border-gray-200 rounded-xl p-5 text-sm text-gray-500">
        Henter backup-status…
      </section>
    )
  }

  if (error || !data) {
    return (
      <section
        data-testid="backup-status-card"
        className="mb-6 bg-white border border-gray-200 rounded-xl p-5"
      >
        <h2 className="font-semibold text-gray-900">Backup</h2>
        <p className="text-sm text-gray-500 mt-1">{problemTitle(error)}</p>
      </section>
    )
  }

  const statusAge = minutesSince(data.generatedAt, now)
  const stale = statusAge > STALE_AFTER_MINUTES
  const sshCommand = data.sshTunnelCommand ?? 'ssh -L 9090:127.0.0.1:9090 <vps>'

  return (
    <section
      data-testid="backup-status-card"
      className="mb-6 bg-white border border-gray-200 rounded-xl p-5"
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <h2 className="font-semibold text-gray-900">Backup</h2>
          <span
            data-testid="backup-status-health"
            className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${healthColors[data.health]}`}
          >
            {healthLabel[data.health]}
          </span>
        </div>
        <span
          data-testid="backup-status-age"
          className={`text-xs ${stale ? 'text-red-700 font-medium' : 'text-gray-500'}`}
        >
          Opdateret {ago(data.generatedAt, now)}
          {stale && ' – agenten melder ikke status'}
        </span>
      </div>

      <dl className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 mt-4 text-sm">
        <div>
          <dt className="text-gray-500">Data sikret</dt>
          <dd className="font-medium text-gray-900">{ago(data.dataSecuredAt, now)}</dd>
        </div>
        <div>
          <dt className="text-gray-500">Seneste fulde backup</dt>
          <dd className="font-medium text-gray-900">{formatDateTime(data.lastFullBackupAt)}</dd>
        </div>
        <div>
          <dt className="text-gray-500">Seneste drill</dt>
          <dd className="font-medium text-gray-900">
            {data.lastDrillAt ? (
              <>
                {formatDateTime(data.lastDrillAt)}{' '}
                <span className={data.lastDrillOk ? 'text-green-700' : 'text-red-700'}>
                  {data.lastDrillOk ? 'OK' : 'fejlede'}
                </span>
                {data.lastDrillMinutes != null && (
                  <span className="text-gray-500"> ({Math.round(data.lastDrillMinutes)} min)</span>
                )}
              </>
            ) : (
              'aldrig'
            )}
          </dd>
        </div>
        <div>
          <dt className="text-gray-500">Ældste gendannelsespunkt</dt>
          <dd
            className={`font-medium ${data.retentionOk ? 'text-gray-900' : 'text-red-700'}`}
            data-testid="backup-status-retention"
          >
            {formatDateTime(data.oldestRestorableAt)}
            {!data.retentionOk && ` – ældre end ${data.retentionDays} dage`}
          </dd>
        </div>
      </dl>

      {data.issues && data.issues.length > 0 && (
        <ul className="mt-4 space-y-1 text-sm text-red-700 list-disc list-inside">
          {data.issues.map((issue) => (
            <li key={issue}>{issue}</li>
          ))}
        </ul>
      )}

      <div className="mt-4 text-sm text-gray-600">
        Handlinger og gendannelse sker i backup-konsollen. Åbn en SSH-tunnel og gå til{' '}
        <span className="font-mono">http://localhost:9090</span>:
        <code className="block mt-1 px-3 py-2 bg-gray-50 border border-gray-200 rounded-md font-mono text-xs text-gray-800 select-all break-all">
          {sshCommand}
        </code>
      </div>
    </section>
  )
}
