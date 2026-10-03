import Logo from './Logo'

/** Top bar for the marketing subpages (/om, /kontakt, /nyheder, /privatlivspolitik). */
export default function PublicNav() {
  return (
    <nav className="sticky top-0 z-50 bg-white border-b border-gray-100 shadow-sm">
      <div className="max-w-6xl mx-auto px-6 h-16 flex items-center justify-between">
        <a href="/" className="flex items-center gap-2">
          <Logo variant="light" size={28} />
          <span className="font-display text-xl font-semibold text-brand-800">
            Skoleoverblikket
          </span>
        </a>
        <div className="flex items-center gap-4">
          <a href="/login" className="text-sm text-gray-600 hover:text-brand-700 transition-colors">
            Log ind
          </a>
          <a
            href="/signup"
            className="text-sm px-4 py-2 bg-brand-600 text-white rounded-lg hover:bg-brand-700 transition-colors font-medium"
          >
            Prøv gratis
          </a>
        </div>
      </div>
    </nav>
  )
}
