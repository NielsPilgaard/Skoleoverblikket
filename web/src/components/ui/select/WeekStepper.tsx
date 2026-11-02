import { ChevronLeftIcon, ChevronRightIcon } from '../icons'

interface WeekStepperProps {
  /** "Uge 45", "Marts 2027", "1. kvartal 2027". */
  main: string
  /** Dates under the main text, e.g. "2.–6. nov". */
  sub?: string
  onPrev: () => void
  onNext: () => void
  prevLabel?: string
  nextLabel?: string
  prevDisabled?: boolean
  nextDisabled?: boolean
  /** Shown when the stepper is away from the current value. */
  onToday?: () => void
  todayLabel?: string
  disabled?: boolean
  mainTestId?: string
  prevTestId?: string
  nextTestId?: string
  className?: string
}

/** ‹ Uge 45 › for values in sequence: weeks, and also days, months and quarters. */
export function WeekStepper({
  main,
  sub,
  onPrev,
  onNext,
  prevLabel = 'Forrige uge',
  nextLabel = 'Næste uge',
  prevDisabled,
  nextDisabled,
  onToday,
  todayLabel = 'Gå til denne uge',
  disabled,
  mainTestId,
  prevTestId,
  nextTestId,
  className = '',
}: WeekStepperProps) {
  const buttonClass =
    'w-11 h-11 shrink-0 rounded-full border border-gray-300 bg-white flex items-center justify-center text-gray-800 hover:bg-gray-50 disabled:opacity-40 disabled:cursor-not-allowed'
  return (
    <div className={className}>
      <div className="flex items-center gap-2">
        <button
          type="button"
          onClick={onPrev}
          disabled={disabled || prevDisabled}
          aria-label={prevLabel}
          data-testid={prevTestId}
          className={buttonClass}
        >
          <ChevronLeftIcon />
        </button>
        <div className="flex-1 min-w-0 text-center" aria-live="polite">
          <p data-testid={mainTestId} className="font-bold leading-tight text-gray-900">
            {main}
          </p>
          {sub && <p className="text-sm text-gray-700">{sub}</p>}
        </div>
        <button
          type="button"
          onClick={onNext}
          disabled={disabled || nextDisabled}
          aria-label={nextLabel}
          data-testid={nextTestId}
          className={buttonClass}
        >
          <ChevronRightIcon />
        </button>
      </div>
      {onToday && (
        <div className="text-center">
          <button
            type="button"
            onClick={onToday}
            disabled={disabled}
            className="h-11 px-3 rounded-lg text-sm font-semibold text-brand-700 hover:bg-brand-50"
          >
            {todayLabel}
          </button>
        </div>
      )}
    </div>
  )
}
