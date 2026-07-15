import { useEffect, useRef } from "react";
import { Button } from "./Button";

export interface InfiniteScrollSentinelProps {
  readonly hasNextPage: boolean;
  readonly isFetchingNextPage: boolean;
  readonly onLoadMore: () => void;
  readonly rootMargin?: string;
  readonly loadMoreLabel?: string;
  readonly loadingLabel?: string;
  readonly className?: string;
}

/** IntersectionObserver sentinel + fallback "Tải thêm" button. */
export function InfiniteScrollSentinel({
  hasNextPage,
  isFetchingNextPage,
  onLoadMore,
  rootMargin = "200px",
  loadMoreLabel = "Tải thêm",
  loadingLabel = "Đang tải…",
  className = "",
}: InfiniteScrollSentinelProps) {
  const ref = useRef<HTMLDivElement | null>(null);
  const busyRef = useRef(false);

  useEffect(() => {
    const el = ref.current;
    if (!el || !hasNextPage) return;

    const observer = new IntersectionObserver(
      (entries) => {
        const hit = entries.some((e) => e.isIntersecting);
        if (!hit || isFetchingNextPage || busyRef.current) return;
        busyRef.current = true;
        onLoadMore();
        window.setTimeout(() => {
          busyRef.current = false;
        }, 400);
      },
      { root: null, rootMargin, threshold: 0 },
    );
    observer.observe(el);
    return () => observer.disconnect();
  }, [hasNextPage, isFetchingNextPage, onLoadMore, rootMargin]);

  if (!hasNextPage && !isFetchingNextPage) {
    return <div ref={ref} className={className} aria-hidden="true" />;
  }

  return (
    <div ref={ref} className={`flex flex-col items-center gap-2 py-4 ${className}`}>
      {isFetchingNextPage ? (
        <p className="text-body-sm text-on-surface-variant">{loadingLabel}</p>
      ) : hasNextPage ? (
        <Button type="button" variant="outline" size="sm" onClick={onLoadMore}>
          {loadMoreLabel}
        </Button>
      ) : null}
    </div>
  );
}
