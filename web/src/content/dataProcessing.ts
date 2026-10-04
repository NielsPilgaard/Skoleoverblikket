/**
 * Databehandleraftale version and sub-processor list, shared by the public pages, the privacy
 * policy and the admin banner.
 *
 * DPA_VERSION must match DataProcessingAgreementService.CurrentVersion in the API. Bump both when
 * the agreement text changes; every school is then asked to accept again.
 *
 * Changing SUB_PROCESSORS: send the 30-day notice from the backoffice first, then update the list
 * on the date given in the notice.
 */
export const DPA_VERSION = '1.0'
export const DPA_UPDATED = 'oktober 2026'

/** Days of notice schools get before a new sub-processor is used. */
export const SUB_PROCESSOR_NOTICE_DAYS = 30

/** Days a school's data is kept after the subscription is canceled. Matches the API. */
export const RETENTION_DAYS_AFTER_CANCELLATION = 90

/** Days production database backups are kept before they are overwritten. */
export const BACKUP_RETENTION_DAYS = 14

export interface SubProcessor {
  name: string
  purpose: string
  data: string
  location: string
}

export const SUB_PROCESSORS: SubProcessor[] = [
  {
    name: 'OVHcloud SAS',
    purpose: 'Servere, database, filopbevaring og sikkerhedskopier',
    data: 'Alle data i Skoleoverblikket',
    location: 'EU (Frankrig)',
  },
  {
    name: 'Scaleway SAS',
    purpose: 'Afsendelse af e-mails (invitationer, notifikationer, beskeder)',
    data: 'Modtagerens navn og e-mailadresse samt e-mailens indhold',
    location: 'EU (Frankrig)',
  },
  {
    name: 'Stripe Payments Europe Ltd.',
    purpose: 'Abonnement og betaling',
    data: 'Skolens navn og administratorens e-mailadresse. Kortdata håndteres kun af Stripe',
    location: 'EU (Irland). Overførsel til USA sker under EU-U.S. Data Privacy Framework',
  },
  {
    name: 'elmah.io ApS',
    purpose: 'Fejllogning, så vi kan rette fejl hurtigt',
    data: "Tekniske oplysninger om fejl og interne id'er. E-mails, telefon- og CPR-numre, formulardata, cookies og IP-adresser fjernes, før fejlen sendes",
    // elmah.io stores all data in Azure West US / East US and does not offer an EU region.
    // ElmahIoScrubber (API) removes personal data before anything is sent.
    location: 'USA (Microsoft Azure). Overførsel sker på et gyldigt grundlag efter GDPR kapitel V',
  },
]
