import { useRef, useCallback } from 'react'

export interface ColumnDef {
  key: string
  label: string
  required?: boolean
  placeholder?: string
  type?: 'text' | 'email' | 'tel' | 'number'
}

export type GridRow = Record<string, string>

interface PasteGridProps {
  columns: ColumnDef[]
  rows: GridRow[]
  onChange: (rows: GridRow[]) => void
  minRows?: number
}

function emptyRow(columns: ColumnDef[]): GridRow {
  return Object.fromEntries(columns.map((c) => [c.key, '']))
}

function ensureMinRows(rows: GridRow[], columns: ColumnDef[], min: number): GridRow[] {
  if (rows.length >= min) return rows
  const extra = Array.from({ length: min - rows.length }, () => emptyRow(columns))
  return [...rows, ...extra]
}

export default function PasteGrid({ columns, rows, onChange, minRows = 10 }: PasteGridProps) {
  const focusedCell = useRef<{ row: number; col: number } | null>(null)
  const displayRows = ensureMinRows(rows, columns, minRows)

  const updateCell = useCallback(
    (rowIdx: number, colKey: string, value: string) => {
      const next = [...displayRows.map((r) => ({ ...r }))]
      next[rowIdx][colKey] = value
      // Trim trailing empty rows but keep at least minRows
      let last = next.length - 1
      while (last > minRows - 1 && columns.every((c) => !next[last][c.key])) {
        last--
      }
      onChange(next.slice(0, last + 1))
    },
    [displayRows, columns, minRows, onChange]
  )

  const handlePaste = useCallback(
    (e: React.ClipboardEvent<HTMLInputElement>, startRow: number, startColIdx: number) => {
      e.preventDefault()
      const text = e.clipboardData.getData('text/plain')
      if (!text) return

      const pasteRows = text.split(/\r?\n/).filter((line, i, arr) => {
        // Drop trailing empty line from Excel
        if (i === arr.length - 1 && !line) return false
        return true
      })

      const next = [...displayRows.map((r) => ({ ...r }))]

      pasteRows.forEach((line, rowOffset) => {
        const cells = line.split('\t')
        const targetRow = startRow + rowOffset
        // Expand grid if needed
        while (next.length <= targetRow) {
          next.push(emptyRow(columns))
        }
        cells.forEach((cell, colOffset) => {
          const colIdx = startColIdx + colOffset
          if (colIdx >= columns.length) return
          next[targetRow][columns[colIdx].key] = cell
        })
      })

      onChange(next)
    },
    [displayRows, columns, onChange]
  )

  return (
    <div className="overflow-x-auto rounded-lg border border-gray-200">
      <table className="w-full text-sm border-collapse">
        <thead>
          <tr className="bg-gray-50 border-b border-gray-200">
            <th className="px-3 py-2 text-left text-xs font-semibold text-gray-400 w-8 select-none">
              #
            </th>
            {columns.map((col) => (
              <th
                key={col.key}
                className="px-3 py-2 text-left text-xs font-semibold text-gray-600 uppercase tracking-wide whitespace-nowrap"
              >
                {col.label}
                {col.required && <span className="text-red-500 ml-0.5">*</span>}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {displayRows.map((row, rowIdx) => {
            const isRequiredMissing = columns
              .filter((c) => c.required)
              .some((c) => !row[c.key]?.trim())
            const hasAnyValue = columns.some((c) => row[c.key]?.trim())
            const showError = hasAnyValue && isRequiredMissing

            return (
              <tr
                key={rowIdx}
                className={`border-b border-gray-100 last:border-0 ${showError ? 'bg-red-50' : 'hover:bg-gray-50'}`}
              >
                <td className="px-3 py-1 text-xs text-gray-400 select-none w-8">{rowIdx + 1}</td>
                {columns.map((col, colIdx) => (
                  <td key={col.key} className="px-1 py-0.5">
                    <input
                      type={col.type ?? 'text'}
                      value={row[col.key] ?? ''}
                      placeholder={col.placeholder ?? col.label}
                      aria-label={`${col.label}, række ${rowIdx + 1}`}
                      aria-invalid={showError && col.required && !row[col.key]?.trim()}
                      data-testid={`paste-grid-${rowIdx}-${col.key}`}
                      onFocus={() => {
                        focusedCell.current = { row: rowIdx, col: colIdx }
                      }}
                      onChange={(e) => updateCell(rowIdx, col.key, e.target.value)}
                      onPaste={(e) => handlePaste(e, rowIdx, colIdx)}
                      className={`w-full px-2 py-1.5 text-sm bg-transparent border border-transparent rounded focus:outline-none focus:border-brand-400 focus:bg-white transition-colors ${
                        showError && col.required && !row[col.key]?.trim()
                          ? 'border-red-300 bg-red-50'
                          : ''
                      }`}
                    />
                  </td>
                ))}
              </tr>
            )
          })}
        </tbody>
      </table>

      <div className="px-4 py-2 border-t border-gray-100 bg-gray-50">
        <button
          type="button"
          onClick={() => onChange([...displayRows, emptyRow(columns)])}
          className="text-xs text-brand-600 hover:text-brand-700 font-medium"
        >
          + Tilføj række
        </button>
      </div>
    </div>
  )
}
