import { StrictMode } from 'react'
import { createRoot, hydrateRoot } from 'react-dom/client'
// Fonts are self-hosted: loading them from Google would send every visitor's IP address to the US.
import '@fontsource/lato/latin-400.css'
import '@fontsource/lato/latin-700.css'
import '@fontsource/playfair-display/latin-600.css'
import './index.css'
import './api/client'
import App from './App.tsx'

// After a deploy the old bundle's lazy chunks are gone, so opening another page fails. Reload to get
// the new bundle (mid-deploy, the maintenance page shows and reloads once the site is back). At most
// once per 10 s, so a chunk that is really broken reaches the ErrorBoundary instead of a reload loop.
window.addEventListener('vite:preloadError', (event) => {
  const key = 'skoleoverblikket_chunk_reload'
  try {
    if (Date.now() - Number(sessionStorage.getItem(key) ?? 0) < 10_000) return
    sessionStorage.setItem(key, String(Date.now()))
  } catch {
    return
  }
  event.preventDefault()
  window.location.reload()
})

const root = document.getElementById('root')!
const app = (
  <StrictMode>
    <App />
  </StrictMode>
)

// Marketing pages ship prerendered HTML (scripts/prerender.mjs); every other route gets an empty root.
if (root.hasChildNodes()) hydrateRoot(root, app)
else createRoot(root).render(app)
