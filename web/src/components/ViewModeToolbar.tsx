import { useNavigate } from 'react-router-dom'
import { useAuth, type ViewAs } from '../auth/useAuth'

const modes: { value: ViewAs; label: string }[] = [
  { value: 'default', label: 'Superadmin' },
  { value: 'admin', label: 'Skoleadmin' },
  { value: 'staff', label: 'Medarbejder' },
  { value: 'parent', label: 'Forælder' },
  { value: 'board', label: 'Bestyrelsesmedlem' },
]

export default function ViewModeToolbar() {
  const { isSuperAdmin, viewAs, setViewAs } = useAuth()
  const navigate = useNavigate()

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

  return (
    <div className="fixed bottom-4 left-1/2 -translate-x-1/2 z-50 flex items-center gap-1 bg-gray-900/95 backdrop-blur text-white text-xs rounded-full px-3 py-1.5 shadow-lg border border-white/10">
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
    </div>
  )
}
