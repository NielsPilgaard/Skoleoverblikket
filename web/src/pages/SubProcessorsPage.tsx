import { Link } from 'react-router-dom'
import LegalPage, { MailLink, Section } from '../components/LegalPage'
import { DPA_UPDATED, SUB_PROCESSOR_NOTICE_DAYS, SUB_PROCESSORS } from '../content/dataProcessing'

export default function SubProcessorsPage() {
  return (
    <LegalPage
      title="Underdatabehandlere"
      description="De leverandører, Skoleoverblikket bruger til at behandle skolens personoplysninger, hvad de bruges til, og hvor data ligger."
      path="/underdatabehandlere"
      updated={`Senest opdateret: ${DPA_UPDATED}`}
    >
      <p>
        Skoleoverblikket bruger disse leverandører (underdatabehandlere) til at levere tjenesten. De
        er bundet af de samme databeskyttelsesforpligtelser som os, jf.{' '}
        <Link to="/databehandleraftale" className="text-brand-700 hover:underline">
          databehandleraftalen
        </Link>
        .
      </p>

      <ul className="space-y-4" data-testid="sub-processor-list">
        {SUB_PROCESSORS.map((p) => (
          <li key={p.name} className="rounded-xl border border-gray-200 p-4">
            <h2 className="font-semibold text-gray-900">{p.name}</h2>
            <dl className="mt-2 grid grid-cols-1 sm:grid-cols-[8rem_1fr] gap-x-4 gap-y-1 text-sm">
              <dt className="text-gray-500">Formål</dt>
              <dd>{p.purpose}</dd>
              <dt className="text-gray-500">Oplysninger</dt>
              <dd>{p.data}</dd>
              <dt className="text-gray-500">Placering</dt>
              <dd>{p.location}</dd>
            </dl>
          </li>
        ))}
      </ul>

      <Section title="Ændringer">
        <p>
          Før vi tager en ny underdatabehandler i brug eller udskifter en eksisterende, får skolens
          administratorer besked på e-mail mindst {SUB_PROCESSOR_NOTICE_DAYS} dage før. Skolen kan
          gøre indsigelse ved at skrive til <MailLink />.
        </p>
      </Section>
    </LegalPage>
  )
}
