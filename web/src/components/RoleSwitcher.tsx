import { useAuth } from '../auth/useAuth'
import type { SchoolRole } from '../auth/actingAs'

const labels: Record<SchoolRole, string> = {
  staff: 'Medarbejder',
  parent: 'Forælder',
  board: 'Bestyrelse',
}

const moduleOff: Record<SchoolRole, string> = {
  staff: '',
  parent: 'Skolen har ikke længere Forældremodulet',
  board: 'Skolen har ikke længere Bestyrelsesmodulet',
}

/**
 * "Vis som" for a person with several roles at the school, e.g. a teacher who is also a parent.
 * Hidden for everyone with one role. Choosing a role reloads the app as that role.
 */
export default function RoleSwitcher() {
  const { schoolRoles, actingAs, switchRole, viewAs } = useAuth()

  // Superadmin's own "Vis som" toolbar (ViewModeToolbar) takes over while it's in use.
  if (schoolRoles.length < 2 || viewAs !== 'default') return null

  return (
    <div data-testid="role-switcher" className="px-3 py-2">
      <p className="text-xs font-medium text-brand-300 mb-1.5">Vis som</p>
      <div className="flex flex-col gap-1">
        {schoolRoles.map(({ role, active }) => {
          const current = role === actingAs
          return (
            <button
              key={role}
              type="button"
              data-testid={`role-switcher-${role}`}
              aria-pressed={current}
              disabled={current || !active}
              onClick={() => switchRole(role)}
              className={`w-full text-left px-3 py-2 rounded-md text-sm transition-colors ${
                current
                  ? 'bg-brand-700 text-white font-medium'
                  : active
                    ? 'text-brand-200 hover:bg-brand-800 hover:text-white'
                    : 'text-brand-400 cursor-not-allowed'
              }`}
            >
              {labels[role]}
              {!active && <span className="block text-xs text-brand-400">{moduleOff[role]}</span>}
            </button>
          )
        })}
      </div>
    </div>
  )
}
