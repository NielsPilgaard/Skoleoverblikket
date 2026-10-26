import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { getApiV1ExternalLinksOptions } from '../api/generated/@tanstack/react-query.gen'
import type { ExternalLinkDto } from '../api/generated/types.gen'

const MAX_LINKS = 6

/**
 * "https://www.aula.dk/portal" → "aula.dk". Shown small under the title so people know where they go.
 * Read from the string, not `new URL`, which would show "fælles.dk" as punycode ("xn--flles-qra.dk").
 */
export function linkHost(url: string): string {
  const host = url.match(/^[a-z][a-z0-9+.-]*:\/\/(?:[^@/?#]*@)?([^/?#]+)/i)?.[1]
  return host ? host.replace(/^www\./i, '') : url
}

export function ExternalLinkIcon({ className = 'w-4 h-4' }: { className?: string }) {
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
      <path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6" />
      <polyline points="15 3 21 3 21 9" />
      <line x1="10" y1="14" x2="21" y2="3" />
    </svg>
  )
}

/** One external link that opens in a new tab. */
export function ExternalLinkRow({ link }: { link: ExternalLinkDto }) {
  return (
    <a
      href={link.url}
      target="_blank"
      rel="noopener noreferrer"
      data-testid="external-link"
      className="flex items-start gap-3 px-5 py-3 hover:bg-brand-50/40 transition-colors"
    >
      <span className="flex items-center justify-center w-8 h-8 rounded-lg bg-brand-50 text-brand-600 shrink-0">
        <ExternalLinkIcon />
      </span>
      <span className="min-w-0 flex-1">
        <span className="block text-sm font-medium text-gray-800">{link.title}</span>
        {link.description && (
          <span className="block text-sm text-gray-500">{link.description}</span>
        )}
        <span className="block text-xs text-gray-400 truncate">{linkHost(link.url)}</span>
      </span>
    </a>
  )
}

/** The school's links on a dashboard. Hidden when the school has none for this role. */
export default function LinksCard({ allTo }: { allTo: string }) {
  const { data: links } = useQuery(getApiV1ExternalLinksOptions())

  if (!links || links.length === 0) return null

  return (
    <div
      className="bg-white rounded-xl border border-gray-200 overflow-hidden"
      data-testid="links-card"
    >
      <div className="px-5 py-4 border-b border-gray-100 flex items-center justify-between gap-4">
        <h2 className="text-sm font-semibold text-gray-700">Links</h2>
        {links.length > MAX_LINKS && (
          <Link
            to={allTo}
            className="text-sm text-brand-600 hover:text-brand-700 font-medium whitespace-nowrap"
          >
            Se alle →
          </Link>
        )}
      </div>
      <ul className="divide-y divide-gray-50">
        {links.slice(0, MAX_LINKS).map((link) => (
          <li key={link.id}>
            <ExternalLinkRow link={link} />
          </li>
        ))}
      </ul>
    </div>
  )
}
