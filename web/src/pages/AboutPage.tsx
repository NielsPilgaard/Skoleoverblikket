import Logo from '../components/Logo'
import Footer from '../components/Footer'
import SeoMeta from '../components/SeoMeta'

export default function AboutPage() {
  return (
    <div className="min-h-screen bg-white font-sans text-gray-900 flex flex-col">
      <SeoMeta
        title="Om Skoleoverblikket"
        description="Skoleoverblikket er bygget til friskoler, privatskoler og folkeskoler der vil have et enkelt skemaværktøj — uden kursus og uden IT-support."
        path="/om"
      />
      <PublicNav />

      <main className="flex-1 py-20 px-6">
        <div className="max-w-2xl mx-auto">
          <h1 className="font-display text-4xl sm:text-5xl font-semibold text-brand-900 leading-tight mb-4">
            Om Skoleoverblikket
          </h1>
          <p className="text-brand-600 text-lg font-medium mb-8">
            Enkelt skemaværktøj til alle skoler
          </p>

          <div className="prose-like space-y-6 text-gray-700 leading-relaxed">
            <p className="text-lg">
              Skoleoverblikket er bygget af mig, Niels — IT-udvikler med forståelse for, hvad der
              skal til for at hverdagen hænger sammen på en lille skole.
            </p>

            <p>
              Mange skemaværktøjer er bygget til store institutioner med budgetter og IT-ressourcer
              der matcher. Skoleoverblikket er anderledes: det er bygget til skoler der vil have
              noget enkelt — folkeskoler, friskoler, privatskoler og efterskoler — steder hvor den
              samme person både tager telefonen, booker vikar og hænger skemaet op mandag morgen.
            </p>

            <p>
              Derfor er enkelhed ikke bare et designvalg — det er et løfte. Skoleoverblikket skal
              kunne tages i brug fra dag ét, uden kursus og uden IT-support.
            </p>

            <p>
              Jeg står for udvikling, drift og support. Det betyder hurtige svar, direkte kontakt og
              et produkt der udvikler sig i takt med de behov skoler har.
            </p>

            <p className="text-brand-700 font-medium">
              Har du spørgsmål eller vil du høre, hvad Skoleoverblikket kan gøre for din skole?
              Skriv til{' '}
              <a
                href="mailto:kontakt@skoleoverblikket.dk"
                className="underline hover:text-brand-900 transition-colors"
              >
                kontakt@skoleoverblikket.dk
              </a>
              .
            </p>
          </div>

          <div className="mt-12 pt-8 border-t border-gray-100 text-sm text-gray-400 space-y-1">
            <p className="font-medium text-gray-500">Virksomhedsoplysninger</p>
            <p>Skoleoverblikket drives af Pilgaard Development</p>
            <p>Nordlyvej 20, 8550 Ryomgård</p>
            <p>
              <a href="tel:+4529917126" className="hover:text-brand-700 transition-colors">
                +45 29 91 71 26
              </a>
            </p>
            <p>
              <a
                href="mailto:kontakt@skoleoverblikket.dk"
                className="hover:text-brand-700 transition-colors"
              >
                kontakt@skoleoverblikket.dk
              </a>
            </p>
          </div>
        </div>
      </main>

      <Footer />
    </div>
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
