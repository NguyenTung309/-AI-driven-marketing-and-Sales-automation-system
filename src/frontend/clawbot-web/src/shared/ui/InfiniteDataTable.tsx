import type { ReactNode } from "react";
import type { Column } from "./DataTable";
import { InfiniteScrollSentinel } from "./InfiniteScrollSentinel";

export interface InfiniteDataTableProps<T> {
  readonly columns: readonly Column<T>[];
  readonly rows: readonly T[];
  readonly rowKey: (row: T) => string;
  readonly empty?: ReactNode;
  readonly hasNextPage: boolean;
  readonly isFetchingNextPage: boolean;
  readonly onLoadMore: () => void;
  readonly total?: number | null;
  readonly footerLabel?: string;
  readonly className?: string;
}

/** DataTable + sticky header shell + infinite sentinel + total footer. */
export function InfiniteDataTable<T>({
  columns,
  rows,
  rowKey,
  empty,
  hasNextPage,
  isFetchingNextPage,
  onLoadMore,
  total,
  footerLabel,
  className = "",
}: InfiniteDataTableProps<T>) {
  const footer =
    footerLabel ??
    (typeof total === "number" ? `Tổng ${total} · đã tải ${rows.length}` : `Đã tải ${rows.length}`);

  return (
    <div className={`flex flex-col gap-0 ${className}`}>
      <div className="overflow-x-auto border border-outline rounded-lg bg-surface-container-lowest max-h-[70vh] overflow-y-auto">
        <table className="w-full text-left">
          <thead className="sticky top-0 z-10">
            <tr className="bg-surface border-b border-outline">
              {columns.map((c) => (
                <th
                  key={c.key}
                  className={`px-4 py-3 text-label-caps uppercase text-on-surface-variant ${c.className ?? ""}`}
                >
                  {c.header}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr>
                <td colSpan={columns.length} className="px-4 py-8 text-center text-body-md text-on-surface-variant">
                  {empty ?? "Không có dữ liệu"}
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
      <div className="flex items-center justify-between px-1 pt-2">
        <span className="text-body-sm text-on-surface-variant">{footer}</span>
      </div>
      <InfiniteScrollSentinel
        hasNextPage={hasNextPage}
        isFetchingNextPage={isFetchingNextPage}
        onLoadMore={onLoadMore}
      />
    </div>
  );
}
