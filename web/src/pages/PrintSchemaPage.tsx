import { useParams, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Helmet } from 'react-helmet-async'
import {
  getApiV1ClassesByIdOptions,
  getApiV1ClassesByClassIdScheduleOptions,
  getApiV1StaffByIdOptions,
  getApiV1StaffByStaffIdScheduleOptions,
  getApiV1RoomsByIdOptions,
  getApiV1RoomsByRoomIdScheduleOptions,
} from '../api/generated/@tanstack/react-query.gen'
import type { ScheduleSlotDto } from '../api/client'
import { WEEKDAY_LABELS, WEEKDAY_NUM } from '../lib/weekdays'

const WEEKDAYS = WEEKDAY_LABELS

function toWeekdayNumber(weekday: number | string): number {
  return typeof weekday === 'string' ? (WEEKDAY_NUM[weekday] ?? -1) : weekday
}

// Collects unique time labels across all active slots, sorted by start time
function buildTimeAxis(
  slots: ScheduleSlotDto[]
): { startTime: string; endTime: string; sortOrder: number }[] {
  const seen = new Map<string, { startTime: string; endTime: string; sortOrder: number }>()
  for (const s of slots) {
    if (!s.startTime || !s.endTime) continue
    const key = `${s.startTime}-${s.endTime}`
    if (!seen.has(key)) {
      seen.set(key, {
        startTime: s.startTime,
        endTime: s.endTime,
        sortOrder: parseInt(s.startTime.replace(':', ''), 10),
      })
    }
  }
  return [...seen.values()].sort((a, b) => a.sortOrder - b.sortOrder)
}

type PrintMode = 'class' | 'staff' | 'room'

function PrintGrid({
  title,
  subtitle,
  slots,
}: {
  title: string
  subtitle: string
  slots: ScheduleSlotDto[]
}) {
  // Build time axis
  const timeAxis = buildTimeAxis(slots)

  // Build slot map: startTime → weekday → slots array (to preserve collisions)
  const slotMap: Record<string, Record<number, ScheduleSlotDto[]>> = {}
  for (const s of slots) {
    if (!s.startTime || !s.weekday) continue
    const day = toWeekdayNumber(s.weekday)
    if (!slotMap[s.startTime]) slotMap[s.startTime] = {}
    if (!slotMap[s.startTime][day]) slotMap[s.startTime][day] = []
    slotMap[s.startTime][day].push(s)
  }

  return (
    <div className="print-page">
      <div className="print-header">
        <div>
          <h1 className="print-title">{title}</h1>
          <p className="print-subtitle">{subtitle}</p>
        </div>
        <p className="print-date">Udskrevet {new Date().toLocaleDateString('da-DK')}</p>
      </div>

      <table className="print-table">
        <thead>
          <tr>
            <th className="print-th print-th-time">Tid</th>
            {WEEKDAYS.map((d) => (
              <th key={d} className="print-th">
                {d}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {timeAxis.map((ts) => (
            <tr key={ts.startTime}>
              <td className="print-td print-td-time">
                <div className="print-td-inner">
                  <span className="print-time">{ts.startTime.slice(0, 5)}</span>
                </div>
              </td>
              {[1, 2, 3, 4, 5].map((day) => {
                const daySlots = slotMap[ts.startTime]?.[day]
                const hasConflict = daySlots && daySlots.length > 1
                return (
                  <td key={day} className="print-td">
                    <div className="print-td-inner">
                      {daySlots?.map((slot, idx) => (
                        <div
                          key={idx}
                          className={`print-cell${hasConflict ? ' print-cell-conflict' : ''}`}
                          style={
                            slot.courseColor
                              ? {
                                  backgroundColor: `${slot.courseColor}22`,
                                  borderLeft: `3px solid ${slot.courseColor}`,
                                  paddingLeft: '5px',
                                  borderRadius: '4px',
                                }
                              : undefined
                          }
                        >
                          <span
                            className="print-course"
                            style={slot.courseColor ? { color: slot.courseColor } : undefined}
                          >
                            {slot.courseName}
                          </span>
                          {slot.teacherName && (
                            <span className="print-info">{slot.teacherName}</span>
                          )}
                          {slot.className && <span className="print-info">{slot.className}</span>}
                          {slot.roomName && (
                            <span className="print-info print-room">{slot.roomName}</span>
                          )}
                          {slot.aideName && <span className="print-info">{slot.aideName}</span>}
                        </div>
                      ))}
                    </div>
                  </td>
                )
              })}
            </tr>
          ))}
        </tbody>
      </table>

      {slots.length === 0 && <p className="print-empty">Ingen lektioner at vise</p>}
    </div>
  )
}

function ClassPrintPage({ classId }: { classId: string }) {
  const { data: klass } = useQuery(getApiV1ClassesByIdOptions({ path: { id: classId } }))

  const { data: rawSlots, isLoading } = useQuery(
    getApiV1ClassesByClassIdScheduleOptions({ path: { classId } })
  )
  const slots: ScheduleSlotDto[] = (rawSlots ?? []) as ScheduleSlotDto[]

  if (isLoading) return <div className="p-8 text-gray-400">Henter skema…</div>
  if (slots.length === 0) return <div className="p-8 text-gray-400">Ingen aktivt skema fundet</div>

  return <PrintGrid title={klass?.name ?? 'Klasse'} subtitle="Ugeskema" slots={slots} />
}

function StaffPrintPage({ staffId }: { staffId: string }) {
  const { data: staff } = useQuery(getApiV1StaffByIdOptions({ path: { id: staffId } }))

  const { data: rawSlots, isLoading } = useQuery(
    getApiV1StaffByStaffIdScheduleOptions({ path: { staffId } })
  )
  const slots: ScheduleSlotDto[] = (rawSlots ?? []) as ScheduleSlotDto[]

  if (isLoading) return <div className="p-8 text-gray-400">Henter skema…</div>

  return (
    <PrintGrid
      title={staff?.name ?? 'Medarbejder'}
      subtitle="Ugeskema på tværs af klasser"
      slots={slots}
    />
  )
}

function RoomPrintPage({ roomId }: { roomId: string }) {
  const { data: room } = useQuery(getApiV1RoomsByIdOptions({ path: { id: roomId } }))

  const { data: rawSlots, isLoading } = useQuery(
    getApiV1RoomsByRoomIdScheduleOptions({ path: { roomId } })
  )
  const slots: ScheduleSlotDto[] = (rawSlots ?? []) as ScheduleSlotDto[]

  if (isLoading) return <div className="p-8 text-gray-400">Henter lokaleplan…</div>

  return <PrintGrid title={room?.name ?? 'Lokale'} subtitle="Ugentlig belægning" slots={slots} />
}

export default function PrintSchemaPage() {
  const { classId, staffId, roomId } = useParams<{
    classId?: string
    staffId?: string
    roomId?: string
  }>()
  const [searchParams] = useSearchParams()

  // Safely validate PrintMode from query param
  const typeParam = searchParams.get('type')
  const isValidPrintMode = (value: string | null): value is PrintMode => {
    return value === 'class' || value === 'staff' || value === 'room'
  }

  const mode: PrintMode = isValidPrintMode(typeParam)
    ? typeParam
    : classId
      ? 'class'
      : staffId
        ? 'staff'
        : 'room'

  return (
    <>
      <Helmet>
        <meta name="robots" content="noindex,nofollow" />
      </Helmet>
      <style>{`
        @page {
          size: A4 landscape;
          margin: 10mm;
        }
        @media print {
          html, body { margin: 0; }
          .no-print { display: none !important; }
          .print-table tr { page-break-inside: avoid; }
        }
        .print-page {
          font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
          padding: 16px 20px;
          max-width: 277mm;
          margin: 0 auto;
        }
        .print-header {
          display: flex;
          justify-content: space-between;
          align-items: flex-end;
          margin-bottom: 10px;
          padding-bottom: 8px;
          border-bottom: 2px solid #1e3a5f;
        }
        .print-title {
          font-size: 16px;
          font-weight: 700;
          color: #111827;
          margin: 0;
        }
        .print-subtitle {
          font-size: 11px;
          color: #6b7280;
          margin: 1px 0 0;
        }
        .print-date {
          font-size: 11px;
          color: #9ca3af;
          margin: 0;
        }
        .print-table {
          width: 100%;
          border-collapse: collapse;
          font-size: 11px;
          table-layout: fixed;
        }
        .print-th {
          background: #f3f4f6;
          text-align: center;
          padding: 4px 4px;
          font-weight: 600;
          color: #374151;
          border: 1px solid #e5e7eb;
          font-size: 10px;
          text-transform: uppercase;
          letter-spacing: 0.04em;
        }
        .print-th-time {
          width: 52px;
          text-align: right;
        }
        .print-td {
          border: 1px solid #e5e7eb;
          padding: 0;
          vertical-align: top;
          width: calc((100% - 52px) / 5);
        }
        .print-td-inner {
          padding: 4px 6px;
          box-sizing: border-box;
        }
        .print-td-time {
          text-align: right;
          background: #f9fafb;
          white-space: nowrap;
          width: 52px;
        }
        .print-td-time .print-td-inner {
          padding: 4px 6px 4px 4px;
        }
        .print-time {
          display: block;
          font-weight: 600;
          font-size: 11px;
          color: #374151;
        }
        .print-cell {
          display: flex;
          flex-direction: column;
          gap: 1px;
        }
        .print-course {
          font-weight: 600;
          color: #111827;
          font-size: 11px;
        }
        .print-info {
          font-size: 10px;
          color: #6b7280;
        }
        .print-room {
          color: #9ca3af;
        }
        .print-empty {
          text-align: center;
          color: #9ca3af;
          margin-top: 32px;
          font-size: 13px;
        }
      `}</style>

      <div className="no-print fixed top-4 right-4 flex gap-2 z-50">
        <button
          onClick={() => window.print()}
          className="flex items-center gap-2 px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 shadow transition-colors"
        >
          <svg
            width="15"
            height="15"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
          >
            <polyline points="6 9 6 2 18 2 18 9" />
            <path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2" />
            <rect x="6" y="14" width="12" height="8" />
          </svg>
          Udskriv
        </button>
        <button
          onClick={() => window.close()}
          className="px-4 py-2 bg-white text-gray-700 text-sm font-medium rounded-lg border border-gray-200 hover:bg-gray-50 shadow transition-colors"
        >
          Luk
        </button>
      </div>

      {mode === 'class' && classId && <ClassPrintPage classId={classId} />}
      {mode === 'staff' && staffId && <StaffPrintPage staffId={staffId} />}
      {mode === 'room' && roomId && <RoomPrintPage roomId={roomId} />}
    </>
  )
}
