import { useEffect, useMemo, useState } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import {
  getApiV1SchoolsSettingsOptions,
  getApiV1SchoolsOnboardingStatusOptions,
} from '../api/generated/@tanstack/react-query.gen'
import { useAuth } from '../auth/useAuth'
import { useSubscription } from '../hooks/useSubscription'
import Logo from './Logo'
import NotificationBell from './NotificationBell'

interface NavItem {
  to: string
  label: string
  icon: React.ReactNode
  order: number
  group?: string
  adminOnly?: boolean
  parentOnly?: boolean
  boardOnly?: boolean
  moduleGated?: boolean
}

/**
 * Single source of truth for the sidebar. Each item's `group` and `order`
 * fully determine where it renders — array position doesn't matter.
 * Group order is: order of first item (by `order`) belonging to that group.
 */
const navItems: NavItem[] = [
  {
    to: '/dashboard',
    label: 'Oversigt',
    adminOnly: true,
    order: 0,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <rect x="3" y="3" width="7" height="7" />
        <rect x="14" y="3" width="7" height="7" />
        <rect x="14" y="14" width="7" height="7" />
        <rect x="3" y="14" width="7" height="7" />
      </svg>
    ),
  },
  {
    to: '/klasser',
    label: 'Klasser',
    group: 'Planlægning',
    order: 10,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
        <circle cx="9" cy="7" r="4" />
        <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
        <path d="M16 3.13a4 4 0 0 1 0 7.75" />
      </svg>
    ),
  },
  {
    to: '/staa-maal-med',
    label: 'Stå mål med',
    adminOnly: true,
    group: 'Planlægning',
    order: 11,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14" />
        <polyline points="22 4 12 14.01 9 11.01" />
      </svg>
    ),
  },
  {
    to: '/kalender',
    label: 'Kalender',
    group: 'Planlægning',
    order: 12,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <rect x="3" y="4" width="18" height="18" rx="2" />
        <line x1="16" y1="2" x2="16" y2="6" />
        <line x1="8" y1="2" x2="8" y2="6" />
        <line x1="3" y1="10" x2="21" y2="10" />
      </svg>
    ),
  },
  {
    to: '/mig/skema',
    label: 'Mit skema',
    group: 'Planlægning',
    order: 13,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <rect x="3" y="4" width="18" height="18" rx="2" />
        <line x1="16" y1="2" x2="16" y2="6" />
        <line x1="8" y1="2" x2="8" y2="6" />
        <line x1="3" y1="10" x2="21" y2="10" />
        <line x1="8" y1="14" x2="16" y2="14" />
        <line x1="8" y1="18" x2="13" y2="18" />
      </svg>
    ),
  },
  {
    to: '/sfo',
    label: 'SFO',
    adminOnly: true,
    group: 'Planlægning',
    order: 14,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <circle cx="12" cy="12" r="10" />
        <polyline points="12 6 12 12 16 14" />
      </svg>
    ),
  },
  {
    to: '/ferieindmelding',
    label: 'Ferietilmelding',
    adminOnly: true,
    group: 'Planlægning',
    order: 15,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <rect x="3" y="4" width="18" height="18" rx="2" />
        <line x1="16" y1="2" x2="16" y2="6" />
        <line x1="8" y1="2" x2="8" y2="6" />
        <line x1="3" y1="10" x2="21" y2="10" />
        <path d="M8 14h.01M12 14h.01M16 14h.01M8 18h.01M12 18h.01" />
      </svg>
    ),
  },
  {
    to: '/aarsrul',
    label: 'Årsrul',
    adminOnly: true,
    group: 'Planlægning',
    order: 16,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M23 4v6h-6" />
        <path d="M1 20v-6h6" />
        <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" />
      </svg>
    ),
  },
  {
    to: '/medarbejdere',
    label: 'Medarbejdere',
    adminOnly: true,
    group: 'Stamdata',
    order: 20,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" />
        <circle cx="12" cy="7" r="4" />
      </svg>
    ),
  },
  {
    to: '/fag',
    label: 'Fag',
    group: 'Stamdata',
    order: 21,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z" />
        <path d="M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z" />
      </svg>
    ),
  },
  {
    to: '/lokaler',
    label: 'Lokaler',
    group: 'Stamdata',
    order: 22,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" />
        <polyline points="9 22 9 12 15 12 15 22" />
      </svg>
    ),
  },
  {
    to: '/elever',
    label: 'Elever',
    adminOnly: true,
    moduleGated: true,
    group: 'Stamdata',
    order: 23,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
        <circle cx="9" cy="7" r="4" />
        <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
        <path d="M16 3.13a4 4 0 0 1 0 7.75" />
      </svg>
    ),
  },
  {
    to: '/foraeldre',
    label: 'Forældre',
    adminOnly: true,
    moduleGated: true,
    group: 'Stamdata',
    order: 24,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" />
        <circle cx="12" cy="7" r="4" />
        <path d="M12 11v4" />
        <path d="M9 14h6" />
      </svg>
    ),
  },
  {
    to: '/import',
    label: 'Importer data',
    adminOnly: true,
    group: 'Stamdata',
    order: 25,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
        <polyline points="17 8 12 3 7 8" />
        <line x1="12" y1="3" x2="12" y2="15" />
      </svg>
    ),
  },
  {
    to: '/filer',
    label: 'Filer',
    group: 'Filer & Eksport',
    order: 40,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z" />
      </svg>
    ),
  },
  {
    to: '/eksporter',
    label: 'Eksporter',
    adminOnly: true,
    group: 'Filer & Eksport',
    order: 41,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
        <polyline points="7 10 12 15 17 10" />
        <line x1="12" y1="15" x2="12" y2="3" />
      </svg>
    ),
  },
  {
    to: '/bestyrelse/oversigt',
    label: 'Oversigt',
    boardOnly: true,
    group: 'Bestyrelse',
    order: 50,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <rect x="3" y="3" width="7" height="7" />
        <rect x="14" y="3" width="7" height="7" />
        <rect x="14" y="14" width="7" height="7" />
        <rect x="3" y="14" width="7" height="7" />
      </svg>
    ),
  },
  {
    to: '/bestyrelse/filer',
    label: 'Filer',
    boardOnly: true,
    group: 'Bestyrelse',
    order: 51,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z" />
      </svg>
    ),
  },
  {
    to: '/bestyrelse/staa-maal-med',
    label: 'Stå mål med',
    boardOnly: true,
    group: 'Bestyrelse',
    order: 52,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14" />
        <polyline points="22 4 12 14.01 9 11.01" />
      </svg>
    ),
  },
  {
    to: '/foraeldrevisning/skema',
    label: 'Skema',
    parentOnly: true,
    order: 60,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <rect x="3" y="4" width="18" height="18" rx="2" />
        <line x1="16" y1="2" x2="16" y2="6" />
        <line x1="8" y1="2" x2="8" y2="6" />
        <line x1="3" y1="10" x2="21" y2="10" />
        <line x1="8" y1="14" x2="16" y2="14" />
        <line x1="8" y1="18" x2="13" y2="18" />
      </svg>
    ),
  },
  {
    to: '/foraeldrevisning/kalender',
    label: 'Kalender',
    parentOnly: true,
    order: 61,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <rect x="3" y="4" width="18" height="18" rx="2" />
        <line x1="16" y1="2" x2="16" y2="6" />
        <line x1="8" y1="2" x2="8" y2="6" />
        <line x1="3" y1="10" x2="21" y2="10" />
      </svg>
    ),
  },
  {
    to: '/foraeldrevisning/ugeplan',
    label: 'Ugeplan',
    parentOnly: true,
    order: 62,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <line x1="8" y1="6" x2="21" y2="6" />
        <line x1="8" y1="12" x2="21" y2="12" />
        <line x1="8" y1="18" x2="21" y2="18" />
        <line x1="3" y1="6" x2="3.01" y2="6" />
        <line x1="3" y1="12" x2="3.01" y2="12" />
        <line x1="3" y1="18" x2="3.01" y2="18" />
      </svg>
    ),
  },
  {
    to: '/foraeldrevisning/kontakt',
    label: 'Kontakter',
    parentOnly: true,
    order: 63,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
        <circle cx="9" cy="7" r="4" />
        <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
        <path d="M16 3.13a4 4 0 0 1 0 7.75" />
      </svg>
    ),
  },
  {
    to: '/foraeldrevisning/ferieindmelding',
    label: 'Ferietilmelding',
    parentOnly: true,
    order: 64,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <rect x="3" y="4" width="18" height="18" rx="2" />
        <line x1="16" y1="2" x2="16" y2="6" />
        <line x1="8" y1="2" x2="8" y2="6" />
        <line x1="3" y1="10" x2="21" y2="10" />
        <path d="M8 14h.01M12 14h.01M16 14h.01M8 18h.01M12 18h.01" />
      </svg>
    ),
  },
  {
    to: '/foraeldrevisning/fravaer',
    label: 'Fravær',
    parentOnly: true,
    group: 'Kontakt',
    order: 70,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <circle cx="12" cy="12" r="10" />
        <line x1="12" y1="8" x2="12" y2="12" />
        <line x1="12" y1="16" x2="12.01" y2="16" />
      </svg>
    ),
  },
  {
    to: '/klassechat',
    label: 'Klassechat',
    parentOnly: true,
    moduleGated: true,
    group: 'Kontakt',
    order: 70.5,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M17 8h2a2 2 0 0 1 2 2v10l-3-3h-8a2 2 0 0 1-2-2v-1" />
        <path d="M14 3H5a2 2 0 0 0-2 2v9l3-3h8a2 2 0 0 0 2-2V5a2 2 0 0 0-2-2z" />
      </svg>
    ),
  },
  {
    to: '/foraeldrevisning/kontaktbog',
    label: 'Kontaktbog',
    parentOnly: true,
    group: 'Kontakt',
    order: 71,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
      </svg>
    ),
  },
  {
    to: '/fravaer',
    label: 'Fravær',
    moduleGated: true,
    group: 'Kontakt',
    order: 72,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <circle cx="12" cy="12" r="10" />
        <line x1="12" y1="8" x2="12" y2="12" />
        <line x1="12" y1="16" x2="12.01" y2="16" />
      </svg>
    ),
  },
  {
    to: '/kontaktbog',
    label: 'Kontaktbog',
    moduleGated: true,
    group: 'Kontakt',
    order: 73,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
      </svg>
    ),
  },
  {
    to: '/klassechat',
    label: 'Klassechat',
    moduleGated: true,
    group: 'Kontakt',
    order: 73.5,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M17 8h2a2 2 0 0 1 2 2v10l-3-3h-8a2 2 0 0 1-2-2v-1" />
        <path d="M14 3H5a2 2 0 0 0-2 2v9l3-3h8a2 2 0 0 0 2-2V5a2 2 0 0 0-2-2z" />
      </svg>
    ),
  },
  {
    to: '/beskeder',
    label: 'Beskeder',
    moduleGated: true,
    group: 'Kontakt',
    order: 74,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z" />
        <polyline points="22,6 12,13 2,6" />
      </svg>
    ),
  },
  {
    to: '/beskeder',
    label: 'Beskeder',
    parentOnly: true,
    moduleGated: true,
    group: 'Kontakt',
    order: 74,
    icon: (
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z" />
        <polyline points="22,6 12,13 2,6" />
      </svg>
    ),
  },
]

function NavItemLink({ item, onClose }: { item: NavItem; onClose: () => void }) {
  return (
    <NavLink
      to={item.to}
      onClick={onClose}
      className={({ isActive }) =>
        `flex items-center gap-3 px-3 py-2.5 rounded-md text-sm font-medium transition-colors ${
          isActive
            ? 'bg-brand-600 text-white'
            : 'text-brand-200 hover:bg-brand-800 hover:text-white'
        }`
      }
    >
      <span className="shrink-0">{item.icon}</span>
      {item.label}
    </NavLink>
  )
}

type NavBlock = { kind: 'item'; item: NavItem } | { kind: 'group'; group: string; items: NavItem[] }

/** Groups items by `group`, sorts groups and in-group items by `order`. Ungrouped items stay standalone blocks. */
function buildNavBlocks(items: NavItem[]): NavBlock[] {
  const groupOrder = new Map<string, number>()
  for (const item of items) {
    if (item.group != null && !groupOrder.has(item.group)) {
      groupOrder.set(item.group, item.order)
    }
  }

  const blocks: NavBlock[] = []
  const groupBlocks = new Map<string, Extract<NavBlock, { kind: 'group' }>>()

  for (const item of items) {
    if (item.group == null) {
      blocks.push({ kind: 'item', item })
      continue
    }
    let block = groupBlocks.get(item.group)
    if (!block) {
      block = { kind: 'group', group: item.group, items: [] }
      groupBlocks.set(item.group, block)
      blocks.push(block)
    }
    block.items.push(item)
  }

  for (const block of blocks) {
    if (block.kind === 'group') {
      block.items.sort((a, b) => a.order - b.order)
    }
  }

  blocks.sort((a, b) => {
    const orderA = a.kind === 'item' ? a.item.order : (groupOrder.get(a.group) ?? 0)
    const orderB = b.kind === 'item' ? b.item.order : (groupOrder.get(b.group) ?? 0)
    return orderA - orderB
  })

  return blocks
}

interface SidebarProps {
  open: boolean
  onClose: () => void
}

const NAV_EXPANDED_GROUPS_KEY = 'nav-expanded-groups'

export default function Sidebar({ open, onClose }: SidebarProps) {
  const { logout, userName, isAdmin, isParent, isBoard } = useAuth()
  const { hasParentModule } = useSubscription()
  const { pathname } = useLocation()
  const { data: school } = useQuery({ ...getApiV1SchoolsSettingsOptions(), enabled: isAdmin })
  const { data: onboarding } = useQuery({
    ...getApiV1SchoolsOnboardingStatusOptions(),
    enabled: isAdmin,
    retry: false,
    staleTime: 0,
  })
  const wizardDone =
    onboarding != null &&
    (onboarding.staffCount ?? 0) > 0 &&
    (onboarding.classCount ?? 0) > 0 &&
    (onboarding.roomCount ?? 0) > 0

  const visibleNavItems = navItems.filter((item) => {
    if (isBoard) return item.boardOnly === true
    if (item.boardOnly) return false
    if (isParent) return item.parentOnly === true
    if (item.parentOnly) return false
    if (item.moduleGated && !hasParentModule) return false
    return !item.adminOnly || isAdmin
  })

  const navBlocks = useMemo(() => buildNavBlocks(visibleNavItems), [visibleNavItems])

  const [expandedGroups, setExpandedGroups] = useState<Set<string>>(() => {
    try {
      const stored = localStorage.getItem(NAV_EXPANDED_GROUPS_KEY)
      if (stored) return new Set(JSON.parse(stored))
    } catch {
      // ignore malformed storage, fall through to default
    }
    return new Set()
  })

  const activeGroup = visibleNavItems.find((item) => item.to === pathname)?.group

  useEffect(() => {
    localStorage.setItem(NAV_EXPANDED_GROUPS_KEY, JSON.stringify([...expandedGroups]))
  }, [expandedGroups])

  const toggleGroup = (group: string) => {
    setExpandedGroups((prev) => {
      const next = new Set(prev)
      if (next.has(group)) {
        next.delete(group)
      } else {
        next.add(group)
      }
      return next
    })
  }

  return (
    <>
      {/* Mobile overlay */}
      {open && (
        <button
          type="button"
          tabIndex={-1}
          aria-label="Luk menu"
          className="fixed inset-0 z-20 bg-black/30 lg:hidden"
          onClick={onClose}
        />
      )}

      {/* Sidebar */}
      <aside
        className={`
          fixed top-0 left-0 z-30 h-full w-60 flex flex-col
          bg-brand-900 text-white
          transform transition-transform duration-200 ease-in-out
          lg:relative lg:translate-x-0 lg:z-auto
          ${open ? 'translate-x-0' : '-translate-x-full'}
        `}
      >
        {/* Brand */}
        <div className="flex items-center justify-between px-5 py-5 border-b border-brand-700">
          <NavLink
            to={isAdmin ? '/dashboard' : isParent ? '/foraeldrevisning/skema' : '/mig/skema'}
            onClick={onClose}
            className="flex items-center gap-2.5 min-w-0 hover:opacity-80 transition-opacity"
          >
            {school?.logoUrl ? (
              <img
                src={school.logoUrl}
                alt=""
                className="h-7 w-7 rounded object-contain shrink-0 bg-white/10"
              />
            ) : (
              <Logo variant="dark" size={28} />
            )}
            <div className="min-w-0">
              <span className="block font-display text-sm font-semibold tracking-tight text-white truncate">
                {school?.name ?? 'Skoleoverblikket'}
              </span>
              {school?.name ? (
                <span className="block text-xs text-brand-400">Skoleoverblikket</span>
              ) : null}
            </div>
          </NavLink>
          <div className="hidden lg:block shrink-0">
            <NotificationBell variant="dark" />
          </div>
          <button
            onClick={onClose}
            className="lg:hidden p-1 rounded text-brand-300 hover:text-white shrink-0"
          >
            <svg
              width="18"
              height="18"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
            >
              <line x1="18" y1="6" x2="6" y2="18" />
              <line x1="6" y1="6" x2="18" y2="18" />
            </svg>
          </button>
        </div>

        {/* Nav */}
        <nav className="flex-1 px-3 py-4 overflow-y-auto">
          {navBlocks.map((block) => {
            if (block.kind === 'item') {
              return (
                <div key={block.item.to}>
                  <NavItemLink item={block.item} onClose={onClose} />
                </div>
              )
            }

            const isCollapsed = !expandedGroups.has(block.group) && block.group !== activeGroup

            return (
              <div key={block.group}>
                <button
                  type="button"
                  onClick={() => toggleGroup(block.group)}
                  className="flex items-center justify-between w-full px-3 pt-3 pb-1 rounded-md text-xs font-semibold uppercase tracking-wider text-white/70 hover:text-white select-none hover:bg-brand-800"
                >
                  {block.group}
                  <svg
                    width="12"
                    height="12"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    className={`transition-transform duration-200 ${isCollapsed ? '-rotate-90' : ''}`}
                  >
                    <polyline points="6 9 12 15 18 9" />
                  </svg>
                </button>
                <div
                  className={`grid transition-[grid-template-rows] duration-200 ease-in-out ${
                    isCollapsed ? 'pointer-events-none' : ''
                  }`}
                  style={{ gridTemplateRows: isCollapsed ? '0fr' : '1fr' }}
                >
                  <div className="overflow-hidden">
                    {block.items.map((item) => (
                      <NavItemLink key={item.to} item={item} onClose={onClose} />
                    ))}
                  </div>
                </div>
              </div>
            )
          })}
        </nav>

        {/* Footer */}
        <div className="px-3 py-4 border-t border-brand-700 space-y-2">
          {isAdmin && !wizardDone && (
            <NavLink
              to="/setup"
              onClick={onClose}
              className={({ isActive }) =>
                `flex items-center gap-3 px-3 py-2.5 rounded-md text-sm font-medium transition-colors ${
                  isActive
                    ? 'bg-brand-600 text-white'
                    : 'text-brand-200 hover:bg-brand-800 hover:text-white'
                }`
              }
            >
              <svg
                width="18"
                height="18"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
              >
                <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2" />
              </svg>
              Opsætningsguide
            </NavLink>
          )}
          {isAdmin && (
            <NavLink
              to="/abonnement"
              onClick={onClose}
              className={({ isActive }) =>
                `flex items-center gap-3 px-3 py-2.5 rounded-md text-sm font-medium transition-colors ${
                  isActive
                    ? 'bg-brand-600 text-white'
                    : 'text-brand-200 hover:bg-brand-800 hover:text-white'
                }`
              }
            >
              <svg
                width="18"
                height="18"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
              >
                <rect x="1" y="4" width="22" height="16" rx="2" ry="2" />
                <line x1="1" y1="10" x2="23" y2="10" />
              </svg>
              Abonnement
            </NavLink>
          )}
          {isAdmin && (
            <NavLink
              to="/indstillinger"
              end
              onClick={onClose}
              className={({ isActive }) =>
                `flex items-center gap-3 px-3 py-2.5 rounded-md text-sm font-medium transition-colors ${
                  isActive
                    ? 'bg-brand-600 text-white'
                    : 'text-brand-200 hover:bg-brand-800 hover:text-white'
                }`
              }
            >
              <svg
                width="18"
                height="18"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
              >
                <circle cx="12" cy="12" r="3" />
                <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" />
              </svg>
              Indstillinger
            </NavLink>
          )}
          <NavLink
            to="/indstillinger/notifikationer"
            onClick={onClose}
            className={({ isActive }) =>
              `flex items-center gap-3 px-3 py-2.5 rounded-md text-sm font-medium transition-colors ${
                isActive
                  ? 'bg-brand-600 text-white'
                  : 'text-brand-200 hover:bg-brand-800 hover:text-white'
              }`
            }
          >
            <svg
              width="18"
              height="18"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9" />
              <path d="M13.73 21a2 2 0 0 1-3.46 0" />
            </svg>
            Notifikationsindstillinger
          </NavLink>
          {isParent && (
            <NavLink
              to="/foraeldrevisning/profil"
              onClick={onClose}
              className={({ isActive }) =>
                `flex items-center gap-3 px-3 py-2.5 rounded-md text-sm font-medium transition-colors ${
                  isActive
                    ? 'bg-brand-600 text-white'
                    : 'text-brand-200 hover:bg-brand-800 hover:text-white'
                }`
              }
            >
              <svg
                width="18"
                height="18"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
              >
                <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" />
                <circle cx="12" cy="7" r="4" />
              </svg>
              Min profil
            </NavLink>
          )}
          {userName && (
            <div className="flex items-center gap-2 px-3 py-2">
              <div className="flex items-center justify-center h-6 w-6 rounded-full bg-brand-600 shrink-0">
                <span className="text-xs font-semibold text-white leading-none">
                  {userName.charAt(0).toUpperCase()}
                </span>
              </div>
              <span className="text-xs text-brand-300 truncate">{userName}</span>
            </div>
          )}
          <button
            onClick={logout}
            className="flex items-center gap-3 w-full px-3 py-2.5 rounded-md text-sm font-medium text-brand-200 hover:bg-brand-800 hover:text-white transition-colors"
          >
            <svg
              width="18"
              height="18"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
              <polyline points="16 17 21 12 16 7" />
              <line x1="21" y1="12" x2="9" y2="12" />
            </svg>
            Log ud
          </button>
        </div>
      </aside>
    </>
  )
}
