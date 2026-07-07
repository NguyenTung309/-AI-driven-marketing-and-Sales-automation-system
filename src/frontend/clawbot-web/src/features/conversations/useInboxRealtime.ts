import { useEffect, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import { getRealtimeAccessToken } from "@/shared/api/client";
import type {
  ConversationDetail,
  ConversationListResponse,
  InboxConversationEvent,
  InboxMessage,
  InboxMessageEvent,
} from "@/shared/api/inbox";

type ConnectionState = "disabled" | "connecting" | "connected" | "reconnecting" | "disconnected" | "error";

function getHubUrl(): string {
  const apiBase = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, "");
  return apiBase ? `${apiBase}/hubs/inbox` : "/hubs/inbox";
}

function toMessage(evt: InboxMessageEvent): InboxMessage {
  return {
    id: evt.messageId,
    direction: evt.direction,
    senderType: evt.senderType,
    senderUserId: null,
    senderDisplayName: evt.senderDisplayName ?? null,
    senderAvatarUrl: evt.senderAvatarUrl ?? null,
    content: evt.content,
    contentType: evt.contentType,
    attachmentUrl: evt.attachmentUrl ?? null,
    sentAt: evt.sentAt,
  };
}

function mergeMessage(messages: readonly InboxMessage[], next: InboxMessage): readonly InboxMessage[] {
  if (messages.some((message) => message.id === next.id)) return messages;
  return [...messages, next];
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
      const nextMessage = toMessage(evt);
      queryClient.setQueriesData<ConversationDetail>({ queryKey: ["inbox", "conversation"] }, (old) => {
        if (!old || old.id !== evt.conversationId) return old;
        return {
          ...old,
          lastMessageAt: evt.sentAt,
          messages: mergeMessage(old.messages, nextMessage),
        };
      });

      queryClient.setQueriesData<ConversationListResponse>({ queryKey: ["inbox", "conversations"] }, (old) => {
        if (!old) return old;
        return {
          ...old,
          items: old.items.map((item) =>
            item.id === evt.conversationId
              ? { ...item, lastMessageAt: evt.sentAt, lastMessagePreview: evt.content }
              : item
          ),
        };
      });
    });

    connection.on("conversation", (evt: InboxConversationEvent) => {
      queryClient.setQueriesData<ConversationDetail>({ queryKey: ["inbox", "conversation"] }, (old) => {
        if (!old || old.id !== evt.conversationId) return old;
        return {
          ...old,
          status: evt.status,
          assignedTo: evt.assignedTo,
          lastMessageAt: evt.lastMessageAt,
        };
      });

      queryClient.setQueriesData<ConversationListResponse>({ queryKey: ["inbox", "conversations"] }, (old) => {
        if (!old) return old;
        return {
          ...old,
          items: old.items.map((item) =>
            item.id === evt.conversationId
              ? { ...item, status: evt.status, assignedTo: evt.assignedTo, lastMessageAt: evt.lastMessageAt }
              : item
          ),
        };
      });
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
