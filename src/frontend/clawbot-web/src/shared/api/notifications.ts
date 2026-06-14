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
}

export interface NotificationListResponse {
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
  readonly items: readonly AppNotification[];
}

export interface NotificationEvent {
  readonly id: string;
  readonly type: string;
  readonly severity: NotificationSeverity;
  readonly title: string;
  readonly body: string | null;
  readonly link: string | null;
  readonly createdAt: string;
}

export interface ListNotificationsParams {
  readonly unread?: boolean;
  readonly page?: number;
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
