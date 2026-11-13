import { useId } from 'react'

export interface SegmentedOption<T extends string> {
  value: T
  label: string
  /** Shown under the label, e.g. why the option is disabled. */
  description?: string
  disabled?: boolean
  testId?: string
}

interface SegmentedControlProps<T extends string> {
  label: string
  /** Visually hide the label (it stays for screen readers). */
  hideLabel?: boolean
  options: SegmentedOption<T>[]
  value: T | null
  onChange: (value: T) => void
  disabled?: boolean
  /** Stack the options, for narrow places like the sidebar. */
  vertical?: boolean
  /** For the dark sidebar. */
  onDark?: boolean
  className?: string
}

/** 2–4 short options side by side. Radio inputs behind, so arrow keys and screen readers work. */
export function SegmentedControl<T extends string>({
  label,
  hideLabel,
  options,
  value,
  onChange,
  disabled,
  vertical,
  onDark,
  className = '',
}: SegmentedControlProps<T>) {
  const name = useId()
  const optionClass = onDark
    ? 'text-brand-200 hover:text-white has-[:checked]:bg-brand-700 has-[:checked]:text-white has-[:focus-visible]:ring-white'
    : 'text-gray-700 has-[:checked]:bg-white has-[:checked]:text-gray-900 has-[:checked]:shadow-xs has-[:focus-visible]:ring-brand-500'
  return (
    <fieldset className={`min-w-0 ${className}`}>
      <legend
        className={
          hideLabel
            ? 'sr-only'
            : `text-sm font-bold mb-1 ${onDark ? 'text-brand-300' : 'text-gray-800'}`
        }
      >
        {label}
      </legend>
      <div
        className={`p-1 rounded-xl gap-1 grid ${vertical ? '' : 'grid-flow-col auto-cols-fr'} ${
          onDark ? 'bg-brand-800' : 'bg-gray-100'
        }`}
      >
        {options.map((o) => {
          const isDisabled = disabled || o.disabled
          return (
            <label
              key={o.value}
              data-testid={o.testId}
              className={`min-h-11 px-2 py-1 rounded-lg flex flex-col items-center justify-center text-center font-semibold has-[:focus-visible]:ring-2 ${optionClass} ${
                isDisabled ? 'cursor-not-allowed opacity-60' : 'cursor-pointer'
              }`}
            >
              <input
                type="radio"
                name={name}
                className="sr-only"
                value={o.value}
                checked={o.value === value}
                disabled={isDisabled}
                onChange={() => onChange(o.value)}
              />
              {o.label}
              {o.description && <span className="text-xs font-normal">{o.description}</span>}
            </label>
          )
        })}
      </div>
    </fieldset>
  )
}
