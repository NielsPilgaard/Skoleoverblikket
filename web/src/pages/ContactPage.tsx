import Logo from '../components/Logo'
import Footer from '../components/Footer'
import SeoMeta from '../components/SeoMeta'
import { useState } from 'react'

export default function ContactPage() {
  return (
    <div className="min-h-screen bg-white font-sans text-gray-900 flex flex-col">
      <SeoMeta
        title="Book en demo"
        description="Book en gratis demo af Skoleoverblikket. Vi kontakter dig hurtigst muligt med et tidspunkt."
        path="/kontakt"
      />
      <PublicNav />

      <main className="flex-1 py-20 px-6">
        <div className="max-w-xl mx-auto">
          <div className="text-center mb-10">
            <h1 className="font-display text-4xl sm:text-5xl font-semibold text-brand-900 leading-tight mb-4">
              Book en gratis demo
            </h1>
            <p className="text-lg text-gray-600">
              Bestil en gratis demo, så kontakter vi dig hurtigst muligt med et tidspunkt!
            </p>
          </div>

          <DemoForm />
        </div>
      </main>

      <Footer />
    </div>
  )
}

function DemoForm() {
  const [submitted, setSubmitted] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault()
    setLoading(true)
    setError(null)
    const form = e.currentTarget
    const data = new FormData(form)

    try {
      // Plain fetch — the generated client always calls keycloak.login() when no token exists,
      // which would redirect unauthenticated visitors to the login page.
      const res = await fetch('/api/v1/demo-request', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          navn: data.get('navn') as string,
          skole: data.get('skole') as string,
          email: data.get('email') as string,
          telefon: (data.get('telefon') as string) || null,
          besked: (data.get('besked') as string) || null,
        }),
      })

      if (!res.ok) {
        setError('Noget gik galt. Prøv igen eller ring til os.')
        return
      }

      setSubmitted(true)
    } catch {
      setError('Noget gik galt. Prøv igen eller ring til os.')
    } finally {
      setLoading(false)
    }
  }

  if (submitted) {
    return (
      <div className="text-center py-12 bg-brand-50 rounded-2xl border border-brand-100">
        <div className="w-12 h-12 bg-brand-600 rounded-full flex items-center justify-center mx-auto mb-4">
          <svg viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5" className="w-6 h-6">
            <polyline points="20 6 9 17 4 12" />
          </svg>
        </div>
        <h2 className="font-display text-2xl font-semibold text-brand-900 mb-2">Tak!</h2>
        <p className="text-gray-600">Vi kontakter dig hurtigst muligt med et tidspunkt.</p>
      </div>
    )
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div>
          <label htmlFor="navn" className="block text-sm font-medium text-gray-700 mb-1">
            Navn <span className="text-red-500">*</span>
          </label>
          <input
            id="navn"
            name="navn"
            type="text"
            required
            placeholder="Hanne Nielsen"
            className="w-full px-4 py-2.5 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>
        <div>
          <label htmlFor="skole" className="block text-sm font-medium text-gray-700 mb-1">
            Skole <span className="text-red-500">*</span>
          </label>
          <input
            id="skole"
            name="skole"
            type="text"
            required
            placeholder="Haslev Friskole"
            className="w-full px-4 py-2.5 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>
      </div>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div>
          <label htmlFor="email" className="block text-sm font-medium text-gray-700 mb-1">
            E-mail <span className="text-red-500">*</span>
          </label>
          <input
            id="email"
            name="email"
            type="email"
            required
            placeholder="hanne@haslev-friskole.dk"
            className="w-full px-4 py-2.5 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>
        <div>
          <label htmlFor="telefon" className="block text-sm font-medium text-gray-700 mb-1">
            Telefon
          </label>
          <input
            id="telefon"
            name="telefon"
            type="tel"
            placeholder="12 34 56 78"
            className="w-full px-4 py-2.5 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
          />
        </div>
      </div>
      <div>
        <label htmlFor="besked" className="block text-sm font-medium text-gray-700 mb-1">
          Evt. besked
        </label>
        <textarea
          id="besked"
          name="besked"
          rows={3}
          placeholder="Fortæl os gerne lidt om jeres situation..."
          className="w-full px-4 py-2.5 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent resize-none"
        />
      </div>
      {error && <p className="text-sm text-red-600 text-center">{error}</p>}
      <button
        type="submit"
        disabled={loading}
        className="w-full py-3 bg-brand-600 text-white font-medium rounded-lg hover:bg-brand-700 transition-colors disabled:opacity-60"
      >
        {loading ? 'Sender...' : 'Book gratis demo'}
      </button>
      <p className="text-xs text-center text-gray-400">Vi svarer typisk inden for 1–2 hverdage</p>
    </form>
  )
}

function PublicNav() {
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
