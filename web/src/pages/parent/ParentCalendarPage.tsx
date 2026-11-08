import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { getApiV1CalendarOptions } from '../../api/generated/@tanstack/react-query.gen'
import type { CalendarEntryDto } from '../../api/client'
import { usePageTitle } from '../../hooks/usePageTitle'
import { useIcsExport } from '../../hooks/useIcsExport'
import {
  getSchoolYears,
  isEntryInSchoolYear,
  CalendarGrid,
} from '../../components/calendar/CalendarGrid'

function currentSchoolStartYear(): number {
  const today = new Date()
  return today.getMonth() >= 7 ? today.getFullYear() : today.getFullYear() - 1
}

export default function ParentCalendarPage() {
  usePageTitle('Kalender')

  const defaultYear = currentSchoolStartYear()
  const [schoolStartYear, setSchoolStartYear] = useState(defaultYear)
  const { startYear, endYear } = getSchoolYears(schoolStartYear)

  const startYearQuery = useQuery({
    ...getApiV1CalendarOptions({ query: { year: startYear } }),
    select: (d) => (d ?? []) as CalendarEntryDto[],
  })
  const endYearQuery = useQuery({
    ...getApiV1CalendarOptions({ query: { year: endYear } }),
    select: (d) => (d ?? []) as CalendarEntryDto[],
  })
  const entriesStartYear = startYearQuery.data ?? []
  const entriesEndYear = endYearQuery.data ?? []
  const isLoading = startYearQuery.isLoading || endYearQuery.isLoading
  const isError = startYearQuery.isError || endYearQuery.isError

  const allEntries: CalendarEntryDto[] = [
    ...entriesStartYear,
    ...entriesEndYear.filter(
      (e) => !entriesStartYear.some((s) => s.id === e.id && s.startDate === e.startDate)
    ),
  ]
  const schoolYearEntries = allEntries.filter((e) => isEntryInSchoolYear(e, schoolStartYear))

  const { exportPending, exportDone, exportError, handleExportIcs } = useIcsExport()

  const yearOptions = Array.from({ length: 3 }, (_, i) => defaultYear - 1 + i)

  return (
    <div className="p-4 md:p-6 space-y-6">
      <div className="space-y-3 sm:space-y-0 sm:flex sm:items-center sm:justify-between sm:gap-4">
        <h1 className="text-xl font-semibold text-gray-900">
          Kalender {startYear}/{endYear}
        </h1>
        <div className="flex items-center gap-2 flex-wrap">
          <select
            value={schoolStartYear}
            onChange={(e) => setSchoolStartYear(Number(e.target.value))}
            className="border border-gray-300 rounded-lg px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
          >
            {yearOptions.map((y) => (
              <option key={y} value={y}>
                {y}/{y + 1}
              </option>
            ))}
          </select>
          <button
            onClick={handleExportIcs}
            disabled={exportPending}
            title="Åbn filen i Google Calendar, Outlook eller Kalender (iPhone/Mac) for at importere begivenhederne."
            className="border border-gray-300 text-gray-600 hover:bg-gray-50 rounded-lg px-3 py-1.5 text-sm disabled:opacity-50 transition-colors"
          >
            {exportPending ? 'Tilføjer...' : 'Tilføj til kalender'}
          </button>
        </div>
      </div>

      {exportDone && (
        <div className="bg-green-50 border border-green-200 rounded-xl px-5 py-3 text-sm text-green-800">
          Filen er hentet. Dobbeltklik på den for at importere – eller åbn din kalender og importer
          derfra.
        </div>
      )}

      {exportError && (
        <div className="bg-red-50 border border-red-200 rounded-xl px-5 py-3 text-sm text-red-800">
          Kunne ikke hente kalenderfilen. Prøv igen.
        </div>
      )}

      {isError ? (
        <div className="bg-red-50 border border-red-200 rounded-xl p-5 text-center">
          <p className="text-red-700 text-sm font-medium">Kunne ikke hente kalenderen</p>
          <button
            onClick={() => {
              startYearQuery.refetch()
              endYearQuery.refetch()
            }}
            className="mt-3 px-4 py-2 text-sm bg-red-100 text-red-700 rounded-lg hover:bg-red-200 transition-colors"
          >
            Prøv igen
          </button>
        </div>
      ) : isLoading ? (
        <div className="flex justify-center py-16">
          <div className="w-6 h-6 border-2 border-brand-600 border-t-transparent rounded-full animate-spin" />
        </div>
      ) : schoolYearEntries.length === 0 ? (
        <p className="text-sm text-gray-500">Ingen kalenderbegivenheder endnu.</p>
      ) : (
        <CalendarGrid
          schoolStartYear={schoolStartYear}
          entries={schoolYearEntries}
          isAdmin={false}
        />
      )}
    </div>
  )
}
