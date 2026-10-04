import Footer from '../components/Footer'
import PublicNav from '../components/PublicNav'
import SeoMeta from '../components/SeoMeta'

export default function ContactPage() {
  return (
    <div className="min-h-screen bg-white font-sans text-gray-900 flex flex-col">
      <SeoMeta
        title="Kontakt"
        description="Kontakt Skoleoverblikket. Skriv til kontakt@skoleoverblikket.dk, så svarer vi typisk inden for 1–2 hverdage."
        path="/kontakt"
      />
      <PublicNav />

      <main className="flex-1 py-20 px-6">
        <div className="max-w-2xl mx-auto">
          <div className="text-center mb-12">
            <h1 className="font-display text-4xl sm:text-5xl font-semibold text-brand-900 leading-tight mb-4">
              Kontakt
            </h1>
            <p className="text-lg text-gray-600">
              Har du spørgsmål til Skoleoverblikket? Skriv til os, så svarer vi typisk inden for 1–2
              hverdage.
            </p>
          </div>

          <div className="text-center bg-brand-50 rounded-2xl border border-brand-100 px-6 py-10 mb-10">
            <p className="text-sm font-medium text-brand-700 mb-2">E-mail</p>
            <a
              href="mailto:kontakt@skoleoverblikket.dk"
              data-testid="contact-email"
              className="font-display text-lg sm:text-3xl font-semibold text-brand-900 hover:text-brand-700 transition-colors break-words"
            >
              kontakt@skoleoverblikket.dk
            </a>
            <p className="mt-3 text-sm text-gray-600">
              Både til nye skoler og til jer, der allerede bruger Skoleoverblikket.
            </p>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <a
              href="/book-demo"
              data-testid="contact-link-demo"
              className="block p-6 rounded-2xl border border-gray-200 hover:border-brand-300 hover:bg-brand-50 transition-colors"
            >
              <p className="font-display text-lg font-semibold text-brand-900 mb-1">
                Vil du se systemet?
              </p>
              <p className="text-sm text-gray-600">
                Book en gratis demo, så viser vi dig Skoleoverblikket. →
              </p>
            </a>
            <a
              href="/signup"
              data-testid="contact-link-signup"
              className="block p-6 rounded-2xl border border-gray-200 hover:border-brand-300 hover:bg-brand-50 transition-colors"
            >
              <p className="font-display text-lg font-semibold text-brand-900 mb-1">
                Vil du bare i gang?
              </p>
              <p className="text-sm text-gray-600">
                Prøv gratis i 30 dage. Intet kreditkort og ingen binding. →
              </p>
            </a>
          </div>

          <div className="mt-12 pt-8 border-t border-gray-100 text-sm text-gray-400 space-y-1">
            <p className="font-medium text-gray-500">Virksomhedsoplysninger</p>
            <p>Skoleoverblikket drives af Pilgaard Development</p>
            <p>Nordlyvej 20, 8550 Ryomgård</p>
          </div>
        </div>
      </main>

      <Footer />
    </div>
  )
}
