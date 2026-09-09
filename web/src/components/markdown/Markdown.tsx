import ReactMarkdown from 'react-markdown'
import remarkBreaks from 'remark-breaks'
import { MD_ALLOWED_TAGS } from './allowed'

// Single newlines from MarkdownTextarea (e.g. a plain multi-line note without
// bullets) must render as line breaks, matching what staff typed. Plain
// CommonMark collapses a lone "\n" to a space.
const REMARK_PLUGINS = [remarkBreaks]

// CommonMark treats any run of 2+ blank lines as a single paragraph break —
// typing three blank lines to space content out looks identical to typing
// one. Splice a zero-width space onto each *extra* blank line (keeping them
// inside one paragraph so remarkBreaks turns each "\n" into its own <br>,
// matching the newline count staff actually typed) — but only from the
// second blank line on. The first blank line in a run is left untouched so
// it still ends the preceding block normally: a ZWS makes a line non-blank,
// and CommonMark lazily continues a list into the next line once it no
// longer sees a blank line, which would swallow a following paragraph into
// the list's last item (and inherit its indentation) instead of splitting.
function preserveBlankLineRuns(markdown: string): string {
  return markdown.replace(/\n{3,}/g, (run) => {
    const lines = run.split('\n')
    return `\n${lines.slice(1).join('\n​')}`
  })
}

// Tailwind's Preflight resets ul/ol to list-style:none and strips their
// padding, so a rendered <ul> looks identical to plain text. Re-apply the
// list markers and indentation here (arbitrary-variant utilities — no
// @tailwindcss/typography in this project). Every Markdown consumer gets
// this; per-call wrappers only tweak margins.
const LIST_STYLES = '[&_ul]:list-disc [&_ul]:pl-5 [&_ol]:list-decimal [&_ol]:pl-5 [&_li]:my-0.5'

/**
 * Renders the weekplan / SFO free-text markdown with the shared restricted
 * tag allowlist. Same rendering on parent views, print views and cell previews.
 */
export function Markdown({ children }: { children: string | null | undefined }) {
  if (!children) {
    return null
  }
  return (
    <div className={LIST_STYLES}>
      <ReactMarkdown
        remarkPlugins={REMARK_PLUGINS}
        allowedElements={MD_ALLOWED_TAGS}
        unwrapDisallowed
      >
        {preserveBlankLineRuns(children)}
      </ReactMarkdown>
    </div>
  )
}
