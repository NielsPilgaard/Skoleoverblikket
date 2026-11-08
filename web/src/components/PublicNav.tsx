import Logo from './Logo'

/**
 * Top bar for every marketing page (/, /om, /kontakt, /book-demo, /nyheder and the legal pages).
 * Logo, "Log ind" and "Prøv gratis" fit on one line at 375px; the rest appears on wider screens.
 */
export default function PublicNav() {
  return (
    <nav className="sticky top-0 z-50 bg-white border-b border-gray-100 shadow-sm">
      <div className="max-w-6xl mx-auto px-4 sm:px-6 h-14 sm:h-16 flex items-center justify-between gap-4">
        <div className="flex items-center gap-8 min-w-0">
          <a href="/" className="flex items-center gap-2 min-w-0">
            <Logo variant="light" size={24} />
            <span className="font-display text-base sm:text-xl font-semibold text-brand-800 truncate">
              Skoleoverblikket
            </span>
          </a>
          <a
            href="/nyheder"
            className="hidden md:inline text-sm text-gray-600 hover:text-brand-700 transition-colors whitespace-nowrap"
          >
            Nyheder
          </a>
        </div>
        <div className="flex items-center gap-2 sm:gap-3 shrink-0">
          <a
            href="/login"
            className="text-sm text-gray-600 hover:text-brand-700 transition-colors whitespace-nowrap sm:mr-1"
          >
            Log ind
          </a>
          <a
            href="/book-demo"
            className="hidden sm:inline text-sm px-4 py-2 text-brand-700 border border-brand-200 rounded-lg hover:bg-brand-50 transition-colors whitespace-nowrap font-medium"
          >
            Book demo
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
