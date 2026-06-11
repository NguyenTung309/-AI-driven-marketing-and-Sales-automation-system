import type { ReactNode } from "react";

export interface Column<T> {
  readonly key: string;
  readonly header: string;
  readonly render: (row: T) => ReactNode;
  readonly className?: string;
}

export interface DataTableProps<T> {
  readonly columns: readonly Column<T>[];
  readonly rows: readonly T[];
  readonly rowKey: (row: T) => string;
  readonly empty?: ReactNode;
}

// Border-bottom rows, uppercase label-caps header on canvas fill, hover highlight.
export function DataTable<T>({ columns, rows, rowKey, empty = "Không có dữ liệu" }: DataTableProps<T>) {
  return (
    <div className="overflow-x-auto border border-outline rounded-lg bg-surface-container-lowest">
      <table className="w-full text-left">
        <thead>
          <tr className="bg-surface border-b border-outline">
            {columns.map((c) => (
              <th key={c.key} className={`px-4 py-3 text-label-caps uppercase text-on-surface-variant ${c.className ?? ""}`}>
                {c.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 ? (
            <tr>
              <td colSpan={columns.length} className="px-4 py-8 text-center text-body-md text-on-surface-variant">
                {empty}
              </td>
            </tr>
          ) : (
            rows.map((row) => (
              <tr key={rowKey(row)} className="border-b border-surface-variant last:border-0 hover:bg-surface">
                {columns.map((c) => (
                  <td key={c.key} className={`px-4 py-3 text-body-md ${c.className ?? ""}`}>
                    {c.render(row)}
                  </td>
                ))}
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}
