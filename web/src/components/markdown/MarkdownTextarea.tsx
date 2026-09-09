import React, { useEffect, useRef } from 'react'
import { Markdown } from './Markdown'

export type SaveStatus = 'idle' | 'saving' | 'saved' | 'error'

// Persisted across editors: once a user hides the preview it stays hidden
// everywhere until they show it again.
const PREVIEW_HIDDEN_KEY = 'markdown-preview-hidden'

function readPreviewHidden(): boolean {
  try {
    return localStorage.getItem(PREVIEW_HIDDEN_KEY) === '1'
  } catch {
    return false
  }
}

function writePreviewHidden(hidden: boolean) {
  try {
    localStorage.setItem(PREVIEW_HIDDEN_KEY, hidden ? '1' : '0')
  } catch {
    // ignore — preview visibility is a nice-to-have, not worth surfacing
  }
}

interface MarkdownTextareaProps {
  value: string
  onChange: (value: string) => void
  onBlur?: React.FocusEventHandler<HTMLTextAreaElement>
  onKeyDown?: React.KeyboardEventHandler<HTMLTextAreaElement>
  placeholder?: string
  rows?: number
  maxLength?: number
  className?: string
  autoFocus?: boolean
  disabled?: boolean
  'aria-label'?: string
  'data-testid'?: string
  /**
   * When supplied, renders a small status line under the textarea:
   * saving → "Gemmer…", saved → "Gemt ✓" (green), error → red retry line.
   * The caller is responsible for transitioning the value; the "saved" line
   * clears itself after ~2s but the state stays 'saved' until the caller changes it.
   */
  saveStatus?: SaveStatus
}

// A single mutation applied to the textarea via setRangeText so the browser
// undo stack and React onChange both stay intact.
function applyEdit(
  el: HTMLTextAreaElement,
  start: number,
  end: number,
  text: string,
  selStart: number,
  selEnd: number
) {
  // setRangeText bypasses the textarea's maxLength (that attribute only guards
  // typing / paste). Truncate the replacement so a toolbar action or list
  // continuation can never push the value past the configured limit — the
  // backend rejects anything over string(8000).
  const limit = el.maxLength
  if (limit > 0) {
    const room = limit - (el.value.length - (end - start))
    if (text.length > room) {
      const clamped = Math.max(0, room)
      text = text.slice(0, clamped)
      selStart = Math.min(selStart, start + clamped)
      selEnd = Math.min(selEnd, start + clamped)
    }
  }
  el.focus()
  el.setSelectionRange(start, end)
  el.setRangeText(text, start, end, 'preserve')
  el.setSelectionRange(selStart, selEnd)
  el.dispatchEvent(new Event('input', { bubbles: true }))
}

function wrapSelection(el: HTMLTextAreaElement, marker: string) {
  const start = el.selectionStart
  const end = el.selectionEnd
  const selected = el.value.slice(start, end)
  const replacement = `${marker}${selected}${marker}`
  if (start === end) {
    // No selection: drop the markers and park the caret between them.
    const caret = start + marker.length
    applyEdit(el, start, end, replacement, caret, caret)
  } else {
    applyEdit(el, start, end, replacement, start + marker.length, end + marker.length)
  }
}

function prefixLinesWithBullet(el: HTMLTextAreaElement) {
  const value = el.value
  const start = el.selectionStart
  const end = el.selectionEnd
  const lineStart = value.lastIndexOf('\n', start - 1) + 1
  let lineEnd = value.indexOf('\n', end)
  if (lineEnd === -1) {
    lineEnd = value.length
  }
  const block = value.slice(lineStart, lineEnd)
  const prefixed = block
    .split('\n')
    .map((line) => `- ${line}`)
    .join('\n')
  const added = prefixed.length - block.length
  applyEdit(el, lineStart, lineEnd, prefixed, start + 2, end + added)
}

// Enter handler: continue / end markdown lists.
// Returns true when it handled the key (caller should preventDefault).
function handleListEnter(el: HTMLTextAreaElement): boolean {
  const value = el.value
  const start = el.selectionStart
  const end = el.selectionEnd
  if (start !== end) {
    return false
  }
  const lineStart = value.lastIndexOf('\n', start - 1) + 1
  const line = value.slice(lineStart, start)

  const bullet = /^(\s*)([-*]) (.*)$/.exec(line)
  const numbered = /^(\s*)(\d+)\. (.*)$/.exec(line)
  if (!bullet && !numbered) {
    return false
  }

  const indent = (bullet ?? numbered)![1]
  const content = (bullet ?? numbered)![3]

  if (content.trim() === '') {
    // Empty marker → clear the line (marker + indent), ending the list.
    applyEdit(el, lineStart, start, '', lineStart, lineStart)
    return true
  }

  let nextMarker: string
  if (bullet) {
    nextMarker = `${bullet[2]} `
  } else {
    const n = parseInt(numbered![2], 10) + 1
    nextMarker = `${n}. `
  }
  const insert = `\n${indent}${nextMarker}`
  applyEdit(el, start, end, insert, start + insert.length, start + insert.length)
  return true
}

// Rewrite a line-leading "* " to "- " so stored markdown uses one bullet
// marker. Only touches "*<space>" at the very start of a line — never the
// "*" used for emphasis. Runs synchronously during the input event, when the
// element already holds the freshly typed value.
function normaliseStarBullet(el: HTMLTextAreaElement) {
  const value = el.value
  const caret = el.selectionStart
  if (caret < 2 || el.selectionStart !== el.selectionEnd) {
    return
  }
  const lineStart = value.lastIndexOf('\n', caret - 1) + 1
  if (value.slice(lineStart, caret) === '* ') {
    applyEdit(el, lineStart, lineStart + 1, '-', caret, caret)
  }
}

// A real toolbar button: bordered, square, clear pressed/hover states. Grouped
// with negative margins so B/I/• read as one segmented control.
const toolbarBtn =
  'inline-flex items-center justify-center w-8 h-8 border border-gray-300 bg-white text-gray-700 ' +
  'hover:bg-gray-100 hover:text-gray-900 active:bg-gray-200 transition-colors text-sm leading-none ' +
  'focus:outline-none focus:ring-2 focus:ring-brand-500 focus:z-10 disabled:opacity-50 ' +
  'first:rounded-l-md last:rounded-r-md -ml-px first:ml-0'

const previewToggleBtn =
  'inline-flex items-center gap-1 text-xs text-gray-500 hover:text-gray-800 transition-colors ' +
  'focus:outline-none focus:ring-2 focus:ring-brand-500 rounded px-1 py-0.5'

export function MarkdownTextarea({
  value,
  onChange,
  onBlur,
  onKeyDown,
  placeholder,
  rows = 5,
  maxLength,
  className = '',
  autoFocus,
  disabled,
  saveStatus,
  'aria-label': ariaLabel,
  'data-testid': dataTestid,
}: MarkdownTextareaProps) {
  const ref = useRef<HTMLTextAreaElement>(null)
  const savedTimer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const [savedVisible, setSavedVisible] = React.useState(false)
  const [previewHidden, setPreviewHidden] = React.useState(readPreviewHidden)

  function togglePreview() {
    setPreviewHidden((hidden) => {
      const next = !hidden
      writePreviewHidden(next)
      return next
    })
  }

  useEffect(() => {
    if (saveStatus === 'saved') {
      setSavedVisible(true)
      savedTimer.current = setTimeout(() => setSavedVisible(false), 2000)
    } else {
      setSavedVisible(false)
    }
    return () => {
      if (savedTimer.current) {
        clearTimeout(savedTimer.current)
      }
    }
  }, [saveStatus])

  function handleKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    const el = ref.current
    if (el) {
      const mod = e.ctrlKey || e.metaKey
      if (mod && (e.key === 'b' || e.key === 'B')) {
        e.preventDefault()
        wrapSelection(el, '**')
        return
      }
      if (mod && (e.key === 'i' || e.key === 'I')) {
        e.preventDefault()
        wrapSelection(el, '*')
        return
      }
      if (e.key === 'Enter' && !e.shiftKey && !mod) {
        if (handleListEnter(el)) {
          e.preventDefault()
          return
        }
      }
    }
    onKeyDown?.(e)
  }

  function handleInput(e: React.ChangeEvent<HTMLTextAreaElement>) {
    // Normalise "* " → "- " on the element first — it holds the just-typed
    // value here — which fires a fresh input event carrying the corrected text.
    normaliseStarBullet(e.currentTarget)
    onChange(e.currentTarget.value)
  }

  // Keep the textarea focused & its selection intact when a toolbar button is
  // pressed (mousedown on a button would otherwise blur the textarea first).
  function preventBlur(e: React.MouseEvent) {
    e.preventDefault()
  }

  function toolbarAction(fn: (el: HTMLTextAreaElement) => void) {
    const el = ref.current
    if (el) {
      fn(el)
    }
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-1.5">
        <div className="inline-flex">
          <button
            type="button"
            disabled={disabled}
            onMouseDown={preventBlur}
            onClick={() => toolbarAction((el) => wrapSelection(el, '**'))}
            title="Fed (Ctrl+B)"
            aria-label="Fed"
            className={`${toolbarBtn} font-bold`}
          >
            B
          </button>
          <button
            type="button"
            disabled={disabled}
            onMouseDown={preventBlur}
            onClick={() => toolbarAction((el) => wrapSelection(el, '*'))}
            title="Kursiv (Ctrl+I)"
            aria-label="Kursiv"
            className={`${toolbarBtn} italic font-serif`}
          >
            I
          </button>
          <button
            type="button"
            disabled={disabled}
            onMouseDown={preventBlur}
            onClick={() => toolbarAction(prefixLinesWithBullet)}
            title="Punktliste"
            aria-label="Punktliste"
            className={toolbarBtn}
          >
            <svg
              width="14"
              height="14"
              viewBox="0 0 16 16"
              fill="none"
              stroke="currentColor"
              strokeWidth="1.6"
              strokeLinecap="round"
              aria-hidden="true"
            >
              <circle cx="2.5" cy="4" r="1.1" fill="currentColor" stroke="none" />
              <circle cx="2.5" cy="12" r="1.1" fill="currentColor" stroke="none" />
              <line x1="6" y1="4" x2="14" y2="4" />
              <line x1="6" y1="12" x2="14" y2="12" />
            </svg>
          </button>
        </div>
        <button
          type="button"
          onMouseDown={preventBlur}
          onClick={togglePreview}
          className={previewToggleBtn}
          aria-pressed={!previewHidden}
          data-testid={dataTestid ? `${dataTestid}-preview-toggle` : undefined}
        >
          {previewHidden ? 'Vis forhåndsvisning' : 'Skjul forhåndsvisning'}
        </button>
      </div>
      <textarea
        ref={ref}
        value={value}
        onChange={handleInput}
        onBlur={onBlur}
        onKeyDown={handleKeyDown}
        placeholder={placeholder}
        rows={rows}
        maxLength={maxLength}
        autoFocus={autoFocus}
        disabled={disabled}
        aria-label={ariaLabel}
        data-testid={dataTestid}
        className={`resize-y ${className}`}
      />
      {!previewHidden && (
        <div
          className="mt-2 rounded-lg border border-gray-200 bg-gray-50 px-3 py-2"
          data-testid={dataTestid ? `${dataTestid}-preview` : undefined}
        >
          <p className="text-[11px] font-medium uppercase tracking-wide text-gray-400 mb-1">
            Forhåndsvisning
          </p>
          {value.trim() ? (
            <div className="prose prose-sm max-w-none text-sm text-gray-700 [&_p]:my-1 [&_p:first-child]:mt-0 [&_p:last-child]:mb-0 [&_ul]:my-1 [&_ol]:my-1 [&_li]:my-0.5">
              <Markdown>{value}</Markdown>
            </div>
          ) : (
            <p className="text-sm italic text-gray-400">Ingenting endnu</p>
          )}
        </div>
      )}
      {saveStatus && saveStatus !== 'idle' && (
        <p
          className={`text-xs mt-1 ${
            saveStatus === 'error'
              ? 'text-red-600'
              : saveStatus === 'saving'
                ? 'text-gray-500'
                : 'text-green-600'
          }`}
          data-testid={dataTestid ? `${dataTestid}-save-status` : undefined}
        >
          {saveStatus === 'saving' && 'Gemmer…'}
          {saveStatus === 'saved' && savedVisible && 'Gemt ✓'}
          {saveStatus === 'error' && 'Kunne ikke gemme — prøv igen'}
        </p>
      )}
    </div>
  )
}
