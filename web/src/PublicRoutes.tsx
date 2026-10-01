import { Routes, Route } from 'react-router-dom'
import LandingPage from './pages/LandingPage'
import AboutPage from './pages/AboutPage'
import ContactPage from './pages/ContactPage'
import PrivacyPolicyPage from './pages/PrivacyPolicyPage'

/** Marketing pages prerendered to static HTML at build time (see scripts/prerender.mjs). */
export const PRERENDERED_PATHS = ['/', '/om', '/kontakt', '/privatlivspolitik']

/**
 * Marketing pages that render without auth. Used for build-time prerendering and while
 * Keycloak initializes, so crawlers and visitors see content instead of a blank page.
 * Any other path renders nothing until auth is ready.
 */
export default function PublicRoutes() {
  return (
    <Routes>
      <Route path="/" element={<LandingPage />} />
      <Route path="om" element={<AboutPage />} />
      <Route path="kontakt" element={<ContactPage />} />
      <Route path="privatlivspolitik" element={<PrivacyPolicyPage />} />
      <Route path="*" element={null} />
    </Routes>
  )
}
