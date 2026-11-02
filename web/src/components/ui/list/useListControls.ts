import { useMemo, useState } from 'react'
import type { ListToolbarProps } from './ListToolbar'

export interface ListFilter<T> {
  value: string
  label: string
  /** Leave out for the "Alle" chip. */
  match?: (item: T) => boolean
}

export interface ListSort<T> {
  value: string
  label: string
  compare: (a: T, b: T) => number
}

export interface ListControls<T> {
  /** Text the search field matches against, e.g. `${name} ${email}`. Leave out for no search. */
  searchText?: (item: T) => string
  searchPlaceholder?: string
  /** First one is the default. Over 5 options render as a select instead of chips. */
  filters?: ListFilter<T>[]
  filterLabel?: string
  /** First one is the default, unless `defaultSort` names another. */
  sorts?: ListSort<T>[]
  defaultSort?: { value: string; descending?: boolean }
  /** "1 medarbejder" / "12 medarbejdere" when nothing is filtered. */
  noun: { one: string; other: string }
}

const norm = (s: string) => s.toLocaleLowerCase('da')

/** Search, filter chips and sort shared by PersonList and DataTable. */
export function useListControls<T>(items: T[], controls: ListControls<T>) {
  const { searchText, filters, sorts } = controls
  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState(filters?.[0]?.value ?? '')
  const [sort, setSort] = useState(controls.defaultSort?.value ?? sorts?.[0]?.value ?? '')
  const [descending, setDescending] = useState(controls.defaultSort?.descending ?? false)

  const visible = useMemo(() => {
    const q = norm(search.trim())
    const match = filters?.find((f) => f.value === filter)?.match
    const hits = items.filter(
      (item) => (!match || match(item)) && (!q || !searchText || norm(searchText(item)).includes(q))
    )
    const compare = sorts?.find((s) => s.value === sort)?.compare
    if (compare) hits.sort((a, b) => (descending ? -compare(a, b) : compare(a, b)))
    return hits
  }, [items, search, filter, sort, descending, searchText, filters, sorts])

  const isFiltered = search.trim() !== '' || filter !== (filters?.[0]?.value ?? '')
  const total = items.length
  const countText =
    visible.length === total
      ? `${total} ${total === 1 ? controls.noun.one : controls.noun.other}`
      : `Viser ${visible.length} af ${total}`

  const hasToolbar = !!searchText || (filters?.length ?? 0) > 1 || (sorts?.length ?? 0) > 1

  function clear() {
    setSearch('')
    setFilter(filters?.[0]?.value ?? '')
  }

  /** Column header click: same column flips direction, a new column sorts ascending. */
  function toggleSort(value: string) {
    setDescending(value === sort ? !descending : false)
    setSort(value)
  }

  const toolbar: ListToolbarProps = {
    search: searchText ? search : undefined,
    onSearch: setSearch,
    searchPlaceholder: controls.searchPlaceholder ?? 'Søg',
    filters: filters?.map(({ value, label }) => ({ value, label })),
    filter,
    onFilter: setFilter,
    filterLabel: controls.filterLabel ?? 'Filtrer',
    sorts:
      sorts && sorts.length > 1 ? sorts.map(({ value, label }) => ({ value, label })) : undefined,
    sort,
    onSort: (v) => {
      setSort(v)
      // Back on the list's own default sort, its direction comes back too.
      setDescending(v === controls.defaultSort?.value ? !!controls.defaultSort.descending : false)
    },
    countText,
  }

  return { visible, total, isFiltered, hasToolbar, toolbar, clear, sort, descending, toggleSort }
}
