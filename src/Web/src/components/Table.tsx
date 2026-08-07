import type { ReactNode } from 'react'

export interface Column<TRow> {
  header: string
  cell: (row: TRow) => ReactNode
  numeric?: boolean | undefined
}

export interface TableProps<TRow> {
  columns: Array<Column<TRow>>
  rows: TRow[]
  rowKey: (row: TRow) => string
  caption: string
  empty: ReactNode
}

export function Table<TRow>({ columns, rows, rowKey, caption, empty }: TableProps<TRow>) {
  if (rows.length === 0) {
    return <div className="text-mu px-1 py-6 text-[12.5px]">{empty}</div>
  }

  return (
    <>
      <div className="hidden overflow-x-auto sm:block">
        <table className="w-full min-w-[600px] border-collapse text-[12.5px]">
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
      </div>

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
