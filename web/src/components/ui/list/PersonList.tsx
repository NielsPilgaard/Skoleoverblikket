import { useEffect, useId, useRef } from 'react'
import type React from 'react'
import { useHistoryEntryState } from '../../../hooks/useHistoryEntryState'
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
  /** Icon lines, like a contact card. `label` is read by screen readers only. */
  details?: PersonDetail[]
  /**
   * Free-form fold-out content under the details, e.g. several contacts.
   * As a function it gets `openPerson(key)`, which unfolds another row and scrolls to it.
   */
  content?: React.ReactNode | ((openPerson: (key: string) => void) => React.ReactNode)
  /** Primary first. */
  actions?: RowAction[]
  testId?: string
}

export interface PersonDetail {
  icon: (props: { className?: string }) => React.ReactNode
  label: string
  value: React.ReactNode
  /** mailto:/tel: link. */
  href?: string
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
  // Search, filter, sort and open rows survive a trip to e.g. "Se skema" and back.
  const stateKey = testId ?? 'person-list'
  const list = useListControls(items ?? [], controls, stateKey)
  /** Most recently opened last. */
  const [open, setOpen] = useHistoryEntryState<string[]>(`${stateKey}:open`, [])
  const baseId = useId()
  const restoring = useRef(open.length > 0)

  function toggle(key: string) {
    setOpen((prev) => (prev.includes(key) ? prev.filter((k) => k !== key) : [...prev, key]))
  }

  function focusRow(key: string, behavior: ScrollBehavior) {
    const row = document.getElementById(`${baseId}-${key}-row`)
    row?.scrollIntoView({ behavior, block: 'center' })
    row?.focus({ preventScroll: true })
  }

  function openPerson(key: string) {
    // A search or filter may hide the row; clear them so it can be shown.
    if (!list.visible.some((item) => toPerson(item).key === key)) list.clear()
    setOpen((prev) => [...prev.filter((k) => k !== key), key])
    requestAnimationFrame(() => focusRow(key, 'smooth'))
  }

  // Back on the page: put the row the user left from where they can see it.
  useEffect(() => {
    if (!restoring.current || !items || isLoading) return
    restoring.current = false
    const last = open[open.length - 1]
    if (last) focusRow(last, 'instant')
  })

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
              <span className="block h-4 w-32 bg-gray-200 rounded-sm" />
              <span className="block h-3 w-24 bg-gray-100 rounded-sm" />
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
            const isOpen = open.includes(p.key)
            const panelId = `${baseId}-${p.key}`
            return (
              <li key={p.key} data-testid={p.testId}>
                <button
                  id={`${panelId}-row`}
                  data-testid="person-row-toggle"
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
                    <ul className="space-y-1.5 text-sm">
                      {p.details.map((d) => {
                        const body = (
                          <>
                            <d.icon className="w-4 h-4 shrink-0 text-gray-500" />
                            <span className="sr-only">{d.label}: </span>
                            <span className="min-w-0 break-words">{d.value}</span>
                          </>
                        )
                        return (
                          <li key={d.label}>
                            {d.href ? (
                              <a
                                href={d.href}
                                className="inline-flex items-center gap-2 text-brand-600 hover:underline"
                              >
                                {body}
                              </a>
                            ) : (
                              <span className="flex items-center gap-2 text-gray-700">{body}</span>
                            )}
                          </li>
                        )
                      })}
                    </ul>
                  )}
                  {typeof p.content === 'function' ? p.content(openPerson) : p.content}
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
