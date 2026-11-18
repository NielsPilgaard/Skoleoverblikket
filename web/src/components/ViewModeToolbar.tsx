import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth, type ViewAs } from '../auth/useAuth'

const modes: { value: ViewAs; label: string }[] = [
  { value: 'default', label: 'Superadmin' },
  { value: 'admin', label: 'Skoleadmin' },
  { value: 'staff', label: 'Medarbejder' },
  { value: 'parent', label: 'Forælder' },
  { value: 'board', label: 'Bestyrelsesmedlem' },
]

const COLLAPSED_KEY = 'skoleoverblikket.viewModeToolbarCollapsed'

function readCollapsed(): boolean {
  try {
    return localStorage.getItem(COLLAPSED_KEY) === '1'
  } catch {
    return false
  }
}

function storeCollapsed(collapsed: boolean) {
  try {
    localStorage.setItem(COLLAPSED_KEY, collapsed ? '1' : '0')
  } catch {
    // Private mode or blocked storage: the choice lasts until the page reloads.
  }
}

export default function ViewModeToolbar() {
  const { isSuperAdmin, viewAs, setViewAs } = useAuth()
  const navigate = useNavigate()
  const [collapsed, setCollapsed] = useState(readCollapsed)

  if (!isSuperAdmin) {
    return null
  }

  function handleChange(mode: ViewAs) {
    setViewAs(mode)
    if (mode === 'default') {
      navigate('/backoffice')
    } else if (mode === 'parent') {
      navigate('/foraeldrevisning/skema')
    } else if (mode === 'admin') {
      navigate('/dashboard')
    } else if (mode === 'board') {
      navigate('/bestyrelse/oversigt')
    } else {
      navigate('/mig/skema')
    }
  }

  function toggleCollapsed() {
    setCollapsed(!collapsed)
    storeCollapsed(!collapsed)
  }

  if (collapsed) {
    return (
      <button
        type="button"
        onClick={toggleCollapsed}
        aria-expanded={false}
        data-testid="view-mode-toolbar-expand"
        className="fixed bottom-4 right-4 z-50 flex items-center gap-1.5 bg-gray-900/95 backdrop-blur-sm text-white text-xs rounded-full px-3 py-2 shadow-lg border border-white/10 hover:bg-gray-800 transition-colors"
      >
        <span className="text-gray-400">Vis som:</span>
        {modes.find((m) => m.value === viewAs)?.label}
        <svg
          aria-hidden="true"
          width="12"
          height="12"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <polyline points="18 15 12 9 6 15" />
        </svg>
      </button>
    )
  }

  return (
    <div className="fixed bottom-4 inset-x-0 mx-auto w-fit max-w-[calc(100vw-2rem)] z-50 flex flex-wrap items-center justify-center gap-1 bg-gray-900/95 backdrop-blur-sm text-white text-xs rounded-2xl px-3 py-1.5 shadow-lg border border-white/10">
      <span className="text-gray-400 mr-1.5 select-none">Vis som:</span>
      {modes.map((m) => (
        <button
          key={m.value}
          onClick={() => handleChange(m.value)}
          className={`px-2.5 py-1 rounded-full transition-colors ${
            viewAs === m.value
              ? 'bg-indigo-600 text-white font-medium'
              : 'text-gray-300 hover:text-white hover:bg-white/10'
          }`}
        >
          {m.label}
        </button>
      ))}
      <button
        type="button"
        onClick={toggleCollapsed}
        aria-expanded={true}
        aria-label="Skjul Vis som"
        title="Skjul"
        data-testid="view-mode-toolbar-collapse"
        className="ml-1 p-1 rounded-full text-gray-300 hover:text-white hover:bg-white/10 transition-colors"
      >
        <svg
          aria-hidden="true"
          width="14"
          height="14"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <polyline points="6 9 12 15 18 9" />
        </svg>
      </button>
    </div>
  )
}
