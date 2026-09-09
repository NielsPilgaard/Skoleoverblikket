import { useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useSearchParams } from 'react-router-dom'
import { Helmet } from 'react-helmet-async'
import { getApiV1SfoUgeplanOptions } from '../api/generated/@tanstack/react-query.gen'
import type { SfoWeekPlanShiftDto } from '../api/client'
import { Markdown } from '../components/markdown/Markdown'

const WEEKDAYS = ['Mandag', 'Tirsdag', 'Onsdag', 'Torsdag', 'Fredag']

function getISOWeek(date: Date): number {
  const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()))
  d.setUTCDate(d.getUTCDate() + 4 - (d.getUTCDay() || 7))
  const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1))
  return Math.ceil(((d.getTime() - yearStart.getTime()) / 86400000 + 1) / 7)
}

function getISOWeekYear(date: Date): number {
  const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()))
  d.setUTCDate(d.getUTCDate() + 4 - (d.getUTCDay() || 7))
  return d.getUTCFullYear()
}

function buildTimeAxis(shifts: SfoWeekPlanShiftDto[]): { startTime: string; endTime: string }[] {
  const seen = new Map<string, { startTime: string; endTime: string }>()
  for (const s of shifts) {
    if (!s.startTime || !s.endTime) continue
    const key = `${s.startTime}-${s.endTime}`
    if (!seen.has(key)) seen.set(key, { startTime: s.startTime, endTime: s.endTime })
  }
  return [...seen.values()].sort((a, b) => a.startTime.localeCompare(b.startTime))
}

export default function SfoPrintPage() {
  const [searchParams] = useSearchParams()
  const now = new Date()
  const isoYear = Number(searchParams.get('isoYear') ?? getISOWeekYear(now))
  const isoWeek = Number(searchParams.get('isoWeek') ?? getISOWeek(now))

  const { data: weekPlan, isLoading } = useQuery(
    getApiV1SfoUgeplanOptions({ query: { isoYear, isoWeek } })
  )

  const shifts: SfoWeekPlanShiftDto[] = (weekPlan?.shifts ?? []) as SfoWeekPlanShiftDto[]
  const generelt = (weekPlan as { generelt?: string | null } | undefined)?.generelt
  const hasGenerelt = Boolean(generelt?.trim())

  useEffect(() => {
    if (!isLoading) {
      const t = setTimeout(() => window.print(), 300)
      return () => clearTimeout(t)
    }
  }, [isLoading])

  if (isLoading) {
    return (
      <>
        <Helmet>
          <meta name="robots" content="noindex,nofollow" />
        </Helmet>
        <div className="p-8 text-gray-400">Henter SFO ugeplan…</div>
      </>
    )
  }

  const timeAxis = buildTimeAxis(shifts)

  // shiftMap: "startTime-endTime" → dayOfWeek (1–5) → shifts
  const shiftMap: Record<string, Record<number, SfoWeekPlanShiftDto[]>> = {}
  for (const s of shifts) {
    if (!s.startTime || !s.endTime || !s.dayOfWeek) continue
    const key = `${s.startTime}-${s.endTime}`
    if (!shiftMap[key]) shiftMap[key] = {}
    if (!shiftMap[key][s.dayOfWeek]) shiftMap[key][s.dayOfWeek] = []
    shiftMap[key][s.dayOfWeek].push(s)
  }

  return (
    <>
      <Helmet>
        <meta name="robots" content="noindex,nofollow" />
      </Helmet>
      <style>{`
      @page { size: A4 landscape; margin: 10mm; }
      @media print {
        html, body { margin: 0; height: 100%; }
        .print-page { height: calc(100vh - 0px); }
        /* Only stretch the table to fill the page when it shares page 1 with
           nothing else. When Generelt forces it to page 2, let it flow. */
        .print-page:not(.has-generelt) .print-table { height: calc(100% - 72px) !important; }
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
        border-bottom: 2px solid #1f6321;
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
      .print-beskrivelse { font-size: 10px; color: #374151; font-style: italic; }
      .print-beskrivelse p { margin: 0; }
      .print-beskrivelse ul, .print-beskrivelse ol { margin: 1px 0; padding-left: 14px; }
      .print-info { font-size: 10px; color: #6b7280; }
      .print-empty { text-align: center; color: #9ca3af; margin-top: 32px; font-size: 13px; }
      .print-generelt { font-size: 11px; color: #374151; border: 1px solid #e5e7eb; border-radius: 6px; padding: 6px 10px; margin-bottom: 10px; }
      .print-generelt p { margin: 0 0 4px; }
      .print-generelt p:last-child { margin-bottom: 0; }
      .print-generelt ul, .print-generelt ol { margin: 2px 0; padding-left: 18px; }
      .print-generelt-page { break-after: page; page-break-after: always; }
    `}</style>
      <div className={`print-page${hasGenerelt ? ' has-generelt' : ''}`}>
        <div className="print-header">
          <div>
            <h1 className="print-title">SFO Ugeplan</h1>
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

        {/* Compact header repeated above the table — lands on page 2 when
            Generelt forced a page break, harmless single line otherwise. */}
        <div className="print-header print-header-repeat">
          <div>
            <h1 className="print-title">SFO Ugeplan</h1>
            <p className="print-subtitle">
              Uge {isoWeek}, {isoYear}
            </p>
          </div>
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
              <tr key={`${ts.startTime}-${ts.endTime}`}>
                <td className="print-td print-td-time">
                  <div className="print-td-inner">
                    <span className="print-time">{ts.startTime.slice(0, 5)}</span>
                  </div>
                </td>
                {[1, 2, 3, 4, 5].map((day) => {
                  const dayShifts = shiftMap[`${ts.startTime}-${ts.endTime}`]?.[day]
                  return (
                    <td key={day} className="print-td">
                      <div className="print-td-inner">
                        {dayShifts?.map((shift) => (
                          <div key={shift.id ?? shift.sfoShiftId} className="print-cell">
                            {shift.label && <span className="print-course">{shift.label}</span>}
                            {shift.beskrivelse && (
                              <div className="print-beskrivelse">
                                <Markdown>{shift.beskrivelse}</Markdown>
                              </div>
                            )}
                            {shift.staff?.map((s) => (
                              <span key={s.id} className="print-info">
                                {s.name}
                              </span>
                            ))}
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

        {shifts.length === 0 && (
          <p className="print-empty">
            Ingen vagtblokke for uge {isoWeek}, {isoYear}
          </p>
        )}
      </div>
    </>
  )
}
