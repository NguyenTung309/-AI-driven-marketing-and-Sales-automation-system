import { useEffect, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import { getRealtimeAccessToken } from "@/shared/api/client";
import type { AppNotification, NotificationEvent, NotificationListResponse } from "@/shared/api/notifications";

type ConnectionState = "disabled" | "connecting" | "connected" | "reconnecting" | "disconnected" | "error";

function getHubUrl(): string {
  const apiBase = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, "");
  return apiBase ? `${apiBase}/hubs/notifications` : "/hubs/notifications";
}

function eventToNotification(evt: NotificationEvent): AppNotification {
  return {
    id: evt.id,
    type: evt.type,
    severity: evt.severity,
    title: evt.title,
    body: evt.body,
    link: evt.link,
    isRead: false,
    readAt: null,
    createdAt: evt.createdAt,
  };
}

export function useNotificationsRealtime(enabled: boolean) {
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

    connection.on("notification", (evt: NotificationEvent) => {
      const nextNotification = eventToNotification(evt);

      queryClient.setQueriesData<NotificationListResponse>({ queryKey: ["notifications", "list"] }, (old) => {
        if (!old || old.items.some((item) => item.id === evt.id)) return old;
        return {
          ...old,
          total: old.total + 1,
          items: [nextNotification, ...old.items].slice(0, old.pageSize),
        };
      });

      queryClient.setQueryData<{ count: number }>(["notifications", "unread-count"], (old) =>
        old ? { count: old.count + 1 } : old
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
