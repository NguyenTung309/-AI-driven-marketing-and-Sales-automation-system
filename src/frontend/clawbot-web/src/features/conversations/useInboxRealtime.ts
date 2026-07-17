import { useEffect, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { useQueryClient, type InfiniteData } from "@tanstack/react-query";
import { getRealtimeAccessToken } from "@/shared/api/client";
import type {
  ConversationCursorPage,
  ConversationListItem,
  ConversationDetail,
  ConversationListResponse,
  InboxConversationEvent,
  InboxMessage,
  InboxMessageEvent,
  InboxMessageStatusEvent,
} from "@/shared/api/inbox";

type ConversationListCache =
  | InfiniteData<ConversationCursorPage | ConversationListResponse>
  | ConversationListResponse
  | ConversationCursorPage;

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

    connection.on("message", (evt: InboxMessageEvent) => {
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

      queryClient.setQueriesData<ConversationListCache>({ queryKey: ["inbox", "conversations"] }, (old) =>
        patchConversationListCache(old, evt.conversationId, {
          lastMessageAt: evt.sentAt,
          lastMessagePreview: evt.content,
        }),
      );
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
      void queryClient.invalidateQueries({
        queryKey: ["inbox", "conversation", evt.conversationId],
        exact: true,
      });
      void queryClient.invalidateQueries({ queryKey: ["inbox", "conversations"] });

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

    return () => {
      disposed = true;
      void connection.stop();
    };
  }, [enabled, queryClient]);

  return enabled ? state : "disabled";
}
