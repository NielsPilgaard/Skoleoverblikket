import { renderToString } from 'react-dom/server'
import { StaticRouter } from 'react-router-dom'
import { HelmetProvider, type HelmetServerState } from 'react-helmet-async'
import PublicRoutes, { PRERENDERED_PATHS } from './PublicRoutes'

export { PRERENDERED_PATHS }

/** Build-time only: renders a marketing page to HTML plus the head tags its SeoMeta/Helmet produced. */
export function render(url: string) {
  const helmetContext: { helmet?: HelmetServerState | null } = {}
  const html = renderToString(
    <HelmetProvider context={helmetContext}>
      <StaticRouter location={url}>
        <PublicRoutes />
      </StaticRouter>
    </HelmetProvider>
  )
  const helmet = helmetContext.helmet
  const head = helmet
    ? [helmet.title, helmet.meta, helmet.link, helmet.script].map((t) => t.toString()).join('\n')
    : ''
  return { html, head }
}
