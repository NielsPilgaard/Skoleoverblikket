import { useState, useRef, useEffect, useId } from 'react'
import { DayPicker } from 'react-day-picker'
import { da } from 'react-day-picker/locale'

interface DatePickerProps {
  value: string // ISO yyyy-MM-dd
  onChange: (value: string) => void
  min?: string // ISO yyyy-MM-dd — dates before this are disabled
  placeholder?: string
  fromYear?: number
  toYear?: number
  align?: 'left' | 'right'
  labelId?: string // id of the visible label; the button is announced as "<label> <date>"
}

function isoToDate(iso: string): Date {
  return new Date(`${iso}T00:00:00`)
}

function dateToIso(d: Date): string {
  const pad = (n: number) => n.toString().padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

function formatDDMMYYYY(iso: string): string {
  const [y, m, d] = iso.split('-')
  return `${d}/${m}/${y}`
}

const currentYear = new Date().getFullYear()

export function DatePicker({
  value,
  onChange,
  min,
  placeholder = 'Vælg dato',
  fromYear = currentYear - 5,
  toYear = currentYear + 5,
  align = 'left',
  labelId,
}: DatePickerProps) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  const valueId = useId()

  const selected = value ? isoToDate(value) : undefined
  const disabled = min ? { before: isoToDate(min) } : undefined

  useEffect(() => {
    function handleClick(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    if (open) document.addEventListener('mousedown', handleClick)
    return () => document.removeEventListener('mousedown', handleClick)
  }, [open])

  function handleSelect(day: Date | undefined) {
    if (day) {
      onChange(dateToIso(day))
      setOpen(false)
    }
  }

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-labelledby={labelId ? `${labelId} ${valueId}` : undefined}
        aria-expanded={open}
        className="w-full flex items-center gap-2 px-3 py-2 border border-gray-300 rounded-lg text-sm text-left focus:outline-none focus:ring-2 focus:ring-brand-500 hover:border-gray-400 bg-white transition-colors"
      >
        <svg
          width="16"
          height="16"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          aria-hidden="true"
          className="text-gray-400 flex-shrink-0"
        >
          <rect x="3" y="4" width="18" height="18" rx="2" ry="2" />
          <line x1="16" y1="2" x2="16" y2="6" />
          <line x1="8" y1="2" x2="8" y2="6" />
          <line x1="3" y1="10" x2="21" y2="10" />
        </svg>
        <span id={valueId} className={value ? 'text-gray-900' : 'text-gray-400'}>
          {value ? formatDDMMYYYY(value) : placeholder}
        </span>
      </button>

      {open && (
        <div
          className={`absolute z-50 top-full mt-1 bg-white border border-gray-200 rounded-xl shadow-xl max-w-[calc(100vw-2rem)] ${align === 'right' ? 'right-0' : 'left-0'}`}
        >
          <div className="relative p-3">
            <style>{`
              .rdp-dropdown select {
                appearance: none;
                background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='%236b7280' stroke-width='2.5'%3E%3Cpolyline points='6 9 12 15 18 9'/%3E%3C/svg%3E");
                background-repeat: no-repeat;
                background-position: right 6px center;
                padding-right: 22px !important;
              }
              .rdp-dropdown select:focus {
                outline: 2px solid var(--color-brand-500, #16a34a);
                outline-offset: 1px;
              }
            `}</style>
            <DayPicker
              mode="single"
              locale={da}
              captionLayout="dropdown"
              startMonth={new Date(fromYear, 0)}
              endMonth={new Date(toYear, 11)}
              showWeekNumber
              selected={selected}
              onSelect={handleSelect}
              disabled={disabled}
              defaultMonth={selected ?? (min ? isoToDate(min) : undefined)}
              classNames={{
                root: 'text-sm w-72',
                months: 'flex flex-col',
                month: 'w-full',
                // Nav renders above months (default, no navLayout) — position it absolute top-right
                nav: 'absolute top-3 right-3 flex items-center gap-0.5',
                button_previous:
                  'p-1.5 rounded-lg hover:bg-gray-100 text-brand-600 transition-colors disabled:opacity-30 disabled:cursor-not-allowed',
                button_next:
                  'p-1.5 rounded-lg hover:bg-gray-100 text-brand-600 transition-colors disabled:opacity-30 disabled:cursor-not-allowed',
                chevron: 'fill-current w-4 h-4',
                // Caption: just the dropdowns, centered; nav floats top-right above
                month_caption: 'flex items-center mb-3',
                caption_label: 'hidden',
                dropdowns: 'flex items-center gap-1.5',
                dropdown: 'rdp-dropdown relative',
                dropdown_root: 'rdp-dropdown relative',
                months_dropdown:
                  'px-2 py-1 pr-6 rounded-md border border-gray-200 text-sm font-semibold text-gray-900 bg-white hover:bg-gray-50 cursor-pointer transition-colors',
                years_dropdown:
                  'px-2 py-1 pr-6 rounded-md border border-gray-200 text-sm font-semibold text-gray-900 bg-white hover:bg-gray-50 cursor-pointer transition-colors',
                // Grid
                weeks: 'w-full',
                week: 'grid grid-cols-8',
                weekdays: 'grid grid-cols-8 mb-1',
                weekday: 'text-xs text-center text-gray-400 font-medium py-1',
                day: 'flex items-center justify-center',
                day_button:
                  'h-8 w-8 rounded-lg text-sm font-medium transition-colors text-gray-700 hover:bg-gray-100 focus:outline-none focus:ring-1 focus:ring-brand-400',
                today:
                  '[&>button]:border [&>button]:border-brand-400 [&>button]:text-brand-700 [&>button]:hover:bg-brand-50',
                selected:
                  '[&>button]:bg-brand-600 [&>button]:text-white [&>button]:hover:bg-brand-700',
                disabled:
                  '[&>button]:opacity-30 [&>button]:cursor-not-allowed [&>button]:hover:bg-transparent',
                outside: '[&>button]:opacity-30',
                week_number:
                  'text-xs text-gray-300 font-normal flex items-center justify-end pr-1 select-none',
                week_number_header: 'text-xs text-gray-300 font-medium text-right pr-1',
              }}
            />
          </div>
        </div>
      )}
    </div>
  )
}
