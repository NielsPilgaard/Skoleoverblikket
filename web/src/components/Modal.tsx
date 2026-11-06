import { useEffect, useId, useRef } from 'react'
import type React from 'react'

interface ModalProps {
  isOpen: boolean
  onClose: () => void
  title?: string
  size?: 'sm' | 'md' | 'lg'
  contentClassName?: string
  backdropClassName?: string
  /** Call preventDefault on Escape to keep the dialog open, e.g. when it closes a list inside. */
  onKeyDown?: (e: React.KeyboardEvent) => void
  children: React.ReactNode
}

const sizeClass = {
  sm: 'max-w-sm',
  md: 'max-w-md',
  lg: 'max-w-lg',
}

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), iframe, [tabindex="0"]'

// A closing dialog hands focus back a moment later. If a new dialog opens first, as when "Næste" in
// the lesson editor replaces the dialog, or in StrictMode's re-run, it cancels that and takes over.
let pendingReturn: { timer: ReturnType<typeof setTimeout>; target: HTMLElement | null } | null =
  null

/** Tab from the last field goes to the first, and back. Otherwise it walks on to the page behind. */
function keepTabInside(e: React.KeyboardEvent, dialog: HTMLElement) {
  const items = [...dialog.querySelectorAll<HTMLElement>(FOCUSABLE)].filter(
    (el) =>
      el.getClientRects().length > 0 &&
      // Only the checked radio in a group is a tab stop.
      !(el instanceof HTMLInputElement && el.type === 'radio' && !el.checked)
  )
  const first = items[0]
  const last = items[items.length - 1]
  const active = document.activeElement
  if (!first) {
    e.preventDefault()
  } else if (e.shiftKey && (active === first || active === dialog)) {
    e.preventDefault()
    last.focus()
  } else if (!e.shiftKey && active === last) {
    e.preventDefault()
    first.focus()
  }
}

export function Modal({
  isOpen,
  onClose,
  title,
  size = 'md',
  contentClassName,
  backdropClassName,
  onKeyDown,
  children,
}: ModalProps) {
  const dialogRef = useRef<HTMLDivElement>(null)
  const headingId = useId()

  // Read while rendering: when effects run, autoFocus has already moved focus into the dialog.
  const opener = useRef<HTMLElement | null>(null)
  const wasOpen = useRef(false)
  if (isOpen && !wasOpen.current) opener.current = document.activeElement as HTMLElement | null
  wasOpen.current = isOpen

  // Focus on the page behind, so keydown never reaches the dialog itself.
  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  // Move focus in on open, and back to the opener on close.
  useEffect(() => {
    if (!isOpen) return
    if (pendingReturn) {
      clearTimeout(pendingReturn.timer)
      opener.current = pendingReturn.target
      pendingReturn = null
    }
    const dialog = dialogRef.current
    if (dialog && !dialog.contains(document.activeElement)) dialog.focus()
    return () => {
      const target = opener.current
      const timer = setTimeout(() => {
        pendingReturn = null
        if (target?.isConnected) target.focus()
      })
      pendingReturn = { timer, target }
    }
  }, [isOpen])

  // Every render, because the content can change: steps in a dialog swap the heading, and can
  // remove the button that had focus.
  useEffect(() => {
    const dialog = dialogRef.current
    if (!dialog) return
    if (document.activeElement === document.body) dialog.focus()
    const heading = dialog.querySelector('h1, h2, h3')
    if (heading) {
      heading.id ||= headingId
      dialog.setAttribute('aria-labelledby', heading.id)
    }
  })

  if (!isOpen) return null

  return (
    <div
      role="none"
      className={
        backdropClassName ??
        'fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/40 text-left'
      }
      onClick={onClose}
    >
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        tabIndex={-1}
        className={`focus:outline-none ${
          contentClassName ?? `bg-white rounded-2xl shadow-xl w-full ${sizeClass[size]}`
        }`}
        onClick={(e) => e.stopPropagation()}
        onKeyDown={(e) => {
          e.stopPropagation()
          onKeyDown?.(e)
          if (e.key === 'Tab') keepTabInside(e, e.currentTarget)
          if (e.key === 'Escape' && !e.defaultPrevented) onClose()
        }}
      >
        {title && (
          <div className="px-6 py-5 border-b border-gray-100">
            <h2 className="font-display text-lg font-semibold text-gray-900">{title}</h2>
          </div>
        )}
        {children}
      </div>
    </div>
  )
}
