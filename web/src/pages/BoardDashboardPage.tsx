import { useQuery } from '@tanstack/react-query'
import { getApiV1StatsDashboardOptions } from '../api/generated/@tanstack/react-query.gen'
import { usePageTitle } from '../hooks/usePageTitle'

export default function BoardDashboardPage() {
  usePageTitle('Bestyrelsesoversigt')
  const { data, isLoading, error } = useQuery(getApiV1StatsDashboardOptions())

  return (
    <div className="p-6 lg:p-8 max-w-4xl mx-auto space-y-8">
      <div>
        <h1 className="font-display text-2xl font-semibold text-gray-900">Oversigt</h1>
        <p className="mt-1 text-sm text-gray-500">Aggregeret statistik for skolen</p>
      </div>

      {isLoading ? (
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
          {[...Array(4)].map((_, i) => (
            <div key={i} className="bg-white rounded-xl border border-gray-200 p-5 animate-pulse">
              <div className="h-4 w-20 bg-gray-200 rounded mb-3" />
              <div className="h-8 w-12 bg-gray-100 rounded" />
            </div>
          ))}
        </div>
      ) : error ? (
        <p className="text-sm text-red-600">Der opstod en fejl. Prøv igen.</p>
      ) : data ? (
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
          <StatCard label="Klasser" value={data.classCount ?? 0} />
          <StatCard label="Medarbejdere" value={data.staffCount ?? 0} />
          <StatCard label="Fag" value={data.courseCount ?? 0} />
          <StatCard label="Lokaler" value={data.roomCount ?? 0} />
        </div>
      ) : null}
    </div>
  )
}

function StatCard({ label, value }: { label: string; value: number }) {
  return (
    <div className="bg-white rounded-xl border border-gray-200 p-5">
      <p className="text-sm text-gray-500">{label}</p>
      <p className="mt-1 text-3xl font-semibold text-gray-900 tabular-nums">{value}</p>
    </div>
  )
}
