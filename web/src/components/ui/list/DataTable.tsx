import { useMemo } from 'react'
import type React from 'react'
import { useMediaQuery } from '../../../hooks/useMediaQuery'
import { SortAscIcon, SortIcon } from '../icons'
import { ListToolbar } from './ListToolbar'
import { ActionButtons, EmptyList, NoMatches, type RowAction, RowActionControl } from './parts'
import { type ListControls, type ListSort, useListControls } from './useListControls'

export interface Column<T> {
  key: string
  header: string
  cell: (row: T) => React.ReactNode
  /** Makes the column sortable: header click on laptop, the toolbar's sort select on phone. */
  sortValue?: (row: T) => string | number
  align?: 'left' | 'right'
}

interface DataTableProps<T> extends Omit<ListControls<T>, 'sorts'> {
  items: T[] | undefined
  rowKey: (row: T) => string
  /** The first column is the card title on phone. */
  columns: Column<T>[]
  actions?: (row: T) => RowAction[]
  /** Clicking the row (outside its buttons) does this, e.g. open a folder. */
  onRowClick?: (row: T) => void
  /** Rows onRowClick applies to. Defaults to every row. */
  isRowClickable?: (row: T) => boolean
  isLoading?: boolean
  /** Shown when there are no rows at all. */
  empty: React.ReactNode
  /** Accessible name for the table. */
  caption: string
  testId?: string
  rowTestId?: (row: T) => string
}

function compareValues(a: string | number, b: string | number) {
  if (typeof a === 'number' && typeof b === 'number') return a - b
  return String(a).localeCompare(String(b), 'da', { numeric: true, sensitivity: 'base' })
}

/**
 * Lists where users compare columns (ferieindmeldinger, files, payments). One column
 * definition renders a table on laptop and label/value cards on phone; nothing is
 * hidden on phone. For people, use PersonList.
 */
export function DataTable<T>({
  items,
  rowKey,
  columns,
  actions,
  onRowClick,
  isRowClickable,
  isLoading,
  empty,
  caption,
  testId,
  rowTestId,
  ...controls
}: DataTableProps<T>) {
  const sorts = useMemo<ListSort<T>[]>(
    () =>
      columns
        .filter((c) => c.sortValue)
        .map((c) => ({
          value: c.key,
          label: c.header,
          compare: (a, b) => compareValues(c.sortValue!(a), c.sortValue!(b)),
        })),
    [columns]
  )
  const list = useListControls(items ?? [], { ...controls, sorts })
  // Render one layout, not both hidden by CSS, so each row and data-testid exists once.
  const isWide = useMediaQuery('(min-width: 768px)')

  if (isLoading) {
    return (
      <div
        aria-busy="true"
        className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100"
      >
        {Array.from({ length: 4 }).map((_, i) => (
          <div key={i} className="px-4 py-4 animate-pulse">
            <div className="h-4 w-40 bg-gray-200 rounded" />
          </div>
        ))}
      </div>
    )
  }

  if (list.total === 0) return <EmptyList>{empty}</EmptyList>

  const [titleCol, ...restCols] = columns
  const clickHandler = (row: T) =>
    onRowClick && (isRowClickable?.(row) ?? true) ? () => onRowClick(row) : undefined

  return (
    <div className="space-y-2" data-testid={testId}>
      {list.hasToolbar && (
        <ListToolbar {...list.toolbar} sorts={isWide ? undefined : list.toolbar.sorts} />
      )}
      {list.visible.length === 0 ? (
        <NoMatches onClear={list.clear} />
      ) : !isWide ? (
        <ul className="space-y-2">
          {list.visible.map((row) => (
            <li
              key={rowKey(row)}
              data-testid={rowTestId?.(row)}
              onClick={clickHandler(row)}
              className={`bg-white rounded-xl border border-gray-200 p-4 ${clickHandler(row) ? 'cursor-pointer' : ''}`}
            >
              <div className="font-semibold text-gray-900">{titleCol.cell(row)}</div>
              {restCols.length > 0 && (
                <dl className="mt-2 grid grid-cols-[7rem_1fr] gap-x-3 gap-y-1 text-sm">
                  {restCols.map((c) => (
                    <div key={c.key} className="contents">
                      <dt className="text-gray-600">{c.header}</dt>
                      <dd className="min-w-0 break-words text-gray-900">{c.cell(row)}</dd>
                    </div>
                  ))}
                </dl>
              )}
              {actions && <ActionButtons actions={actions(row)} />}
            </li>
          ))}
        </ul>
      ) : (
        <div className="bg-white rounded-xl border border-gray-200 overflow-x-auto">
          <table className="w-full text-sm">
            <caption className="sr-only">{caption}</caption>
            <thead className="bg-gray-50 border-b border-gray-200">
              <tr>
                {columns.map((c) => {
                  const active = list.sort === c.key
                  const ariaSort = active
                    ? list.descending
                      ? 'descending'
                      : 'ascending'
                    : undefined
                  return (
                    <th
                      key={c.key}
                      scope="col"
                      aria-sort={ariaSort}
                      className={`text-xs font-bold text-gray-700 uppercase tracking-wide ${
                        c.align === 'right' ? 'text-right' : 'text-left'
                      }`}
                    >
                      {c.sortValue ? (
                        <button
                          type="button"
                          onClick={() => list.toggleSort(c.key)}
                          className={`flex items-center gap-1 w-full px-4 py-3 uppercase tracking-wide hover:bg-gray-100 ${
                            c.align === 'right' ? 'justify-end' : ''
                          }`}
                        >
                          {c.header}
                          {active ? (
                            <SortAscIcon
                              className={`w-4 h-4 ${list.descending ? 'rotate-180' : ''}`}
                            />
                          ) : (
                            <SortIcon className="w-4 h-4 text-gray-500" />
                          )}
                        </button>
                      ) : (
                        <span className="block px-4 py-3">{c.header}</span>
                      )}
                    </th>
                  )
                })}
                {actions && (
                  <th scope="col" className="px-4 py-3">
                    <span className="sr-only">Handlinger</span>
                  </th>
                )}
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {list.visible.map((row) => (
                <tr
                  key={rowKey(row)}
                  data-testid={rowTestId?.(row)}
                  onClick={clickHandler(row)}
                  className={`hover:bg-gray-50 ${clickHandler(row) ? 'cursor-pointer' : ''}`}
                >
                  {columns.map((c, i) => (
                    <td
                      key={c.key}
                      className={`px-4 py-3 ${c.align === 'right' ? 'text-right' : ''} ${
                        i === 0 ? 'font-semibold text-gray-900' : 'text-gray-700'
                      }`}
                    >
                      {c.cell(row)}
                    </td>
                  ))}
                  {actions && (
                    <td className="px-4 py-2 text-right">
                      <RowActionLinks actions={actions(row)} />
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

/** Compact text buttons for the laptop table's last column. */
function RowActionLinks({ actions }: { actions: RowAction[] }) {
  return (
    <div className="flex items-center justify-end gap-1">
      {actions.map((a) => (
        <RowActionControl
          key={a.label}
          action={a}
          className={`h-10 px-3 rounded-lg font-semibold whitespace-nowrap disabled:opacity-50 disabled:cursor-not-allowed ${
            a.variant === 'danger'
              ? 'text-red-700 hover:bg-red-50'
              : 'text-brand-700 hover:bg-brand-50'
          }`}
        />
      ))}
    </div>
  )
}
