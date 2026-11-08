import { useState, useMemo, useCallback, useEffect } from 'react'
import { Modal } from '../components/Modal'
import { useParams, Link } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  DndContext,
  DragOverlay,
  type DragOverEvent,
  PointerSensor,
  useSensor,
  useSensors,
  useDroppable,
  useDraggable,
  type DragStartEvent,
  type DragEndEvent,
  closestCenter,
} from '@dnd-kit/core'
import { CSS } from '@dnd-kit/utilities'
import { usePageTitle } from '../hooks/usePageTitle'
import CoursesSidebar from '../components/CoursesSidebar'
import { DatePicker } from '../components/DatePicker'
import { decodeSidebarDragId } from '../utils/sidebarDragId'
import {
  getApiV1ClassesByClassIdSchemasBySchemaIdOptions,
  getApiV1ClassesByClassIdSchemasBySchemaIdQueryKey,
  getApiV1ClassesByClassIdSchemasBySchemaIdTimeSlotsOptions,
  getApiV1CoursesOptions,
  getApiV1StaffOptions,
  getApiV1RoomsOptions,
  putApiV1ClassesByClassIdSchemasBySchemaIdSlotsMutation,
  deleteApiV1ClassesByClassIdSchemasBySchemaIdSlotsByTimeSlotIdByWeekdayMutation,
  putApiV1ClassesByClassIdSchemasBySchemaIdRenameMutation,
  putApiV1ClassesByClassIdSchemasBySchemaIdDaterangeMutation,
} from '../api/generated/@tanstack/react-query.gen'
import type {
  SchemaDetailDto,
  SlotsAndConflictsDto,
  TimeSlotDto,
  CourseDto,
  StaffDto,
  RoomDto,
  SlotDto,
  ConflictInfo,
} from '../api/client'

const WEEKDAYS = ['Mandag', 'Tirsdag', 'Onsdag', 'Torsdag', 'Fredag']
const WEEKDAY_NAMES = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'] as const
type WeekdayName = (typeof WEEKDAY_NAMES)[number]

function weekdayLabel(day: string | undefined): string {
  if (!day) return ''
  const idx = WEEKDAY_NAMES.indexOf(day as WeekdayName)
  return idx >= 0 ? WEEKDAYS[idx] : day
}

const SESSION_KEY_COURSE = 'schema-last-courseId'
const SESSION_KEY_TEACHER = 'schema-last-teacherId'

const COURSE_COLORS = [
  'bg-blue-100 text-blue-800 border-blue-200',
  'bg-purple-100 text-purple-800 border-purple-200',
  'bg-teal-100 text-teal-800 border-teal-200',
  'bg-orange-100 text-orange-800 border-orange-200',
  'bg-pink-100 text-pink-800 border-pink-200',
  'bg-indigo-100 text-indigo-800 border-indigo-200',
  'bg-cyan-100 text-cyan-800 border-cyan-200',
  'bg-yellow-100 text-yellow-800 border-yellow-200',
  'bg-lime-100 text-lime-800 border-lime-200',
  'bg-rose-100 text-rose-800 border-rose-200',
]

function getCourseColor(
  courseId: string,
  courseIds: string[],
  courses?: CourseDto[]
): { colorClass: string; colorStyle?: React.CSSProperties } {
  const hex = courses?.find((c) => c.id === courseId)?.color
  if (hex) {
    // Derive a readable text color: use a dark tint of the hex for text/border
    return {
      colorClass: 'border',
      colorStyle: { backgroundColor: `${hex}22`, color: hex, borderColor: `${hex}66` },
    }
  }
  const idx = courseIds.indexOf(courseId)
  return { colorClass: COURSE_COLORS[idx % COURSE_COLORS.length] }
}

// Encode/decode drag IDs as "timeSlotId:weekday"
function encodeDragId(timeSlotId: string, weekday: number) {
  return `${timeSlotId}:${weekday}`
}
function decodeDragId(id: string): { timeSlotId: string; weekday: number } {
  const [timeSlotId, weekdayStr] = id.split(':')
  return { timeSlotId, weekday: parseInt(weekdayStr, 10) }
}

// ─── Select field ────────────────────────────────────────────────────────────

interface SelectFieldProps {
  label: string
  required?: boolean
  options: { id: string; label: string }[]
  value: string
  onChange: (id: string) => void
  emptyLabel?: string
  autoFocus?: boolean
  'data-testid'?: string
}

function SelectField({
  label,
  required,
  options,
  value,
  onChange,
  emptyLabel = '—',
  autoFocus,
  'data-testid': testId,
}: SelectFieldProps) {
  return (
    <div>
      <label className="block text-sm font-medium text-gray-700 mb-1">
        {label}
        {required && <span className="text-red-500 ml-0.5">*</span>}
      </label>
      <select
        autoFocus={autoFocus}
        required={required}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter') {
            e.preventDefault()
            e.currentTarget.form?.requestSubmit()
          }
        }}
        data-testid={testId}
        className="w-full px-3 py-2 border border-gray-300 rounded-lg bg-white text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent"
      >
        <option value="">{emptyLabel}</option>
        {options.map((o) => (
          <option key={o.id} value={o.id}>
            {o.label}
          </option>
        ))}
      </select>
    </div>
  )
}

// ─── Assignment panel ────────────────────────────────────────────────────────

interface AssignmentPanelProps {
  classId: string
  schemaId: string
  timeSlotId: string
  weekday: number
  existing: SlotDto | null
  courses: CourseDto[]
  staff: StaffDto[]
  rooms: RoomDto[]
  initialCourseId?: string
  onCourseChange?: (courseId: string) => void
  onClose: () => void
  onDeleted: () => void
  onSaved: (updated: SlotsAndConflictsDto) => void
}

function AssignmentPanel({
  classId,
  schemaId,
  timeSlotId,
  weekday,
  existing,
  courses,
  staff,
  rooms,
  initialCourseId,
  onCourseChange,
  onClose,
  onDeleted,
  onSaved,
}: AssignmentPanelProps) {
  const [courseId, setCourseId] = useState(
    existing?.courseId ?? initialCourseId ?? sessionStorage.getItem(SESSION_KEY_COURSE) ?? ''
  )

  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  const [teacherId, setTeacherId] = useState(
    existing?.teacherId ?? sessionStorage.getItem(SESSION_KEY_TEACHER) ?? ''
  )
  const [roomId, setRoomId] = useState(existing?.roomId ?? '')
  const [aideId, setAideId] = useState(existing?.aideId ?? '')

  const teachers = staff.filter((s) => s.role === 'Teacher')
  const aides = staff.filter((s) => s.role === 'Aide' || s.role === 'Substitute')

  const saveMutation = useMutation({
    ...putApiV1ClassesByClassIdSchemasBySchemaIdSlotsMutation(),
    onSuccess: (data) => {
      sessionStorage.setItem(SESSION_KEY_COURSE, courseId)
      sessionStorage.setItem(SESSION_KEY_TEACHER, teacherId)
      onSaved(data as SlotsAndConflictsDto)
    },
  })

  const deleteMutation = useMutation({
    ...deleteApiV1ClassesByClassIdSchemasBySchemaIdSlotsByTimeSlotIdByWeekdayMutation(),
    onSuccess: onDeleted,
  })

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    if (!(courseId && teacherId) || saveMutation.isPending) return
    saveMutation.mutate({
      path: { classId, schemaId },
      body: {
        timeSlotId,
        weekday: WEEKDAY_NAMES[weekday - 1],
        courseId,
        teacherId,
        roomId: roomId || null,
        aideId: aideId || null,
      },
    })
  }

  return (
    <Modal
      isOpen
      onClose={onClose}
      contentClassName="bg-white rounded-2xl shadow-xl w-full max-w-md"
    >
      <form onSubmit={handleSubmit}>
        <div className="px-6 py-5 border-b border-gray-100 flex items-center justify-between">
          <h2 className="font-display text-lg font-semibold text-gray-900">
            {WEEKDAYS[weekday - 1]}
          </h2>
          <button
            type="button"
            onClick={onClose}
            className="p-1 text-gray-400 hover:text-gray-600 rounded-md"
          >
            <svg
              width="18"
              height="18"
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

        <div className="px-6 py-5 space-y-4">
          <SelectField
            label="Fag"
            required
            autoFocus
            options={courses.map((c) => ({ id: c.id ?? '', label: c.name ?? '' }))}
            value={courseId}
            onChange={(id) => {
              setCourseId(id)
              onCourseChange?.(id)
            }}
            emptyLabel="Vælg fag..."
            data-testid="slot-course-input"
          />
          <SelectField
            label="Lærer"
            required
            options={teachers.map((s) => ({ id: s.id ?? '', label: s.name ?? '' }))}
            value={teacherId}
            onChange={(id) => setTeacherId(id)}
            emptyLabel="Vælg lærer..."
            data-testid="slot-teacher-input"
          />
          <SelectField
            label="Lokale"
            options={rooms.map((r) => ({ id: r.id ?? '', label: r.name ?? '' }))}
            value={roomId}
            onChange={(id) => setRoomId(id)}
            emptyLabel="Intet lokale"
            data-testid="slot-room-input"
          />
          <SelectField
            label="Pædagog / Vikar"
            options={aides.map((s) => ({ id: s.id ?? '', label: s.name ?? '' }))}
            value={aideId}
            onChange={(id) => setAideId(id)}
            emptyLabel="Ingen"
            data-testid="slot-aide-input"
          />
          {(saveMutation.isError || deleteMutation.isError) && (
            <p className="text-sm text-red-600">Der opstod en fejl. Prøv igen.</p>
          )}
        </div>

        <div className="px-6 py-4 border-t border-gray-100 flex items-center justify-between">
          {existing ? (
            <button
              type="button"
              onClick={() =>
                deleteMutation.mutate({ path: { classId, schemaId, timeSlotId, weekday } })
              }
              disabled={deleteMutation.isPending}
              className="px-3 py-1.5 text-sm text-red-600 hover:bg-red-50 rounded-lg transition-colors disabled:opacity-50"
            >
              {deleteMutation.isPending ? 'Sletter...' : 'Slet lektion'}
            </button>
          ) : (
            <span />
          )}
          <div className="flex gap-3">
            <button
              type="button"
              onClick={onClose}
              className="px-4 py-2 text-sm text-gray-600 hover:text-gray-900 transition-colors"
            >
              Annuller
            </button>
            <button
              type="submit"
              disabled={!(courseId && teacherId) || saveMutation.isPending}
              className="px-4 py-2 text-sm bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {saveMutation.isPending ? 'Gemmer...' : 'Gem'}
            </button>
          </div>
        </div>
      </form>
    </Modal>
  )
}

// ─── Sidebar drag overlay card ───────────────────────────────────────────────

function SidebarDragOverlayCard({ course }: { course: CourseDto }) {
  const style: React.CSSProperties = course.color
    ? {
        backgroundColor: `${course.color}22`,
        borderColor: `${course.color}66`,
        color: course.color,
      }
    : {}
  return (
    <div
      className="h-20 w-36 flex flex-col gap-0.5 p-2 rounded-lg border-2 shadow-lg rotate-1 opacity-90 select-none"
      style={course.color ? style : { borderColor: '#94a3b8', backgroundColor: '#f1f5f9' }}
    >
      <span className="text-xs font-semibold leading-tight line-clamp-2">{course.name}</span>
      <span className="text-xs opacity-60 mt-auto">Slip for at placere</span>
    </div>
  )
}

// ─── Slot card (used both in grid and DragOverlay) ───────────────────────────

interface SlotCardProps {
  slot: SlotDto
  isConflict: boolean
  colorClass: string
  colorStyle?: React.CSSProperties
  isDragging?: boolean
}

function SlotCard({ slot, isConflict, colorClass, colorStyle, isDragging }: SlotCardProps) {
  if (isConflict) {
    return (
      <div
        className={`h-full w-full flex flex-col gap-0.5 p-2 rounded-lg border-2 border-red-300 bg-red-50 text-left select-none ${isDragging ? 'opacity-80 shadow-lg rotate-1' : ''}`}
      >
        <div className="flex items-start justify-between gap-1">
          <span className="text-xs font-semibold text-red-800 leading-tight line-clamp-1">
            {slot.courseName}
          </span>
          <svg
            width="12"
            height="12"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2.5"
            className="text-red-500 shrink-0 mt-0.5"
          >
            <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
            <line x1="12" y1="9" x2="12" y2="13" />
            <line x1="12" y1="17" x2="12.01" y2="17" />
          </svg>
        </div>
        <div className="flex items-center gap-1">
          <span className="text-xs text-red-600 leading-tight line-clamp-1 flex-1">
            {slot.teacherName}
          </span>
          {slot.teacherName && (
            <span className="text-xs text-red-500 font-medium shrink-0">
              {deriveInitials(slot.teacherName)}
            </span>
          )}
        </div>
        {slot.roomName && (
          <span className="text-xs text-red-400 leading-tight line-clamp-1">{slot.roomName}</span>
        )}
      </div>
    )
  }

  return (
    <div
      className={`h-full w-full flex flex-col gap-0.5 p-2 rounded-lg border text-left select-none ${colorClass} ${isDragging ? 'opacity-80 shadow-lg rotate-1' : ''}`}
      style={colorStyle}
    >
      <span className="text-xs font-semibold leading-tight line-clamp-2">{slot.courseName}</span>
      <div className="flex items-center gap-1">
        <span className="text-xs opacity-75 leading-tight line-clamp-1 flex-1">
          {slot.teacherName}
        </span>
        {slot.teacherName && (
          <span className="text-xs text-gray-500 font-medium shrink-0">
            {deriveInitials(slot.teacherName)}
          </span>
        )}
      </div>
      {slot.roomName && (
        <span className="text-xs opacity-60 leading-tight line-clamp-1">{slot.roomName}</span>
      )}
    </div>
  )
}

// ─── Draggable cell wrapper ──────────────────────────────────────────────────

interface DraggableCellProps {
  dragId: string
  slot: SlotDto
  isConflict: boolean
  colorClass: string
  colorStyle?: React.CSSProperties
  onClick: () => void
  isBeingDragged: boolean
}

function DraggableCell({
  dragId,
  slot,
  isConflict,
  colorClass,
  colorStyle,
  onClick,
  isBeingDragged,
}: DraggableCellProps) {
  const { attributes, listeners, setNodeRef, transform } = useDraggable({ id: dragId })

  const style = transform
    ? { transform: CSS.Translate.toString(transform), zIndex: 10, position: 'relative' as const }
    : undefined

  return (
    <div
      ref={setNodeRef}
      style={style}
      {...listeners}
      {...attributes}
      className={`h-full w-full cursor-grab active:cursor-grabbing ${isBeingDragged ? 'opacity-30' : ''}`}
    >
      <button
        type="button"
        className="w-full h-full text-left"
        onClick={(e) => {
          if (!transform) onClick()
          e.stopPropagation()
        }}
      >
        <SlotCard
          slot={slot}
          isConflict={isConflict}
          colorClass={colorClass}
          colorStyle={colorStyle}
        />
      </button>
    </div>
  )
}

// ─── Droppable cell wrapper ──────────────────────────────────────────────────

interface DroppableCellProps {
  dropId: string
  children: React.ReactNode
  isOver: boolean
  isEmpty: boolean
  isSidebarDrag?: boolean
}

function DroppableCell({ dropId, children, isOver, isEmpty, isSidebarDrag }: DroppableCellProps) {
  const { setNodeRef } = useDroppable({ id: dropId })

  const highlight =
    isOver && isEmpty
      ? 'ring-2 ring-brand-500 ring-offset-1 bg-brand-100'
      : isOver
        ? 'ring-2 ring-brand-400 ring-offset-1'
        : isSidebarDrag && isEmpty
          ? 'ring-1 ring-brand-200 ring-offset-1 bg-brand-50/50'
          : ''

  return (
    <div ref={setNodeRef} className={`h-full w-full transition-all rounded-lg ${highlight}`}>
      {children}
    </div>
  )
}

// ─── Empty cell ──────────────────────────────────────────────────────────────

function EmptyCell({ onClick }: { onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      className="group h-full w-full flex items-center justify-center rounded-lg border-2 border-dashed border-gray-200 hover:border-brand-400 hover:bg-brand-50 transition-all"
    >
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        className="text-gray-300 group-hover:text-brand-400 transition-colors"
      >
        <line x1="12" y1="5" x2="12" y2="19" />
        <line x1="5" y1="12" x2="19" y2="12" />
      </svg>
    </button>
  )
}

function deriveInitials(name: string | null | undefined): string {
  if (!name) return ''
  const parts = name.trim().split(/\s+/)
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
}

// ─── Main page ───────────────────────────────────────────────────────────────

export default function SchemaBuilderPage() {
  usePageTitle('Skema')
  const { classId, schemaId } = useParams<{ classId: string; schemaId: string }>()
  const qc = useQueryClient()

  const [panelCell, setPanelCell] = useState<{ timeSlotId: string; weekday: number } | null>(null)
  const [localSlots, setLocalSlots] = useState<SlotDto[] | null>(null)
  const [localConflicts, setLocalConflicts] = useState<ConflictInfo[] | null>(null)
  const [activeDragId, setActiveDragId] = useState<string | null>(null)
  const [overDropId, setOverDropId] = useState<string | null>(null)
  const [selectedCourseId, setSelectedCourseId] = useState('')
  const [sidebarOpen, setSidebarOpen] = useState(false)

  const [editingName, setEditingName] = useState(false)
  const [editingDates, setEditingDates] = useState(false)
  const [nameValue, setNameValue] = useState('')
  const [startDateValue, setStartDateValue] = useState('')
  const [endDateValue, setEndDateValue] = useState('')

  const renameMutation = useMutation({
    ...putApiV1ClassesByClassIdSchemasBySchemaIdRenameMutation(),
    onSuccess: () => {
      setEditingName(false)
      qc.setQueryData(
        getApiV1ClassesByClassIdSchemasBySchemaIdQueryKey({
          path: { classId: classId!, schemaId: schemaId! },
        }),
        (old: SchemaDetailDto | undefined) =>
          old ? { ...old, schema: { ...old.schema, name: nameValue } } : old
      )
    },
  })

  const daterangeMutation = useMutation({
    ...putApiV1ClassesByClassIdSchemasBySchemaIdDaterangeMutation(),
    onSuccess: () => {
      setEditingDates(false)
      qc.setQueryData(
        getApiV1ClassesByClassIdSchemasBySchemaIdQueryKey({
          path: { classId: classId!, schemaId: schemaId! },
        }),
        (old: SchemaDetailDto | undefined) =>
          old
            ? {
                ...old,
                schema: {
                  ...old.schema,
                  startDate: startDateValue || null,
                  endDate: endDateValue || null,
                },
              }
            : old
      )
    },
  })

  function startEditName() {
    setNameValue(schema?.name ?? '')
    setEditingName(true)
  }

  function submitName(e: React.FormEvent) {
    e.preventDefault()
    if (!nameValue.trim() || renameMutation.isPending) return
    renameMutation.mutate({
      path: { classId: classId!, schemaId: schemaId! },
      body: { name: nameValue.trim() },
    })
  }

  function startEditDates() {
    setStartDateValue(schema?.startDate ?? '')
    setEndDateValue(schema?.endDate ?? '')
    setEditingDates(true)
  }

  function submitDates(e: React.FormEvent) {
    e.preventDefault()
    if (daterangeMutation.isPending) return
    daterangeMutation.mutate({
      path: { classId: classId!, schemaId: schemaId! },
      body: {
        startDate: startDateValue || null,
        endDate: endDateValue || null,
      },
    })
  }

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }))

  const {
    data: detail,
    isLoading: loadingDetail,
    isError: errorDetail,
  } = useQuery({
    ...getApiV1ClassesByClassIdSchemasBySchemaIdOptions({
      path: { classId: classId!, schemaId: schemaId! },
    }),
    enabled: !!classId && !!schemaId,
  })

  const { data: rawTimeSlots, isLoading: loadingTs } = useQuery({
    ...getApiV1ClassesByClassIdSchemasBySchemaIdTimeSlotsOptions({
      path: { classId: classId!, schemaId: schemaId! },
    }),
    enabled: !!classId && !!schemaId,
  })
  const timeSlots = useMemo(() => (rawTimeSlots ?? []) as TimeSlotDto[], [rawTimeSlots])

  const { data: rawCourses } = useQuery(getApiV1CoursesOptions())
  const courses = useMemo(() => (rawCourses ?? []) as CourseDto[], [rawCourses])

  const { data: rawStaff } = useQuery(getApiV1StaffOptions())
  const staff = (rawStaff ?? []) as StaffDto[]

  const { data: rawRooms } = useQuery(getApiV1RoomsOptions())
  const rooms = (rawRooms ?? []) as RoomDto[]

  const upsertSlotMutation = useMutation({
    ...putApiV1ClassesByClassIdSchemasBySchemaIdSlotsMutation(),
    onSuccess: (data) => {
      const result = data as SlotsAndConflictsDto
      setLocalSlots(result.slots ?? null)
      setLocalConflicts(result.conflicts ?? null)
      qc.setQueryData(
        getApiV1ClassesByClassIdSchemasBySchemaIdQueryKey({
          path: { classId: classId!, schemaId: schemaId! },
        }),
        (old: SchemaDetailDto | undefined) =>
          old ? { ...old, slots: result.slots, conflicts: result.conflicts } : old
      )
    },
  })

  const slots = useMemo(() => localSlots ?? detail?.slots ?? [], [localSlots, detail?.slots])
  const conflicts = useMemo(
    () => localConflicts ?? detail?.conflicts ?? [],
    [localConflicts, detail?.conflicts]
  )
  const schema = detail?.schema

  const slotMap = useMemo(() => {
    const map: Record<string, Record<number, SlotDto>> = {}
    for (const s of slots) {
      if (!s.timeSlotId || s.weekday === undefined) continue
      const weekdayNum =
        typeof s.weekday === 'number'
          ? s.weekday
          : WEEKDAY_NAMES.indexOf(s.weekday as WeekdayName) + 1
      if (!map[s.timeSlotId]) map[s.timeSlotId] = {}
      map[s.timeSlotId][weekdayNum] = s
    }
    return map
  }, [slots])

  const conflictSlotIds = useMemo(() => {
    const ids = new Set<string>()
    for (const c of conflicts) {
      if (c.slotAId) ids.add(c.slotAId)
      if (c.slotBId) ids.add(c.slotBId)
    }
    return ids
  }, [conflicts])

  const courseIds = useMemo(() => {
    const seen: string[] = []
    for (const s of slots) {
      if (s.courseId && !seen.includes(s.courseId)) seen.push(s.courseId)
    }
    return seen
  }, [slots])

  const sortedTimeSlots = useMemo(
    () => [...(timeSlots ?? [])].sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0)),
    [timeSlots]
  )

  const handleCellSaved = useCallback(
    (updated: SlotsAndConflictsDto) => {
      setLocalSlots(updated.slots ?? null)
      setLocalConflicts(updated.conflicts ?? null)
      setPanelCell(null)
      qc.setQueryData(
        getApiV1ClassesByClassIdSchemasBySchemaIdQueryKey({
          path: { classId: classId!, schemaId: schemaId! },
        }),
        (old: SchemaDetailDto | undefined) =>
          old ? { ...old, slots: updated.slots, conflicts: updated.conflicts } : old
      )
    },
    [qc, classId, schemaId]
  )

  const handlePanelClose = useCallback(() => {
    setPanelCell(null)
  }, [])

  const handleCellDeleted = useCallback(() => {
    setPanelCell(null)
    qc.invalidateQueries({
      queryKey: getApiV1ClassesByClassIdSchemasBySchemaIdQueryKey({
        path: { classId: classId!, schemaId: schemaId! },
      }),
    })
    setLocalSlots(null)
    setLocalConflicts(null)
  }, [qc, classId, schemaId])

  // ─── Drag handlers ────────────────────────────────────────────────────────

  const handleDragStart = useCallback((event: DragStartEvent) => {
    setActiveDragId(event.active.id as string)
  }, [])

  const handleDragOver = useCallback((event: DragOverEvent) => {
    setOverDropId(event.over ? String(event.over.id) : null)
  }, [])

  const handleDragEnd = useCallback(
    (event: DragEndEvent) => {
      setActiveDragId(null)
      setOverDropId(null)

      const { active, over } = event
      if (!over || active.id === over.id) return

      const activeId = active.id as string
      const overId = over.id as string

      const sidebarCourseId = decodeSidebarDragId(activeId)
      if (sidebarCourseId) {
        const dst = decodeDragId(overId)
        const dstTs = sortedTimeSlots.find((ts) => ts.id === dst.timeSlotId)
        if (!dstTs || dstTs.isBreak) return
        // Drop existing slot → clear wip first, don't overwrite
        if (slotMap[dst.timeSlotId]?.[dst.weekday]) return
        setSelectedCourseId(sidebarCourseId)
        setPanelCell({ timeSlotId: dst.timeSlotId, weekday: dst.weekday })
        return
      }

      const src = decodeDragId(activeId)
      const dst = decodeDragId(overId)

      const srcSlot = slotMap[src.timeSlotId]?.[src.weekday]
      const dstSlot = slotMap[dst.timeSlotId]?.[dst.weekday]

      // Don't allow drops onto break rows
      const dstTs = sortedTimeSlots.find((ts) => ts.id === dst.timeSlotId)
      if (dstTs?.isBreak) return

      if (!srcSlot) return

      if (dstSlot) {
        // Swap: put dst's assignment into src's cell, src's into dst's cell
        upsertSlotMutation.mutate({
          path: { classId: classId!, schemaId: schemaId! },
          body: {
            timeSlotId: dst.timeSlotId,
            weekday: WEEKDAY_NAMES[dst.weekday - 1],
            courseId: srcSlot.courseId ?? '',
            teacherId: srcSlot.teacherId ?? '',
            roomId: srcSlot.roomId ?? null,
            aideId: srcSlot.aideId ?? null,
          },
        })
        upsertSlotMutation.mutate({
          path: { classId: classId!, schemaId: schemaId! },
          body: {
            timeSlotId: src.timeSlotId,
            weekday: WEEKDAY_NAMES[src.weekday - 1],
            courseId: dstSlot.courseId ?? '',
            teacherId: dstSlot.teacherId ?? '',
            roomId: dstSlot.roomId ?? null,
            aideId: dstSlot.aideId ?? null,
          },
        })
      } else {
        // Move: optimistically remove from source immediately so no ghost appears
        setLocalSlots((prev) => {
          const base = prev ?? detail?.slots ?? []
          return base.filter(
            (s) =>
              !(
                s.timeSlotId === src.timeSlotId &&
                (typeof s.weekday === 'number'
                  ? s.weekday
                  : WEEKDAY_NAMES.indexOf(s.weekday as WeekdayName) + 1) === src.weekday
              )
          )
        })
        upsertSlotMutation.mutate({
          path: { classId: classId!, schemaId: schemaId! },
          body: {
            timeSlotId: dst.timeSlotId,
            weekday: WEEKDAY_NAMES[dst.weekday - 1],
            courseId: srcSlot.courseId ?? '',
            teacherId: srcSlot.teacherId ?? '',
            roomId: srcSlot.roomId ?? null,
            aideId: srcSlot.aideId ?? null,
          },
        })
        const { mutationFn: deleteSlot } =
          deleteApiV1ClassesByClassIdSchemasBySchemaIdSlotsByTimeSlotIdByWeekdayMutation()
        deleteSlot!(
          {
            path: {
              classId: classId!,
              schemaId: schemaId!,
              timeSlotId: src.timeSlotId,
              weekday: src.weekday,
            },
          },
          undefined as never
        ).then(() =>
          qc.invalidateQueries({
            queryKey: getApiV1ClassesByClassIdSchemasBySchemaIdQueryKey({
              path: { classId: classId!, schemaId: schemaId! },
            }),
          })
        )
      }
    },
    [slotMap, sortedTimeSlots, upsertSlotMutation, classId, schemaId, qc, detail?.slots]
  )

  // Active drag slot (for overlay)
  const activeSidebarCourse = useMemo(() => {
    if (!activeDragId) return null
    const courseId = decodeSidebarDragId(activeDragId)
    return courseId ? (courses.find((c) => c.id === courseId) ?? null) : null
  }, [activeDragId, courses])

  const activeDragSlot = useMemo(() => {
    if (!activeDragId || activeSidebarCourse) return null
    const { timeSlotId, weekday } = decodeDragId(activeDragId)
    return slotMap[timeSlotId]?.[weekday] ?? null
  }, [activeDragId, activeSidebarCourse, slotMap])

  const activeDragColor = activeDragSlot
    ? getCourseColor(activeDragSlot.courseId ?? '', courseIds, courses)
    : { colorClass: '' }
  const activeDragConflict = activeDragSlot ? conflictSlotIds.has(activeDragSlot.id ?? '') : false

  const isLoading = loadingDetail || loadingTs
  const hasConflicts = conflicts.length > 0

  const openPanel = panelCell
    ? { ...panelCell, existing: slotMap[panelCell.timeSlotId]?.[panelCell.weekday] ?? null }
    : null

  if (errorDetail) {
    return (
      <div className="p-8">
        <div className="bg-red-50 border border-red-200 rounded-xl p-6 text-center">
          <p className="text-red-700 font-medium">Kunne ikke hente skema</p>
        </div>
      </div>
    )
  }

  return (
    <div className="flex flex-col h-full">
      {/* Status bar */}
      <div className="shrink-0 bg-white border-b border-gray-200 px-4 lg:px-6 py-3 flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-3 min-w-0">
          <Link
            to="/klasser"
            className="text-gray-400 hover:text-gray-600 transition-colors shrink-0"
          >
            <svg
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
            >
              <polyline points="15 18 9 12 15 6" />
            </svg>
          </Link>
          {isLoading ? (
            <div className="h-5 w-36 bg-gray-200 rounded animate-pulse" />
          ) : (
            <>
              {editingName ? (
                <form onSubmit={submitName} className="flex items-center gap-1">
                  <input
                    autoFocus
                    value={nameValue}
                    onChange={(e) => setNameValue(e.target.value)}
                    onKeyDown={(e) => e.key === 'Escape' && setEditingName(false)}
                    onBlur={() => !renameMutation.isPending && setEditingName(false)}
                    className="font-display text-base font-semibold text-gray-900 border-b border-brand-400 bg-transparent outline-none w-48 truncate"
                  />
                  <button
                    type="submit"
                    disabled={!nameValue.trim() || renameMutation.isPending}
                    className="p-1 text-brand-600 hover:text-brand-800 disabled:opacity-40"
                  >
                    <svg
                      width="14"
                      height="14"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2.5"
                    >
                      <polyline points="20 6 9 17 4 12" />
                    </svg>
                  </button>
                  <button
                    type="button"
                    onClick={() => setEditingName(false)}
                    className="p-1 text-gray-400 hover:text-gray-600"
                  >
                    <svg
                      width="14"
                      height="14"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2"
                    >
                      <line x1="18" y1="6" x2="6" y2="18" />
                      <line x1="6" y1="6" x2="18" y2="18" />
                    </svg>
                  </button>
                </form>
              ) : (
                <button
                  type="button"
                  onClick={startEditName}
                  className="group flex items-center gap-1.5 font-display text-base font-semibold text-gray-900 truncate hover:text-brand-700 transition-colors"
                >
                  {schema?.name}
                  <svg
                    width="12"
                    height="12"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                    className="text-gray-300 group-hover:text-brand-400 shrink-0 transition-colors"
                  >
                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
                    <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
                  </svg>
                </button>
              )}

              {editingDates ? (
                <form onSubmit={submitDates} className="flex items-center gap-1 shrink-0">
                  <DatePicker
                    value={startDateValue}
                    onChange={setStartDateValue}
                    placeholder="Startdato"
                  />
                  <span className="text-xs text-gray-400">–</span>
                  <DatePicker
                    value={endDateValue}
                    onChange={setEndDateValue}
                    placeholder="Slutdato"
                    min={startDateValue || undefined}
                  />
                  <button
                    type="submit"
                    disabled={daterangeMutation.isPending}
                    className="p-1 text-brand-600 hover:text-brand-800 disabled:opacity-40"
                  >
                    <svg
                      width="14"
                      height="14"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2.5"
                    >
                      <polyline points="20 6 9 17 4 12" />
                    </svg>
                  </button>
                  <button
                    type="button"
                    onClick={() => setEditingDates(false)}
                    className="p-1 text-gray-400 hover:text-gray-600"
                  >
                    <svg
                      width="14"
                      height="14"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2"
                    >
                      <line x1="18" y1="6" x2="6" y2="18" />
                      <line x1="6" y1="6" x2="18" y2="18" />
                    </svg>
                  </button>
                </form>
              ) : (
                <button
                  type="button"
                  onClick={startEditDates}
                  className="group shrink-0 flex items-center gap-1 text-xs text-gray-400 hover:text-brand-600 transition-colors"
                >
                  {schema?.startDate || schema?.endDate ? (
                    schema?.startDate && schema?.endDate ? (
                      `${schema.startDate} – ${schema.endDate}`
                    ) : (
                      (schema?.startDate ?? schema?.endDate)
                    )
                  ) : (
                    <span className="italic">Ingen datoer</span>
                  )}
                  <svg
                    width="11"
                    height="11"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                    className="text-gray-300 group-hover:text-brand-400 shrink-0 transition-colors"
                  >
                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
                    <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
                  </svg>
                </button>
              )}

              {schema?.startDate &&
                schema?.endDate &&
                new Date().toISOString().slice(0, 10) >= schema.startDate &&
                new Date().toISOString().slice(0, 10) <= schema.endDate && (
                  <span className="shrink-0 flex items-center gap-1 px-2 py-0.5 text-xs font-medium rounded-full bg-green-100 text-green-700">
                    <span className="w-1.5 h-1.5 rounded-full bg-green-500 inline-block" />
                    Aktiv nu
                  </span>
                )}
            </>
          )}
        </div>

        <div className="flex items-center gap-2">
          <Link
            to={`/klasser/${classId}/ugeplan`}
            className="flex items-center gap-1.5 px-3 py-1.5 text-sm text-gray-600 border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
          >
            <svg
              width="14"
              height="14"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
            >
              <rect x="3" y="4" width="18" height="18" rx="2" ry="2" />
              <line x1="16" y1="2" x2="16" y2="6" />
              <line x1="8" y1="2" x2="8" y2="6" />
              <line x1="3" y1="10" x2="21" y2="10" />
            </svg>
            <span className="hidden sm:inline">Ugeplan</span>
          </Link>
          <Link
            to={`/klasser/${classId}/schemas/${schemaId}/lektioner`}
            className="flex items-center gap-1.5 px-3 py-1.5 text-sm text-gray-600 border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
          >
            <svg
              width="14"
              height="14"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
            >
              <rect x="3" y="4" width="18" height="18" rx="2" ry="2" />
              <line x1="16" y1="2" x2="16" y2="6" />
              <line x1="8" y1="2" x2="8" y2="6" />
              <line x1="3" y1="10" x2="21" y2="10" />
            </svg>
            <span className="hidden sm:inline">Lektionsstruktur</span>
          </Link>
          <a
            href={`/udskriv/klasse/${classId}`}
            target="_blank"
            rel="noopener noreferrer"
            className="flex items-center gap-1.5 px-3 py-1.5 text-sm text-gray-600 border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
          >
            <svg
              width="14"
              height="14"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
            >
              <polyline points="6 9 6 2 18 2 18 9" />
              <path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2" />
              <rect x="6" y="14" width="12" height="8" />
            </svg>
            <span className="hidden sm:inline">Udskriv</span>
          </a>
          {hasConflicts && (
            <span className="flex items-center gap-1.5 px-3 py-1.5 bg-red-50 text-red-600 text-xs font-medium rounded-lg border border-red-200">
              <svg
                width="13"
                height="13"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2.5"
              >
                <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
                <line x1="12" y1="9" x2="12" y2="13" />
                <line x1="12" y1="17" x2="12.01" y2="17" />
              </svg>
              {conflicts.length} konflikt{conflicts.length !== 1 ? 'er' : ''}
            </span>
          )}
        </div>
      </div>

      {/* Content row: sidebar + grid */}
      <DndContext
        sensors={sensors}
        collisionDetection={closestCenter}
        onDragStart={handleDragStart}
        onDragOver={handleDragOver}
        onDragEnd={handleDragEnd}
      >
        <div className="flex flex-1 min-h-0">
          <div className="hidden lg:block">
            <CoursesSidebar
              courses={courses}
              selectedCourseId={selectedCourseId}
              onSelectCourse={setSelectedCourseId}
              isOpen={sidebarOpen}
              onToggle={() => setSidebarOpen((v) => !v)}
            />
          </div>

          {/* Main grid */}
          <div className="flex-1 overflow-auto">
            <div className="p-4 lg:p-6">
              {isLoading ? (
                <div className="animate-pulse">
                  <div className="grid grid-cols-6 gap-2 mb-2">
                    {Array.from({ length: 6 }).map((_, i) => (
                      <div key={i} className="h-8 bg-gray-200 rounded" />
                    ))}
                  </div>
                  {Array.from({ length: 6 }).map((_, r) => (
                    <div key={r} className="grid grid-cols-6 gap-2 mb-2">
                      {Array.from({ length: 6 }).map((_, c) => (
                        <div key={c} className="h-20 bg-gray-100 rounded-lg" />
                      ))}
                    </div>
                  ))}
                </div>
              ) : (
                <>
                  <div className="overflow-x-auto">
                    <div className="min-w-[640px]">
                      {/* Header */}
                      <div className="grid grid-cols-[100px_repeat(5,1fr)] gap-2 mb-2">
                        <div />
                        {WEEKDAYS.map((day) => (
                          <div
                            key={day}
                            className="text-center text-xs font-semibold text-gray-500 uppercase tracking-wider py-1"
                          >
                            {day}
                          </div>
                        ))}
                      </div>

                      {/* Rows */}
                      <div className="space-y-1.5">
                        {sortedTimeSlots.map((ts) => {
                          if (ts.isBreak) {
                            return (
                              <div
                                key={ts.id}
                                className="grid grid-cols-[100px_repeat(5,1fr)] gap-2"
                              >
                                <div className="flex flex-col justify-center text-right pr-3">
                                  <span className="text-xs text-gray-400 tabular-nums">
                                    {ts.startTime?.slice(0, 5)}
                                  </span>
                                </div>
                                <div className="col-span-5 flex items-center gap-2 px-3 py-1.5 bg-gray-50 border border-dashed border-gray-200 rounded-lg">
                                  <svg
                                    width="12"
                                    height="12"
                                    viewBox="0 0 24 24"
                                    fill="none"
                                    stroke="currentColor"
                                    strokeWidth="2"
                                    className="text-gray-400 shrink-0"
                                  >
                                    <path d="M18 8h1a4 4 0 0 1 0 8h-1" />
                                    <path d="M2 8h16v9a4 4 0 0 1-4 4H6a4 4 0 0 1-4-4V8z" />
                                    <line x1="6" y1="1" x2="6" y2="4" />
                                    <line x1="10" y1="1" x2="10" y2="4" />
                                    <line x1="14" y1="1" x2="14" y2="4" />
                                  </svg>
                                  <span className="text-xs text-gray-400">
                                    Pause · {ts.startTime?.slice(0, 5)}–{ts.endTime?.slice(0, 5)}
                                  </span>
                                </div>
                              </div>
                            )
                          }

                          return (
                            <div key={ts.id} className="grid grid-cols-[100px_repeat(5,1fr)] gap-2">
                              {/* Time label */}
                              <div className="flex flex-col justify-center text-right pr-3">
                                <span className="text-xs font-medium text-gray-600 tabular-nums">
                                  {ts.startTime?.slice(0, 5)}
                                </span>
                                <span className="text-xs text-gray-400 tabular-nums">
                                  {ts.endTime?.slice(0, 5)}
                                </span>
                                {ts.label && (
                                  <span className="text-xs text-gray-400 truncate">{ts.label}</span>
                                )}
                              </div>

                              {/* Day cells */}
                              {[1, 2, 3, 4, 5].map((weekday) => {
                                const slot = ts.id ? (slotMap[ts.id]?.[weekday] ?? null) : null
                                const dropId = ts.id ? encodeDragId(ts.id, weekday) : ''
                                const dragId = dropId
                                const isConflict = slot ? conflictSlotIds.has(slot.id ?? '') : false
                                const { colorClass, colorStyle } = slot
                                  ? getCourseColor(slot.courseId ?? '', courseIds, courses)
                                  : { colorClass: '' }
                                const isCurrentlyDragged = activeDragId === dragId
                                const isOver = overDropId === dropId
                                const isSidebarDrag = activeDragId
                                  ? decodeSidebarDragId(activeDragId) !== null
                                  : false
                                return (
                                  <div key={weekday} className="h-20 overflow-hidden">
                                    <DroppableCell
                                      dropId={dropId}
                                      isOver={isOver}
                                      isEmpty={!slot}
                                      isSidebarDrag={isSidebarDrag}
                                    >
                                      {slot ? (
                                        <DraggableCell
                                          dragId={dragId}
                                          slot={slot}
                                          isConflict={isConflict}
                                          colorClass={colorClass}
                                          colorStyle={colorStyle}
                                          onClick={() =>
                                            ts.id && setPanelCell({ timeSlotId: ts.id, weekday })
                                          }
                                          isBeingDragged={isCurrentlyDragged}
                                        />
                                      ) : (
                                        <EmptyCell
                                          onClick={() =>
                                            ts.id && setPanelCell({ timeSlotId: ts.id, weekday })
                                          }
                                        />
                                      )}
                                    </DroppableCell>
                                  </div>
                                )
                              })}
                            </div>
                          )
                        })}
                      </div>

                      {sortedTimeSlots.length === 0 && (
                        <div className="text-center py-16">
                          <p className="text-gray-500 text-sm font-medium">
                            Ingen lektionsstruktur defineret
                          </p>
                          <p className="text-gray-400 text-sm mt-1">
                            Opsæt skoledagens lektioner og pauser, så kan du begynde at bygge
                            skemaet.
                          </p>
                          <Link
                            to={`/klasser/${classId}/schemas/${schemaId}/lektioner`}
                            className="inline-block mt-4 px-4 py-2 text-sm font-medium bg-brand-600 text-white rounded-lg hover:bg-brand-700 transition-colors"
                          >
                            Opsæt lektionsstruktur
                          </Link>
                        </div>
                      )}
                    </div>
                  </div>

                  {/* Drag overlay — floating card that follows the cursor */}
                  <DragOverlay
                    dropAnimation={{ duration: 180, easing: 'cubic-bezier(0.18, 0.67, 0.6, 1.22)' }}
                  >
                    {activeSidebarCourse ? (
                      <SidebarDragOverlayCard course={activeSidebarCourse} />
                    ) : activeDragSlot ? (
                      <div className="h-20 w-36 pointer-events-none">
                        <SlotCard
                          slot={activeDragSlot}
                          isConflict={activeDragConflict}
                          {...activeDragColor}
                          isDragging
                        />
                      </div>
                    ) : null}
                  </DragOverlay>
                </>
              )}

              {/* Conflicts panel */}
              {!isLoading && hasConflicts && (
                <div className="mt-6 bg-red-50 border border-red-200 rounded-xl overflow-hidden">
                  <div className="px-5 py-3 border-b border-red-200 flex items-center gap-2">
                    <svg
                      width="15"
                      height="15"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2"
                      className="text-red-500"
                    >
                      <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
                      <line x1="12" y1="9" x2="12" y2="13" />
                      <line x1="12" y1="17" x2="12.01" y2="17" />
                    </svg>
                    <h3 className="text-sm font-semibold text-red-800">
                      {conflicts.length} konflikt{conflicts.length !== 1 ? 'er' : ''} fundet
                    </h3>
                  </div>
                  <div className="divide-y divide-red-100">
                    {conflicts.map((c, i) => {
                      const day = weekdayLabel(c.weekday)
                      const time = `${c.startTime?.slice(0, 5)}–${c.endTime?.slice(0, 5)}`
                      const aLabel = [c.slotACourseName, c.slotAClassName]
                        .filter(Boolean)
                        .join(' · ')
                      const bLabel = [c.slotBCourseName, c.slotBClassName]
                        .filter(Boolean)
                        .join(' · ')
                      return (
                        <div key={i} className="px-5 py-3">
                          <p className="text-sm font-medium text-red-800">
                            {c.type === 'TeacherDoubleBooked' && (
                              <>
                                Lærer <span className="font-semibold">{c.resourceName}</span> er
                                booket to gange
                              </>
                            )}
                            {c.type === 'RoomDoubleBooked' && (
                              <>
                                Lokale <span className="font-semibold">{c.resourceName}</span> er
                                booket to gange
                              </>
                            )}
                            {c.type === 'AideDoubleBooked' && (
                              <>
                                Pædagog <span className="font-semibold">{c.resourceName}</span> er
                                booket to gange
                              </>
                            )}
                          </p>
                          <p className="text-xs text-red-600 mt-0.5">
                            {day} {time} — {aLabel} og {bLabel}
                          </p>
                        </div>
                      )
                    })}
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>
        {/* end content row */}
      </DndContext>

      {/* Assignment panel */}
      {openPanel && (
        <AssignmentPanel
          classId={classId!}
          schemaId={schemaId!}
          timeSlotId={openPanel.timeSlotId}
          weekday={openPanel.weekday}
          existing={openPanel.existing}
          courses={courses}
          staff={staff}
          rooms={rooms}
          initialCourseId={selectedCourseId}
          onCourseChange={setSelectedCourseId}
          onClose={handlePanelClose}
          onDeleted={handleCellDeleted}
          onSaved={handleCellSaved}
        />
      )}
    </div>
  )
}
