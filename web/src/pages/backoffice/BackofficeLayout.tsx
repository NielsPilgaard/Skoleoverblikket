import { Outlet, Link, useLocation } from 'react-router-dom'
import { useAuth } from '../../auth/useAuth'

export default function BackofficeLayout() {
  const { logout, userName } = useAuth()
  const location = useLocation()

  return (
    <div className="min-h-screen bg-gray-50">
      <header className="bg-white border-b border-gray-200 px-6 py-3 flex items-center justify-between">
        <div className="flex items-center gap-4">
          <span className="font-semibold text-gray-900">Skoleoverblikket Backoffice</span>
          <nav className="flex gap-1">
            <Link
              to="/backoffice/tenants"
              className={`px-3 py-1.5 text-sm rounded-md transition-colors ${
                location.pathname.startsWith('/backoffice/tenants')
                  ? 'bg-indigo-50 text-indigo-700 font-medium'
                  : 'text-gray-600 hover:text-gray-900 hover:bg-gray-100'
              }`}
            >
              Skoler
            </Link>
            <Link
              to="/backoffice/emails"
              className={`px-3 py-1.5 text-sm rounded-md transition-colors ${
                location.pathname.startsWith('/backoffice/emails')
                  ? 'bg-indigo-50 text-indigo-700 font-medium'
                  : 'text-gray-600 hover:text-gray-900 hover:bg-gray-100'
              }`}
            >
              E-mails
            </Link>
            <Link
              to="/backoffice/underdatabehandlere"
              className={`px-3 py-1.5 text-sm rounded-md transition-colors ${
                location.pathname.startsWith('/backoffice/underdatabehandlere')
                  ? 'bg-indigo-50 text-indigo-700 font-medium'
                  : 'text-gray-600 hover:text-gray-900 hover:bg-gray-100'
              }`}
            >
              Underdatabehandlere
            </Link>
          </nav>
        </div>
        <div className="flex items-center gap-3 text-sm text-gray-500">
          <span>{userName}</span>
          <button onClick={logout} className="text-gray-400 hover:text-gray-600 transition-colors">
            Log ud
          </button>
        </div>
      </header>
      <main className="p-6 lg:p-8 max-w-7xl mx-auto">
        <Outlet />
      </main>
    </div>
  )
}
