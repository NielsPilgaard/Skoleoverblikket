import type { ReactNode } from 'react'
import type { SidebarRoute } from '../components/Sidebar'

/**
 * Features shown on the landing page, in display order. Single source of truth: the landing
 * page renders these, and the changelog workflow (.github/workflows/changelog.yml) adds new ones.
 * Write for Hanne: what the feature saves her, in plain Danish, one or two sentences.
 */
interface Feature {
  key: string
  title: string
  description: string
  icon: ReactNode
}

function icon(children: ReactNode) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      className="w-6 h-6"
    >
      {children}
    </svg>
  )
}

export const features = [
  {
    key: 'conflicts',
    title: 'Konfliktkontrol i realtid',
    description:
      'Systemet advarer øjeblikkeligt om dobbeltbookede lærere eller lokaler — uden manuel kontrol.',
    icon: icon(<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />),
  },
  {
    key: 'schemaBuilder',
    title: 'Skemabygger',
    description:
      'Træk og slip lektioner på plads. Nemt at lære — for alle, uanset teknisk erfaring.',
    icon: icon(
      <>
        <rect x="3" y="3" width="18" height="18" rx="2" />
        <path d="M3 9h18M9 21V9" />
      </>
    ),
  },
  {
    key: 'staffSchedules',
    title: 'Medarbejderoversigt',
    description: 'Se alle medarbejderes skemaer samlet — lærere, pædagoger og vikarer.',
    icon: icon(
      <>
        <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
        <circle cx="9" cy="7" r="4" />
        <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
        <path d="M16 3.13a4 4 0 0 1 0 7.75" />
      </>
    ),
  },
  {
    key: 'substitute',
    title: 'Vikardækning med egne kolleger',
    description:
      'Læreren melder sig syg, og kontoret ser straks hvilke lektioner der mangler dækning, med skolens egne ledige medarbejdere foreslået først. Ingen kobling til vikarbureauer.',
    icon: icon(
      <>
        <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" />
        <circle cx="9" cy="7" r="4" />
        <polyline points="16 11 18 13 22 9" />
      </>
    ),
  },
  {
    key: 'weekPlan',
    title: 'Ugeplan',
    description:
      'Lærerne skriver ugeplanen pr. klasse og vedhæfter filer til lektionerne. Forældrene ser den med det samme.',
    icon: icon(
      <>
        <path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20" />
        <path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z" />
      </>
    ),
  },
  {
    key: 'calendar',
    title: 'Skolekalender',
    description:
      'Ferier, arrangementer og faste begivenheder samlet ét sted — og forældrene kan se dem.',
    icon: icon(
      <>
        <rect x="3" y="4" width="18" height="18" rx="2" />
        <path d="M16 2v4M8 2v4M3 10h18" />
      </>
    ),
  },
  {
    key: 'print',
    title: 'Udskriv skemaer',
    description: 'Udskriv klasse-, lærer- og lokaleskemaer med ét klik. Print-venligt format.',
    icon: icon(
      <>
        <polyline points="6 9 6 2 18 2 18 9" />
        <path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2" />
        <rect x="6" y="14" width="12" height="8" />
      </>
    ),
  },
  {
    key: 'files',
    title: 'Filhåndtering',
    description: 'Upload og del filer pr. fag. Let tilgængeligt for alle medarbejdere.',
    icon: icon(
      <>
        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
        <polyline points="14 2 14 8 20 8" />
      </>
    ),
  },
  {
    key: 'parentModule',
    title: 'Forældremodul',
    description:
      'Forældre får adgang til klassens skema, kalender og ugeplan. Kontaktbog, beskeder og kontaktbibliotek inkluderet.',
    icon: icon(
      <>
        <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
        <circle cx="9" cy="7" r="4" />
        <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
        <path d="M16 3.13a4 4 0 0 1 0 7.75" />
        <path d="M3 13h4M9 13h4" />
      </>
    ),
  },
  {
    key: 'absence',
    title: 'Fravær',
    description:
      'Læreren noterer fremmøde fra telefonen, forældre melder sygdom og søger fri, og kontoret ser fraværet pr. kvartal med advarsel ved 10 % og 15 % ulovligt fravær.',
    icon: icon(
      <>
        <circle cx="12" cy="12" r="10" />
        <polyline points="12 6 12 12 16 14" />
      </>
    ),
  },
  {
    key: 'board',
    title: 'Bestyrelse & Tilsyn',
    description:
      'Bestyrelsesmedlemmer får dedikeret adgang med statistikker og dokumentdeling. Inkluderer overblik over "stå mål med"-dækning pr. fag og klasse.',
    icon: icon(
      <>
        <rect x="2" y="7" width="20" height="14" rx="2" />
        <path d="M16 7V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v2" />
        <line x1="12" y1="12" x2="12" y2="16" />
        <line x1="10" y1="14" x2="14" y2="14" />
      </>
    ),
  },
  {
    key: 'sfo',
    title: 'SFO ugeplan',
    description:
      "Byg og udskriv SFO's ugeoverblik med ét klik. Printvenligt format — klar til opslagstavlen.",
    icon: icon(
      <>
        <rect x="3" y="4" width="18" height="18" rx="2" />
        <path d="M16 2v4M8 2v4M3 10h18" />
        <path d="M8 14h.01M12 14h.01M16 14h.01M8 18h.01M12 18h.01M16 18h.01" />
      </>
    ),
  },
  {
    key: 'vacation',
    title: 'Ferieindmelding',
    description:
      'Forældre melder ind om barnet har behov for pasning i ferien. Overblik over tilmeldte, og eksport til CSV med ét klik.',
    icon: icon(
      <>
        <path d="M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2" />
        <rect x="9" y="3" width="6" height="4" rx="1" />
        <polyline points="9 12 11 14 15 10" />
      </>
    ),
  },
  {
    key: 'reports',
    title: 'Timetal og eksport',
    description:
      "Eksportér lærernes timer til Excel og sammenlign med UVM's vejledende timetal — klar til ledelsen. Hent alle skolens data og filer med ét klik.",
    icon: icon(
      <>
        <line x1="18" y1="20" x2="18" y2="10" />
        <line x1="12" y1="20" x2="12" y2="4" />
        <line x1="6" y1="20" x2="6" y2="14" />
      </>
    ),
  },
  {
    key: 'import',
    title: 'Nem import fra regneark',
    description:
      'Indsæt data fra Excel — elever, forældre, medarbejdere og lokaler oprettes på få minutter. Ingen manuel indtastning.',
    icon: icon(
      <>
        <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
        <polyline points="17 8 12 3 7 8" />
        <line x1="12" y1="3" x2="12" y2="15" />
      </>
    ),
  },
] as const satisfies readonly Feature[]

export type FeatureKey = (typeof features)[number]['key']
/**
 * Which landing-page feature covers each sidebar page; null for basic setup pages not worth
 * marketing on their own. Adding a sidebar item without an entry here fails `tsc`, so every new
 * page gets a conscious "is this on the landing page?" decision.
 */
export const sidebarRouteFeatures = {
  '/dashboard': null,
  '/mig/oversigt': 'staffSchedules',
  '/mig/skema': 'staffSchedules',
  '/vikardaekning': 'substitute',
  '/mig/fravaer': 'substitute',
  '/klasser': 'schemaBuilder',
  '/staa-maal-med': 'board',
  '/kalender': 'calendar',
  '/sfo': 'sfo',
  '/ferieindmelding': 'vacation',
  '/aarsrul': null,
  '/medarbejdere': 'staffSchedules',
  '/fag': null,
  '/lokaler': null,
  '/elever': null,
  '/foraeldre': 'parentModule',
  '/import': 'import',
  '/filer': 'files',
  '/eksporter': 'reports',
  '/bestyrelse/oversigt': 'board',
  '/bestyrelse/filer': 'board',
  '/bestyrelse/staa-maal-med': 'board',
  '/foraeldrevisning/skema': 'parentModule',
  '/foraeldrevisning/kalender': 'calendar',
  '/foraeldrevisning/ugeplan': 'weekPlan',
  '/foraeldrevisning/kontakt': 'parentModule',
  '/foraeldrevisning/ferieindmelding': 'vacation',
  '/foraeldrevisning/fravaer': 'absence',
  '/foraeldrevisning/kontaktbog': 'parentModule',
  '/klassechat': 'parentModule',
  '/fravaer': 'absence',
  '/kontaktbog': 'parentModule',
  '/beskeder': 'parentModule',
} as const satisfies Record<SidebarRoute, FeatureKey | null>
