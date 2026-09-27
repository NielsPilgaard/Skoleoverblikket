import type { ReactNode } from 'react'

/**
 * Matches bare URLs and www-prefixed hosts. Kept deliberately narrow: only http/https
 * links become anchors, so a `javascript:` or `data:` payload pasted into a message
 * renders as inert text.
 */
const URL_PATTERN = /(https?:\/\/[^\s<>"']+|www\.[^\s<>"']+)/gi

/** Trailing punctuation that is almost always sentence punctuation, not part of the URL. */
const TRAILING_PUNCTUATION = /[.,;:!?)\]}'"]+$/

function isSafeHref(href: string): boolean {
  try {
    const { protocol } = new URL(href)
    return protocol === 'http:' || protocol === 'https:'
  } catch {
    return false
  }
}

/**
 * Splits plain message text into text nodes and safe anchor elements.
 * Never renders HTML from the message body — every segment is a React text node,
 * so the output cannot inject markup.
 */
export function linkify(text: string): ReactNode[] {
  const nodes: ReactNode[] = []
  let lastIndex = 0
  let key = 0

  URL_PATTERN.lastIndex = 0

  let match = URL_PATTERN.exec(text)
  while (match !== null) {
    const raw = match[0]
    const trailing = raw.match(TRAILING_PUNCTUATION)?.[0] ?? ''
    const candidate = trailing ? raw.slice(0, -trailing.length) : raw
    const href = candidate.startsWith('www.') ? `https://${candidate}` : candidate

    if (match.index > lastIndex) {
      nodes.push(text.slice(lastIndex, match.index))
    }

    if (isSafeHref(href)) {
      nodes.push(
        <a
          key={`link-${key++}`}
          href={href}
          target="_blank"
          rel="noopener noreferrer nofollow"
          className="text-brand-700 underline underline-offset-2 hover:text-brand-800 break-all"
        >
          {candidate}
        </a>
      )
    } else {
      nodes.push(candidate)
    }

    if (trailing) {
      nodes.push(trailing)
    }

    lastIndex = match.index + raw.length
    match = URL_PATTERN.exec(text)
  }

  if (lastIndex < text.length) {
    nodes.push(text.slice(lastIndex))
  }

  return nodes
}
