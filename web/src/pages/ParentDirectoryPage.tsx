import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { usePageTitle } from '../hooks/usePageTitle'
import { PersonList } from '../components/ui/list'
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

function ParentContact({ parent }: { parent: KontaktParentDto }) {
  const address = [parent.address, parent.postalCode, parent.city].filter(Boolean).join(', ')
  return (
    <div className="flex items-start gap-3">
      {parent.avatarUrl ? (
        <img src={parent.avatarUrl} alt="" className="w-9 h-9 shrink-0 rounded-full object-cover" />
      ) : null}
      <div className="min-w-0 text-sm">
        <p className="font-semibold text-gray-900">{parent.name}</p>
        {parent.phone && (
          <a
            href={`tel:${parent.phone}`}
            className="flex items-center min-h-[44px] text-brand-700 underline"
          >
            {parent.phone}
          </a>
        )}
        {parent.email && (
          <a
            href={`mailto:${parent.email}`}
            className="flex items-center min-h-[44px] text-brand-700 underline break-all"
          >
            {parent.email}
          </a>
        )}
        {address && <p className="text-gray-700">{address}</p>}
        {!parent.phone && !parent.email && !address && (
          <p className="text-gray-600">Ingen kontaktoplysninger</p>
        )}
      </div>
    </div>
  )
}

export default function ParentDirectoryPage() {
  usePageTitle('Kontakt')

  const { data: parents = [], isLoading } = useQuery({
    ...getApiV1ContactDirectoryOptions(),
    select: (data) => data as KontaktParentDto[],
  })

  const groups = useMemo(() => groupByStudent(parents), [parents])

  return (
    <div className="max-w-3xl mx-auto px-4 py-8 space-y-4">
      <h1 className="font-display text-2xl font-semibold text-gray-900">Kontakter</h1>
      <PersonList
        items={groups}
        isLoading={isLoading}
        testId="contact-directory"
        // Not "elever": parents with no linked student get a card of their own.
        noun={{ one: 'kontakt', other: 'kontakter' }}
        searchText={(g) => `${g.studentName} ${g.parents.map((p) => p.name ?? '').join(' ')}`}
        searchPlaceholder="Søg efter elev eller forælder"
        empty={<p>Ingen forældre at vise.</p>}
        toPerson={(g) => ({
          key: g.studentId,
          name: g.studentName,
          subtitle: g.parents.map((p) => p.name).join(' og '),
          content: (
            <div className="space-y-3">
              {g.parents.map((p) => (
                <ParentContact key={p.id} parent={p} />
              ))}
            </div>
          ),
        })}
      />
    </div>
  )
}
