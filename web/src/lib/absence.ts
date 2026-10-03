import type { AbsenceCategory, LeaveStatus } from '../api/generated/types.gen'

export const CATEGORY_LABEL: Record<AbsenceCategory, string> = {
  Illness: 'Sygdom',
  ExtraordinaryLeave: 'Ekstraordinær frihed',
  Unauthorized: 'Ulovligt fravær',
}

export const CATEGORY_BADGE: Record<AbsenceCategory, string> = {
  Illness: 'bg-blue-50 text-blue-800',
  ExtraordinaryLeave: 'bg-emerald-50 text-emerald-800',
  Unauthorized: 'bg-orange-50 text-orange-800',
}

/**
 * Chart fill per category — the validated categorical slots (blue, orange, aqua). Stack order is
 * Unauthorized at the baseline, then Illness, then ExtraordinaryLeave (see CHART_CATEGORY_ORDER).
 */
export const CATEGORY_FILL: Record<AbsenceCategory, string> = {
  Illness: 'bg-[#2a78d6]',
  ExtraordinaryLeave: 'bg-[#1baf7a]',
  Unauthorized: 'bg-[#eb6834]',
}

export const CHART_CATEGORY_ORDER: AbsenceCategory[] = [
  'Unauthorized',
  'Illness',
  'ExtraordinaryLeave',
]

export const LEAVE_STATUS_LABEL: Record<LeaveStatus, string> = {
  Pending: 'Afventer godkendelse',
  Approved: 'Godkendt',
  Rejected: 'Afvist',
}

/** Today in Danish local time as yyyy-mm-dd. */
export function todayIso(): string {
  return new Intl.DateTimeFormat('sv-SE', { timeZone: 'Europe/Copenhagen' }).format(new Date())
}

/** yyyy-mm-dd → "3. okt." (adds the year when it isn't the current one). */
export function formatShortDate(iso: string): string {
  const d = new Date(`${iso}T12:00:00`)
  const sameYear = d.getFullYear() === new Date().getFullYear()
  return d.toLocaleDateString('da-DK', {
    day: 'numeric',
    month: 'short',
    ...(sameYear ? {} : { year: 'numeric' }),
  })
}

export function formatDateRange(date: string, endDate?: string | null): string {
  return endDate && endDate !== date
    ? `${formatShortDate(date)} – ${formatShortDate(endDate)}`
    : formatShortDate(date)
}

/** yyyy-mm-dd → "fredag 3. oktober". */
export function formatLongDate(iso: string): string {
  return new Date(`${iso}T12:00:00`).toLocaleDateString('da-DK', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
  })
}

export function addDaysIso(iso: string, days: number): string {
  const d = new Date(`${iso}T12:00:00`)
  d.setDate(d.getDate() + days)
  return d.toISOString().slice(0, 10)
}

/** The school year a date falls in, by its start year (1 August boundary): 2025 = 2025/26. */
export function schoolYearStart(iso: string): number {
  const [y, m] = iso.split('-').map(Number)
  return m >= 8 ? y : y - 1
}

export function schoolYearLabel(start: number): string {
  return `${start}/${String((start + 1) % 100).padStart(2, '0')}`
}

export function currentQuarter(iso: string): { year: number; quarter: number } {
  const [y, m] = iso.split('-').map(Number)
  return { year: y, quarter: Math.floor((m - 1) / 3) + 1 }
}

/** Days with one decimal, Danish comma: 2.5 → "2,5". */
export function formatDays(days: number): string {
  return days.toLocaleString('da-DK', { maximumFractionDigits: 1 })
}

export function formatPercent(value: number): string {
  return `${value.toLocaleString('da-DK', { maximumFractionDigits: 1 })} %`
}

export function saveBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  a.click()
  URL.revokeObjectURL(url)
}
