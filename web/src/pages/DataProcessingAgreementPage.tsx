import { Link } from 'react-router-dom'
import LegalPage, { Li, List, MailLink, Section } from '../components/LegalPage'
import {
  DPA_UPDATED,
  DPA_VERSION,
  RETENTION_DAYS_AFTER_CANCELLATION,
  SUB_PROCESSOR_NOTICE_DAYS,
} from '../content/dataProcessing'

/**
 * The databehandleraftale (GDPR art. 28) schools accept at signup. Changing the text: bump
 * DPA_VERSION in content/dataProcessing.ts and DataProcessingAgreementService.CurrentVersion.
 */
export default function DataProcessingAgreementPage() {
  return (
    <LegalPage
      title="Databehandleraftale"
      description="Databehandleraftalen mellem skolen og Skoleoverblikket efter GDPR artikel 28: hvad vi behandler, hvordan vi passer på det, og hvornår det slettes."
      path="/databehandleraftale"
      updated={`Version ${DPA_VERSION} · ${DPA_UPDATED}`}
    >
      <Section title="1. Parter">
        <p>
          Aftalen er indgået mellem skolen, der bruger Skoleoverblikket (den{' '}
          <strong>dataansvarlige</strong>
          ), og Skoleoverblikket (<strong>databehandleren</strong>). Den accepteres elektronisk af
          en administrator på skolens vegne, når skolen oprettes, eller senere i Skoleoverblikket.
          Aftalen gælder, så længe skolen har en konto, og indtil alle skolens data er slettet.
        </p>
        <p>
          Aftalen opfylder kravene i databeskyttelsesforordningen (GDPR) artikel 28, stk. 3. Ved
          uoverensstemmelse mellem denne aftale og andre aftaler mellem parterne går denne aftale
          forud for behandlingen af personoplysninger.
        </p>
      </Section>

      <Section title="2. Formål og behandlingens art">
        <p>
          Databehandleren behandler personoplysninger for at levere Skoleoverblikket til skolen:
          skema, ugeplan, SFO-plan, vikardækning, kalender, forældrekommunikation (kontaktbog og
          beskeder), fravær og fremmøde, ferieindmelding, filarkiv, bestyrelsesmodul, eksport og
          rapporter. Behandlingen omfatter opbevaring, visning, ændring, afsendelse af e-mails og
          sletning.
        </p>
      </Section>

      <Section title="3. Registrerede og oplysninger">
        <List>
          <Li>
            <strong>Elever:</strong> navn, klasse, billede, forældretilknytning, fravær og fremmøde
            (herunder at et fravær skyldes sygdom), ferieønsker og beskeder om eleven.
          </Li>
          <Li>
            <strong>Forældre:</strong> navn, e-mail, telefonnummer, adresse, billede, samtykke til
            deling af kontaktoplysninger og beskeder.
          </Li>
          <Li>
            <strong>Medarbejdere:</strong> navn, e-mail, telefonnummer, rolle, billede, skema,
            fravær og vikartimer.
          </Li>
          <Li>
            <strong>Bestyrelsesmedlemmer:</strong> navn, e-mail og bestyrelsens filer.
          </Li>
          <Li>
            <strong>Filer og fritekst:</strong> indholdet bestemmes af skolen. Skolen må ikke
            uploade CPR-numre eller andre følsomme oplysninger, som tjenesten ikke er beregnet til.
          </Li>
        </List>
        <p>
          Oplysning om, at et fravær skyldes sygdom, er en helbredsoplysning (GDPR artikel 9).
          Skolen registrerer den efter reglerne om elevfravær.
        </p>
      </Section>

      <Section title="4. Databehandlerens forpligtelser">
        <List>
          <Li>
            Behandler kun personoplysninger efter skolens dokumenterede instruks, som er denne
            aftale og skolens brug af Skoleoverblikket. Kræver EU-ret eller dansk ret anden
            behandling, giver vi skolen besked først, medmindre loven forbyder det.
          </Li>
          <Li>Sikrer, at alle, der har adgang til oplysningerne, har tavshedspligt.</Li>
          <Li>
            Hjælper skolen med at besvare de registreredes anmodninger om indsigt, berigtigelse,
            sletning, begrænsning og dataportabilitet (GDPR artikel 15–22), så vidt muligt via
            funktionerne i Skoleoverblikket.
          </Li>
          <Li>
            Hjælper skolen med sikkerhed, anmeldelse af brud, konsekvensanalyser og forudgående
            høring (GDPR artikel 32–36).
          </Li>
          <Li>
            Stiller de oplysninger til rådighed, som er nødvendige for at vise, at aftalen
            overholdes, og giver mulighed for revision, se afsnit 9.
          </Li>
        </List>
      </Section>

      <Section title="5. Sikkerhed">
        <List>
          <Li>Al trafik er krypteret med TLS (https).</Li>
          <Li>
            Hver skoles data er adskilt fra andre skolers. Adgang kræver login, og hver bruger ser
            kun det, rollen giver adgang til (administrator, medarbejder, forælder, bestyrelse).
          </Li>
          <Li>Adgangskoder gemmes ikke i klartekst.</Li>
          <Li>Database, uploadede filer og sikkerhedskopier opbevares i EU.</Li>
          <Li>
            Kun databehandlerens egne folk med et driftsmæssigt behov har adgang til
            produktionsdata.
          </Li>
        </List>
      </Section>

      <Section title="6. Underdatabehandlere">
        <p>
          Skolen giver generel forudgående godkendelse til, at databehandleren bruger
          underdatabehandlere. De nuværende står på{' '}
          <Link to="/underdatabehandlere" className="text-brand-700 hover:underline">
            listen over underdatabehandlere
          </Link>
          .
        </p>
        <p>
          Før en ny underdatabehandler tages i brug eller en eksisterende udskiftes, får skolens
          administratorer besked på e-mail mindst {SUB_PROCESSOR_NOTICE_DAYS} dage før. Skolen kan
          gøre indsigelse inden da. Kan vi ikke imødekomme en berettiget indsigelse, kan skolen
          opsige abonnementet.
        </p>
        <p>
          Underdatabehandlere pålægges de samme databeskyttelsesforpligtelser som i denne aftale.
          Databehandleren hæfter over for skolen for underdatabehandlernes overholdelse.
        </p>
      </Section>

      <Section title="7. Overførsel til lande uden for EU/EØS">
        <p>
          Personoplysninger overføres kun til lande uden for EU/EØS, når der er et gyldigt grundlag
          efter GDPR kapitel V, fx en tilstrækkelighedsafgørelse (herunder EU-U.S. Data Privacy
          Framework) eller EU-Kommissionens standardkontraktbestemmelser. Hvilke underdatabehandlere
          det gælder, står på listen.
        </p>
      </Section>

      <Section title="8. Brud på persondatasikkerheden">
        <p>
          Opdager databehandleren et brud på persondatasikkerheden, får skolen besked uden unødig
          forsinkelse og senest 48 timer efter, at vi er blevet opmærksomme på det. Beskeden
          beskriver så vidt muligt bruddet, de berørte oplysninger og registrerede, de sandsynlige
          konsekvenser og de tiltag, vi har iværksat, så skolen kan anmelde bruddet til Datatilsynet
          inden for 72 timer.
        </p>
      </Section>

      <Section title="9. Revision og tilsyn">
        <p>
          Skolen kan én gang om året, og derudover ved konkret mistanke om brud på aftalen, bede om
          skriftlig dokumentation for, at aftalen overholdes. Fysisk tilsyn aftales på forhånd og
          afholdes for skolens regning.
        </p>
      </Section>

      <Section title="10. Ophør og sletning">
        <p>
          Når abonnementet er opsagt, kan skolen fortsat logge ind og eksportere sine data i{' '}
          {RETENTION_DAYS_AFTER_CANCELLATION} dage. Skolens administratorer får en e-mail 7 dage før
          sletning. Derefter sletter databehandleren automatisk alle skolens data permanent:
          database, uploadede filer og brugernes logins. Sikkerhedskopier overskrives løbende og
          slettes, når deres opbevaringsperiode udløber.
        </p>
        <p>Skolen kan bede om at få data slettet tidligere ved at skrive til os.</p>
      </Section>

      <Section title="11. Ændringer og kontakt">
        <p>
          Ændres aftalens tekst, får den et nyt versionsnummer, og skolens administratorer bliver
          bedt om at acceptere den nye version i Skoleoverblikket. Spørgsmål om aftalen:{' '}
          <MailLink />.
        </p>
      </Section>
    </LegalPage>
  )
}
