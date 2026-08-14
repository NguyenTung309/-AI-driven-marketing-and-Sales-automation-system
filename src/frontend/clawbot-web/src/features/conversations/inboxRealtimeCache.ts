import type { InfiniteData } from "@tanstack/react-query";
import type {
  ConversationCursorPage,
  ConversationListItem,
  ConversationListResponse,
  ConversationStatus,
  InboxMessageEvent,
} from "@/shared/api/inbox";

export type ConversationListCache =
  | InfiniteData<ConversationCursorPage | ConversationListResponse>
  | ConversationListResponse
  | ConversationCursorPage;

/** Bộ lọc server đang gắn vào từng query danh sách, đọc ra từ queryKey. */
export interface ConversationListFilters {
  readonly status?: string;
  readonly inboxId?: string;
  readonly assignedTo?: string;
  readonly platform?: string;
  readonly q?: string;
}

export interface ReconcileResult {
  readonly cache: ConversationListCache | undefined;
  /** Hội thoại có mặt trong các trang đã tải hay không. */
  readonly found: boolean;
  /** Đã đổi trạng thái (vd resolved -> open khi khách nhắn lại). */
  readonly statusChanged: boolean;
  /** Query này lẽ ra phải chứa hội thoại nhưng chưa có -> cần server trả về bản ghi đầy đủ. */
  readonly needsRefresh: boolean;
}

/** Giữ đúng cách API cắt nội dung xem trước để bản vá lạc quan không lệch với lần tải sau. */
const PREVIEW_MAX_LENGTH = 140;

export function previewOf(content: string): string {
  return content.length <= PREVIEW_MAX_LENGTH ? content : `${content.slice(0, PREVIEW_MAX_LENGTH)}...`;
}

function isInfinite(
  cache: ConversationListCache,
): cache is InfiniteData<ConversationCursorPage | ConversationListResponse> {
  return "pages" in cache && Array.isArray(cache.pages);
}

function pagesOf(cache: ConversationListCache): readonly (ConversationCursorPage | ConversationListResponse)[] {
  return isInfinite(cache) ? cache.pages : [cache as ConversationCursorPage];
}

/**
 * Ghép lại cache theo đúng số phần tử của từng trang ban đầu.
 * Giữ nguyên nextCursor/total/pageParams để lần fetchNextPage sau không nhảy cóc hay lặp bản ghi.
 */
function repartition(
  cache: ConversationListCache,
  items: readonly ConversationListItem[],
): ConversationListCache {
  if (!isInfinite(cache)) {
    return { ...(cache as ConversationCursorPage), items };
  }

  let offset = 0;
  const pages = cache.pages.map((page) => {
    const size = page.items.length;
    const slice = items.slice(offset, offset + size);
    offset += size;
    return { ...page, items: slice };
  });
  return { ...cache, pages };
}

/** Tin nhắn tới sau mới được đẩy hội thoại lên đầu; sự kiện SignalR đến trễ không được ghi đè dữ liệu mới hơn. */
function isNewer(eventSentAt: string, cachedLastMessageAt: string | null): boolean {
  if (!cachedLastMessageAt) return true;
  const next = Date.parse(eventSentAt);
  const current = Date.parse(cachedLastMessageAt);
  if (Number.isNaN(next) || Number.isNaN(current)) return true;
  return next >= current;
}

/**
 * Hội thoại vắng mặt trong cache có thể vì query đang lọc và nó thật sự không thuộc danh sách này.
 * Chỉ những trường biết chắc từ sự kiện mới được dùng để loại trừ; lọc theo từ khoá thì không thể
 * kết luận nên vẫn phải hỏi lại server.
 */
function mayBelong(
  filters: ConversationListFilters,
  evt: InboxMessageEvent,
  nextStatus: ConversationStatus | null,
): boolean {
  if (filters.inboxId && filters.inboxId !== (evt.inboxId ?? "")) return false;
  if (filters.assignedTo && filters.assignedTo !== (evt.assignedTo ?? "")) return false;
  if (filters.status && nextStatus && filters.status !== nextStatus) return false;
  return true;
}

/**
 * Cập nhật một cache danh sách hội thoại theo sự kiện tin nhắn mới:
 * vá nội dung xem trước, đẩy hội thoại lên đầu, và báo lại khi cần gọi server.
 */
export function reconcileConversationList(
  cache: ConversationListCache | undefined,
  evt: InboxMessageEvent,
  filters: ConversationListFilters,
): ReconcileResult {
  const nextStatus = evt.conversationStatus ?? null;
  if (!cache) {
    return { cache, found: false, statusChanged: false, needsRefresh: false };
  }

  const flat = pagesOf(cache).flatMap((page) => page.items);
  const existing = flat.find((item) => item.id === evt.conversationId);

  if (!existing) {
    return {
      cache,
      found: false,
      statusChanged: false,
      needsRefresh: mayBelong(filters, evt, nextStatus),
    };
  }

  const statusChanged = Boolean(nextStatus) && nextStatus !== existing.status;
  if (!isNewer(evt.sentAt, existing.lastMessageAt)) {
    // Sự kiện cũ hơn dữ liệu đang có: chỉ sửa trạng thái nếu thật sự đổi, không đụng thứ tự.
    if (!statusChanged) {
      return { cache, found: true, statusChanged: false, needsRefresh: false };
    }
    const patchedInPlace = flat.map((item) =>
      item.id === evt.conversationId ? { ...item, status: nextStatus as ConversationStatus } : item,
    );
    return {
      cache: repartition(cache, patchedInPlace),
      found: true,
      statusChanged: true,
      needsRefresh: false,
    };
  }

  const patched: ConversationListItem = {
    ...existing,
    lastMessageAt: evt.sentAt,
    lastMessagePreview: previewOf(evt.content),
    ...(nextStatus ? { status: nextStatus } : {}),
  };

  // Xoá mọi bản sao rồi chèn lên đầu: hội thoại có thể nằm ở trang 2 hoặc lặp giữa các trang cũ.
  const withoutTarget = flat.filter((item) => item.id !== evt.conversationId);
  const reordered = [patched, ...withoutTarget];

  return {
    cache: repartition(cache, reordered),
    found: true,
    statusChanged,
    needsRefresh: false,
  };
}

/** Đọc bộ lọc server ra khỏi queryKey dạng ["inbox", "conversations", { ... }]. */
export function filtersFromQueryKey(queryKey: readonly unknown[]): ConversationListFilters {
  const last = queryKey[queryKey.length - 1];
  if (!last || typeof last !== "object" || Array.isArray(last)) return {};
  const raw = last as Record<string, unknown>;
  // "all" nằm trong queryKey nhưng khi gọi API lại bỏ trống — coi như không lọc.
  const pick = (key: string): string | undefined => {
    const value = raw[key];
    if (typeof value !== "string" || !value || value === "all") return undefined;
    return value;
  };
  return {
    status: pick("status"),
    inboxId: pick("inboxId"),
    assignedTo: pick("assignedTo"),
    platform: pick("platform"),
    q: pick("q"),
  };
}
