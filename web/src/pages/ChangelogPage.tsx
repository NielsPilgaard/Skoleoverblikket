import Footer from '../components/Footer'
import PublicNav from '../components/PublicNav'
import SeoMeta from '../components/SeoMeta'
import { changelog } from '../content/changelog'

// UTC so the prerendered HTML and the hydrating browser print the same day.
const dateFormat = new Intl.DateTimeFormat('da-DK', {
  day: 'numeric',
  month: 'long',
  year: 'numeric',
  timeZone: 'UTC',
})

export default function ChangelogPage() {
  return (
    <div className="min-h-screen bg-white font-sans text-gray-900 flex flex-col">
      <SeoMeta
        title="Nyheder"
        description="Se hvad der er nyt i Skoleoverblikket. Vi forbedrer systemet hver uge ud fra skolernes ønsker."
        path="/nyheder"
      />
      <PublicNav />

      <main className="flex-1 py-20 px-6">
        <div className="max-w-2xl mx-auto">
          <h1 className="font-display text-4xl sm:text-5xl font-semibold text-brand-900 leading-tight mb-4">
            Nyheder
          </h1>
          <p className="text-brand-600 text-lg font-medium mb-12">
            Skoleoverblikket bliver bedre hver uge. Her kan du se, hvad der er nyt.
          </p>

          <ol className="space-y-12" data-testid="changelog-list">
            {changelog.map((entry) => (
              <li key={entry.date} className="border-l-2 border-brand-200 pl-6">
                <time dateTime={entry.date} className="text-sm text-gray-500">
                  {dateFormat.format(new Date(entry.date))}
                </time>
                <h2 className="font-display text-2xl font-semibold text-gray-900 mt-1 mb-3">
                  {entry.title}
                </h2>
                <ul className="space-y-2 text-gray-700 leading-relaxed">
                  {entry.changes.map((change) => (
                    <li key={change} className="flex gap-2">
                      <span className="mt-2.5 w-1.5 h-1.5 rounded-full bg-brand-400 shrink-0" />
                      <span>{change}</span>
                    </li>
                  ))}
                </ul>
              </li>
            ))}
          </ol>
        </div>
      </main>

      <Footer />
    </div>
  )
}
