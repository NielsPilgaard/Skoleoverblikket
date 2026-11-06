import { useId, useState } from 'react'
import type React from 'react'
import { ChevronDownIcon } from '../icons'
import { ListToolbar } from './ListToolbar'
import {
  ActionButtons,
  type Badge,
  EmptyList,
  NoMatches,
  type RowAction,
  StatusBadge,
} from './parts'
import { type ListControls, useListControls } from './useListControls'

export interface Person {
  key: string
  name: string
  /** Second line: "role · classes". */
  subtitle?: string
  /** Only when the person isn't simply active, e.g. "Inviteret". */
  badge?: Badge
  details?: { label: string; value: React.ReactNode }[]
  /** Free-form fold-out content under the details, e.g. several contacts. */
  content?: React.ReactNode
  /** Primary first. */
  actions?: RowAction[]
  testId?: string
}

interface PersonListProps<T> extends ListControls<T> {
  items: T[] | undefined
  toPerson: (item: T) => Person
  isLoading?: boolean
  /** Shown when there are no people at all. */
  empty: React.ReactNode
  testId?: string
}

const avatarTones = [
  'bg-sky-100 text-sky-900',
  'bg-amber-100 text-amber-900',
  'bg-violet-100 text-violet-900',
  'bg-teal-100 text-teal-900',
  'bg-rose-100 text-rose-900',
  'bg-brand-100 text-brand-800',
]

function initials(name: string) {
  const parts = name.trim().split(/\s+/)
  const first = parts[0]?.[0] ?? ''
  const last = parts.length > 1 ? parts[parts.length - 1][0] : ''
  return (first + last).toUpperCase()
}

function avatarTone(name: string) {
  let h = 0
  for (const c of name) h = (h * 31 + c.charCodeAt(0)) | 0
  return avatarTones[Math.abs(h) % avatarTones.length]
}

/**
 * People (staff, students, parents): two-line rows that fold out details and that
 * person's actions inline. Same on phone and laptop. For lists where users compare
 * columns, use DataTable.
 */
export function PersonList<T>({
  items,
  toPerson,
  isLoading,
  empty,
  testId,
  ...controls
}: PersonListProps<T>) {
  const list = useListControls(items ?? [], controls)
  const [open, setOpen] = useState<Set<string>>(new Set())
  const baseId = useId()

  function toggle(key: string) {
    setOpen((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })
  }

  if (isLoading) {
    return (
      <ul
        aria-busy="true"
        className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100"
      >
        {Array.from({ length: 4 }).map((_, i) => (
          <li key={i} className="flex items-center gap-3 px-4 min-h-[64px] animate-pulse">
            <span className="w-10 h-10 rounded-full bg-gray-200" />
            <span className="flex-1 space-y-2">
              <span className="block h-4 w-32 bg-gray-200 rounded" />
              <span className="block h-3 w-24 bg-gray-100 rounded" />
            </span>
          </li>
        ))}
      </ul>
    )
  }

  // No data and not loading means the request failed. The page shows that, not "Ingen …".
  if (!items) return null
  if (list.total === 0) return <EmptyList>{empty}</EmptyList>

  return (
    <div className="space-y-2" data-testid={testId}>
      {list.hasToolbar && <ListToolbar {...list.toolbar} />}
      {list.visible.length === 0 ? (
        <NoMatches onClear={list.clear} />
      ) : (
        <ul className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100 overflow-hidden">
          {list.visible.map((item) => {
            const p = toPerson(item)
            const isOpen = open.has(p.key)
            const panelId = `${baseId}-${p.key}`
            return (
              <li key={p.key} data-testid={p.testId}>
                <button
                  type="button"
                  aria-expanded={isOpen}
                  aria-controls={panelId}
                  onClick={() => toggle(p.key)}
                  className="group w-full flex items-center gap-3 px-4 min-h-[64px] py-2 text-left hover:bg-gray-50 aria-expanded:bg-gray-50"
                >
                  <span
                    aria-hidden="true"
                    className={`w-10 h-10 shrink-0 rounded-full font-bold flex items-center justify-center ${avatarTone(p.name)}`}
                  >
                    {initials(p.name)}
                  </span>
                  <span className="flex-1 min-w-0">
                    <span className="block font-semibold text-gray-900 truncate">{p.name}</span>
                    {p.subtitle && (
                      <span className="block text-sm text-gray-600 truncate">{p.subtitle}</span>
                    )}
                  </span>
                  {p.badge && <StatusBadge badge={p.badge} />}
                  <ChevronDownIcon className="w-5 h-5 text-gray-500 shrink-0 transition-transform group-aria-expanded:rotate-180" />
                </button>
                <div id={panelId} hidden={!isOpen} className="px-4 pb-4 sm:pl-[4.25rem] bg-gray-50">
                  {p.details && p.details.length > 0 && (
                    <dl className="grid grid-cols-[6rem_1fr] md:grid-cols-[6rem_1fr_6rem_1fr] gap-x-3 gap-y-1 text-sm">
                      {p.details.map((d) => (
                        <div key={d.label} className="contents">
                          <dt className="text-gray-600">{d.label}</dt>
                          <dd className="min-w-0 break-words text-gray-900">{d.value}</dd>
                        </div>
                      ))}
                    </dl>
                  )}
                  {p.content}
                  <ActionButtons actions={p.actions ?? []} />
                </div>
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}
