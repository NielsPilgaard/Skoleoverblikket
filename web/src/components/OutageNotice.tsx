import { useEffect, useState } from 'react'
import { useOutage } from '../api/outage'

/**
 * Shown while requests wait for the API or Keycloak to come back, usually during a deploy.
 * Nothing to click: the waiting requests carry on by themselves.
 */
export default function OutageNotice() {
  const down = useOutage()
  const [visible, setVisible] = useState(false)

  // A blip shorter than a second is not worth a notice.
  useEffect(() => {
    if (!down) {
      setVisible(false)
      return
    }
    const timer = setTimeout(() => setVisible(true), 1_000)
    return () => clearTimeout(timer)
  }, [down])

  if (!visible) return null

  return (
    <div
      role="status"
      data-testid="outage-notice"
      className="fixed inset-x-0 bottom-4 z-50 flex justify-center px-4 pointer-events-none"
    >
      <div className="flex items-center gap-3 max-w-md rounded-xl bg-white px-4 py-3 shadow-lg ring-1 ring-gray-200">
        <span
          aria-hidden="true"
          className="h-5 w-5 shrink-0 rounded-full border-2 border-brand-100 border-t-brand-600 animate-spin motion-reduce:animate-none"
        />
        <div className="text-sm">
          <p className="font-semibold text-gray-900">Vi opdaterer Skoleoverblikket</p>
          <p className="text-gray-500">
            Det tager som regel under et minut. Bliv bare på siden, den fortsætter selv.
          </p>
        </div>
      </div>
    </div>
  )
}
