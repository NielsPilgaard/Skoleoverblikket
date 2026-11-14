import type React from 'react'
import { Link } from 'react-router-dom'

/** Give `href` for navigation (ctrl-click and copy-link work), `onClick` for anything else. */
export type RowAction = RowActionBase &
  ({ href: string; onClick?: never } | { onClick: () => void; href?: never })

interface RowActionBase {
  label: string
  /** Defaults: the first action is primary, the rest secondary. */
  variant?: 'primary' | 'secondary' | 'danger'
  disabled?: boolean
  /** Why the action is disabled. Shown as text under the buttons, not only as a tooltip. */
  disabledReason?: string
  testId?: string
}

/** A row action as a router link or a button. Clicks don't reach the row's own onClick. */
export function RowActionControl({
  action: a,
  className,
}: {
  action: RowAction
  className: string
}) {
  if (a.href !== undefined && !a.disabled) {
    return (
      <Link
        to={a.href}
        onClick={(e) => e.stopPropagation()}
        data-testid={a.testId}
        className={`inline-flex items-center justify-center ${className}`}
      >
        {a.label}
      </Link>
    )
  }
  return (
    <button
      type="button"
      onClick={(e) => {
        e.stopPropagation()
        a.onClick?.()
      }}
      disabled={a.disabled}
      title={a.disabled ? a.disabledReason : undefined}
      data-testid={a.testId}
      className={className}
    >
      {a.label}
    </button>
  )
}

export interface Badge {
  label: string
  tone: 'amber' | 'red' | 'gray' | 'brand' | 'sky'
}

const badgeTone: Record<Badge['tone'], string> = {
  amber: 'bg-amber-100 text-amber-900',
  red: 'bg-red-100 text-red-800',
  gray: 'bg-gray-100 text-gray-700',
  brand: 'bg-brand-100 text-brand-800',
  sky: 'bg-sky-100 text-sky-900',
}

export function StatusBadge({ badge }: { badge: Badge }) {
  return (
    <span
      className={`shrink-0 px-2 py-0.5 rounded-full text-xs font-bold whitespace-nowrap ${badgeTone[badge.tone]}`}
    >
      {badge.label}
    </span>
  )
}

const actionStyle = {
  primary: 'bg-brand-600 text-white hover:bg-brand-700',
  secondary: 'border border-gray-300 bg-white text-gray-800 hover:bg-gray-50',
  danger: 'border border-red-200 bg-white text-red-700 hover:bg-red-50',
}

/** Full-size action buttons: fold-outs in PersonList and cards in DataTable. */
export function ActionButtons({ actions }: { actions: RowAction[] }) {
  if (actions.length === 0) return null
  const reasons = actions.filter((a) => a.disabled && a.disabledReason)
  return (
    <div className="mt-3">
      <div className="flex flex-wrap gap-2">
        {actions.map((a, i) => (
          <RowActionControl
            key={a.label}
            action={a}
            className={`flex-1 sm:flex-none sm:px-6 h-11 px-4 rounded-lg font-semibold whitespace-nowrap disabled:opacity-50 disabled:cursor-not-allowed ${
              actionStyle[a.variant ?? (i === 0 ? 'primary' : 'secondary')]
            }`}
          />
        ))}
      </div>
      {reasons.map((a) => (
        <p key={a.label} className="mt-2 text-sm text-gray-600">
          {a.disabledReason}
        </p>
      ))}
    </div>
  )
}

/** Rows exist, but the search or filter hides them all. "Vis alle" clears both. */
export function NoMatches({ onClear }: { onClear: () => void }) {
  return (
    <div className="bg-white rounded-xl border border-gray-200 p-6 text-center">
      <p className="font-semibold text-gray-900">Ingen matcher søgningen eller filteret</p>
      <button
        type="button"
        onClick={onClear}
        className="mt-2 h-11 px-4 rounded-lg border border-gray-300 font-semibold text-gray-800 hover:bg-gray-50"
      >
        Vis alle
      </button>
    </div>
  )
}

/** Shown when the list has no rows at all (not a search miss). */
export function EmptyList({ children }: { children: React.ReactNode }) {
  return (
    <div className="bg-white rounded-xl border border-gray-200 px-5 py-12 text-center text-gray-600">
      {children}
    </div>
  )
}
