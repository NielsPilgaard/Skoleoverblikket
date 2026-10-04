import { StrictMode } from 'react'
import { createRoot, hydrateRoot } from 'react-dom/client'
// Fonts are self-hosted: loading them from Google would send every visitor's IP address to the US.
import '@fontsource/lato/latin-400.css'
import '@fontsource/lato/latin-700.css'
import '@fontsource/playfair-display/latin-600.css'
import './index.css'
import './api/client'
import App from './App.tsx'

const root = document.getElementById('root')!
const app = (
  <StrictMode>
    <App />
  </StrictMode>
)

// Marketing pages ship prerendered HTML (scripts/prerender.mjs); every other route gets an empty root.
if (root.hasChildNodes()) hydrateRoot(root, app)
else createRoot(root).render(app)
