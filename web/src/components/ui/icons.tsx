import type React from 'react'

// Small stroke icons shared by the ui/ components. Decorative only: always pair with text.

interface IconProps {
  className?: string
}

function Icon({ className = 'w-5 h-5', children }: IconProps & { children: React.ReactNode }) {
  return (
    <svg
      className={className}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {children}
    </svg>
  )
}

export function ChevronDownIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <polyline points="6 9 12 15 18 9" />
    </Icon>
  )
}

export function SearchIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <circle cx="11" cy="11" r="7" />
      <line x1="21" y1="21" x2="16.65" y2="16.65" />
    </Icon>
  )
}

export function SortIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <polyline points="7 15 12 20 17 15" />
      <polyline points="7 9 12 4 17 9" />
    </Icon>
  )
}

export function SortAscIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <polyline points="18 15 12 9 6 15" />
    </Icon>
  )
}
