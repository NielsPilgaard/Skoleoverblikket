import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getApiV1AbsenceStatsOptions,
  getApiV1ClassesOptions,
  postApiV1AbsenceFollowUpsMutation,
} from '../../api/generated/@tanstack/react-query.gen'
import { getApiV1AbsenceFlaggedExport } from '../../api/generated/sdk.gen'
import type {
  AbsenceCategory,
  AbsenceFlag,
  StudentQuarterStatsDto,
  WeekAbsenceDto,
} from '../../api/generated/types.gen'
import {
  CATEGORY_FILL,
  CATEGORY_LABEL,
  CHART_CATEGORY_ORDER,
  currentQuarter,
  formatDayCount,
  formatDays,
  formatPercent,
  formatShortDate,
  saveBlob,
  todayIso,
} from '../../lib/absence'
import { problemDetail } from '../../lib/problem'
import { type Column, DataTable } from '../ui/list'
import { Select, WeekStepper } from '../ui/select'

const QUARTER_MONTHS = ['januar–marts', 'april–juni', 'juli–september', 'oktober–december']

function weekValue(w: WeekAbsenceDto, c: AbsenceCategory): number {
  return c === 'Illness'
    ? w.illnessDays
    : c === 'ExtraordinaryLeave'
      ? w.leaveDays
      : w.unauthorizedDays
}

interface DayCounts {
  illnessDays: number
  leaveDays: number
  unauthorizedDays: number
}

/** Sygdom / Frihed / Ulovligt columns, shared by the class and student tables. */
function dayColumns<T extends DayCounts>(): Column<T>[] {
  const days = (key: keyof DayCounts, header: string): Column<T> => ({
    key,
    header,
    align: 'right',
    cell: (r) => <span className="tabular-nums">{formatDays(r[key])}</span>,
    sortValue: (r) => r[key],
  })
  return [
    days('illnessDays', 'Sygdom'),
    days('leaveDays', 'Frihed'),
    days('unauthorizedDays', 'Ulovligt'),
  ]
}

function FlagBadge({ flag }: { flag: AbsenceFlag }) {
  if (flag === 'None') return null
  const critical = flag === 'FifteenPercent'
  return (
    <span
      className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-sm text-xs font-medium ${
        critical ? 'bg-red-100 text-red-800' : 'bg-amber-100 text-amber-900'
      }`}
    >
      <span aria-hidden>{critical ? '▲' : '●'}</span>
      {critical ? '15 % ulovligt fravær' : '10 % ulovligt fravær'}
    </span>
  )
}

function Legend() {
  return (
    <div className="flex flex-wrap gap-4 text-xs text-gray-600">
      {CHART_CATEGORY_ORDER.map((c) => (
        <span key={c} className="inline-flex items-center gap-1.5">
          <span className={`w-2.5 h-2.5 rounded-xs ${CATEGORY_FILL[c]}`} aria-hidden />
          {CATEGORY_LABEL[c]}
        </span>
      ))}
    </div>
  )
}

/** Fraværsdage per week, stacked by category with ulovligt at the baseline. */
function WeeklyAbsenceChart({ weeks }: { weeks: WeekAbsenceDto[] }) {
  const max = Math.max(1, ...weeks.map((w) => w.illnessDays + w.leaveDays + w.unauthorizedDays))
  return (
    <figure className="bg-white border border-gray-200 rounded-xl p-4">
      <figcaption className="flex flex-wrap items-baseline justify-between gap-2 mb-3">
        <span className="text-sm font-medium text-gray-900">Fraværsdage pr. uge</span>
        <Legend />
      </figcaption>
      <div className="relative h-40 border-b border-gray-200">
        <div className="absolute inset-x-0 top-0 border-t border-gray-100" aria-hidden />
        <span className="absolute -top-2 left-0 bg-white pr-1 text-[10px] text-gray-400">
          {formatDays(max)}
        </span>
        <div className="absolute inset-0 pl-6 flex items-end justify-around">
          {weeks.map((w) => {
            const total = w.illnessDays + w.leaveDays + w.unauthorizedDays
            const segments = CHART_CATEGORY_ORDER.map((c) => ({ c, v: weekValue(w, c) })).filter(
              (s) => s.v > 0
            )
            return (
              <div
                key={w.weekStart}
                className="group relative flex-1 h-full flex items-end justify-center"
                data-testid={`absence-week-${w.isoWeek}`}
              >
                <div
                  className="w-full max-w-6 flex flex-col-reverse gap-[2px]"
                  style={{ height: `${(total / max) * 100}%` }}
                >
                  {segments.map((s, i) => (
                    <div
                      key={s.c}
                      className={`${CATEGORY_FILL[s.c]} ${i === segments.length - 1 ? 'rounded-t-[4px]' : ''}`}
                      style={{ flexGrow: s.v, flexBasis: 0 }}
                    />
                  ))}
                </div>
                <div className="pointer-events-none absolute bottom-full mb-1 hidden group-hover:block z-10 w-44 rounded-lg bg-gray-900 text-white text-xs p-2 shadow-lg">
                  <p className="font-medium mb-1">
                    Uge {w.isoWeek} · {formatDayCount(total)}
                  </p>
                  {CHART_CATEGORY_ORDER.map((c) => (
                    <p key={c} className="flex items-center gap-1.5">
                      <span className={`w-2 h-2 rounded-xs ${CATEGORY_FILL[c]}`} aria-hidden />
                      {CATEGORY_LABEL[c]}: {formatDays(weekValue(w, c))}
                    </p>
                  ))}
                </div>
              </div>
            )
          })}
        </div>
      </div>
      <div className="pl-6 flex justify-around mt-1">
        {weeks.map((w) => (
          <span key={w.weekStart} className="flex-1 text-center text-[10px] text-gray-400">
            {w.isoWeek}
          </span>
        ))}
      </div>
    </figure>
  )
}

export function AbsenceStatsTab({ isAdmin }: { isAdmin: boolean }) {
  const qc = useQueryClient()
  const now = currentQuarter(todayIso())
  const [{ year, quarter }, setPeriod] = useState(now)
  const [classId, setClassId] = useState('')
  const [category, setCategory] = useState<AbsenceCategory | ''>('')
  const [downloadError, setDownloadError] = useState<string | null>(null)

  const { data: classes = [] } = useQuery(getApiV1ClassesOptions())
  const { data: stats, isLoading } = useQuery(
    getApiV1AbsenceStatsOptions({ query: { year, quarter, classId: classId || undefined } })
  )

  const markInformed = useMutation({
    ...postApiV1AbsenceFollowUpsMutation(),
    onSuccess: () => qc.invalidateQueries({ queryKey: [{ _id: 'getApiV1AbsenceStats' }] }),
  })

  function shift(delta: number) {
    const index = year * 4 + (quarter - 1) + delta
    setPeriod({ year: Math.floor(index / 4), quarter: (index % 4) + 1 })
  }

  async function downloadFlagged() {
    setDownloadError(null)
    try {
      const { data } = await getApiV1AbsenceFlaggedExport({
        query: { year, quarter },
        parseAs: 'blob',
        throwOnError: true,
      })
      saveBlob(data as Blob, `ulovligt-fravaer-15-procent-${year}-k${quarter}.csv`)
    } catch {
      setDownloadError('Listen kunne ikke hentes. Prøv igen om lidt.')
    }
  }

  const students = stats?.students ?? []
  const weeks = stats?.weeks ?? []
  const flagged = students.filter((s) => s.flag !== 'None')
  const anyCritical = flagged.some((s) => s.flag === 'FifteenPercent')
  const isCurrent = year === now.year && quarter === now.quarter
  const isFuture = year * 4 + quarter > now.year * 4 + now.quarter
  const totals = {
    Illness: students.reduce((sum, s) => sum + s.illnessDays, 0),
    ExtraordinaryLeave: students.reduce((sum, s) => sum + s.leaveDays, 0),
    Unauthorized: students.reduce((sum, s) => sum + s.unauthorizedDays, 0),
  } satisfies Record<AbsenceCategory, number>
  // Name order first, so DataTable's stable sort keeps ties alphabetical.
  const visibleStudents = students
    .filter((s) =>
      category === ''
        ? s.illnessDays + s.leaveDays + s.unauthorizedDays > 0
        : category === 'Illness'
          ? s.illnessDays > 0
          : category === 'ExtraordinaryLeave'
            ? s.leaveDays > 0
            : s.unauthorizedDays > 0
    )
    .sort((a, b) => a.studentName.localeCompare(b.studentName, 'da'))

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center gap-2">
        <WeekStepper
          className="w-64"
          main={`${quarter}. kvartal ${year}`}
          onPrev={() => shift(-1)}
          onNext={() => shift(1)}
          prevLabel="Forrige kvartal"
          nextLabel="Næste kvartal"
          nextDisabled={isCurrent || isFuture}
          mainTestId="absence-stats-quarter"
        />
        <Select
          label="Klasse"
          hideLabel
          options={[
            { value: '', label: 'Hele skolen' },
            ...classes.map((c) => ({ value: c.id, label: c.name })),
          ]}
          value={classId}
          onChange={setClassId}
          testId="absence-stats-class"
        />
      </div>

      {isLoading && <p className="text-sm text-gray-400">Indlæser…</p>}

      {stats && (
        <>
          <p className="text-sm text-gray-600" data-testid="absence-stats-school-days">
            {QUARTER_MONTHS[quarter - 1]}: {stats.schoolDaysInQuarter} skoledage i kvartalet
            {isCurrent ? ` (${stats.schoolDaysSoFar} indtil i dag)` : ''}. Ulovligt fravær i procent
            regnes af alle kvartalets skoledage. Mangler der en ferie i kalenderen, bliver tallet
            for højt.
          </p>

          <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
            {CHART_CATEGORY_ORDER.map((c) => (
              <div key={c} className="bg-white border border-gray-200 rounded-xl p-4">
                <p className="flex items-center gap-1.5 text-xs text-gray-500">
                  <span className={`w-2.5 h-2.5 rounded-xs ${CATEGORY_FILL[c]}`} aria-hidden />
                  {CATEGORY_LABEL[c]}
                </p>
                <p className="text-2xl font-semibold text-gray-900 mt-1">
                  {formatDays(totals[c])}{' '}
                  <span className="text-sm font-normal text-gray-500">
                    {totals[c] === 1 ? 'dag' : 'dage'}
                  </span>
                </p>
              </div>
            ))}
          </div>

          {weeks.length > 0 && <WeeklyAbsenceChart weeks={weeks} />}

          {flagged.length > 0 && (
            <section className="bg-white border border-gray-200 rounded-xl p-4">
              <div className="flex flex-wrap items-center justify-between gap-2 mb-3">
                <h2 className="text-sm font-semibold text-gray-900">
                  Elever med meget ulovligt fravær
                </h2>
                {isAdmin && anyCritical && (
                  <button
                    type="button"
                    onClick={downloadFlagged}
                    data-testid="absence-flagged-export"
                    className="px-3 py-1.5 text-sm font-medium border border-gray-300 rounded-lg hover:bg-gray-50"
                  >
                    Hent liste over elever med 15 % (CSV)
                  </button>
                )}
              </div>
              {downloadError && <p className="text-sm text-red-600 mb-2">{downloadError}</p>}
              {markInformed.isError && (
                <p className="text-sm text-red-600 mb-2">
                  {problemDetail(markInformed.error) ?? 'Kunne ikke gemme.'}
                </p>
              )}
              <ul className="divide-y divide-gray-100">
                {flagged.map((s: StudentQuarterStatsDto) => (
                  <li
                    key={s.studentId}
                    className="flex flex-wrap items-center gap-3 py-2"
                    data-testid={`absence-flag-${s.studentId}`}
                  >
                    <span className="text-sm text-gray-900">
                      {s.studentName} <span className="text-gray-500">· {s.className}</span>
                    </span>
                    <FlagBadge flag={s.flag} />
                    <span className="text-sm text-gray-600">
                      {formatPercent(s.unauthorizedPercent)}
                    </span>
                    <span className="ml-auto">
                      {s.parentsInformedAt ? (
                        <span className="text-xs text-gray-500">
                          Forældre orienteret {formatShortDate(s.parentsInformedAt.slice(0, 10))}
                        </span>
                      ) : (
                        <button
                          type="button"
                          onClick={() =>
                            markInformed.mutate({ body: { studentId: s.studentId, year, quarter } })
                          }
                          disabled={markInformed.isPending}
                          data-testid={`absence-mark-informed-${s.studentId}`}
                          className="px-3 py-1.5 text-xs font-medium bg-brand-600 text-white rounded-lg hover:bg-brand-700 disabled:opacity-50"
                        >
                          Markér som orienteret
                        </button>
                      )}
                    </span>
                  </li>
                ))}
              </ul>
            </section>
          )}

          {!classId && (stats.classes ?? []).length > 0 && (
            <section>
              <h2 className="text-sm font-semibold text-gray-900 mb-2">Klasser</h2>
              <DataTable
                items={stats.classes ?? []}
                caption="Fravær pr. klasse"
                rowKey={(c) => c.classId}
                noun={{ one: 'klasse', other: 'klasser' }}
                empty={null}
                columns={[
                  {
                    key: 'class',
                    header: 'Klasse',
                    cell: (c) => c.className,
                    sortValue: (c) => c.className,
                  },
                  {
                    key: 'students',
                    header: 'Elever',
                    align: 'right',
                    cell: (c) => c.studentCount,
                    sortValue: (c) => c.studentCount,
                  },
                  ...dayColumns(),
                  {
                    key: 'total',
                    header: 'Fravær i alt',
                    align: 'right',
                    cell: (c) => formatPercent(c.absencePercent),
                    sortValue: (c) => c.absencePercent,
                  },
                  {
                    key: 'flagged',
                    header: 'Elever med flag',
                    align: 'right',
                    cell: (c) => c.flaggedStudents || '–',
                    sortValue: (c) => c.flaggedStudents,
                  },
                ]}
              />
            </section>
          )}

          <section>
            <div className="flex flex-wrap items-center justify-between gap-2 mb-2">
              <h2 className="text-sm font-semibold text-gray-900">Elever</h2>
              <div className="flex items-center gap-2 text-sm text-gray-700">
                <span aria-hidden="true">Vis elever med</span>
                <Select
                  label="Vis elever med"
                  hideLabel
                  options={[
                    { value: '', label: 'Alt fravær' },
                    ...CHART_CATEGORY_ORDER.map((c) => ({ value: c, label: CATEGORY_LABEL[c] })),
                  ]}
                  value={category}
                  onChange={(value) => setCategory(value as AbsenceCategory | '')}
                  testId="absence-stats-category"
                  className="w-44"
                />
              </div>
            </div>
            <DataTable
              items={visibleStudents}
              caption="Fravær pr. elev"
              testId="absence-stats-students"
              rowKey={(s) => s.studentId}
              noun={{ one: 'elev', other: 'elever' }}
              searchText={(s) => `${s.studentName} ${s.className}`}
              searchPlaceholder="Søg efter elev eller klasse"
              defaultSort={{ value: 'percent', descending: true }}
              empty={
                <p>
                  {category === ''
                    ? 'Intet fravær i kvartalet.'
                    : `Ingen elever med ${CATEGORY_LABEL[category].toLowerCase()} i kvartalet.`}
                </p>
              }
              columns={[
                {
                  key: 'name',
                  header: 'Elev',
                  cell: (s) => s.studentName,
                  sortValue: (s) => s.studentName,
                },
                {
                  key: 'class',
                  header: 'Klasse',
                  cell: (s) => s.className,
                  sortValue: (s) => s.className,
                },
                ...dayColumns(),
                {
                  key: 'percent',
                  header: 'Ulovligt %',
                  align: 'right',
                  sortValue: (s) => s.unauthorizedPercent,
                  cell: (s) => (
                    <span className="inline-flex flex-wrap items-center justify-end gap-2">
                      <FlagBadge flag={s.flag} />
                      {formatPercent(s.unauthorizedPercent)}
                    </span>
                  ),
                },
              ]}
            />
          </section>
        </>
      )}
    </div>
  )
}
