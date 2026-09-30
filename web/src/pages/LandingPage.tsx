import { useState } from 'react'
import { Helmet } from 'react-helmet-async'
import Logo from '../components/Logo'
import Footer from '../components/Footer'
import CookieBanner from '../components/CookieBanner'
import SeoMeta from '../components/SeoMeta'
import type { BillingInterval } from '../api/client'

// Keep in sync with web/src/pages/BillingPage.tsx — same prices, must match Stripe Price IDs in StripeOptions.
const MONTHLY_PRICE_KR = 300
const YEARLY_PRICE_KR = 3000
const YEARLY_EFFECTIVE_MONTHLY_KR = Math.round(YEARLY_PRICE_KR / 12)
const YEARLY_SAVINGS_KR = MONTHLY_PRICE_KR * 12 - YEARLY_PRICE_KR

const PARENT_MODULE_MONTHLY_KR = 300
const PARENT_MODULE_YEARLY_KR = 3000
const BOARD_MODULE_MONTHLY_KR = 300
const BOARD_MODULE_YEARLY_KR = 3000

const ALL_MODULES_YEARLY_TOTAL_KR =
  YEARLY_PRICE_KR + PARENT_MODULE_YEARLY_KR + BOARD_MODULE_YEARLY_KR

const SOFTWARE_APPLICATION_JSON_LD = {
  '@context': 'https://schema.org',
  '@type': 'SoftwareApplication',
  name: 'Skoleoverblikket',
  applicationCategory: 'BusinessApplication',
  operatingSystem: 'Web',
  description:
    'Billig og enkel skoleadministration. Skema, SFO, ugeplan, vikardækning og forældrekontakt samlet i ét system — i stedet for fem. Prøv gratis i 30 dage.',
  offers: {
    '@type': 'Offer',
    price: String(MONTHLY_PRICE_KR),
    priceCurrency: 'DKK',
    priceValidUntil: '2026-12-31',
    description: 'Basis-abonnement pr. måned, inkl. moms, pr. skole. Ingen bindingsperiode.',
  },
}

export default function LandingPage() {
  const [billingInterval, setBillingInterval] = useState<BillingInterval>('Monthly')
  const isYearly = billingInterval === 'Yearly'
  return (
    <div className="min-h-screen bg-white font-sans text-gray-900">
      <SeoMeta
        title="Billig og enkel skoleadministration til friskoler og privatskoler"
        description="Billig og enkel skoleadministration. Skema, SFO, ugeplan, vikardækning og forældrekontakt samlet i ét system — i stedet for fem. Prøv gratis i 30 dage."
        path="/"
      />
      <Helmet>
        <script type="application/ld+json">{JSON.stringify(SOFTWARE_APPLICATION_JSON_LD)}</script>
      </Helmet>
      {/* Nav */}
      <nav className="sticky top-0 z-50 bg-white border-b border-gray-100 shadow-sm">
        <div className="max-w-6xl mx-auto px-4 sm:px-6 h-14 sm:h-16 flex items-center justify-between gap-4">
          <div className="flex items-center gap-2 min-w-0">
            <Logo variant="light" size={24} />
            <span className="font-display text-base sm:text-xl font-semibold text-brand-800 truncate">
              Skoleoverblikket
            </span>
          </div>
          <div className="flex items-center gap-2 sm:gap-4 shrink-0">
            <a
              href="/login"
              className="text-sm text-gray-600 hover:text-brand-700 transition-colors whitespace-nowrap"
            >
              Log ind
            </a>
            <a
              href="/kontakt"
              className="hidden sm:inline text-sm text-brand-700 hover:text-brand-800 transition-colors whitespace-nowrap font-medium"
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

      {/* Hero */}
      <section className="py-24 px-6 text-center bg-gradient-to-b from-brand-50 to-white">
        <div className="max-w-3xl mx-auto">
          <h1 className="font-display text-5xl sm:text-6xl font-semibold text-brand-900 leading-tight">
            Spar tid på det kedelige — brug den på børnene
          </h1>
          <p className="mt-6 text-lg text-gray-600 max-w-xl mx-auto">
            Skema, SFO, ugeplan, vikardækning og forældrekontakt i ét system. Bygget til friskoler.
            Ingen oplæring nødvendig.
          </p>
          <div className="mt-8 flex flex-col sm:flex-row gap-3 justify-center">
            <a
              href="/signup"
              className="px-6 py-3 bg-brand-600 text-white text-base font-medium rounded-lg hover:bg-brand-700 transition-colors"
            >
              Kom i gang gratis — på 2 minutter
            </a>
            <a
              href="/kontakt"
              className="px-6 py-3 bg-white text-brand-700 text-base font-medium rounded-lg border border-brand-200 hover:bg-brand-50 transition-colors"
            >
              Book en demo
            </a>
          </div>
          <p className="mt-5 text-sm text-gray-400">
            Intet kreditkort · Ingen binding · Klar til brug samme dag
          </p>
        </div>
      </section>

      {/* Features */}
      <section className="py-20 px-6 bg-white">
        <div className="max-w-6xl mx-auto">
          <h2 className="font-display text-3xl font-semibold text-center text-gray-900 mb-12">
            Alt hvad din skole behøver
          </h2>
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-6">
            <FeatureCard
              icon={
                <svg
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.8"
                  className="w-6 h-6"
                >
                  <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
                </svg>
              }
              title="Konfliktkontrol i realtid"
              description="Systemet advarer øjeblikkeligt om dobbeltbookede lærere eller lokaler — uden manuel kontrol."
            />
            <FeatureCard
              icon={
                <svg
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.8"
                  className="w-6 h-6"
                >
                  <rect x="3" y="3" width="18" height="18" rx="2" />
                  <path d="M3 9h18M9 21V9" />
                </svg>
              }
              title="Skemabygger"
              description="Træk og slip lektioner på plads. Nemt at lære — for alle, uanset teknisk erfaring."
            />
            <FeatureCard
              icon={
                <svg
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.8"
                  className="w-6 h-6"
                >
                  <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
                  <circle cx="9" cy="7" r="4" />
                  <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
                  <path d="M16 3.13a4 4 0 0 1 0 7.75" />
                </svg>
              }
              title="Medarbejderoversigt"
              description="Se alle medarbejderes skemaer samlet — lærere, pædagoger og vikarer."
            />
            <FeatureCard
              icon={
                <svg
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.8"
                  className="w-6 h-6"
                >
                  <polyline points="6 9 6 2 18 2 18 9" />
                  <path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2" />
                  <rect x="6" y="14" width="12" height="8" />
                </svg>
              }
              title="Udskriv skemaer"
              description="Udskriv klasse-, lærer- og lokaleskemaer med ét klik. Print-venligt format."
            />
            <FeatureCard
              icon={
                <svg
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.8"
                  className="w-6 h-6"
                >
                  <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                  <polyline points="14 2 14 8 20 8" />
                </svg>
              }
              title="Filhåndtering"
              description="Upload og del filer pr. fag. Let tilgængeligt for alle medarbejdere."
            />
            <FeatureCard
              icon={
                <svg
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.8"
                  className="w-6 h-6"
                >
                  <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
                  <circle cx="9" cy="7" r="4" />
                  <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
                  <path d="M16 3.13a4 4 0 0 1 0 7.75" />
                  <path d="M3 13h4M9 13h4" stroke="currentColor" strokeWidth="1.8" />
                </svg>
              }
              title="Forældremodul"
              description="Forældre får adgang til klassens skema, kalender og ugeplan. Kontaktbog, beskeder og kontaktbibliotek inkluderet."
            />
            <FeatureCard
              icon={
                <svg
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.8"
                  className="w-6 h-6"
                >
                  <rect x="2" y="7" width="20" height="14" rx="2" />
                  <path d="M16 7V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v2" />
                  <line x1="12" y1="12" x2="12" y2="16" />
                  <line x1="10" y1="14" x2="14" y2="14" />
                </svg>
              }
              title="Bestyrelse & Tilsyn"
              description={
                'Bestyrelsesmedlemmer får dedikeret adgang med statistikker og dokumentdeling. Inkluderer overblik over "stå mål med"-dækning pr. fag og klasse.'
              }
            />
            <FeatureCard
              icon={
                <svg
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.8"
                  className="w-6 h-6"
                >
                  <rect x="3" y="4" width="18" height="18" rx="2" />
                  <path d="M16 2v4M8 2v4M3 10h18" />
                  <path d="M8 14h.01M12 14h.01M16 14h.01M8 18h.01M12 18h.01M16 18h.01" />
                </svg>
              }
              title="SFO ugeplan"
              description="Byg og udskriv SFO's ugeoverblik med ét klik. Printvenligt format — klar til opslagstavlen."
            />
            <FeatureCard
              icon={
                <svg
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.8"
                  className="w-6 h-6"
                >
                  <path d="M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2" />
                  <rect x="9" y="3" width="6" height="4" rx="1" />
                  <polyline points="9 12 11 14 15 10" />
                </svg>
              }
              title="Ferieindmelding"
              description="Forældre melder ind om barnet har behov for pasning i ferien. Overblik over tilmeldte, og eksport til CSV med ét klik."
            />
            <FeatureCard
              icon={
                <svg
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.8"
                  className="w-6 h-6"
                >
                  <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                  <polyline points="17 8 12 3 7 8" />
                  <line x1="12" y1="3" x2="12" y2="15" />
                </svg>
              }
              title="Nem import fra regneark"
              description="Indsæt data fra Excel — elever, forældre, medarbejdere og lokaler oprettes på få minutter. Ingen manuel indtastning."
            />
          </div>
        </div>
      </section>

      {/* Feature screenshots */}
      <section className="py-20 px-6 bg-brand-50 border-t border-brand-100">
        <div className="max-w-6xl mx-auto space-y-24">
          <FeatureShowcase
            eyebrow="Skemabygger"
            title="Træk og slip — konflikter opdages med det samme"
            description="Byg hele ugeskemaet visuelt. Systemet viser øjeblikkeligt om en lærer eller et lokale er dobbeltbooket — ingen manuel kontrol."
            imageSrc="/media/schema_1a.png"
            imageAlt="Skemabygger med ugeoversigt"
            imageRight
          />
          <FeatureShowcase
            eyebrow="Konfliktkontrol"
            title="Ingen overraskelser når skemaet er sat"
            description="Konfliktpanelet viser præcis hvilke lektioner der kolliderer og hvorfor. Ret dem direkte — uden at starte forfra."
            imageSrc="/media/schema_1a_conflict.png"
            imageAlt="Konfliktoverblik"
          />
          <FeatureShowcase
            eyebrow="Forældremodul"
            title="Forældre med i loopet — uden ekstra arbejde"
            description="Forældre inviteres med ét klik og ser klassens skema, kalender og ugeplan. Kontaktbog, gruppebesked og fraværsindberetning er inkluderet."
            imageSrc="/media/forældre.png"
            imageAlt="Forældreoversigt"
            imageRight
          />
          <FeatureShowcase
            eyebrow="Stå mål med"
            title="Hold styr på minimumstimetallet"
            description="Se på ét skærmbillede om alle klasser opfylder UVM's vejledende timetal pr. fag. Rød, gul, grøn — opdateres automatisk."
            imageSrc="/media/staa-maal-med.png"
            imageAlt="Stå mål med-overblik"
          />
        </div>
      </section>

      {/* Audience */}
      <section className="py-16 px-6 bg-white border-t border-gray-100">
        <div className="max-w-4xl mx-auto text-center">
          <h2 className="font-display text-3xl font-semibold text-brand-900 mb-4">
            Hvem er Skoleoverblikket til?
          </h2>
          <p className="text-gray-600 mb-10 max-w-xl mx-auto">
            Skoleoverblikket passer til alle skoler der vil samle skema, SFO, ugeplan og
            forældrekontakt i ét enkelt system — uanset udgangspunktet.
          </p>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 text-left max-w-2xl mx-auto">
            <AudienceItem
              title="Nyoprettet skole"
              description="Intet at flytte — kom i gang samme dag og byg skemaet op fra start."
            />
            <AudienceItem
              title="Skoler der vil skifte system"
              description="Skift fra et gammelt eller dyrt system. Hjælp til overgangen kan købes som tillægsservice."
            />
            <AudienceItem
              title="Skoler med begrænset IT-erfaring"
              description="Ingen IT-afdeling nødvendig. Fungerer fra dag ét uden kursus."
            />
            <AudienceItem
              title="Skoler der vil spare tid og penge"
              description="Fast lav månedspris — ingen binding, ingen skjulte gebyrer."
            />
          </div>
        </div>
      </section>

      {/* Pricing */}
      <section id="priser" className="py-20 px-6 bg-brand-900 text-white">
        <div className="max-w-5xl mx-auto text-center">
          <h2 className="font-display text-3xl font-semibold mb-3">Enkel og gennemsigtig pris</h2>
          <p className="text-brand-200 mb-2">300 kr/md pr. modul — udvid efter behov.</p>
          <p className="text-brand-300 text-sm mb-12">
            Vælg alle tre årligt og betal {ALL_MODULES_YEARLY_TOTAL_KR} kr/år i alt
          </p>
          {/* Layout: Basis left, modules stacked right */}
          <div className="flex flex-col lg:flex-row gap-6 items-start max-w-4xl mx-auto">
            {/* Basis card */}
            <div className="bg-white text-gray-900 rounded-2xl shadow-xl overflow-visible w-full lg:w-auto lg:flex-1">
              <div className="px-8 pt-8 pb-6 bg-brand-50 border-b border-brand-100 rounded-t-2xl">
                <p className="text-sm font-medium text-brand-600 uppercase tracking-wide">Basis</p>
                <div className="mt-2 flex items-end gap-1 justify-center">
                  <span className="font-display text-5xl font-semibold text-brand-900">
                    {isYearly ? YEARLY_EFFECTIVE_MONTHLY_KR : MONTHLY_PRICE_KR}
                  </span>
                  <span className="text-lg text-gray-500 mb-2">kr/md</span>
                </div>
                <p className="text-sm text-gray-500 mt-1">
                  {isYearly
                    ? `${YEARLY_PRICE_KR} kr/år · inkl. moms · pr. skole`
                    : 'inkl. moms · pr. skole'}
                </p>
                <div className="mt-4 flex items-center justify-center gap-2">
                  <div
                    className="inline-flex items-center rounded-lg border border-brand-200 bg-white p-1"
                    role="tablist"
                    aria-label="Betalingsinterval"
                  >
                    {(['Monthly', 'Yearly'] as const).map((opt) => (
                      <button
                        key={opt}
                        type="button"
                        role="tab"
                        aria-selected={billingInterval === opt}
                        onClick={() => setBillingInterval(opt)}
                        className={`px-3 py-1.5 text-sm font-medium rounded-md transition-colors ${
                          billingInterval === opt
                            ? 'bg-brand-600 text-white'
                            : 'text-brand-700 hover:bg-brand-50'
                        }`}
                      >
                        {opt === 'Monthly' ? 'Månedligt' : 'Årligt'}
                      </button>
                    ))}
                  </div>
                </div>
                <div className="mt-3 flex items-center justify-center gap-2">
                  {isYearly && (
                    <span className="px-2 py-0.5 text-xs font-medium rounded-full bg-green-100 text-green-700">
                      Spar {YEARLY_SAVINGS_KR} kr/år
                    </span>
                  )}
                  <span className="px-2 py-0.5 text-xs font-medium rounded-full bg-amber-100 text-amber-700">
                    Intropris
                  </span>
                </div>
              </div>
              <div className="px-8 py-6 space-y-3 text-left">
                {[
                  'Ubegrænset antal klasser og lærere',
                  'Konfliktkontrol – ingen dobbeltbookede lærere eller lokaler',
                  'Medarbejder- og lokaleskemaer',
                  'Printbare skemaer',
                  'Filhåndtering (100 GB)',
                  'E-mail support (svar inden 5 hverdage)',
                ].map((item) => (
                  <div key={item} className="flex items-center gap-3 text-sm">
                    <svg
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2.5"
                      className="w-4 h-4 text-brand-600 shrink-0"
                    >
                      <polyline points="20 6 9 17 4 12" />
                    </svg>
                    <span>{item}</span>
                  </div>
                ))}
                <div className="flex items-center gap-3 text-sm">
                  <svg
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2.5"
                    className="w-4 h-4 text-brand-600 shrink-0"
                  >
                    <polyline points="20 6 9 17 4 12" />
                  </svg>
                  <span>Ingen bindingsperiode — opsig når som helst</span>
                  <RefundTooltip />
                </div>
                <div className="pt-4">
                  <a
                    href={`/signup?interval=${billingInterval}`}
                    className="block w-full text-center py-3 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors"
                  >
                    Start 30 dages gratis prøve
                  </a>
                  <p className="text-xs text-center text-gray-400 mt-2">
                    Intet kreditkort påkrævet
                  </p>
                </div>
              </div>
            </div>

            {/* Add-on modules — separate cards stacked */}
            <div className="w-full lg:w-auto lg:flex-1 flex flex-col gap-3">
              <div className="text-center text-xs text-brand-200 uppercase tracking-wide font-medium pb-1">
                Valgfrie tillægsmoduler
              </div>
              <ModuleCard
                title="Forældremodul"
                monthlyPrice={PARENT_MODULE_MONTHLY_KR}
                yearlyPrice={PARENT_MODULE_YEARLY_KR}
                isYearly={isYearly}
                features={[
                  'Ugeplaner og kalenderadgang',
                  'Kontaktbog og beskeder',
                  'Kontaktbibliotek',
                  'Fraværsindberetning',
                ]}
              />
              <ModuleCard
                title="Bestyrelse & Tilsyn"
                monthlyPrice={BOARD_MODULE_MONTHLY_KR}
                yearlyPrice={BOARD_MODULE_YEARLY_KR}
                isYearly={isYearly}
                features={[
                  'Filhåndtering (100 GB)',
                  'Overblik over "stå mål med"-dækning',
                  'UVM-timetal sammenligning pr. fag',
                ]}
              />
              <p className="text-xs text-brand-200/70 text-center pt-1">
                Kræver Basis · Samles på én faktura
              </p>
            </div>
          </div>
        </div>
      </section>

      {/* Trust */}
      <section className="py-16 px-6 bg-white border-t border-gray-100">
        <div className="max-w-4xl mx-auto grid grid-cols-1 sm:grid-cols-3 gap-8 text-center">
          <TrustItem
            icon={
              <svg
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="1.8"
                className="w-6 h-6 mx-auto"
              >
                <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
              </svg>
            }
            title="Data opbevares i EU"
            description="Alle data gemmes på EU-baserede servere i overensstemmelse med GDPR."
          />
          <TrustItem
            icon={
              <svg
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="1.8"
                className="w-6 h-6 mx-auto"
              >
                <line x1="12" y1="1" x2="12" y2="23" />
                <path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6" />
              </svg>
            }
            title="Gennemsigtige priser"
            description="300 kr/md for Basis. Ingen bindingsperiode. Ingen skjulte gebyrer."
          />
          <TrustItem
            icon={
              <svg
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="1.8"
                className="w-6 h-6 mx-auto"
              >
                <circle cx="12" cy="12" r="10" />
                <polyline points="12 6 12 12 16 14" />
              </svg>
            }
            title="Simpelt fra dag ét"
            description="Ingen oplæring nødvendig. De fleste skoler er kørende på under 2 minutter."
          />
        </div>
      </section>

      {/* CTA footer */}
      <section className="py-20 px-6 bg-brand-50 text-center">
        <div className="max-w-2xl mx-auto">
          <h2 className="font-display text-3xl font-semibold text-brand-900">
            Klar til at spare tid?
          </h2>
          <p className="mt-3 text-gray-600">
            30 dage gratis. Intet kreditkort. Kom i gang på under 2 minutter — uanset hvilken skole
            du er.
          </p>
          <a
            href="/signup"
            className="inline-block mt-6 px-8 py-3 bg-brand-600 text-white text-base font-medium rounded-lg hover:bg-brand-700 transition-colors"
          >
            Opret din skole gratis
          </a>
        </div>
      </section>

      <Footer />
      <CookieBanner />
    </div>
  )
}

function FeatureCard({
  icon,
  title,
  description,
}: {
  icon: React.ReactNode
  title: string
  description: string
}) {
  return (
    <div className="p-6 rounded-xl border border-gray-100 bg-brand-50 hover:border-brand-200 transition-colors">
      <div className="w-10 h-10 bg-brand-100 text-brand-700 rounded-lg flex items-center justify-center mb-4">
        {icon}
      </div>
      <h3 className="font-semibold text-gray-900 mb-1">{title}</h3>
      <p className="text-sm text-gray-600 leading-relaxed">{description}</p>
    </div>
  )
}

function TrustItem({
  icon,
  title,
  description,
}: {
  icon: React.ReactNode
  title: string
  description: string
}) {
  return (
    <div className="space-y-2">
      <div className="text-brand-600">{icon}</div>
      <h3 className="font-semibold text-gray-900">{title}</h3>
      <p className="text-sm text-gray-500">{description}</p>
    </div>
  )
}

function AudienceItem({ title, description }: { title: string; description: string }) {
  return (
    <div className="p-5 rounded-xl bg-white border border-brand-100">
      <h3 className="font-semibold text-gray-900 mb-1">{title}</h3>
      <p className="text-sm text-gray-600 leading-relaxed">{description}</p>
    </div>
  )
}

function ModuleCard({
  title,
  monthlyPrice,
  yearlyPrice,
  isYearly,
  features,
}: {
  title: string
  monthlyPrice: number
  yearlyPrice: number
  isYearly: boolean
  features: string[]
}) {
  const effectiveMonthly = isYearly ? Math.round(yearlyPrice / 12) : monthlyPrice
  const yearlySavings = monthlyPrice * 12 - yearlyPrice
  return (
    <div className="bg-white text-gray-900 rounded-2xl shadow-xl overflow-hidden">
      <div className="px-6 pt-6 pb-4 bg-brand-50 border-b border-brand-100 text-center">
        <p className="text-sm font-medium text-brand-600 uppercase tracking-wide">{title}</p>
        <div className="mt-2 flex items-end gap-1 justify-center">
          <span className="text-lg text-gray-500 mb-2">+</span>
          <span className="font-display text-5xl font-semibold text-brand-900">
            {effectiveMonthly}
          </span>
          <span className="text-lg text-gray-500 mb-2">kr/md</span>
        </div>
        <p className="text-sm text-gray-500 mt-1">
          {isYearly ? `${yearlyPrice} kr/år · inkl. moms · pr. skole` : 'inkl. moms · pr. skole'}
        </p>
        <div className="mt-2 flex items-center justify-center gap-2">
          {isYearly && (
            <span className="px-2 py-0.5 text-xs font-medium rounded-full bg-green-100 text-green-700">
              Spar {yearlySavings} kr/år
            </span>
          )}
          <span className="px-2 py-0.5 text-xs font-medium rounded-full bg-amber-100 text-amber-700">
            Intropris
          </span>
        </div>
      </div>
      <div className="px-6 py-4 space-y-2">
        {features.map((f) => (
          <div key={f} className="flex items-center gap-3 text-sm">
            <svg
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.5"
              className="w-4 h-4 text-brand-600 shrink-0"
            >
              <polyline points="20 6 9 17 4 12" />
            </svg>
            <span>{f}</span>
          </div>
        ))}
      </div>
    </div>
  )
}

function FeatureShowcase({
  eyebrow,
  title,
  description,
  imageSrc,
  imageAlt,
  imageRight,
}: {
  eyebrow: string
  title: string
  description: string
  imageSrc: string
  imageAlt: string
  imageRight?: boolean
}) {
  const text = (
    <div className="lg:w-5/12 flex flex-col justify-center">
      <p className="text-xs font-semibold uppercase tracking-widest text-brand-600 mb-3">
        {eyebrow}
      </p>
      <h3 className="font-display text-2xl sm:text-3xl font-semibold text-gray-900 mb-4">
        {title}
      </h3>
      <p className="text-gray-600 leading-relaxed">{description}</p>
    </div>
  )
  const image = (
    <div className="lg:w-7/12">
      <div className="rounded-xl overflow-hidden shadow-xl border border-gray-100">
        <img src={imageSrc} alt={imageAlt} className="w-full block" loading="lazy" />
      </div>
    </div>
  )
  return (
    <div
      className={`flex flex-col lg:flex-row items-center gap-12 ${imageRight ? '' : 'lg:flex-row-reverse'}`}
    >
      {text}
      {image}
    </div>
  )
}

function RefundTooltip() {
  return (
    <div className="group relative inline-flex cursor-default select-none shrink-0">
      <svg
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        className="w-3.5 h-3.5 text-gray-400 group-hover:text-gray-600 transition-colors"
      >
        <rect x="3" y="11" width="18" height="11" rx="2" ry="2" />
        <path d="M7 11V7a5 5 0 0 1 9.9-1" />
      </svg>
      <div className="pointer-events-none absolute bottom-full right-0 mb-2 w-64 rounded-lg bg-gray-900 px-3 py-2 text-xs text-gray-100 opacity-0 group-hover:opacity-100 transition-opacity z-10 shadow-lg text-left">
        Abonnementet kan opsiges til enhver tid.
      </div>
    </div>
  )
}
