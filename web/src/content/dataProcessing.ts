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

/** The legal entity behind Skoleoverblikket: data processor in the DPA, controller in the privacy policy. */
export const COMPANY = {
  name: 'Pilgaard Development',
  cvr: '41249269',
  address: 'Nordlyvej 20, 8550 Ryomgård',
}

export interface SubProcessor {
  name: string
  /** Registered address, so the school can identify the legal entity. */
  address: string
  purpose: string
  data: string
  location: string
}

/**
 * Processors of the schools' data. Stripe is not here: it only gets our own customer data (the
 * school as a paying customer), where we are the controller. See the privacy policy.
 */
export const SUB_PROCESSORS: SubProcessor[] = [
  {
    name: 'OVH SAS (OVHcloud)',
    address: '2 rue Kellermann, 59100 Roubaix, Frankrig',
    purpose: 'Servere, database, filopbevaring og sikkerhedskopier',
    data: 'Alle data i Skoleoverblikket',
    location: 'EU (Frankrig)',
  },
  {
    name: 'Scaleway SAS',
    address: "8 rue de la Ville l'Evêque, 75008 Paris, Frankrig",
    purpose: 'Afsendelse af e-mails (invitationer, notifikationer, beskeder)',
    data: 'Modtagerens navn og e-mailadresse samt e-mailens indhold',
    location: 'EU (Frankrig)',
  },
  {
    name: 'elmah.io ApS',
    address: 'Danmark',
    purpose: 'Fejllogning, så vi kan rette fejl hurtigt',
    data: "Tekniske oplysninger om fejl og interne id'er. Navne, e-mails, telefon- og CPR-numre, formulardata, cookies og IP-adresser sendes ikke med",
    // elmah.io stores all data in Azure West US / East US and does not offer an EU region.
    // ElmahIoScrubber and the JWT name claim (sub) keep personal data out of what is sent.
    location:
      'USA (Microsoft Azure). Microsoft, som opbevarer dataene, er certificeret under EU-U.S. Data Privacy Framework',
  },
]
