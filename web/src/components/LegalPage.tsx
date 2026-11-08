import Footer from './Footer'
import PublicNav from './PublicNav'
import SeoMeta from './SeoMeta'

/** Shell for the public legal pages: privatlivspolitik, databehandleraftale, underdatabehandlere. */
export default function LegalPage({
  title,
  description,
  path,
  updated,
  children,
}: {
  title: string
  description: string
  path: string
  updated: string
  children: React.ReactNode
}) {
  return (
    <div className="min-h-screen bg-white font-sans text-gray-900 flex flex-col">
      <SeoMeta title={title} description={description} path={path} />
      <PublicNav />

      <main className="flex-1 py-20 px-6">
        <div className="max-w-2xl mx-auto">
          <h1 className="font-display text-3xl sm:text-5xl font-semibold text-brand-900 leading-tight mb-2 break-words">
            {title}
          </h1>
          <p className="text-sm text-gray-400 mb-10">{updated}</p>

          <div className="space-y-10 text-gray-700 leading-relaxed">{children}</div>
        </div>
      </main>

      <Footer />
    </div>
  )
}

export function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section>
      <h2 className="font-display text-xl font-semibold text-gray-900 mb-3">{title}</h2>
      <div className="space-y-3">{children}</div>
    </section>
  )
}

export function Li({ children }: { children: React.ReactNode }) {
  return (
    <li className="flex gap-2">
      <span className="mt-1 w-1.5 h-1.5 rounded-full bg-brand-400 shrink-0" />
      <span>{children}</span>
    </li>
  )
}

export function List({ children }: { children: React.ReactNode }) {
  return <ul className="mt-3 space-y-2 text-sm list-none">{children}</ul>
}

export function MailLink() {
  return (
    <a href="mailto:kontakt@skoleoverblikket.dk" className="text-brand-700 hover:underline">
      kontakt@skoleoverblikket.dk
    </a>
  )
}
