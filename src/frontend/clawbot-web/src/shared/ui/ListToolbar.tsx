import type { ReactNode } from "react";
import { Button } from "./Button";
import { Input } from "./Input";

export interface ListToolbarProps {
  readonly search?: string;
  readonly onSearchChange?: (value: string) => void;
  readonly searchPlaceholder?: string;
  readonly filters?: ReactNode;
  readonly shownCount?: number;
  readonly totalCount?: number | null;
  readonly onReset?: () => void;
  readonly resetLabel?: string;
  readonly actions?: ReactNode;
  readonly className?: string;
}

/** Shared filter/search bar with "Hiển thị X / Y" count. */
export function ListToolbar({
  search,
  onSearchChange,
  searchPlaceholder = "Tìm kiếm…",
  filters,
  shownCount,
  totalCount,
  onReset,
  resetLabel = "Đặt lại",
  actions,
  className = "",
}: ListToolbarProps) {
  const countLabel =
    typeof shownCount === "number"
      ? typeof totalCount === "number"
        ? `Hiển thị ${shownCount} / ${totalCount}`
        : `Hiển thị ${shownCount}`
      : typeof totalCount === "number"
        ? `Tổng ${totalCount}`
        : null;

  return (
    <div className={`flex flex-wrap items-center gap-3 ${className}`}>
      {onSearchChange ? (
        <div className="min-w-[200px] flex-1 max-w-md">
          <Input
            icon="search"
            value={search ?? ""}
            onChange={(e) => onSearchChange(e.target.value)}
            placeholder={searchPlaceholder}
            aria-label={searchPlaceholder}
          />
        </div>
      ) : null}
      {filters}
      {countLabel ? (
        <span className="text-body-sm text-on-surface-variant whitespace-nowrap">{countLabel}</span>
      ) : null}
      <div className="ml-auto flex items-center gap-2">
        {actions}
        {onReset ? (
          <Button type="button" variant="ghost" size="sm" onClick={onReset}>
            {resetLabel}
          </Button>
        ) : null}
      </div>
    </div>
  );
}
