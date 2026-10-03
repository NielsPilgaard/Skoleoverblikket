import { useState } from 'react'
import { Link, Outlet } from 'react-router-dom'
import Sidebar from './Sidebar'
import Logo from './Logo'
import ErrorBoundary from './ErrorBoundary'
import DataProcessingAgreementBanner from './DataProcessingAgreementBanner'
import NotificationBell from './NotificationBell'
import { useQuery } from '@tanstack/react-query'
import { getApiV1BillingSubscriptionOptions } from '../api/generated/@tanstack/react-query.gen'
import { useAuth } from '../auth/useAuth'

function SubscriptionBanner() {
  const { isAdmin } = useAuth()
  const { data } = useQuery({ ...getApiV1BillingSubscriptionOptions(), enabled: isAdmin })

  if (!isAdmin || !data || data.hasAccess) return null

  return (
    <div className="bg-red-50 border-b border-red-200 text-red-800 px-4 py-2.5 flex items-center justify-between gap-4 shrink-0">
      <p className="text-sm font-medium">Abonnement udløbet — abonner for at foretage ændringer.</p>
      <Link
        to="/abonnement"
        className="shrink-0 px-3 py-1 text-xs font-semibold bg-red-700 text-white rounded-lg hover:bg-red-800 transition-colors"
      >
        Forny abonnement
      </Link>
    </div>
  )
}

export default function Layout() {
  const [sidebarOpen, setSidebarOpen] = useState(false)

  return (
    <div className="flex h-screen bg-brand-50 overflow-hidden">
      <Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} />

      <div className="flex-1 flex flex-col min-w-0 overflow-hidden">
        <SubscriptionBanner />
        <DataProcessingAgreementBanner />

        {/* Mobile top bar */}
        <header className="lg:hidden flex items-center gap-3 px-4 py-3 bg-white border-b border-gray-200 shrink-0">
          <button
            onClick={() => setSidebarOpen(true)}
            className="p-1.5 rounded text-gray-500 hover:text-gray-700 hover:bg-gray-100"
          >
            <svg
              width="20"
              height="20"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
            >
              <line x1="3" y1="6" x2="21" y2="6" />
              <line x1="3" y1="12" x2="21" y2="12" />
              <line x1="3" y1="18" x2="21" y2="18" />
            </svg>
          </button>
          <Link to="/dashboard" className="flex items-center gap-3 flex-1">
            <Logo variant="light" size={24} />
            <span className="font-display text-lg font-semibold text-brand-800">
              Skoleoverblikket
            </span>
          </Link>
          <NotificationBell />
        </header>

        {/* Page content */}
        <main className="flex-1 overflow-y-auto">
          <ErrorBoundary>
            <Outlet />
          </ErrorBoundary>
        </main>
      </div>
    </div>
  )
}
