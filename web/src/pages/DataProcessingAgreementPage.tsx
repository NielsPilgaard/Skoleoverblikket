import { Link } from 'react-router-dom'
import LegalPage, { Li, List, MailLink, Section } from '../components/LegalPage'
import {
  DPA_UPDATED,
  DPA_VERSION,
  BACKUP_RETENTION_DAYS,
  COMPANY,
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
          ), og {COMPANY.name}, CVR {COMPANY.cvr}, {COMPANY.address}, som driver Skoleoverblikket (
          <strong>databehandleren</strong>). Er skolen en folkeskole, er kommunen den
          dataansvarlige. Ønsker kommunen at bruge sin egen databehandleraftale, så skriv til os.
        </p>
        <p>
          Aftalen accepteres elektronisk af en administrator på skolens vegne, når skolen oprettes,
          eller senere i Skoleoverblikket. Administratoren bekræfter dermed at have bemyndigelse til
          at indgå aftalen. Aftalen gælder, så længe skolen har en konto, og indtil alle skolens
          data er slettet.
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
            (herunder at et fravær skyldes sygdom og forældrenes bemærkning til en sygemelding),
            ferieønsker og beskeder om eleven.
          </Li>
          <Li>
            <strong>Forældre:</strong> navn, e-mail, telefonnummer, adresse, billede, samtykke til
            deling af kontaktoplysninger og beskeder.
          </Li>
          <Li>
            <strong>Medarbejdere:</strong> navn, e-mail, telefonnummer, rolle, billede, skema,
            fravær med en valgfri note og vikartimer.
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
          Oplysning om, at en elev eller medarbejder er syg, er en helbredsoplysning (GDPR artikel
          9). Skolen registrerer elevfravær efter reglerne om elevfravær. Det er nok at registrere,
          at der er tale om sygdom. Forældre og medarbejdere bliver bedt om ikke at skrive diagnoser
          eller andre helbredsoplysninger i bemærkningen, og skolen bør heller ikke selv gøre det.
        </p>
      </Section>

      <Section title="4. Databehandlerens forpligtelser">
        <List>
          <Li>
            Behandler kun personoplysninger efter skolens dokumenterede instruks, som er denne
            aftale og skolens brug af Skoleoverblikket, også når det gælder overførsel til lande
            uden for EU/EØS (se afsnit 7). Kræver EU-ret eller dansk ret anden behandling, giver vi
            skolen besked først, medmindre loven forbyder det.
          </Li>
          <Li>
            Giver straks skolen besked, hvis en instruks efter vores vurdering strider mod
            databeskyttelsesforordningen eller anden databeskyttelseslovgivning.
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
          <Li>Adgangskoder gemmes kun som hash, aldrig i klartekst.</Li>
          <Li>
            Database, uploadede filer og sikkerhedskopier opbevares i EU. Databasen
            sikkerhedskopieres dagligt.
          </Li>
          <Li>
            Fejllogs indeholder kun tekniske oplysninger og interne id'er. Navne, kontaktoplysninger
            og indtastede data fjernes, før en fejl logges.
          </Li>
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
          Underdatabehandlere pålægges tilsvarende databeskyttelsesforpligtelser som i denne aftale.
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
          afholdes for skolens regning. Skolen kan lade en uafhængig revisor med tavshedspligt
          foretage tilsynet på sine vegne.
        </p>
        <p>
          Datatilsynet og andre myndigheder, der efter loven har adgang til databehandlerens
          faciliteter og oplysninger, får den adgang.
        </p>
      </Section>

      <Section title="10. Ophør og sletning">
        <p>
          Når abonnementet er opsagt, kan skolen fortsat logge ind i{' '}
          {RETENTION_DAYS_AFTER_CANCELLATION} dage. Under Eksporter kan skolen selv hente timer pr.
          medarbejder, timer pr. fag, det komplette skema og UVM-timetal, og under Fravær kan
          fraværet hentes som Excel-fil. Øvrige data, fx elever, forældre, beskeder og uploadede
          filer, udleverer vi på anmodning til <MailLink /> inden sletning.
        </p>
        <p>
          Skolens administratorer får en e-mail 7 dage før sletning. Derefter sletter
          databehandleren automatisk alle skolens data permanent: database, uploadede filer og
          brugernes logins. Sikkerhedskopier af databasen gemmes i {BACKUP_RETENTION_DAYS} dage og
          overskrives derefter, så de sidste kopier af skolens data er væk senest{' '}
          {BACKUP_RETENTION_DAYS} dage efter sletningen.
        </p>
        <p>
          Skolen kan bede om at få data slettet tidligere ved at skrive til os. Data slettes ikke,
          hvis EU-ret eller dansk ret kræver, at de opbevares.
        </p>
      </Section>

      <Section title="11. Ændringer, lovvalg og kontakt">
        <p>
          Ændres aftalens tekst, får den et nyt versionsnummer, og skolens administratorer bliver
          bedt om at acceptere den nye version i Skoleoverblikket. Indtil den nye version er
          accepteret, gælder den hidtidige. Vil skolen ikke acceptere den nye version, kan skolen
          opsige abonnementet.
        </p>
        <p>
          Aftalen er underlagt dansk ret, og uenigheder afgøres ved de danske domstole. Spørgsmål om
          aftalen: <MailLink />.
        </p>
      </Section>
    </LegalPage>
  )
}
