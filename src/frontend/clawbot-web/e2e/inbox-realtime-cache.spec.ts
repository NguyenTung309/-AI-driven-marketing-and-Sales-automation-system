import { expect, test } from "@playwright/test";
import type { InfiniteData } from "@tanstack/react-query";
import {
  filtersFromQueryKey,
  reconcileConversationList,
  type ConversationListCache,
} from "../src/features/conversations/inboxRealtimeCache";
import type {
  ConversationCursorPage,
  ConversationListItem,
  InboxMessageEvent,
} from "../src/shared/api/inbox";

/**
 * Kiểm tra logic thuần của cache danh sách hội thoại realtime — không cần trình duyệt.
 * Chạy bằng Playwright vì repo chưa có runner unit test cho frontend.
 */

function item(id: string, overrides: Partial<ConversationListItem> = {}): ConversationListItem {
  return {
    id,
    platform: "facebook",
    externalThreadId: `thread-${id}`,
    status: "open",
    contactId: null,
    contactDisplayName: `Khách ${id}`,
    contactAvatarUrl: null,
    inboxId: "inbox-a",
    inboxName: "Inbox A",
    inboxAvatarUrl: null,
    assignedTo: null,
    lastMessageAt: "2026-08-14T08:00:00Z",
    lastMessagePreview: "cũ",
    rowVersion: null,
    unreadCount: 0,
    aiAutoReplyEnabled: true,
    ...overrides,
  };
}

function infiniteCache(
  pages: readonly (readonly ConversationListItem[])[],
): InfiniteData<ConversationCursorPage> {
  return {
    pages: pages.map((items, index) => ({
      items,
      nextCursor: index === pages.length - 1 ? null : `cursor-${index}`,
      total: 75,
    })),
    pageParams: pages.map((_, index) => (index === 0 ? null : `cursor-${index - 1}`)),
  };
}

function messageEvent(overrides: Partial<InboxMessageEvent> = {}): InboxMessageEvent {
  return {
    conversationId: "c-5",
    messageId: "m-1",
    direction: "in",
    senderType: "customer",
    content: "Tin mới nhất",
    contentType: "text",
    sentAt: "2026-08-14T09:00:00Z",
    inboxId: "inbox-a",
    assignedTo: null,
    conversationStatus: "open",
    ...overrides,
  };
}

function flatten(cache: ConversationListCache | undefined): readonly ConversationListItem[] {
  const infinite = cache as InfiniteData<ConversationCursorPage>;
  return infinite.pages.flatMap((page) => page.items);
}

test.describe("reconcileConversationList", () => {
  test("đẩy hội thoại ở trang 2 lên đầu mà không cần gọi lại server", () => {
    const cache = infiniteCache([
      [item("c-1"), item("c-2")],
      [item("c-5"), item("c-6")],
    ]);
    const snapshot = JSON.stringify(cache);

    const result = reconcileConversationList(cache, messageEvent(), {});

    expect(result.found).toBe(true);
    expect(result.needsRefresh).toBe(false);
    expect(result.statusChanged).toBe(false);

    const items = flatten(result.cache);
    expect(items.map((entry) => entry.id)).toEqual(["c-5", "c-1", "c-2", "c-6"]);
    expect(items[0]!.lastMessageAt).toBe("2026-08-14T09:00:00Z");
    expect(items[0]!.lastMessagePreview).toBe("Tin mới nhất");
    expect(items.filter((entry) => entry.id === "c-5")).toHaveLength(1);

    // Giữ nguyên khung phân trang để fetchNextPage sau đó không nhảy cóc hay lặp bản ghi.
    const pages = (result.cache as InfiniteData<ConversationCursorPage>).pages;
    expect(pages.map((page) => page.items.length)).toEqual([2, 2]);
    expect(pages.map((page) => page.nextCursor)).toEqual(["cursor-0", null]);
    expect(pages.every((page) => page.total === 75)).toBe(true);
    expect((result.cache as InfiniteData<ConversationCursorPage>).pageParams).toEqual([null, "cursor-0"]);

    // Không được sửa cache gốc.
    expect(JSON.stringify(cache)).toBe(snapshot);
  });

  test("hội thoại chưa có trong cache thì yêu cầu tải lại thay vì dựng bản ghi thiếu", () => {
    const cache = infiniteCache([[item("c-1")]]);

    const result = reconcileConversationList(cache, messageEvent({ conversationId: "c-99" }), {});

    expect(result.found).toBe(false);
    expect(result.needsRefresh).toBe(true);
    expect(flatten(result.cache).map((entry) => entry.id)).toEqual(["c-1"]);
  });

  test("lệch bộ lọc inbox thì không tải lại", () => {
    const cache = infiniteCache([[item("c-1")]]);

    const result = reconcileConversationList(cache, messageEvent({ conversationId: "c-99" }), {
      inboxId: "inbox-b",
    });

    expect(result.needsRefresh).toBe(false);
  });

  test("lệch bộ lọc người phụ trách thì không tải lại", () => {
    const cache = infiniteCache([[item("c-1")]]);

    const result = reconcileConversationList(
      cache,
      messageEvent({ conversationId: "c-99", assignedTo: "user-1" }),
      { assignedTo: "user-2" },
    );

    expect(result.needsRefresh).toBe(false);
  });

  for (const previous of ["resolved", "snoozed"] as const) {
    test(`hội thoại ${previous} được mở lại thì lên đầu và báo tải lại`, () => {
      const cache = infiniteCache([[item("c-1"), item("c-5", { status: previous })]]);

      const result = reconcileConversationList(cache, messageEvent(), {});

      const items = flatten(result.cache);
      expect(items[0]!.id).toBe("c-5");
      expect(items[0]!.status).toBe("open");
      expect(result.statusChanged).toBe(true);
      expect(items.filter((entry) => entry.id === "c-5")).toHaveLength(1);
    });
  }

  test("sự kiện đến trễ không ghi đè dữ liệu mới hơn", () => {
    const cache = infiniteCache([[item("c-1"), item("c-5", { lastMessageAt: "2026-08-14T10:00:00Z", lastMessagePreview: "mới" })]]);

    const result = reconcileConversationList(cache, messageEvent({ sentAt: "2026-08-14T09:00:00Z" }), {});

    const items = flatten(result.cache);
    expect(items.map((entry) => entry.id)).toEqual(["c-1", "c-5"]);
    expect(items[1]!.lastMessagePreview).toBe("mới");
    expect(items[1]!.lastMessageAt).toBe("2026-08-14T10:00:00Z");
  });

  test("nội dung dài cắt giống API", () => {
    const cache = infiniteCache([[item("c-5")]]);
    const long = "x".repeat(200);

    const result = reconcileConversationList(cache, messageEvent({ content: long }), {});

    expect(flatten(result.cache)[0]!.lastMessagePreview).toBe(`${"x".repeat(140)}...`);
  });

  test("server cũ không gửi trạng thái thì giữ nguyên trạng thái đang có", () => {
    const cache = infiniteCache([[item("c-5", { status: "resolved" })]]);

    const result = reconcileConversationList(
      cache,
      messageEvent({ conversationStatus: undefined }),
      {},
    );

    expect(flatten(result.cache)[0]!.status).toBe("resolved");
    expect(result.statusChanged).toBe(false);
  });
});

test.describe("filtersFromQueryKey", () => {
  test('bỏ qua "all" vì lúc gọi API trường đó để trống', () => {
    const filters = filtersFromQueryKey([
      "inbox",
      "conversations",
      { status: undefined, inboxId: "all", q: undefined, assignedTo: undefined },
    ]);

    expect(filters.inboxId).toBeUndefined();
  });

  test("đọc được inbox và người phụ trách đang lọc", () => {
    const filters = filtersFromQueryKey([
      "inbox",
      "conversations",
      { status: "open", inboxId: "inbox-b", assignedTo: "user-1" },
    ]);

    expect(filters).toMatchObject({ status: "open", inboxId: "inbox-b", assignedTo: "user-1" });
  });
});
