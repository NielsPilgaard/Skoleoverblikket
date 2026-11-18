import { useId } from 'react'
import type React from 'react'

export interface RadioCardOption<T extends string> {
  value: T
  label: string
  /** The consequence of choosing this option. */
  description?: React.ReactNode
  disabled?: boolean
  testId?: string
}

interface RadioCardsProps<T extends string> {
  label: string
  hideLabel?: boolean
  options: RadioCardOption<T>[]
  value: T
  onChange: (value: T) => void
  className?: string
}

/** Choices whose consequence needs a line of explanation, one card per option. */
export function RadioCards<T extends string>({
  label,
  hideLabel,
  options,
  value,
  onChange,
  className = '',
}: RadioCardsProps<T>) {
  const name = useId()
  return (
    <fieldset className={`min-w-0 ${className}`}>
      <legend className={hideLabel ? 'sr-only' : 'text-sm font-bold text-gray-800 mb-1'}>
        {label}
      </legend>
      <div className="space-y-2">
        {options.map((o) => (
          <label
            key={o.value}
            className={`flex items-start gap-3 min-h-11 p-3 rounded-xl border border-gray-300 hover:bg-gray-50 has-[:checked]:border-brand-600 has-[:checked]:bg-brand-50 has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-brand-500 ${
              o.disabled ? 'cursor-not-allowed opacity-60' : 'cursor-pointer'
            }`}
          >
            <input
              type="radio"
              name={name}
              data-testid={o.testId}
              className="mt-0.5 w-5 h-5 shrink-0 accent-brand-600"
              value={o.value}
              checked={o.value === value}
              disabled={o.disabled}
              onChange={() => onChange(o.value)}
            />
            <span className="min-w-0">
              <span className="block font-semibold text-gray-900">{o.label}</span>
              {o.description && (
                <span className="block text-sm text-gray-700">{o.description}</span>
              )}
            </span>
          </label>
        ))}
      </div>
    </fieldset>
  )
}
