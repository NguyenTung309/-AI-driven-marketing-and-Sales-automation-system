import { apiClient } from "./client";

export type NotificationSeverity = "info" | "success" | "warning" | "error" | "critical" | string;

export interface AppNotification {
  readonly id: string;
  readonly type: string;
  readonly severity: NotificationSeverity;
  readonly title: string;
  readonly body: string | null;
  readonly link: string | null;
  readonly isRead: boolean;
  readonly readAt: string | null;
  readonly createdAt: string;
  /** Số sự kiện đã gom vào dòng này (kiểu Facebook). 1 = sự kiện lẻ. */
  readonly occurrenceCount?: number;
}

export interface NotificationListResponse {
  readonly items: readonly AppNotification[];
  readonly nextCursor: string | null;
  readonly total: number | null;
}

export interface NotificationEvent {
  readonly id: string;
  readonly type: string;
  readonly severity: NotificationSeverity;
  readonly title: string;
  readonly body: string | null;
  readonly link: string | null;
  readonly createdAt: string;
  readonly occurrenceCount?: number;
  /** false = chỉ vào feed, không nổi toast (nhóm việc máy móc hoặc user đã tắt). */
  readonly push?: boolean;
}

export interface ListNotificationsParams {
  readonly unread?: boolean;
  readonly cursor?: string | null;
  readonly pageSize?: number;
}

export interface UnreadNotificationCount {
  readonly count: number;
}

export interface MarkAllNotificationsReadResponse {
  readonly updated: number;
}

export async function listNotifications(params?: ListNotificationsParams): Promise<NotificationListResponse> {
  const res = await apiClient.get<NotificationListResponse>("/api/notifications", { params });
  return res.data;
}

export async function getUnreadNotificationCount(): Promise<UnreadNotificationCount> {
  const res = await apiClient.get<UnreadNotificationCount>("/api/notifications/unread-count");
  return res.data;
}

export async function markNotificationRead(id: string): Promise<void> {
  await apiClient.post(`/api/notifications/${id}/read`);
}

export async function markAllNotificationsRead(): Promise<MarkAllNotificationsReadResponse> {
  const res = await apiClient.post<MarkAllNotificationsReadResponse>("/api/notifications/read-all");
  return res.data;
}
