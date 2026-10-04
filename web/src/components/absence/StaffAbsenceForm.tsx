import { useState } from 'react'
import { DatePicker } from '../DatePicker'
import { addDaysIso, todayIso } from '../../lib/absence'

export interface StaffAbsenceValues {
  staffId: string
  date: string
  endDate: string | null
  startTime: string | null
  endTime: string | null
  reason: string | null
}

interface StaffAbsenceFormProps {
  /** Shown as a picker when set: the office reports for someone else. */
  staff?: { id: string; name: string }[]
  initial?: Partial<StaffAbsenceValues>
  /** Earliest date that can be picked. Defaults to 14 days back, the API limit. */
  minDate?: string
  submitLabel: string
  pendingLabel: string
  reasonLabel: string
  reasonPlaceholder?: string
  isPending: boolean
  error?: string | null
  onSubmit: (values: StaffAbsenceValues) => void
  onCancel?: () => void
  testIdPrefix: string
}

/** Times come back from the API as "08:00:00"; the time input wants "08:00". */
function toInputTime(time?: string | null): string {
  return time ? time.slice(0, 5) : ''
}

/**
 * Report or edit a staff absence: one day or a range, and on a single day optionally only part of
 * the day, so only the lektioner in that window need a vikar.
 */
export function StaffAbsenceForm({
  staff,
  initial,
  minDate,
  submitLabel,
  pendingLabel,
  reasonLabel,
  reasonPlaceholder,
  isPending,
  error,
  onSubmit,
  onCancel,
  testIdPrefix,
}: StaffAbsenceFormProps) {
  const today = todayIso()
  const [staffId, setStaffId] = useState(initial?.staffId ?? '')
  const [date, setDate] = useState(initial?.date ?? today)
  const [endDate, setEndDate] = useState(initial?.endDate ?? initial?.date ?? today)
  const [partial, setPartial] = useState(!!initial?.startTime)
  const [startTime, setStartTime] = useState(toInputTime(initial?.startTime) || '08:00')
  const [endTime, setEndTime] = useState(toInputTime(initial?.endTime) || '12:00')
  const [reason, setReason] = useState(initial?.reason ?? '')

  const singleDay = endDate === date
  const timeInvalid = partial && singleDay && startTime >= endTime

  function submit(e: React.FormEvent) {
    e.preventDefault()
    if ((staff && !staffId) || timeInvalid) return
    const usesTimes = partial && singleDay
    onSubmit({
      staffId,
      date,
      endDate: singleDay ? null : endDate,
      startTime: usesTimes ? `${startTime}:00` : null,
      endTime: usesTimes ? `${endTime}:00` : null,
      reason: reason.trim() || null,
    })
  }

  return (
    <form onSubmit={submit} className="space-y-4" data-testid={`${testIdPrefix}-form`}>
      {staff && (
        <div>
          <label
            htmlFor={`${testIdPrefix}-staff`}
            className="block text-sm font-medium text-gray-700 mb-1"
          >
            Medarbejder
          </label>
          <select
            id={`${testIdPrefix}-staff`}
            value={staffId}
            onChange={(e) => setStaffId(e.target.value)}
            required
            data-testid={`${testIdPrefix}-staff`}
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm"
          >
            <option value="">Vælg medarbejder</option>
            {staff.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        </div>
      )}
      <div className="flex gap-3">
        <div className="flex-1 min-w-0">
          <span className="block text-sm font-medium text-gray-700 mb-1">Fra dato</span>
          <DatePicker
            value={date}
            onChange={(v) => {
              setDate(v)
              if (endDate < v) setEndDate(v)
            }}
            min={minDate ?? addDaysIso(today, -14)}
          />
        </div>
        <div className="flex-1 min-w-0">
          <span className="block text-sm font-medium text-gray-700 mb-1">Til dato</span>
          <DatePicker value={endDate} onChange={setEndDate} min={date} align="right" />
        </div>
      </div>
      {singleDay && (
        <div className="space-y-3">
          <label className="flex items-center gap-2 text-sm text-gray-700">
            <input
              type="checkbox"
              checked={partial}
              onChange={(e) => setPartial(e.target.checked)}
              data-testid={`${testIdPrefix}-partial`}
              className="w-4 h-4 rounded border-gray-300 text-brand-600 focus:ring-brand-500"
            />
            Kun en del af dagen
          </label>
          {partial && (
            <div className="flex gap-3">
              <div className="flex-1">
                <label
                  htmlFor={`${testIdPrefix}-start`}
                  className="block text-sm font-medium text-gray-700 mb-1"
                >
                  Fra kl.
                </label>
                <input
                  id={`${testIdPrefix}-start`}
                  type="time"
                  step={300}
                  value={startTime}
                  onChange={(e) => setStartTime(e.target.value)}
                  required
                  data-testid={`${testIdPrefix}-start`}
                  className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm"
                />
              </div>
              <div className="flex-1">
                <label
                  htmlFor={`${testIdPrefix}-end`}
                  className="block text-sm font-medium text-gray-700 mb-1"
                >
                  Til kl.
                </label>
                <input
                  id={`${testIdPrefix}-end`}
                  type="time"
                  step={300}
                  value={endTime}
                  onChange={(e) => setEndTime(e.target.value)}
                  required
                  data-testid={`${testIdPrefix}-end`}
                  className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm"
                />
              </div>
            </div>
          )}
          {timeInvalid && (
            <p className="text-sm text-red-600">"Til kl." skal være senere end "Fra kl.".</p>
          )}
        </div>
      )}
      <div>
        <label
          htmlFor={`${testIdPrefix}-reason`}
          className="block text-sm font-medium text-gray-700 mb-1"
        >
          {reasonLabel}
        </label>
        <input
          id={`${testIdPrefix}-reason`}
          type="text"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          maxLength={500}
          placeholder={reasonPlaceholder}
          data-testid={`${testIdPrefix}-reason`}
          className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-brand-500"
        />
        <p className="text-xs text-gray-500 mt-1">Skriv ikke diagnoser.</p>
      </div>
      <div className="flex gap-3">
        <button
          type="submit"
          disabled={isPending || timeInvalid}
          data-testid={`${testIdPrefix}-submit`}
          className="px-4 py-2 bg-brand-600 text-white text-sm font-medium rounded-lg hover:bg-brand-700 disabled:opacity-50"
        >
          {isPending ? pendingLabel : submitLabel}
        </button>
        {onCancel && (
          <button
            type="button"
            onClick={onCancel}
            className="px-4 py-2 text-sm text-gray-600 hover:text-gray-900"
          >
            Annuller
          </button>
        )}
      </div>
      {error && <p className="text-sm text-red-600">{error}</p>}
    </form>
  )
}
