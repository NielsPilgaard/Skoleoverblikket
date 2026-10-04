import Logo from './Logo'

/**
 * Top bar for the marketing subpages (/om, /kontakt, /nyheder, /privatlivspolitik). Sized like the
 * landing page nav so logo and buttons fit on one line at 375px.
 */
export default function PublicNav() {
  return (
    <nav className="sticky top-0 z-50 bg-white border-b border-gray-100 shadow-sm">
      <div className="max-w-6xl mx-auto px-4 sm:px-6 h-14 sm:h-16 flex items-center justify-between gap-4">
        <a href="/" className="flex items-center gap-2 min-w-0">
          <Logo variant="light" size={24} />
          <span className="font-display text-base sm:text-xl font-semibold text-brand-800 truncate">
            Skoleoverblikket
          </span>
        </a>
        <div className="flex items-center gap-2 sm:gap-4 shrink-0">
          <a
            href="/login"
            className="text-sm text-gray-600 hover:text-brand-700 transition-colors whitespace-nowrap"
          >
            Log ind
          </a>
          <a
            href="/signup"
            className="text-sm px-3 py-1.5 sm:px-4 sm:py-2 bg-brand-600 text-white rounded-lg hover:bg-brand-700 transition-colors font-medium whitespace-nowrap"
          >
            Prøv gratis
          </a>
        </div>
      </div>
    </nav>
  )
}
