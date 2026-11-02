import { SearchIcon, SortAscIcon } from '../icons'
import { Select } from '../select'

interface Option {
  value: string
  label: string
}

export interface ListToolbarProps {
  /** Undefined hides the search field. */
  search?: string
  onSearch: (value: string) => void
  searchPlaceholder: string
  filters?: Option[]
  filter: string
  onFilter: (value: string) => void
  filterLabel: string
  sorts?: Option[]
  sort: string
  onSort: (value: string) => void
  /** Phones have no column headers to click, so the direction gets its own button. */
  descending?: boolean
  onFlipSort?: () => void
  countText: string
}

/** Search, filter chips, "Viser N af M" and sort. Same in every list. */
export function ListToolbar({
  search,
  onSearch,
  searchPlaceholder,
  filters,
  filter,
  onFilter,
  filterLabel,
  sorts,
  sort,
  onSort,
  descending,
  onFlipSort,
  countText,
}: ListToolbarProps) {
  // A lone "Alle" option filters nothing, so show no filter at all.
  const chips = filters && filters.length > 1 && filters.length <= 5
  const filterSelect = filters && filters.length > 5
  return (
    <div className="space-y-2">
      {search !== undefined && (
        <label className="relative block">
          <span className="sr-only">Søg</span>
          <SearchIcon className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-500" />
          <input
            type="search"
            value={search}
            onChange={(e) => onSearch(e.target.value)}
            placeholder={searchPlaceholder}
            data-testid="list-search"
            className="w-full h-11 pl-10 pr-3 rounded-lg border border-gray-300 bg-white text-base placeholder:text-gray-500 focus:outline-none focus:ring-2 focus:ring-brand-500"
          />
        </label>
      )}
      {chips && (
        <fieldset className="flex gap-2 overflow-x-auto pb-1 [scrollbar-width:none]">
          <legend className="sr-only">{filterLabel}</legend>
          {filters.map((f) => (
            <button
              key={f.value}
              type="button"
              aria-pressed={f.value === filter}
              onClick={() => onFilter(f.value)}
              className="shrink-0 h-11 px-4 rounded-full border border-gray-300 bg-white text-sm font-semibold text-gray-800 hover:bg-gray-50 aria-pressed:bg-gray-900 aria-pressed:text-white aria-pressed:border-gray-900"
            >
              {f.label}
            </button>
          ))}
        </fieldset>
      )}
      <div className="flex flex-wrap items-end gap-3">
        <p className="flex-1 min-w-[8rem] text-sm text-gray-700" aria-live="polite">
          {countText}
        </p>
        {filterSelect && (
          <Select
            label={filterLabel}
            hideLabel
            options={filters}
            value={filter}
            onChange={onFilter}
            className="w-44"
          />
        )}
        {sorts && (
          <Select
            label="Sortér"
            hideLabel
            options={sorts}
            value={sort}
            onChange={onSort}
            className="w-44"
          />
        )}
        {sorts && onFlipSort && (
          <button
            type="button"
            onClick={onFlipSort}
            aria-label={
              descending
                ? 'Sorteret faldende. Vend rækkefølgen'
                : 'Sorteret stigende. Vend rækkefølgen'
            }
            data-testid="list-sort-direction"
            className="w-11 h-11 shrink-0 rounded-lg border border-gray-300 bg-white flex items-center justify-center text-gray-800 hover:bg-gray-50"
          >
            <SortAscIcon className={`w-5 h-5 ${descending ? 'rotate-180' : ''}`} />
          </button>
        )}
      </div>
    </div>
  )
}
