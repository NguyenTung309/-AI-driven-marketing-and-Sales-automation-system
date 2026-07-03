import { useEffect, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import { getRealtimeAccessToken } from "@/shared/api/client";

type ConnectionState = "disabled" | "connecting" | "connected" | "reconnecting" | "disconnected" | "error";

function getHubUrl(): string {
  const apiBase = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, "");
  return apiBase ? `${apiBase}/hubs/notifications` : "/hubs/notifications";
}

/**
 * C1: realtime run updates. AgentService publish "run.updated" qua Redis → API relay đẩy sự kiện
 * "runUpdated" vào NotificationHub theo tenant group → hook invalidate query của run tương ứng.
 * Trả về trạng thái kết nối để caller hạ polling xuống mức dự phòng khi realtime hoạt động.
 */
export function useOrchestrationRealtime(enabled: boolean): ConnectionState {
  const queryClient = useQueryClient();
  const [state, setState] = useState<ConnectionState>("connecting");

  useEffect(() => {
    if (!enabled) return;
    let disposed = false;
    const setConnectionState = (nextState: ConnectionState) => {
      if (!disposed) setState(nextState);
    };

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(getHubUrl(), { accessTokenFactory: getRealtimeAccessToken })
      .configureLogging(signalR.LogLevel.None)
      .withAutomaticReconnect()
      .build();

    connection.on("runUpdated", (evt: { readonly sessionId: string }) => {
      void queryClient.invalidateQueries({ queryKey: ["orchestration", "runs"] });
      void queryClient.invalidateQueries({ queryKey: ["orchestration", "session", evt.sessionId] });
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
