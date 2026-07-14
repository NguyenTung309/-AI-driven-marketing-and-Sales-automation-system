import { useEffect } from "react";
import * as signalR from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import { getRealtimeAccessToken } from "@/shared/api/client";
import type { JobEvent } from "@/shared/api/jobs";

function getHubUrl(): string {
  const apiBase = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, "");
  return apiBase ? `${apiBase}/hubs/notifications` : "/hubs/notifications";
}

/**
 * Lắng nghe tiến độ job trên NotificationHub (event "job") và invalidate cache.
 * Dùng chung hub với thông báo — không dựng hub riêng cho job.
 */
export function useJobsRealtime(enabled: boolean): void {
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!enabled) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(getHubUrl(), { accessTokenFactory: getRealtimeAccessToken })
      .configureLogging(signalR.LogLevel.None)
      .withAutomaticReconnect()
      .build();

    connection.on("job", (evt: JobEvent) => {
      void queryClient.invalidateQueries({ queryKey: ["jobs"] });
      void queryClient.invalidateQueries({ queryKey: ["job", evt.jobId] });
    });

    void connection.start().catch(() => undefined);

    return () => {
      void connection.stop();
    };
  }, [enabled, queryClient]);
}
