import { Link } from 'react-router-dom'
import LegalPage, { Li, List, MailLink, Section } from '../components/LegalPage'
import {
  BACKUP_RETENTION_DAYS,
  COMPANY,
  RETENTION_DAYS_AFTER_CANCELLATION,
  SUB_PROCESSORS,
} from '../content/dataProcessing'

export default function PrivacyPolicyPage() {
  return (
    <LegalPage
      title="Privatlivspolitik"
      description="Læs Skoleoverblikkets privatlivspolitik om behandling af personoplysninger for skoler, medarbejdere og forældre."
      path="/privatlivspolitik"
      updated="Senest opdateret: oktober 2026"
    >
      <Section title="1. Hvem er vi?">
        <p>Skoleoverblikket drives af:</p>
        <address className="not-italic mt-3 text-sm bg-brand-50 rounded-xl p-4 border border-brand-100">
          <strong className="text-gray-900">{COMPANY.name}</strong>
          <br />
          CVR {COMPANY.cvr}
          <br />
          {COMPANY.address}
          <br />
          E-mail: <MailLink />
        </address>
      </Section>

      <Section title="2. Skolen er dataansvarlig for skolens data">
        <p>
          For de oplysninger, skolen lægger i Skoleoverblikket om elever, forældre, medarbejdere og
          bestyrelse, er skolen dataansvarlig. Er skolen en folkeskole, er det kommunen. Vi
          behandler kun oplysningerne på skolens vegne og efter{' '}
          <Link to="/databehandleraftale" className="text-brand-700 hover:underline">
            databehandleraftalen
          </Link>
          , som skolen accepterer, når den oprettes.
        </p>
        <p>
          Det er skolen, der bestemmer, hvilke oplysninger der registreres, og på hvilket
          retsgrundlag. Det står i skolens egen privatlivspolitik. Vil du have indsigt i eller
          rettet oplysninger om dig eller dit barn, så kontakt skolen. Skriver du til os, sender vi
          henvendelsen videre til skolen.
        </p>
        <p>Skolens data i Skoleoverblikket omfatter blandt andet:</p>
        <List>
          <Li>
            <strong>Brugere:</strong> navn, e-mail, telefonnummer, rolle og billede på medarbejdere,
            forældre og bestyrelsesmedlemmer.
          </Li>
          <Li>
            <strong>Elever:</strong> navn, klasse, billede og tilknyttede forældre.
          </Li>
          <Li>
            <strong>Skema, ugeplaner, kalender, beskeder og filer</strong>, som skolen og dens
            brugere opretter.
          </Li>
          <Li>
            <strong>Fravær og fremmøde:</strong> elevers daglige fremmøde og fravær med kategorien
            sygdom, ekstraordinær frihed eller ulovligt fravær, forældres sygemeldinger og
            anmodninger om fri samt skolens afgørelser.
          </Li>
          <Li>
            <strong>Medarbejderfravær og vikardækning:</strong> hvornår en medarbejder er
            fraværende, en valgfri note og hvilke vikarer der dækker de berørte lektioner.
          </Li>
        </List>
        <p>
          At nogen er syg, er en helbredsoplysning. Det er nok at registrere, at der er tale om
          sygdom. Skriv ikke diagnoser eller andre helbredsoplysninger i bemærkningsfelterne.
        </p>
        <p>
          Til at behandle skolens data bruger vi{' '}
          <Link to="/underdatabehandlere" className="text-brand-700 hover:underline">
            disse underdatabehandlere
          </Link>
          : {SUB_PROCESSORS.map((p) => p.name).join(', ')}.
        </p>
      </Section>

      <Section title="3. Oplysninger, vi selv er dataansvarlige for">
        <List>
          <Li>
            <strong>Skolen som kunde:</strong> skolens navn og abonnement og navn og e-mail på den
            administrator, der opretter skolen eller accepterer databehandleraftalen. Formålet er at
            administrere kundeforholdet og dokumentere aftalen. Retsgrundlaget er vores legitime
            interesse i at kunne det (GDPR artikel 6, stk. 1, litra f).
          </Li>
          <Li>
            <strong>Betaling og bogføring:</strong> fakturaer og betalinger. Retsgrundlaget er
            bogføringslovens krav (artikel 6, stk. 1, litra c). Vi opbevarer ikke kortoplysninger.
          </Li>
          <Li>
            <strong>Demo-forespørgsler og henvendelser:</strong> navn, skole, e-mail, telefonnummer
            og besked, når du skriver til os. Formålet er at svare dig. Retsgrundlaget er vores
            legitime interesse i at besvare henvendelser (artikel 6, stk. 1, litra f).
          </Li>
          <Li>
            <strong>Fejllogs:</strong> tekniske oplysninger om fejl og interne id'er, så vi kan
            rette fejl og holde tjenesten sikker. Navne, kontaktoplysninger og indtastede data
            fjernes, før en fejl logges. Retsgrundlaget er vores legitime interesse i at drive en
            sikker og stabil tjeneste (artikel 6, stk. 1, litra f).
          </Li>
        </List>
      </Section>

      <Section title="4. Hvem deler vi oplysningerne med?">
        <List>
          <Li>
            <strong>Stripe Payments Europe, Limited</strong> (Irland) håndterer abonnement og
            betaling og er selv dataansvarlig for kortoplysningerne. Stripe kan overføre oplysninger
            til USA under EU-U.S. Data Privacy Framework.
          </Li>
          <Li>
            <strong>OVH SAS</strong> (Frankrig) driver vores servere, og{' '}
            <strong>Scaleway SAS</strong> (Frankrig) sender vores e-mails.
          </Li>
          <Li>
            <strong>elmah.io ApS</strong> (Danmark) opbevarer vores fejllogs hos Microsoft Azure i
            USA. Microsoft er certificeret under EU-U.S. Data Privacy Framework.
          </Li>
        </List>
      </Section>

      <Section title="5. Sikkerhed">
        <p>
          Hver skoles data er adskilt fra andre skolers, og kun skolens egne brugere kan se dem. Al
          trafik er krypteret, og database, filer og sikkerhedskopier ligger i EU. Se de konkrete
          foranstaltninger i{' '}
          <Link to="/databehandleraftale" className="text-brand-700 hover:underline">
            databehandleraftalen
          </Link>
          .
        </p>
      </Section>

      <Section title="6. Opbevaring og sletning">
        <p>
          Skolens data opbevares i abonnementsperioden. Ved opsigelse opbevares de i{' '}
          {RETENTION_DAYS_AFTER_CANCELLATION} dage for at give mulighed for genaktivering eller
          eksport. Skolens administratorer får en e-mail 7 dage før, og derefter slettes alle
          skolens data automatisk og permanent, også uploadede filer og brugernes logins.
          Sikkerhedskopier af databasen gemmes i {BACKUP_RETENTION_DAYS} dage og overskrives
          derefter.
        </p>
        <p>
          Elevers fravær og fremmøde gemmes i indeværende og forrige skoleår. Skoleåret skifter 1.
          august, og så slettes fravær fra det ældste skoleår automatisk. Skolen får besked 1. juli,
          så den kan hente fraværet som Excel-fil først. Fravær slettes også, når en elev slettes.
          Medarbejderfravær følger samme regel.
        </p>
        <p>
          Fakturaer og betalinger gemmes i 5 år fra udgangen af regnskabsåret, som bogføringsloven
          kræver. Henvendelser slettes, når dialogen er afsluttet og ikke fører til et kundeforhold.
          Fejllogs slettes, når vi ikke længere har brug for dem til at rette fejl.
        </p>
      </Section>

      <Section title="7. Dine rettigheder">
        <p>
          Du har ret til indsigt i de oplysninger, vi behandler om dig, og til at få dem rettet,
          slettet eller begrænset. Du har også ret til at få dine oplysninger udleveret
          (dataportabilitet) og til at gøre indsigelse mod behandling, der sker på grundlag af vores
          legitime interesse.
        </p>
        <p>
          Gælder det oplysninger, skolen er dataansvarlig for, så kontakt skolen. Ellers skriv til{' '}
          <MailLink />. Vi svarer inden for en måned.
        </p>
      </Section>

      <Section title="8. Cookies og lokal lagring">
        <p>
          Login sætter nogle få cookies, der holder dig logget ind og beskytter login mod misbrug.
          Skoleoverblikket gemmer også dit login og dine visningsvalg, fx hvilke menuer der er
          foldet ud, i browserens lokale lager. Det er nødvendigt, for at tjenesten virker og husker
          dine valg, og kræver derfor ikke samtykke.
        </p>
        <p>
          Vi bruger ingen analyse-, sporings- eller reklamecookies, og skrifttyper og andre filer
          hentes fra vores egne servere. Når skolen betaler, sker det på Stripes egen side, som har
          sine egne cookies.
        </p>
      </Section>

      <Section title="9. Lovvalg og klage">
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
