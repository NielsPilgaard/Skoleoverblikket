import { useState, useEffect } from 'react'

const STORAGE_KEY = 'cookie_consent'

export default function CookieBanner() {
  const [visible, setVisible] = useState(false)

  useEffect(() => {
    if (!localStorage.getItem(STORAGE_KEY)) {
      setVisible(true)
    }
  }, [])

  if (!visible) return null

  function dismiss() {
    localStorage.setItem(STORAGE_KEY, '1')
    setVisible(false)
  }

  return (
    <>
      {/* Takes up the banner's height at the end of the page, so the footer can scroll above it. */}
      <div aria-hidden className="h-36 sm:h-20" />
      <div className="fixed bottom-0 left-0 right-0 z-50 bg-brand-900 text-brand-100 px-6 py-4 flex flex-col sm:flex-row items-center justify-between gap-4 shadow-lg">
        <p className="text-sm text-center sm:text-left">
          Vi bruger kun de cookies, der er nødvendige for login. Ingen sporing, ingen tredjeparts
          cookies.
        </p>
        <button
          data-testid="cookie-banner-dismiss"
          onClick={dismiss}
          className="shrink-0 px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors"
        >
          Forstået
        </button>
      </div>
    </>
  )
}
