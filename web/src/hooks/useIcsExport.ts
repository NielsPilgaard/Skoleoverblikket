import { useEffect, useRef, useState } from 'react'
import keycloak, { refreshToken } from '../auth/keycloak'
import { apiFetch } from '../api/outage'
import { actingAsHeaders } from '../auth/actingAs'

export function useIcsExport() {
  const [exportPending, setExportPending] = useState(false)
  const [exportDone, setExportDone] = useState(false)
  const [exportError, setExportError] = useState(false)
  const exportTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(() => {
    return () => {
      if (exportTimeoutRef.current) clearTimeout(exportTimeoutRef.current)
    }
  }, [])

  async function handleExportIcs() {
    if (exportPending) return
    setExportPending(true)
    setExportDone(false)
    setExportError(false)
    try {
      await refreshToken(30)
      // Raw fetch intentional: SDK client cannot return Blob responses (typed as unknown).
      const res = await apiFetch('/api/v1/calendar/export.ics', {
        headers: { Authorization: `Bearer ${keycloak.token}`, ...actingAsHeaders() },
      })
      if (!res.ok) throw new Error('Export fejlede')
      const blob = await res.blob()
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = 'skoleoverblikket-kalender.ics'
      a.style.display = 'none'
      document.body.appendChild(a)
      a.click()
      document.body.removeChild(a)
      URL.revokeObjectURL(url)
      setExportDone(true)
      if (exportTimeoutRef.current) clearTimeout(exportTimeoutRef.current)
      exportTimeoutRef.current = setTimeout(() => {
        setExportDone(false)
        exportTimeoutRef.current = null
      }, 8000)
    } catch {
      setExportError(true)
    } finally {
      setExportPending(false)
    }
  }

  return { exportPending, exportDone, exportError, handleExportIcs }
}
