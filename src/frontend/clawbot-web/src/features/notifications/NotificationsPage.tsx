import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AppShell } from "@/shared/layout/AppShell";
import { Card } from "@/shared/ui/Card";
import { StatusPill } from "@/shared/ui/StatusPill";
import {
  getUnreadNotificationCount,
  listNotifications,
  markAllNotificationsRead,
  markNotificationRead,
  type AppNotification,
  type NotificationSeverity,
} from "@/shared/api/notifications";
import { useNotificationsRealtime } from "./useNotificationsRealtime";

type NotificationTab = "all" | "unread" | "system" | "lead";
type NotificationTone = "info" | "success" | "warning" | "error";

const TABS: readonly { value: NotificationTab; label: string }[] = [
  { value: "all", label: "Tất cả" },
  { value: "unread", label: "Chưa đọc" },
  { value: "system", label: "Hệ thống" },
  { value: "lead", label: "Khách hàng (Lead)" },
];
const EMPTY_NOTIFICATIONS: readonly AppNotification[] = [];

function normalize(value: string): string {
  return value.trim().toLowerCase();
}

function notificationTone(severity: NotificationSeverity): NotificationTone {
  const value = normalize(severity);
  if (value.includes("error") || value.includes("critical")) return "error";
  if (value.includes("warn")) return "warning";
  if (value.includes("success")) return "success";
  return "info";
}

function toneClasses(tone: NotificationTone): string {
  if (tone === "error") return "bg-red-100 text-red-700";
  if (tone === "warning") return "bg-amber-100 text-amber-700";
  if (tone === "success") return "bg-emerald-100 text-emerald-700";
  return "bg-blue-100 text-blue-700";
}

function typeIcon(notification: AppNotification): string {
  const type = normalize(notification.type);
  if (type.includes("lead") || type.includes("hot")) return "person";
  if (type.includes("content")) return "edit_square";
  if (type.includes("budget") || type.includes("token")) return "toll";
  if (type.includes("agent")) return "smart_toy";
  if (notificationTone(notification.severity) === "error") return "warning";
  return "notifications";
}

function typeLabel(type: string): string {
  const value = normalize(type);
  if (value.includes("lead")) return "Lead";
  if (value.includes("content")) return "Nội dung";
  if (value.includes("budget")) return "Ngân sách";
  if (value.includes("agent")) return "Agent";
  if (value.includes("system")) return "Hệ thống";
  return type || "Thông báo";
}

function matchesTab(notification: AppNotification, tab: NotificationTab): boolean {
  const type = normalize(notification.type);
  if (tab === "all") return true;
  if (tab === "unread") return !notification.isRead;
  if (tab === "system") return type.includes("system") || type.includes("agent") || type.includes("budget");
  return type.includes("lead") || type.includes("customer") || type.includes("contact");
}

function formatRelative(value: string): string {
  const at = new Date(value).getTime();
  if (Number.isNaN(at)) return value;
  const diff = Date.now() - at;
  const mins = Math.max(0, Math.round(diff / 60000));
  if (mins < 1) return "Vừa xong";
  if (mins < 60) return `${mins} phút trước`;
  const hours = Math.round(mins / 60);
  if (hours < 24) return `${hours} giờ trước`;
  return new Intl.DateTimeFormat("vi-VN", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" }).format(
    new Date(value)
  );
}

function realtimeLabel(state: ReturnType<typeof useNotificationsRealtime>): string {
  if (state === "connected") return "Cập nhật tức thì đang bật";
  if (state === "reconnecting" || state === "connecting") return "Đang kết nối cập nhật";
  if (state === "disabled") return "Cập nhật tức thì đang tắt";
  return "Cập nhật tức thì chưa sẵn sàng";
}

function notificationActionLabel(notification: AppNotification): string {
  if (notification.link) return "Mở liên kết";
  if (!notification.isRead) return "Đánh dấu đã đọc";
  return "Đã đọc";
}

function NotificationRow({
  notification,
  onMarkRead,
  pending,
}: {
  readonly notification: AppNotification;
  readonly onMarkRead: (id: string) => void;
  readonly pending: boolean;
}) {
  const tone = notificationTone(notification.severity);
  const actionLabel = notificationActionLabel(notification);

  return (
    <article
      className={[
        "grid grid-cols-[40px_minmax(0,1fr)] gap-3 border-b border-outline p-4 transition-colors sm:grid-cols-[40px_minmax(0,1fr)_auto]",
        notification.isRead ? "bg-white hover:bg-surface-container-low" : "bg-blue-50/60 hover:bg-blue-50",
      ].join(" ")}
    >
      <div className={`flex size-10 items-center justify-center rounded-full ${toneClasses(tone)}`}>
        <span aria-hidden="true" className="material-symbols-outlined text-[20px]">{typeIcon(notification)}</span>
      </div>
      <div className="min-w-0">
        <div className="mb-1 flex flex-wrap items-center gap-2">
          <span className="rounded-full bg-surface-container px-2 py-0.5 font-mono text-[11px] font-bold uppercase text-secondary">
            {typeLabel(notification.type)}
          </span>
          <span className="text-label-sm text-on-surface-variant">{formatRelative(notification.createdAt)}</span>
          {!notification.isRead ? <span className="size-2 rounded-full bg-primary" aria-label="Chưa đọc" /> : null}
        </div>
        <h3 className={notification.isRead ? "text-body-md font-semibold text-on-surface-variant" : "text-body-md font-bold"}>
          {notification.title}
        </h3>
        {notification.body ? <p className="mt-1 line-clamp-2 text-body-md text-on-surface-variant">{notification.body}</p> : null}
      </div>
      <div className="col-span-2 flex items-center gap-2 sm:col-span-1 sm:justify-end">
        {notification.link ? (
          <a
            className="rounded border border-outline px-3 py-2 text-body-md font-semibold text-secondary hover:border-primary hover:text-primary"
            href={notification.link}
            rel="noreferrer"
            target={notification.link.startsWith("http") ? "_blank" : undefined}
          >
            {actionLabel}
          </a>
        ) : null}
        {!notification.isRead ? (
          <button
            className="rounded bg-primary px-3 py-2 text-body-md font-semibold text-on-primary hover:bg-primary-hover disabled:cursor-not-allowed disabled:opacity-60"
            disabled={pending}
            onClick={() => onMarkRead(notification.id)}
            type="button"
          >
            Đã đọc
          </button>
        ) : null}
      </div>
    </article>
  );
}

export default function NotificationsPage() {
  const [tab, setTab] = useState<NotificationTab>("all");
  const queryClient = useQueryClient();
  const realtimeState = useNotificationsRealtime(true);

  const listQuery = useQuery({
    queryKey: ["notifications", "list", tab],
    queryFn: () => listNotifications({ unread: tab === "unread" ? true : undefined, page: 1, pageSize: 30 }),
  });
  const unreadQuery = useQuery({
    queryKey: ["notifications", "unread-count"],
    queryFn: getUnreadNotificationCount,
    staleTime: 30_000,
  });

  const markRead = useMutation({
    mutationFn: markNotificationRead,
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["notifications", "list"] }),
        queryClient.invalidateQueries({ queryKey: ["notifications", "unread-count"] }),
      ]);
    },
  });

  const markAllRead = useMutation({
    mutationFn: markAllNotificationsRead,
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["notifications", "list"] }),
        queryClient.invalidateQueries({ queryKey: ["notifications", "unread-count"] }),
      ]);
    },
  });

  const notifications = listQuery.data?.items ?? EMPTY_NOTIFICATIONS;
  const visibleNotifications = useMemo(
    () => notifications.filter((notification) => matchesTab(notification, tab)),
    [notifications, tab]
  );
  const unreadCount = unreadQuery.data?.count ?? notifications.filter((notification) => !notification.isRead).length;
  const totalCount = listQuery.data?.total ?? notifications.length;
  const lastNotification = notifications[0] ?? null;

  return (
    <AppShell title="Trung tâm thông báo">
      <div className="mb-stack-lg flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div>
          <h1 className="text-headline-md font-bold text-secondary">Trung tâm thông báo</h1>
          <p className="mt-1 text-body-md text-on-surface-variant">
            Theo dõi cảnh báo hệ thống, lead nóng và trạng thái agent ngay trong ứng dụng.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <StatusPill tone={realtimeState === "connected" ? "success" : realtimeState === "error" ? "error" : "warning"}>
            {realtimeLabel(realtimeState)}
          </StatusPill>
          <button
            className="rounded bg-primary px-4 py-2 text-body-md font-bold text-on-primary hover:bg-primary-hover disabled:cursor-not-allowed disabled:opacity-60"
            disabled={markAllRead.isPending || unreadCount === 0}
            onClick={() => markAllRead.mutate()}
            type="button"
          >
            Đánh dấu tất cả đã đọc
          </button>
        </div>
      </div>

      <section className="mb-gutter grid grid-cols-1 gap-gutter md:grid-cols-3">
        <Card>
          <p className="text-label-caps uppercase text-on-surface-variant">Chưa đọc</p>
          <p className="mt-2 text-telemetry-data text-secondary">{unreadCount.toLocaleString("vi-VN")}</p>
          <p className="mt-1 font-mono text-mono-status text-primary">Số đếm cập nhật trực tiếp</p>
        </Card>
        <Card>
          <p className="text-label-caps uppercase text-on-surface-variant">Tổng hiển thị</p>
          <p className="mt-2 text-telemetry-data text-secondary">{totalCount.toLocaleString("vi-VN")}</p>
          <p className="mt-1 font-mono text-mono-status text-success">Bảng thông báo</p>
        </Card>
        <Card>
          <p className="text-label-caps uppercase text-on-surface-variant">Sự kiện mới nhất</p>
          <p className="mt-2 line-clamp-2 text-headline-sm font-bold text-secondary">
            {lastNotification?.title ?? "Chưa có dữ liệu"}
          </p>
          <p className="mt-1 text-body-md text-on-surface-variant">
            {lastNotification ? formatRelative(lastNotification.createdAt) : "Đang chờ thông báo mới"}
          </p>
        </Card>
      </section>

      <section className="grid grid-cols-1 gap-gutter xl:grid-cols-[minmax(0,1fr)_360px]">
        <Card className="overflow-hidden p-0">
          <div className="flex flex-col gap-4 border-b border-outline p-card-padding lg:flex-row lg:items-center lg:justify-between">
            <div>
              <h2 className="text-headline-sm font-bold text-secondary">Thông báo</h2>
              <p className="mt-1 text-body-md text-on-surface-variant">
                Tự cập nhật khi agent phát cảnh báo mới hoặc khi sale xử lý trạng thái đã đọc.
              </p>
            </div>
            <div className="flex flex-wrap gap-2">
              {TABS.map((item) => (
                <button
                  className={[
                    "whitespace-nowrap rounded px-3 py-2 text-body-md font-semibold transition-colors",
                    tab === item.value ? "bg-primary text-on-primary" : "bg-surface-container-low text-secondary hover:bg-surface-container",
                  ].join(" ")}
                  key={item.value}
                  onClick={() => setTab(item.value)}
                  type="button"
                >
                  {item.label}
                </button>
              ))}
            </div>
          </div>

          {listQuery.isLoading ? (
            <div className="p-card-padding text-body-md text-on-surface-variant">Đang tải thông báo...</div>
          ) : listQuery.isError ? (
            <div className="m-card-padding rounded-lg border border-error/30 bg-red-50 p-4 text-body-md text-error">
              Không thể tải thông báo. Vui lòng thử lại hoặc kiểm tra quyền truy cập.
            </div>
          ) : visibleNotifications.length ? (
            <div className="xl:max-h-[640px] xl:overflow-y-auto">
              {visibleNotifications.map((notification) => (
                <NotificationRow
                  key={notification.id}
                  notification={notification}
                  onMarkRead={(id) => markRead.mutate(id)}
                  pending={markRead.isPending}
                />
              ))}
            </div>
          ) : (
            <div className="flex min-h-72 flex-col items-center justify-center p-card-padding text-center">
              <div className="mb-3 flex size-12 items-center justify-center rounded-full bg-surface-container text-secondary">
                <span aria-hidden="true" className="material-symbols-outlined">notifications_off</span>
              </div>
              <h3 className="text-headline-sm font-bold text-secondary">Không có thông báo trong bộ lọc này</h3>
              <p className="mt-2 max-w-sm text-body-md text-on-surface-variant">
                Khi có cảnh báo mới, danh sách sẽ tự cập nhật để đội sale xử lý kịp thời.
              </p>
            </div>
          )}
        </Card>

        <aside className="space-y-gutter">
          <Card>
            <div className="flex items-start justify-between gap-3">
              <div>
                <p className="text-label-caps uppercase text-on-surface-variant">Kênh Telegram</p>
                <h2 className="mt-2 text-headline-sm font-bold text-secondary">Đang ưu tiên thông báo trong ứng dụng</h2>
              </div>
              <span className="rounded-full bg-surface-container px-2 py-1 font-mono text-[11px] font-bold uppercase text-on-surface-variant">
                Chưa bật
              </span>
            </div>
            <p className="mt-3 text-body-md text-on-surface-variant">
              Cảnh báo hiện đang gửi qua Trung tâm thông báo. Kênh Telegram sẽ được bật khi cấu hình tích hợp hoàn tất.
            </p>
            <div className="mt-4 rounded-lg border border-outline bg-surface-container-low p-3">
              <div className="flex items-center justify-between gap-3">
                <span className="font-mono text-mono-status text-secondary">@HocBaAlertBot</span>
                <span className="rounded-full bg-amber-100 px-2 py-1 text-[11px] font-bold uppercase text-amber-700">Tạm dừng</span>
              </div>
            </div>
            <button
              aria-disabled="true"
              className="mt-4 w-full rounded border border-outline px-4 py-2 text-body-md font-bold text-on-surface-variant opacity-70"
              disabled
              type="button"
            >
              Kết nối Telegram
            </button>
          </Card>

          <Card>
            <p className="text-label-caps uppercase text-on-surface-variant">Luồng cảnh báo</p>
            <div className="mt-3 space-y-3 text-body-md text-secondary">
              <div className="flex items-center gap-2">
                <span aria-hidden="true" className="material-symbols-outlined text-[18px] text-success">check_circle</span>
                <span>Lưu cảnh báo vào trung tâm thông báo</span>
              </div>
              <div className="flex items-center gap-2">
                <span aria-hidden="true" className="material-symbols-outlined text-[18px] text-success">check_circle</span>
                <span>Đếm thông báo chưa đọc theo từng người dùng</span>
              </div>
              <div className="flex items-center gap-2">
                <span aria-hidden="true" className="material-symbols-outlined text-[18px] text-success">check_circle</span>
                <span>Đồng bộ trạng thái đã đọc</span>
              </div>
              <div className="flex items-center gap-2">
                <span aria-hidden="true" className="material-symbols-outlined text-[18px] text-success">sensors</span>
                <span>Cập nhật tức thì trong ứng dụng</span>
              </div>
            </div>
          </Card>
        </aside>
      </section>
    </AppShell>
  );
}
