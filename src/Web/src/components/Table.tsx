import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'

export interface Column<TRow> {
  /** Stable key, also the mobile card's label. */
  header: string
  /** Cell contents. Kept a render function so money stays a formatted string. */
  cell: (row: TRow) => ReactNode
  /** Right-align and monospace — for quantities and money. */
  numeric?: boolean | undefined
}

export interface TableProps<TRow> {
  columns: Array<Column<TRow>>
  rows: TRow[]
  rowKey: (row: TRow) => string
  caption: string
  empty?: ReactNode | undefined
}

/**
 * One data set, two presentations. Below sm the table is hidden and the same rows render
 * as labelled cards — a horizontally scrolling table at 375px is unreadable, and the brief
 * asks for a usable mobile layout rather than a shrunken desktop one.
 */
export function Table<TRow>({ columns, rows, rowKey, caption, empty }: TableProps<TRow>) {
  const { t } = useTranslation('common')

  if (rows.length === 0) {
    // Every current caller passes its own `empty`; this is the defensive default for one
    // that does not.
    return <div className="text-mu px-1 py-6 text-[12.5px]">{empty ?? t('emptyTableFallback')}</div>
  }

  return (
    <>
      <table className="hidden w-full border-collapse text-[12.5px] sm:table">
        <caption className="sr-only">{caption}</caption>
        <thead>
          <tr className="border-bd border-b">
            {columns.map((column) => (
              <th
                key={column.header}
                scope="col"
                className={`text-mu px-2 py-2 text-[11.5px] font-medium tracking-[0.04em] uppercase ${
                  column.numeric ? 'text-right' : 'text-left'
                }`}
              >
                {column.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={rowKey(row)} className="border-bd/60 border-b last:border-0">
              {columns.map((column) => (
                <td
                  key={column.header}
                  className={`px-2 py-2.5 ${column.numeric ? 'text-right font-mono' : 'text-tx'}`}
                >
                  {column.cell(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>

      {/*
       * The two presentations are swapped with `display: none`, not with a media query in
       * JS, so exactly one of them is in the accessibility tree at any width — a screen
       * reader never reads the same row twice. In jsdom no CSS applies, so a test that
       * queries by text finds both; scope the query to the table or the list.
       */}
      <ul className="flex flex-col gap-2.5 sm:hidden" aria-label={caption}>
        {rows.map((row) => (
          <li key={rowKey(row)} className="border-bd bg-panel-2 flex flex-col gap-1.5 rounded-lg border p-3">
            {columns.map((column) => (
              <div key={column.header} className="flex items-baseline justify-between gap-3">
                <span className="text-mu text-[11.5px] tracking-[0.04em] uppercase">{column.header}</span>
                <span className={column.numeric ? 'font-mono text-[12.5px]' : 'text-tx text-[12.5px]'}>
                  {column.cell(row)}
                </span>
              </div>
            ))}
          </li>
        ))}
      </ul>
    </>
  )
}
