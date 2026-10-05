interface ChangelogEntry {
  /** ISO date the changes went live, e.g. '2026-10-03'. */
  date: string
  title: string
  /** One sentence per change, written for school staff and parents in plain Danish. */
  changes: string[]
}

/**
 * Shown on /nyheder, newest first. Drafted weekly from merged `feat` commits by
 * .github/workflows/changelog.yml, which reads the first `date` below to know where to start.
 */
export const changelog: ChangelogEntry[] = [
  {
    date: '2026-10-05',
    title: 'Fravær og vikardækning',
    changes: [
      'Fravær: læreren registrerer dagens fremmøde for klassen, også fra telefonen, forældre kan søge om fri, og kontoret ser fraværet pr. kvartal med markering ved 10 % og 15 % ulovligt fravær.',
      'Vikardækning: medarbejdere melder selv fravær under "Mit fravær", og kontoret ser hver lektion, der mangler dækning, med ledige kolleger foreslået, og kan sætte en vikar på, som får besked.',
      'Stå mål med: fag uden vejledende timetal på et klassetrin, fx dansk i 0. klasse, vises nu som ekstra timer i stedet for en advarsel.',
      'Databehandleraftalen kan nu godkendes direkte i Skoleoverblikket, og hvis skolen opsiger abonnementet, slettes alle skolens data efter 90 dage med en advarsel på e-mail 7 dage før.',
      'Nye skoler får nu 30 dages gratis prøveperiode i stedet for 14.',
      'Notifikationsklokken og fraværssiderne fungerer nu bedre på telefonen.',
    ],
  },
  {
    date: '2026-09-13',
    title: 'Klassechat og forsider efter rolle',
    changes: [
      'Klassechat: forældre og lærere i samme klasse kan nu skrive sammen i én fælles samtale.',
      'Forsiden efter login er tilpasset din rolle, så ledelse, lærere og forældre ser det, der er vigtigst for dem.',
    ],
  },
  {
    date: '2026-08-25',
    title: 'Nemmere fraværsindberetning',
    changes: [
      'Fravær: vælg datoen i en kalender, vælg årsagen med ét klik, og få et ekstra spørgsmål før en indberetning annulleres.',
      'Stå mål med: gem øjebliksbilleder af dækningen pr. fag og klasse.',
      'Skift mellem månedlig og årlig betaling direkte under Abonnement.',
      'Telefonnumre og postnumre på forældre bliver nu tjekket, så tastefejl fanges med det samme.',
    ],
  },
  {
    date: '2026-08-18',
    title: 'Årlig betaling og profilside til forældre',
    changes: [
      'Alle moduler kan nu betales årligt — det svarer til to måneder gratis.',
      'Forældre har fået en profilside, hvor de selv kan rette deres oplysninger.',
    ],
  },
  {
    date: '2026-08-15',
    title: 'Kontaktbogen er blevet bedre',
    changes: [
      'Forældre vælger selv, hvilke medarbejdere der skal have besked, når de skriver i kontaktbogen.',
      'Beskeder i kontaktbogen vises nu som samlede samtaler.',
    ],
  },
  {
    date: '2026-08-06',
    title: 'Importér klasser fra regneark',
    changes: ['Klasser kan nu importeres fra et regneark — også direkte fra opsætningsguiden.'],
  },
]
