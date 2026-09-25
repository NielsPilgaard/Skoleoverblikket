import { useEffect, useRef } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import { getApiV1ClassesByClassIdWeekPlanQueryKey } from '../../api/generated/@tanstack/react-query.gen'

interface AvailableStaffDto {
  id: string
  name: string
  role: 'Teacher' | 'Aide' | 'Substitute'
}

interface BusyStaffDto {
  id: string
  name: string
  role: 'Teacher' | 'Aide' | 'Substitute'
  conflictDescription: string
}

interface StaffAvailabilityDto {
  available: AvailableStaffDto[]
  busy: BusyStaffDto[]
}

interface AssignSubstitutePanelProps {
  weekPlanId: string
  slotId: string
  classId: string
  isoYear: number
  isoWeek: number
  weekday: number
  timeSlotId: string
  courseName: string
  weekdayLabel: string
  startTime: string
  currentSubstituteTeacherId: string | null
  currentSubstituteTeacherName: string | null
  currentSubstituteAideId: string | null
  currentSubstituteAideName: string | null
  schemaId: string | null
  onClose: () => void
}

export function AssignSubstitutePanel({
  weekPlanId,
  slotId,
  classId,
  isoYear,
  isoWeek,
  weekday,
  timeSlotId,
  courseName,
  weekdayLabel,
  startTime,
  currentSubstituteTeacherId,
  currentSubstituteTeacherName,
  currentSubstituteAideId,
  currentSubstituteAideName,
  schemaId,
  onClose,
}: AssignSubstitutePanelProps) {
  const qc = useQueryClient()
  const panelRef = useRef<HTMLDivElement>(null)

  const ugeplanQueryKey = getApiV1ClassesByClassIdWeekPlanQueryKey({
    path: { classId },
    query: { isoYear, isoWeek, ...(schemaId ? { schemaId } : {}) },
  })

  const { data: availability, isLoading } = useQuery<StaffAvailabilityDto>({
    queryKey: ['staff-available', isoYear, isoWeek, weekday, timeSlotId],
    queryFn: () =>
      api.get<StaffAvailabilityDto>(
        `/staff/available?isoYear=${isoYear}&isoWeek=${isoWeek}&weekday=${weekday}&timeSlotId=${timeSlotId}`
      ),
  })

  const assignMutation = useMutation({
    mutationFn: ({
      substituteTeacherId,
      substituteAideId,
    }: {
      substituteTeacherId: string | null
      substituteAideId: string | null
    }) =>
      api.put(`/week-plans/${weekPlanId}/slots/${slotId}/substitute`, {
        substituteTeacherId,
        substituteAideId,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ugeplanQueryKey })
      onClose()
    },
  })

  function handleAssign(staffId: string) {
    assignMutation.mutate({ substituteTeacherId: staffId, substituteAideId: null })
  }

  function handleClear() {
    assignMutation.mutate({ substituteTeacherId: null, substituteAideId: null })
  }

  // Close on Escape
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  // Trap focus inside panel
  useEffect(() => {
    panelRef.current?.focus()
  }, [])

  const hasCurrentSubstitute = !!currentSubstituteTeacherId || !!currentSubstituteAideId

  return (
    <>
      {/* Backdrop */}
      <div className="fixed inset-0 z-40 bg-black/30" onClick={onClose} aria-hidden="true" />

      {/* Slide-over panel */}
      <div
        ref={panelRef}
        tabIndex={-1}
        className="fixed inset-y-0 right-0 z-50 flex w-full max-w-sm flex-col bg-white shadow-xl focus:outline-none"
        role="dialog"
        aria-modal="true"
        aria-label="Tildel vikar"
        data-testid="assign-substitute-panel"
      >
        {/* Header */}
        <div className="flex items-start justify-between px-5 py-4 border-b border-gray-200">
          <div>
            <h2 className="font-display text-base font-semibold text-gray-900">Tildel vikar</h2>
            <p className="text-xs text-gray-500 mt-0.5">
              {courseName} · {weekdayLabel} {startTime.slice(0, 5)}
            </p>
          </div>
          <button
            onClick={onClose}
            className="ml-4 text-gray-400 hover:text-gray-600 transition-colors"
            aria-label="Luk"
            data-testid="close-substitute-panel"
          >
            <svg
              width="20"
              height="20"
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

        {/* Body */}
        <div className="flex-1 overflow-y-auto px-5 py-4 space-y-5">
          {/* Current substitute */}
          {hasCurrentSubstitute && (
            <div className="rounded-lg border border-amber-200 bg-amber-50 px-4 py-3">
              <p className="text-xs font-semibold text-amber-700 mb-1">Nuværende vikar</p>
              <div className="flex items-center justify-between">
                <span className="text-sm font-medium text-amber-900">
                  {currentSubstituteTeacherName ?? currentSubstituteAideName}
                </span>
                <button
                  onClick={handleClear}
                  disabled={assignMutation.isPending}
                  className="text-xs text-red-600 hover:text-red-800 font-medium disabled:opacity-50"
                  data-testid="remove-substitute-button"
                >
                  Fjern vikar
                </button>
              </div>
            </div>
          )}

          {isLoading && (
            <div className="text-sm text-gray-400 text-center animate-pulse py-6">
              Henter ledige medarbejdere...
            </div>
          )}

          {!isLoading && availability && (
            <>
              {/* Available staff */}
              {availability.available.length > 0 && (
                <div>
                  <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">
                    Ledige nu
                  </h3>
                  <ul className="space-y-1">
                    {availability.available.map((s) => (
                      <li key={s.id}>
                        <button
                          type="button"
                          onClick={() => handleAssign(s.id)}
                          disabled={assignMutation.isPending}
                          className="w-full flex items-center justify-between rounded-lg px-3 py-2.5 text-sm hover:bg-green-50 hover:text-green-900 transition-colors disabled:opacity-50 group"
                          data-testid={`assign-staff-${s.id}`}
                        >
                          <span className="font-medium">{s.name}</span>
                          <span className="text-xs text-gray-400 group-hover:text-green-600">
                            {roleLabel(s.role)}
                          </span>
                        </button>
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              {availability.available.length === 0 && (
                <p className="text-sm text-gray-400 text-center py-4">
                  Ingen ledige medarbejdere på dette tidspunkt
                </p>
              )}

              {/* Busy staff */}
              {availability.busy.length > 0 && (
                <div>
                  <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">
                    Alle medarbejdere
                  </h3>
                  <ul className="space-y-1">
                    {availability.busy.map((s) => (
                      <li key={s.id}>
                        <div className="flex items-center justify-between rounded-lg px-3 py-2.5 text-sm opacity-50">
                          <span className="font-medium text-gray-600">{s.name}</span>
                          <span className="text-xs text-gray-400 text-right max-w-[55%] truncate">
                            {s.conflictDescription}
                          </span>
                        </div>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </>
          )}

          {assignMutation.isError && (
            <p className="text-sm text-red-600">Der opstod en fejl. Prøv igen.</p>
          )}
        </div>
      </div>
    </>
  )
}

function roleLabel(role: string): string {
  if (role === 'Teacher') return 'Lærer'
  if (role === 'Aide') return 'Pædagog'
  if (role === 'Substitute') return 'Vikar'
  return role
}
