import { useAuth } from '../auth/useAuth'
import type { SchoolRole } from '../auth/actingAs'
import { SegmentedControl } from './ui/select'

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
      <SegmentedControl
        label="Vis som"
        vertical
        onDark
        value={actingAs}
        onChange={switchRole}
        options={schoolRoles.map(({ role, active }) => ({
          value: role,
          label: labels[role],
          description: active ? undefined : moduleOff[role],
          disabled: !active,
          testId: `role-switcher-${role}`,
        }))}
      />
    </div>
  )
}
