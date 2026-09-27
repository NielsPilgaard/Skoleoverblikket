import { useQuery } from '@tanstack/react-query'
import { getApiV1ClassesOptions } from '../api/generated/@tanstack/react-query.gen'
import { usePageTitle } from '../hooks/usePageTitle'

export default function BoardSchemasPage() {
  usePageTitle('Skemaer')
  const { data: classes, isLoading, error } = useQuery(getApiV1ClassesOptions())

  return (
    <div className="p-6 lg:p-8 max-w-4xl mx-auto space-y-8">
      <div>
        <h1 className="font-display text-2xl font-semibold text-gray-900">Skemaer</h1>
        <p className="mt-1 text-sm text-gray-500">Oversigt over alle klassers skemaer</p>
      </div>

      {isLoading ? (
        <div className="space-y-3">
          {[...Array(5)].map((_, i) => (
            <div key={i} className="bg-white rounded-xl border border-gray-200 p-4 animate-pulse">
              <div className="h-4 w-32 bg-gray-200 rounded" />
            </div>
          ))}
        </div>
      ) : error ? (
        <p className="text-sm text-red-600">Der opstod en fejl. Prøv igen.</p>
      ) : (
        <div className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100">
          {(classes ?? []).map((cls) => (
            <div key={cls.id} className="px-5 py-4">
              <p className="text-sm font-medium text-gray-900">{cls.name}</p>
              {cls.description && <p className="mt-0.5 text-xs text-gray-500">{cls.description}</p>}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
