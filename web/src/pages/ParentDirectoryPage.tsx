import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { usePageTitle } from '../hooks/usePageTitle'
import { getApiV1ContactDirectoryOptions } from '../api/generated/@tanstack/react-query.gen'
import type { ContactDirectoryControllerContactDirectoryParentDto as KontaktParentDto } from '../api/generated/types.gen'

interface StudentGroup {
  studentId: string
  studentName: string
  parents: KontaktParentDto[]
}

function groupByStudent(parents: KontaktParentDto[]): StudentGroup[] {
  const groups = new Map<string, StudentGroup>()

  for (const parent of parents) {
    const students = parent.students ?? []
    // Parents with no linked student each get their own group, keyed by their own id —
    // otherwise every parentless parent would collide into one shared group labeled with
    // whichever parent's name happened to create it first.
    const targets =
      students.length > 0 ? students : [{ id: `__no_student_${parent.id}`, name: parent.name }]
    for (const student of targets) {
      let group = groups.get(student.id)
      if (!group) {
        group = { studentId: student.id, studentName: student.name, parents: [] }
        groups.set(student.id, group)
      }
      group.parents.push(parent)
    }
  }

  return [...groups.values()].sort((a, b) => a.studentName.localeCompare(b.studentName, 'da'))
}

export default function ParentDirectoryPage() {
  usePageTitle('Kontakt')
  const [search, setSearch] = useState('')
  const [expandedStudentId, setExpandedStudentId] = useState<string | null>(null)

  const { data: parents = [], isLoading } = useQuery({
    ...getApiV1ContactDirectoryOptions(),
    select: (data) => data as KontaktParentDto[],
  })

  const groups = useMemo(() => groupByStudent(parents), [parents])

  const filtered = groups.filter(
    (g) =>
      g.studentName.toLowerCase().includes(search.toLowerCase()) ||
      g.parents.some((p) => (p.name ?? '').toLowerCase().includes(search.toLowerCase()))
  )

  return (
    <div className="max-w-3xl mx-auto px-4 py-8">
      <div className="mb-6">
        <h1 className="font-display text-2xl font-semibold text-gray-900 mb-4">Kontakter</h1>
        <input
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Søg efter navn…"
          className="w-full max-w-sm px-4 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
        />
      </div>

      {isLoading && (
        <div className="flex justify-center py-16">
          <div className="w-6 h-6 border-2 border-brand-600 border-t-transparent rounded-full animate-spin" />
        </div>
      )}

      {!isLoading && filtered.length === 0 && (
        <p className="text-sm text-gray-500 py-8">
          {search ? 'Ingen resultater for denne søgning.' : 'Ingen forældre at vise.'}
        </p>
      )}

      <div className="space-y-3">
        {filtered.map((group) => {
          const isStudentExpanded = expandedStudentId === group.studentId
          return (
            <div key={group.studentId} className="bg-white border border-gray-200 rounded-xl p-4">
              <button
                type="button"
                onClick={() => setExpandedStudentId(isStudentExpanded ? null : group.studentId)}
                aria-expanded={isStudentExpanded}
                aria-controls={`kontakt-group-${group.studentId}`}
                className="w-full flex items-center justify-between gap-4 text-left"
              >
                <div className="min-w-0">
                  <p className="font-medium text-gray-900 text-sm">{group.studentName}</p>
                  <p className="text-xs text-gray-500 mt-0.5">
                    {group.parents.length === 1 ? '1 forælder' : `${group.parents.length} forældre`}
                  </p>
                </div>
                <svg
                  width="16"
                  height="16"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  className={`shrink-0 text-gray-400 transition-transform ${isStudentExpanded ? 'rotate-180' : ''}`}
                >
                  <polyline points="6 9 12 15 18 9" />
                </svg>
              </button>

              {isStudentExpanded && (
                <div
                  id={`kontakt-group-${group.studentId}`}
                  className="mt-3 pt-3 border-t border-gray-100 space-y-2"
                >
                  {group.parents.map((parent) => {
                    const hasContactInfo = !!(
                      parent.phone ||
                      parent.address ||
                      parent.city ||
                      parent.email
                    )
                    return (
                      <div key={parent.id}>
                        <div className="w-full flex items-start gap-3 text-left">
                          <div className="shrink-0">
                            {parent.avatarUrl ? (
                              <img
                                src={parent.avatarUrl}
                                alt=""
                                className="w-9 h-9 rounded-full object-cover"
                              />
                            ) : (
                              <div className="w-9 h-9 rounded-full bg-brand-100 flex items-center justify-center">
                                <span className="text-sm font-semibold text-brand-700">
                                  {(parent.name ?? '?').charAt(0).toUpperCase()}
                                </span>
                              </div>
                            )}
                          </div>
                          <div className="flex-1 min-w-0 pt-1.5">
                            <p className="font-medium text-gray-900 text-sm">{parent.name}</p>
                          </div>
                        </div>
                        {hasContactInfo && (
                          <div className="mt-2 pl-12 space-y-0.5">
                            {parent.phone && (
                              <p className="text-sm text-gray-700">
                                <a href={`tel:${parent.phone}`} className="hover:text-brand-600">
                                  {parent.phone}
                                </a>
                              </p>
                            )}
                            {parent.email && (
                              <p className="text-sm text-gray-700">
                                <a href={`mailto:${parent.email}`} className="hover:text-brand-600">
                                  {parent.email}
                                </a>
                              </p>
                            )}
                            {(parent.address || parent.city) && (
                              <p className="text-sm text-gray-600">
                                {[parent.address, parent.postalCode, parent.city]
                                  .filter(Boolean)
                                  .join(', ')}
                              </p>
                            )}
                          </div>
                        )}
                      </div>
                    )
                  })}
                </div>
              )}
            </div>
          )
        })}
      </div>
    </div>
  )
}
