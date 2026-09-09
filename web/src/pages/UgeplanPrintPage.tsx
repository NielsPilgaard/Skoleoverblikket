import { useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useSearchParams } from 'react-router-dom'
import { Helmet } from 'react-helmet-async'
import {
  getApiV1ClassesByClassIdUgeplanOptions,
  getApiV1ClassesOptions,
} from '../api/generated/@tanstack/react-query.gen'
import { getISOWeek, getISOWeekYear } from '../utils/isoWeek'
import { Markdown } from '../components/markdown/Markdown'

const WEEKDAYS = ['Mandag', 'Tirsdag', 'Onsdag', 'Torsdag', 'Fredag']
const WEEKDAY_KEYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday']

export default function UgeplanPrintPage() {
  const [searchParams] = useSearchParams()
  const classId = searchParams.get('classId') ?? ''
  const schemaId = searchParams.get('schemaId') ?? undefined
  const now = new Date()
  const isoYear = Number(searchParams.get('isoYear') ?? getISOWeekYear(now))
  const isoWeek = Number(searchParams.get('isoWeek') ?? getISOWeek(now))

  const { data: weekPlan, isLoading } = useQuery({
    ...getApiV1ClassesByClassIdUgeplanOptions({
      path: { classId },
      query: { isoYear, isoWeek, ...(schemaId ? { schemaId } : {}) },
    }),
    enabled: !!classId,
  })

  const { data: classEntry } = useQuery({
    ...getApiV1ClassesOptions(),
    select: (all) => (all ?? []).find((c) => c.id === classId),
    enabled: !!classId,
  })

  useEffect(() => {
    if (!isLoading && weekPlan) {
      const t = setTimeout(() => window.print(), 300)
      return () => clearTimeout(t)
    }
  }, [isLoading, weekPlan])

  const robotsTag = (
    <Helmet>
      <meta name="robots" content="noindex,nofollow" />
    </Helmet>
  )

  if (!classId) {
    return (
      <>
        {robotsTag}
        <div className="p-8 text-gray-400">Mangler classId parameter.</div>
      </>
    )
  }
  if (isLoading) {
    return (
      <>
        {robotsTag}
        <div className="p-8 text-gray-400">Henter ugeplan…</div>
      </>
    )
  }

  type SlotRow = {
    id: string
    weekday: string
    startTime: string
    endTime: string
    timeSlotLabel: string | null
    courseName: string | null
    originalCourseName: string | null
    beskrivelse: string | null
    lektier: string | null
  }

  const slots = (weekPlan?.slots ?? []) as SlotRow[]
  const generelt = (weekPlan as { generelt?: string | null } | undefined)?.generelt
  const hasGenerelt = Boolean(generelt?.trim())

  // Build time axis from unique startTime values
  const timeAxis = [
    ...new Map(
      slots.map((s) => [s.startTime, { startTime: s.startTime, endTime: s.endTime }])
    ).values(),
  ].sort((a, b) => a.startTime.localeCompare(b.startTime))

  // Build slot map: startTime → weekday → slots
  const slotMap: Record<string, Record<string, SlotRow[]>> = {}
  for (const s of slots) {
    if (!slotMap[s.startTime]) slotMap[s.startTime] = {}
    if (!slotMap[s.startTime][s.weekday]) slotMap[s.startTime][s.weekday] = []
    slotMap[s.startTime][s.weekday].push(s)
  }

  const className = classEntry?.name ?? ''

  return (
    <>
      {robotsTag}
      <style>{`
        @page { size: A4 landscape; margin: 10mm; }
        @media print {
          html, body { margin: 0; height: 100%; }
          .no-print { display: none !important; }
          .print-page { height: calc(100vh - 0px); }
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
          border-bottom: 2px solid #2563eb;
        }
        .print-header-repeat {
          margin-bottom: 8px;
          padding-bottom: 5px;
          border-bottom: 1px solid #d1d5db;
        }
        .print-header-repeat .print-title { font-size: 13px; }
        .print-title { font-size: 16px; font-weight: 700; color: #111827; margin: 0; }
        .print-subtitle { font-size: 11px; color: #6b7280; margin: 1px 0 0; }
        .print-date { font-size: 11px; color: #9ca3af; margin: 0; }
        .print-table { width: 100%; border-collapse: collapse; font-size: 11px; table-layout: fixed; }
        .print-th {
          background: #f3f4f6;
          text-align: center;
          padding: 4px;
          font-weight: 600;
          color: #374151;
          border: 1px solid #e5e7eb;
          font-size: 10px;
          text-transform: uppercase;
          letter-spacing: 0.04em;
        }
        .print-th-time { width: 52px; text-align: right; }
        .print-td { border: 1px solid #e5e7eb; padding: 0; vertical-align: top; width: calc((100% - 52px) / 5); }
        .print-td-inner { padding: 4px 6px; box-sizing: border-box; }
        .print-td-time { text-align: right; background: #f9fafb; white-space: nowrap; width: 52px; }
        .print-td-time .print-td-inner { padding: 4px 6px 4px 4px; }
        .print-time { display: block; font-weight: 600; font-size: 11px; color: #374151; }
        .print-cell { display: flex; flex-direction: column; gap: 1px; }
        .print-course { font-weight: 600; color: #111827; font-size: 11px; }
        .print-swap { font-size: 10px; color: #6b7280; font-style: italic; }
        .print-beskrivelse { font-size: 10px; color: #374151; }
        .print-lektier { font-size: 10px; color: #2563eb; }
        .print-generelt { font-size: 11px; color: #374151; border: 1px solid #e5e7eb; border-radius: 6px; padding: 6px 10px; margin-bottom: 10px; }
        .print-generelt p { margin: 0 0 4px; }
        .print-generelt p:last-child { margin-bottom: 0; }
        .print-generelt ul, .print-generelt ol { margin: 2px 0; padding-left: 18px; }
        .print-beskrivelse p, .print-lektier p { margin: 0; }
        .print-beskrivelse ul, .print-beskrivelse ol,
        .print-lektier ul, .print-lektier ol { margin: 1px 0; padding-left: 14px; }
        .print-generelt-page { break-after: page; page-break-after: always; }
        .print-empty { text-align: center; color: #9ca3af; margin-top: 32px; font-size: 13px; }
        .no-print-bar {
          position: fixed; top: 0; right: 0; left: 0;
          background: #fff; border-bottom: 1px solid #e5e7eb;
          padding: 8px 16px; display: flex; gap: 8px; align-items: center;
          z-index: 10;
        }
      `}</style>

      <div className="no-print no-print-bar">
        <button
          onClick={() => window.print()}
          className="px-4 py-1.5 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 transition-colors"
        >
          Udskriv
        </button>
        <button
          onClick={() => window.close()}
          className="px-4 py-1.5 text-sm text-gray-600 hover:text-gray-900 border border-gray-300 rounded-lg transition-colors"
        >
          Luk
        </button>
      </div>

      <div className="print-page" style={{ marginTop: '48px' }}>
        <div className="print-header">
          <div>
            <h1 className="print-title">{className ? `${className} – Ugeplan` : 'Ugeplan'}</h1>
            <p className="print-subtitle">
              Uge {isoWeek}, {isoYear}
            </p>
          </div>
          <p className="print-date">Udskrevet {new Date().toLocaleDateString('da-DK')}</p>
        </div>

        {hasGenerelt && (
          <div className="print-generelt print-generelt-page">
            <Markdown>{generelt}</Markdown>
          </div>
        )}

        {/* Compact header repeated above the table — only when Generelt forced a
            page break, so it lands on page 2. Without Generelt it would just be a
            duplicate title/week on page 1. */}
        {hasGenerelt && (
          <div className="print-header print-header-repeat">
            <div>
              <h1 className="print-title">{className ? `${className} – Ugeplan` : 'Ugeplan'}</h1>
              <p className="print-subtitle">
                Uge {isoWeek}, {isoYear}
              </p>
            </div>
          </div>
        )}

        {slots.length === 0 ? (
          <p className="print-empty">
            Ingen lektioner for uge {isoWeek}, {isoYear}
          </p>
        ) : (
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
                  {WEEKDAY_KEYS.map((day) => {
                    const daySlots = slotMap[ts.startTime]?.[day]
                    return (
                      <td key={day} className="print-td">
                        <div className="print-td-inner">
                          {daySlots?.map((slot) => (
                            <div key={slot.id} className="print-cell">
                              <span className="print-course">{slot.courseName}</span>
                              {slot.originalCourseName &&
                                slot.originalCourseName !== slot.courseName && (
                                  <span className="print-swap">↔ {slot.originalCourseName}</span>
                                )}
                              {slot.beskrivelse && (
                                <div className="print-beskrivelse">
                                  <Markdown>{slot.beskrivelse}</Markdown>
                                </div>
                              )}
                              {slot.lektier && (
                                <div className="print-lektier">
                                  <strong>Lektier:</strong>
                                  <Markdown>{slot.lektier}</Markdown>
                                </div>
                              )}
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
        )}
      </div>
    </>
  )
}
