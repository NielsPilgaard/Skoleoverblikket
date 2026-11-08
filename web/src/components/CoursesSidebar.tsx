import { useState } from 'react'
import { useDraggable } from '@dnd-kit/core'
import { CSS } from '@dnd-kit/utilities'
import type { CourseDto } from '../api/client'
import { SUBJECT_CATEGORY_LABELS } from '../utils/subjectCategory'
import { encodeSidebarDragId } from '../utils/sidebarDragId'

interface DraggableCourseRowProps {
  course: CourseDto
  isSelected: boolean
  onSelect: (id: string) => void
}

function DraggableCourseRow({ course, isSelected, onSelect }: DraggableCourseRowProps) {
  const dragId = encodeSidebarDragId(course.id ?? '')
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({ id: dragId })

  const style = transform
    ? {
        transform: CSS.Translate.toString(transform),
        zIndex: 20,
        position: 'relative' as const,
        opacity: isDragging ? 0.5 : 1,
      }
    : undefined

  const categoryLabel = course.category
    ? (SUBJECT_CATEGORY_LABELS[course.category] ?? course.category)
    : null

  return (
    // biome-ignore lint/a11y/noStaticElementInteractions: dnd-kit spreads role/tabIndex via {...attributes}
    <div
      ref={setNodeRef}
      style={style}
      {...listeners}
      {...attributes}
      onClick={() => onSelect(course.id ?? '')}
      data-testid={`sidebar-course-${course.id}`}
      className={[
        'w-full flex items-center gap-2.5 px-3 py-2 text-left transition-colors cursor-grab active:cursor-grabbing',
        isSelected
          ? 'bg-brand-50 border-l-2 border-brand-500'
          : 'hover:bg-gray-50 border-l-2 border-transparent',
      ].join(' ')}
    >
      <span
        className="shrink-0 w-3 h-3 rounded-full border"
        style={
          course.color
            ? { backgroundColor: `${course.color}33`, borderColor: course.color }
            : { backgroundColor: '#e5e7eb', borderColor: '#d1d5db' }
        }
      />
      <span className="flex-1 min-w-0">
        <span className="block text-sm font-medium text-gray-800 truncate">{course.name}</span>
        {categoryLabel && categoryLabel !== course.name && (
          <span className="block text-xs text-gray-400 truncate">{categoryLabel}</span>
        )}
      </span>
      {isSelected && (
        <svg
          width="12"
          height="12"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2.5"
          className="shrink-0 text-brand-500"
        >
          <polyline points="20 6 9 17 4 12" />
        </svg>
      )}
    </div>
  )
}

interface CoursesSidebarProps {
  courses: CourseDto[]
  selectedCourseId: string
  onSelectCourse: (id: string) => void
  isOpen: boolean
  onToggle: () => void
}

export default function CoursesSidebar({
  courses,
  selectedCourseId,
  onSelectCourse,
  isOpen,
  onToggle,
}: CoursesSidebarProps) {
  const [search, setSearch] = useState('')

  const sorted = [...courses].sort((a, b) => (a.name ?? '').localeCompare(b.name ?? '', 'da'))

  const filtered = search.trim()
    ? sorted.filter((c) => (c.name ?? '').toLowerCase().includes(search.trim().toLowerCase()))
    : sorted

  return (
    <>
      {/* Mobile toggle button */}
      <button
        type="button"
        onClick={onToggle}
        className="lg:hidden fixed bottom-4 left-4 z-40 flex items-center gap-2 px-4 py-2.5 bg-brand-600 text-white text-sm font-medium rounded-full shadow-lg hover:bg-brand-700 transition-colors"
        aria-label="Vis fag"
      >
        <svg
          width="16"
          height="16"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
        >
          <path d="M4 6h16M4 12h16M4 18h7" />
        </svg>
        Fag
      </button>

      {/* Mobile backdrop */}
      {isOpen && (
        <button
          type="button"
          tabIndex={-1}
          aria-label="Luk sidebar"
          className="lg:hidden fixed inset-0 z-40 bg-black/30"
          onClick={onToggle}
        />
      )}

      {/* Sidebar panel */}
      <aside
        className={[
          'flex flex-col bg-white border-r border-gray-200',
          // Desktop: always visible, inline
          'lg:relative lg:flex lg:w-64 lg:shrink-0',
          // Mobile: fixed overlay, toggled
          isOpen ? 'fixed inset-y-0 left-0 z-50 w-72 flex flex-col' : 'hidden lg:flex',
        ].join(' ')}
      >
        <div className="shrink-0 px-4 py-3 border-b border-gray-100 flex items-center justify-between">
          <h2 className="text-sm font-semibold text-gray-700">Fag</h2>
          <button
            type="button"
            onClick={onToggle}
            className="lg:hidden p-1 text-gray-400 hover:text-gray-600 rounded"
          >
            <svg
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
            >
              <line x1="18" y1="6" x2="6" y2="18" />
              <line x1="6" y1="6" x2="18" y2="18" />
            </svg>
          </button>
        </div>

        <div className="shrink-0 px-3 py-2 border-b border-gray-100">
          <div className="relative">
            <svg
              width="13"
              height="13"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              className="absolute left-2.5 top-1/2 -translate-y-1/2 text-gray-400 pointer-events-none"
            >
              <circle cx="11" cy="11" r="8" />
              <line x1="21" y1="21" x2="16.65" y2="16.65" />
            </svg>
            <input
              type="search"
              placeholder="Søg fag..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="w-full pl-8 pr-3 py-1.5 text-sm bg-gray-50 border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
            />
          </div>
        </div>

        <div className="flex-1 overflow-y-auto py-1">
          {filtered.length === 0 ? (
            <p className="px-4 py-6 text-xs text-gray-400 text-center">Ingen fag fundet</p>
          ) : (
            filtered.map((course) => (
              <DraggableCourseRow
                key={course.id}
                course={course}
                isSelected={course.id === selectedCourseId}
                onSelect={onSelectCourse}
              />
            ))
          )}
        </div>

        <div className="shrink-0 px-3 py-2 border-t border-gray-100">
          <p className="text-xs text-gray-400 text-center">{filtered.length} fag</p>
        </div>
      </aside>
    </>
  )
}
