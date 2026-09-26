import { useQuery } from '@tanstack/react-query'
import { getApiV1StaffOptions } from '../api/generated/@tanstack/react-query.gen'
import { usePageTitle } from '../hooks/usePageTitle'

export default function BoardStaffPage() {
  usePageTitle('Medarbejdere')
  const { data: staff, isLoading, error } = useQuery(getApiV1StaffOptions())

  return (
    <div className="p-6 lg:p-8 max-w-4xl mx-auto space-y-8">
      <div>
        <h1 className="font-display text-2xl font-semibold text-gray-900">Medarbejdere</h1>
        <p className="mt-1 text-sm text-gray-500">Oversigt over skolens medarbejdere</p>
      </div>

      {isLoading ? (
        <div className="space-y-3">
          {[...Array(5)].map((_, i) => (
            <div key={i} className="bg-white rounded-xl border border-gray-200 p-4 animate-pulse">
              <div className="h-4 w-40 bg-gray-200 rounded" />
            </div>
          ))}
        </div>
      ) : error ? (
        <p className="text-sm text-red-600">Der opstod en fejl. Prøv igen.</p>
      ) : (
        <div className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100">
          {(staff ?? []).map((member) => (
            <div key={member.id} className="px-5 py-4 flex items-center justify-between">
              <div>
                <p className="text-sm font-medium text-gray-900">{member.name}</p>
                {member.email && <p className="mt-0.5 text-xs text-gray-500">{member.email}</p>}
              </div>
              <span className="text-xs text-gray-400">{member.role}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
