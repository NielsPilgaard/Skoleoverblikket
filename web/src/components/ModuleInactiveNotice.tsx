import { Link } from 'react-router-dom'

/** Shown on an admin page whose module is off: data stays readable, changes need the module. */
export default function ModuleInactiveNotice({ text, testId }: { text: string; testId: string }) {
  return (
    <div
      data-testid={testId}
      className="rounded-xl border border-amber-300 bg-amber-50 px-5 py-4 text-sm text-amber-800"
    >
      {text}{' '}
      <Link to="/abonnement" className="font-medium underline hover:text-amber-900">
        Aktivér det under Abonnement
      </Link>
      .
    </div>
  )
}
