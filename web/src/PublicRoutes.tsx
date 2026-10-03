import { Routes, Route } from 'react-router-dom'
import LandingPage from './pages/LandingPage'
import AboutPage from './pages/AboutPage'
import ContactPage from './pages/ContactPage'
import PrivacyPolicyPage from './pages/PrivacyPolicyPage'
import ChangelogPage from './pages/ChangelogPage'

/** Marketing pages prerendered to static HTML at build time (see scripts/prerender.mjs). */
export const PRERENDERED_PATHS = ['/', '/om', '/kontakt', '/nyheder', '/privatlivspolitik']

/**
 * Marketing pages for build-time prerendering, so crawlers see content instead of a blank page.
 * App.tsx renders the same pages outside its AuthReady gate, so the browser hydrates this HTML
 * and keeps it mounted when Keycloak finishes initializing.
 */
export default function PublicRoutes() {
  return (
    <Routes>
      <Route path="/" element={<LandingPage />} />
      <Route path="om" element={<AboutPage />} />
      <Route path="kontakt" element={<ContactPage />} />
      <Route path="nyheder" element={<ChangelogPage />} />
      <Route path="privatlivspolitik" element={<PrivacyPolicyPage />} />
      <Route path="*" element={null} />
    </Routes>
  )
}
