import { Link } from 'react-router-dom'
import LegalPage, { Li, Section } from '../components/LegalPage'
import { RETENTION_DAYS_AFTER_CANCELLATION, SUB_PROCESSORS } from '../content/dataProcessing'

export default function PrivacyPolicyPage() {
  return (
    <LegalPage
      title="Privatlivspolitik"
      description="Læs Skoleoverblikkets privatlivspolitik om behandling af personoplysninger for skoler, medarbejdere og forældre."
      path="/privatlivspolitik"
      updated="Senest opdateret: oktober 2026"
    >
      <Section title="1. Dataansvarlig">
        <p>Den dataansvarlige for behandlingen af dine personoplysninger er:</p>
        <address className="not-italic mt-3 text-sm bg-brand-50 rounded-xl p-4 border border-brand-100">
          <strong className="text-gray-900">Skoleoverblikket</strong>
          <br />
          E-mail:{' '}
          <a href="mailto:kontakt@skoleoverblikket.dk" className="text-brand-700 hover:underline">
            kontakt@skoleoverblikket.dk
          </a>
        </address>
      </Section>

      <Section title="2. Hvilke oplysninger behandler vi?">
        <p>Vi behandler følgende personoplysninger:</p>
        <ul className="mt-3 space-y-2 text-sm list-none">
          <Li>
            <strong>Kontooplysninger:</strong> navn og e-mailadresse på skolens administratorer og
            medarbejdere.
          </Li>
          <Li>
            <strong>Skoledata:</strong> klassenavne, medarbejdernavne, fagnavne, lokalenavne og
            skemaer.
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
            <strong>Session-cookie:</strong> en enkelt cookie der bruges til login-godkendelse.
            Ingen sporings- eller reklamecookies.
          </Li>
        </ul>
      </Section>

      <Section title="3. Formål og retsgrundlag">
        <p>
          Vi behandler dine oplysninger for at kunne levere og drifte Skoleoverblikket-tjenesten.
          Retsgrundlaget er opfyldelse af aftale (GDPR artikel 6, stk. 1, litra b) og i relevant
          omfang vores legitime interesse i at drive og forbedre tjenesten (artikel 6, stk. 1, litra
          f).
        </p>
      </Section>

      <Section title="4. Adgangskontrol og sikkerhed">
        <p>
          Al skoledata er strengt isoleret pr. skole (tenant). Det er kun medlemmer af en given
          skole der kan tilgå den pågældende skoles data. Vi anvender tekniske og organisatoriske
          sikkerhedsforanstaltninger for at beskytte dine oplysninger mod uautoriseret adgang, tab
          eller misbrug.
        </p>
      </Section>

      <Section title="5. Skolen er dataansvarlig, vi er databehandler">
        <p>
          For de oplysninger, skolen lægger i Skoleoverblikket om elever, forældre, medarbejdere og
          bestyrelse, er skolen dataansvarlig, og Skoleoverblikket er databehandler. Det er
          reguleret i{' '}
          <Link to="/databehandleraftale" className="text-brand-700 hover:underline">
            databehandleraftalen
          </Link>
          , som skolen accepterer, når den oprettes.
        </p>
        <p>Vi bruger disse underdatabehandlere:</p>
        <ul className="mt-3 space-y-2 text-sm list-none">
          {SUB_PROCESSORS.map((p) => (
            <Li key={p.name}>
              <strong>{p.name}</strong> — {p.purpose.toLowerCase()}. {p.location}.
            </Li>
          ))}
        </ul>
        <p>
          Se detaljerne på{' '}
          <Link to="/underdatabehandlere" className="text-brand-700 hover:underline">
            listen over underdatabehandlere
          </Link>
          .
        </p>
      </Section>

      <Section title="6. Opbevaring og sletning">
        <p>
          Dine oplysninger opbevares i abonnementsperioden. Ved opsigelse opbevares data i{' '}
          {RETENTION_DAYS_AFTER_CANCELLATION} dage for at give mulighed for genaktivering eller
          eksport. Skolens administratorer får en e-mail 7 dage før, og derefter slettes alle
          skolens data automatisk og permanent, også uploadede filer og brugernes logins.
        </p>
        <p className="mt-3">
          Elevers fravær og fremmøde gemmes i indeværende og forrige skoleår. Skoleåret skifter 1.
          august, og så slettes fravær fra det ældste skoleår automatisk. Skolen får besked 1. juli,
          så den kan hente fraværet som Excel-fil først. Fravær slettes også, når en elev slettes.
        </p>
      </Section>

      <Section title="7. Dine rettigheder">
        <p>
          Du har ret til at anmode om indsigt i, berigtigelse eller sletning af de personoplysninger
          vi behandler om dig. Du kan rette henvendelse til{' '}
          <a href="mailto:kontakt@skoleoverblikket.dk" className="text-brand-700 hover:underline">
            kontakt@skoleoverblikket.dk
          </a>
          . Vi besvarer din henvendelse inden for 30 dage.
        </p>
      </Section>

      <Section title="8. Cookies">
        <p>
          Vi anvender én session-cookie udelukkende til at opretholde din login-session. Vi benytter
          ingen analyse-, sporings- eller reklamecookies.
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
    </LegalPage>
  )
}
