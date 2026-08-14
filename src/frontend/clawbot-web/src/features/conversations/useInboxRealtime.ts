import { useCallback, useEffect, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { useQueryClient, type InfiniteData } from "@tanstack/react-query";
import { getRealtimeAccessToken } from "@/shared/api/client";
import {
  CONVERSATION_COUNTS_QUERY_KEY,
  type ConversationCursorPage,
  type ConversationListItem,
  type ConversationDetail,
  type ConversationListResponse,
  type InboxConversationEvent,
  type InboxMessage,
  type InboxMessageEvent,
  type InboxMessageStatusEvent,
  type InboxTypingEvent,
} from "@/shared/api/inbox";
import {
  filtersFromQueryKey,
  reconcileConversationList,
  type ConversationListCache,
} from "./inboxRealtimeCache";

// Phòng mất frame typing:false (relay/Redis rớt): gRPC auto-reply deadline 100s + margin.
const TYPING_EXPIRE_MS = 120_000;

const CONVERSATIONS_QUERY_KEY = ["inbox", "conversations"] as const;

function patchConversationItems(
  items: readonly ConversationListItem[],
  conversationId: string,
  patch: Partial<ConversationListItem>,
): ConversationListItem[] {
  return items.map((item) => (item.id === conversationId ? { ...item, ...patch } : item));
}

function patchConversationListCache(
  old: ConversationListCache | undefined,
  conversationId: string,
  patch: Partial<ConversationListItem>,
): ConversationListCache | undefined {
  if (!old) return old;
  if ("pages" in old && Array.isArray((old as InfiniteData<ConversationCursorPage>).pages)) {
    const infinite = old as InfiniteData<ConversationCursorPage | ConversationListResponse>;
    return {
      ...infinite,
      pages: infinite.pages.map((page) => ({
        ...page,
        items: patchConversationItems(page.items, conversationId, patch),
      })),
    };
  }
  const flat = old as ConversationListResponse | ConversationCursorPage;
  return {
    ...flat,
    items: patchConversationItems(flat.items, conversationId, patch),
  };
}

type ConnectionState = "disabled" | "connecting" | "connected" | "reconnecting" | "disconnected" | "error";

function getHubUrl(): string {
  const apiBase = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, "");
  return apiBase ? `${apiBase}/hubs/inbox` : "/hubs/inbox";
}

function toSyntheticMessage(evt: InboxMessageEvent): InboxMessage {
  return {
    id: evt.messageId,
    direction: evt.direction,
    senderType: evt.senderType,
    senderUserId: null,
    content: evt.content,
    contentType: evt.contentType,
    sentAt: evt.sentAt,
    senderDisplayName: evt.senderDisplayName ?? null,
    senderAvatarUrl: evt.senderAvatarUrl ?? null,
    attachmentUrl: evt.attachmentUrl ?? null,
    status: "sent",
  };
}

export function useInboxRealtime(enabled: boolean) {
  const queryClient = useQueryClient();
  const [state, setState] = useState<ConnectionState>("connecting");
  // Hội thoại đang có AI soạn phản hồi — FE hiện bong bóng "AI đang soạn..." theo set này.
  const [typingConversationIds, setTypingConversationIds] = useState<ReadonlySet<string>>(() => new Set());
  const typingTimersRef = useRef<Map<string, number>>(new Map());

  const clearTyping = useCallback((conversationId: string) => {
    const timer = typingTimersRef.current.get(conversationId);
    if (timer !== undefined) {
      window.clearTimeout(timer);
      typingTimersRef.current.delete(conversationId);
    }
    setTypingConversationIds((current) => {
      if (!current.has(conversationId)) return current;
      const next = new Set(current);
      next.delete(conversationId);
      return next;
    });
  }, []);

  useEffect(() => {
    if (!enabled) return;
    let disposed = false;
    const setConnectionState = (nextState: ConnectionState) => {
      if (!disposed) setState(nextState);
    };

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(getHubUrl(), {
        accessTokenFactory: getRealtimeAccessToken,
      })
      .configureLogging(signalR.LogLevel.None)
      .withAutomaticReconnect()
      .build();

    connection.on("typing", (evt: InboxTypingEvent) => {
      if (disposed) return;
      if (!evt.isTyping) {
        clearTyping(evt.conversationId);
        return;
      }
      const existing = typingTimersRef.current.get(evt.conversationId);
      if (existing !== undefined) window.clearTimeout(existing);
      typingTimersRef.current.set(
        evt.conversationId,
        window.setTimeout(() => clearTyping(evt.conversationId), TYPING_EXPIRE_MS),
      );
      setTypingConversationIds((current) => {
        if (current.has(evt.conversationId)) return current;
        const next = new Set(current);
        next.add(evt.conversationId);
        return next;
      });
    });

    connection.on("message", (evt: InboxMessageEvent) => {
      // Tin mới về (kể cả tin AI) ⇒ AI đã soạn xong — dự phòng khi frame typing:false thất lạc.
      clearTyping(evt.conversationId);
      if (evt.isSynthetic) {
        const syntheticMessage = toSyntheticMessage(evt);
        queryClient.setQueryData<ConversationDetail>(["inbox", "conversation", evt.conversationId], (old) => {
          if (!old || old.messages.some((message) => message.id === syntheticMessage.id)) return old;
          return { ...old, messages: [...old.messages, syntheticMessage] };
        });
      } else {
        void queryClient.invalidateQueries({
          queryKey: ["inbox", "conversation", evt.conversationId],
          exact: true,
        });
      }

      // Tin nhắn giả (nhắc hội thoại treo) không được ghi vào DB nên không đụng tới danh sách.
      if (evt.isSynthetic) return;

      // Tin thường: vá + đẩy lên đầu ngay trong cache. Chỉ gọi lại server khi danh sách
      // đang tải chưa có hội thoại này, hoặc trạng thái đổi làm nó ra/vào bộ lọc.
      let needsListRefresh = false;
      let needsCountsRefresh = false;
      for (const query of queryClient.getQueryCache().findAll({ queryKey: CONVERSATIONS_QUERY_KEY })) {
        const result = reconcileConversationList(
          query.state.data as ConversationListCache | undefined,
          evt,
          filtersFromQueryKey(query.queryKey),
        );
        if (result.cache !== query.state.data) {
          queryClient.setQueryData(query.queryKey, result.cache);
        }
        // Vắng mặt: có thể là hội thoại mới, phải hỏi server mới có đủ tên khách/inbox.
        // Đổi trạng thái: hội thoại vừa ra/vào bộ lọc nên số đếm và các tab khác đã lệch.
        if (result.needsRefresh || result.statusChanged) {
          needsListRefresh = true;
          needsCountsRefresh = true;
        }
      }

      if (needsListRefresh) {
        void queryClient.invalidateQueries({ queryKey: CONVERSATIONS_QUERY_KEY });
      }
      if (needsCountsRefresh) {
        void queryClient.invalidateQueries({ queryKey: CONVERSATION_COUNTS_QUERY_KEY });
      }
    });

    connection.on("messageStatus", (evt: InboxMessageStatusEvent) => {
      queryClient.setQueryData<ConversationDetail>(["inbox", "conversation", evt.conversationId], (old) => {
        if (!old) return old;
        return {
          ...old,
          messages: old.messages.map((message) =>
            message.id === evt.messageId ? { ...message, status: evt.status } : message,
          ),
        };
      });
      void queryClient.invalidateQueries({
        queryKey: ["inbox", "conversation", evt.conversationId],
        exact: true,
      });
    });

    connection.on("conversation", (evt: InboxConversationEvent) => {
      clearTyping(evt.conversationId);
      void queryClient.invalidateQueries({
        queryKey: ["inbox", "conversation", evt.conversationId],
        exact: true,
      });
      void queryClient.invalidateQueries({ queryKey: ["inbox", "conversations"] });
      void queryClient.invalidateQueries({ queryKey: CONVERSATION_COUNTS_QUERY_KEY });

      queryClient.setQueriesData<ConversationListCache>({ queryKey: ["inbox", "conversations"] }, (old) =>
        patchConversationListCache(old, evt.conversationId, {
          status: evt.status,
          assignedTo: evt.assignedTo,
          lastMessageAt: evt.lastMessageAt,
        }),
      );
    });

    connection.onreconnecting(() => setConnectionState("reconnecting"));
    connection.onreconnected(() => setConnectionState("connected"));
    connection.onclose(() => setConnectionState("disconnected"));

    void connection
      .start()
      .then(() => setConnectionState("connected"))
      .catch(() => setConnectionState("error"));

    const timers = typingTimersRef.current;
    return () => {
      disposed = true;
      for (const timer of timers.values()) window.clearTimeout(timer);
      timers.clear();
      setTypingConversationIds(new Set());
      void connection.stop();
    };
  }, [enabled, queryClient, clearTyping]);

  return { state: enabled ? state : ("disabled" as ConnectionState), typingConversationIds };
}
