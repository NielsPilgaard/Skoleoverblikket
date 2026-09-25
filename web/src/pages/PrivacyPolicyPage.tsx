import Logo from '../components/Logo'
import Footer from '../components/Footer'
import SeoMeta from '../components/SeoMeta'

export default function PrivacyPolicyPage() {
  return (
    <div className="min-h-screen bg-white font-sans text-gray-900 flex flex-col">
      <SeoMeta
        title="Privatlivspolitik"
        description="Læs Skoleoverblikkets privatlivspolitik om behandling af personoplysninger for skoler, medarbejdere og forældre."
        path="/privatlivspolitik"
      />
      <PublicNav />

      <main className="flex-1 py-20 px-6">
        <div className="max-w-2xl mx-auto">
          <h1 className="font-display text-4xl sm:text-5xl font-semibold text-brand-900 leading-tight mb-2">
            Privatlivspolitik
          </h1>
          <p className="text-sm text-gray-400 mb-10">Senest opdateret: april 2026</p>

          <div className="space-y-10 text-gray-700 leading-relaxed">
            <Section title="1. Dataansvarlig">
              <p>Den dataansvarlige for behandlingen af dine personoplysninger er:</p>
              <address className="not-italic mt-3 text-sm bg-brand-50 rounded-xl p-4 border border-brand-100">
                <strong className="text-gray-900">Skoleoverblikket</strong>
                <br />
                E-mail:{' '}
                <a
                  href="mailto:kontakt@skoleoverblikket.dk"
                  className="text-brand-700 hover:underline"
                >
                  kontakt@skoleoverblikket.dk
                </a>
              </address>
            </Section>

            <Section title="2. Hvilke oplysninger behandler vi?">
              <p>Vi behandler følgende personoplysninger:</p>
              <ul className="mt-3 space-y-2 text-sm list-none">
                <Li>
                  <strong>Kontooplysninger:</strong> navn og e-mailadresse på skolens
                  administratorer og medarbejdere.
                </Li>
                <Li>
                  <strong>Skoledata:</strong> klassenavne, medarbejdernavne, fagnavne, lokalenavne
                  og skemaer.
                </Li>
                <Li>
                  <strong>Uploadede filer:</strong> filer der uploades pr. skole, og som kun er
                  tilgængelige for den pågældende skoles brugere.
                </Li>
                <Li>
                  <strong>Betalingsdata:</strong> håndteres udelukkende af Stripe. Vi opbevarer ikke
                  kortoplysninger eller andre betalingsdata.
                </Li>
                <Li>
                  <strong>Session-cookie:</strong> en enkelt cookie der bruges til
                  login-godkendelse. Ingen sporings- eller reklamecookies.
                </Li>
              </ul>
            </Section>

            <Section title="3. Formål og retsgrundlag">
              <p>
                Vi behandler dine oplysninger for at kunne levere og drifte
                Skoleoverblikket-tjenesten. Retsgrundlaget er opfyldelse af aftale (GDPR artikel 6,
                stk. 1, litra b) og i relevant omfang vores legitime interesse i at drive og
                forbedre tjenesten (artikel 6, stk. 1, litra f).
              </p>
            </Section>

            <Section title="4. Adgangskontrol og sikkerhed">
              <p>
                Al skoledata er strengt isoleret pr. skole (tenant). Det er kun medlemmer af en
                given skole der kan tilgå den pågældende skoles data. Vi anvender tekniske og
                organisatoriske sikkerhedsforanstaltninger for at beskytte dine oplysninger mod
                uautoriseret adgang, tab eller misbrug.
              </p>
            </Section>

            <Section title="5. Databehandlere (underdatabehandlere)">
              <p>Vi anvender følgende underdatabehandlere:</p>
              <ul className="mt-3 space-y-2 text-sm list-none">
                <Li>
                  <strong>Stripe</strong> — betaling og fakturering. Stripe er underlagt sin egen
                  databehandleraftale (DPA).
                </Li>
                <Li>
                  <strong>OVHcloud</strong> — hosting og filopbevaring. Servere befinder sig i
                  EU-datacentre.
                </Li>
              </ul>
            </Section>

            <Section title="6. Opbevaring og sletning">
              <p>
                Dine oplysninger opbevares i abonnementsperioden. Ved opsigelse opbevares data i 90
                dage for at give mulighed for genaktivering eller eksport — herefter slettes de
                permanent.
              </p>
              <p className="mt-3 text-sm text-gray-500">
                Bemærk: automatisk sletning er endnu ikke implementeret. Indtil videre håndteres det
                manuelt. Kontakt os, hvis du ønsker øjeblikkelig sletning.
              </p>
            </Section>

            <Section title="7. Dine rettigheder">
              <p>
                Du har ret til at anmode om indsigt i, berigtigelse eller sletning af de
                personoplysninger vi behandler om dig. Du kan rette henvendelse til{' '}
                <a
                  href="mailto:kontakt@skoleoverblikket.dk"
                  className="text-brand-700 hover:underline"
                >
                  kontakt@skoleoverblikket.dk
                </a>
                . Vi besvarer din henvendelse inden for 30 dage.
              </p>
            </Section>

            <Section title="8. Cookies">
              <p>
                Vi anvender én session-cookie udelukkende til at opretholde din login-session. Vi
                benytter ingen analyse-, sporings- eller reklamecookies.
              </p>
            </Section>

            <Section title="9. Lovvalg og tilsynsmyndighed">
              <p>
                Behandlingen af personoplysninger er underlagt dansk ret. Hvis du mener, at vi ikke
                behandler dine oplysninger korrekt, kan du klage til:
              </p>
              <address className="not-italic mt-3 text-sm bg-brand-50 rounded-xl p-4 border border-brand-100">
                <strong className="text-gray-900">Datatilsynet</strong>
                <br />
                <a
                  href="https://www.datatilsynet.dk"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="text-brand-700 hover:underline"
                >
                  datatilsynet.dk
                </a>
              </address>
            </Section>
          </div>
        </div>
      </main>

      <Footer />
    </div>
  )
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section>
      <h2 className="font-display text-xl font-semibold text-gray-900 mb-3">{title}</h2>
      <div className="space-y-3">{children}</div>
    </section>
  )
}

function Li({ children }: { children: React.ReactNode }) {
  return (
    <li className="flex gap-2">
      <span className="mt-1 w-1.5 h-1.5 rounded-full bg-brand-400 shrink-0" />
      <span>{children}</span>
    </li>
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
