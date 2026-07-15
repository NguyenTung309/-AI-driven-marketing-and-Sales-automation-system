import { useEffect, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { useQueryClient, type InfiniteData } from "@tanstack/react-query";
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
    occurrenceCount: evt.occurrenceCount ?? 1,
  };
}

export function useNotificationsRealtime(enabled: boolean, onNotification?: (n: AppNotification) => void) {
  const queryClient = useQueryClient();
  const [state, setState] = useState<ConnectionState>("connecting");
  // Keep the latest callback without re-running the connection effect on every render.
  const cbRef = useRef(onNotification);
  useEffect(() => {
    cbRef.current = onNotification;
  }, [onNotification]);

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

      queryClient.setQueriesData<InfiniteData<NotificationListResponse> | NotificationListResponse>(
        { queryKey: ["notifications", "list"] },
        (old) => {
          if (!old) return old;
          // Infinite list cache (useInfiniteList)
          if ("pages" in old && Array.isArray(old.pages)) {
            const pages = old.pages;
            if (!pages.length) return old;
            const first = pages[0];
            if (first.items.some((item) => item.id === evt.id)) {
              return {
                ...old,
                pages: pages.map((page, idx) =>
                  idx === 0
                    ? {
                        ...page,
                        items: page.items.map((item) =>
                          item.id === evt.id ? { ...item, ...nextNotification, isRead: item.isRead } : item,
                        ),
                      }
                    : page,
                ),
              };
            }
            return {
              ...old,
              pages: [
                {
                  ...first,
                  total: (first.total ?? first.items.length) + 1,
                  items: [nextNotification, ...first.items],
                },
                ...pages.slice(1),
              ],
            };
          }
          // Legacy flat list shape
          const flat = old as NotificationListResponse;
          if (flat.items.some((item) => item.id === evt.id)) {
            return {
              ...flat,
              items: flat.items.map((item) =>
                item.id === evt.id ? { ...item, ...nextNotification, isRead: item.isRead } : item,
              ),
            };
          }
          return {
            ...flat,
            total: (flat.total ?? flat.items.length) + 1,
            items: [nextNotification, ...flat.items],
          };
        },
      );

      queryClient.setQueryData<{ count: number }>(["notifications", "unread-count"], (old) =>
        old ? { count: old.count + 1 } : old
      );

      // Toast chỉ nổi khi server cho phép push: nhóm việc máy móc (đổi giá thầu, auto-reply) vào feed
      // nhưng không rung chuông. Cảnh báo lỗi luôn có push=true.
      if (evt.push !== false) cbRef.current?.(nextNotification);
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
