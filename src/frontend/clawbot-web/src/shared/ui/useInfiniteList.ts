import {
  useInfiniteQuery,
  type InfiniteData,
  type QueryKey,
  type UseInfiniteQueryOptions,
  type UseInfiniteQueryResult,
} from "@tanstack/react-query";
import { useMemo } from "react";

export interface CursorPageShape<T> {
  readonly items: readonly T[];
  readonly nextCursor?: string | null;
  readonly total?: number | null;
}

export interface OffsetPageShape<T> {
  readonly items: readonly T[];
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
}

export type ListPageShape<T> = CursorPageShape<T> | OffsetPageShape<T>;

function isOffsetPage<T>(page: ListPageShape<T>): page is OffsetPageShape<T> {
  return "page" in page && typeof (page as OffsetPageShape<T>).page === "number";
}

function getNextPageParam<T>(lastPage: ListPageShape<T>, allPages: ListPageShape<T>[]): unknown {
  if (isOffsetPage(lastPage)) {
    const loaded = allPages.reduce((sum, p) => sum + p.items.length, 0);
    if (loaded >= lastPage.total) return undefined;
    return lastPage.page + 1;
  }
  return lastPage.nextCursor ?? undefined;
}

export interface UseInfiniteListOptions<T, TPage extends ListPageShape<T>> {
  readonly queryKey: QueryKey;
  readonly queryFn: (pageParam: unknown) => Promise<TPage>;
  readonly initialPageParam?: unknown;
  readonly enabled?: boolean;
  readonly staleTime?: number;
  readonly refetchOnWindowFocus?: boolean;
  /** Poll interval ms, or a function of the infinite data (e.g. poll only while items are active). */
  readonly refetchInterval?:
    | number
    | false
    | ((query: { state: { data: InfiniteData<TPage> | undefined } }) => number | false | undefined);
}

export interface UseInfiniteListResult<T> {
  readonly items: T[];
  readonly total: number | null;
  readonly hasNextPage: boolean;
  readonly isFetchingNextPage: boolean;
  readonly isLoading: boolean;
  readonly isError: boolean;
  readonly error: Error | null;
  readonly fetchNextPage: () => void;
  readonly refetch: () => void;
  readonly query: UseInfiniteQueryResult<InfiniteData<ListPageShape<T>>, Error>;
}

export function useInfiniteList<T, TPage extends ListPageShape<T> = ListPageShape<T>>(
  options: UseInfiniteListOptions<T, TPage>,
): UseInfiniteListResult<T> {
  const query = useInfiniteQuery({
    queryKey: options.queryKey,
    queryFn: ({ pageParam }) => options.queryFn(pageParam),
    initialPageParam: options.initialPageParam ?? null,
    getNextPageParam: (lastPage, allPages) => getNextPageParam(lastPage, allPages),
    enabled: options.enabled,
    staleTime: options.staleTime,
    refetchOnWindowFocus: options.refetchOnWindowFocus,
    refetchInterval: options.refetchInterval as never,
  } as UseInfiniteQueryOptions<TPage, Error, InfiniteData<TPage>, QueryKey, unknown>);

  const items = useMemo(
    () => (query.data?.pages ?? []).flatMap((p) => [...p.items]) as T[],
    [query.data],
  );

  const total = useMemo(() => {
    const pages = query.data?.pages ?? [];
    for (const p of pages) {
      if (typeof p.total === "number") return p.total;
    }
    return null;
  }, [query.data]);

  return {
    items,
    total,
    hasNextPage: Boolean(query.hasNextPage),
    isFetchingNextPage: query.isFetchingNextPage,
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
    fetchNextPage: () => {
      void query.fetchNextPage();
    },
    refetch: () => {
      void query.refetch();
    },
    query: query as UseInfiniteQueryResult<InfiniteData<ListPageShape<T>>, Error>,
  };
}
