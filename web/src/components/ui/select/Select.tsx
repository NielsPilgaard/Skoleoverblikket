import { useId } from 'react'
import type React from 'react'
import { ChevronDownIcon } from '../icons'

export interface SelectOption {
  value: string
  label: string
  disabled?: boolean
}

interface SelectProps extends Omit<React.SelectHTMLAttributes<HTMLSelectElement>, 'onChange'> {
  label: string
  /** Visually hide the label (it stays for screen readers). */
  hideLabel?: boolean
  options: SelectOption[]
  value: string
  onChange: (value: string) => void
  /** Inline error or conflict text under the field. */
  error?: string
  testId?: string
}

/** Native <select> styled for the app: 44 px tall, 16 px text so iPhone doesn't zoom. */
export function Select({
  label,
  hideLabel,
  options,
  value,
  onChange,
  error,
  testId,
  className,
  id,
  ...rest
}: SelectProps) {
  const autoId = useId()
  const selectId = id ?? autoId
  const errorId = `${selectId}-error`
  return (
    <div className={className}>
      <label
        htmlFor={selectId}
        className={hideLabel ? 'sr-only' : 'block text-sm font-bold text-gray-800 mb-1'}
      >
        {label}
      </label>
      <div className="relative">
        <select
          {...rest}
          id={selectId}
          data-testid={testId}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? errorId : undefined}
          className={`appearance-none w-full h-11 pl-3 pr-10 rounded-lg border bg-white text-base text-gray-900 focus:outline-none focus:ring-2 focus:ring-brand-500 disabled:bg-gray-50 disabled:text-gray-500 ${
            error ? 'border-red-500' : 'border-gray-300'
          }`}
        >
          {options.map((o) => (
            <option key={o.value} value={o.value} disabled={o.disabled}>
              {o.label}
            </option>
          ))}
        </select>
        <ChevronDownIcon className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-600" />
      </div>
      {error && (
        <p id={errorId} className="mt-1 text-sm text-red-700">
          {error}
        </p>
      )}
    </div>
  )
}
