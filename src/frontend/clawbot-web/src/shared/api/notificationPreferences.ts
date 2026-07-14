import { apiClient } from "./client";

export interface NotificationPreference {
  readonly type: string;
  readonly label: string;
  readonly inApp: boolean;
  readonly push: boolean;
  readonly email: boolean;
}

export interface NotificationPreferencesResponse {
  readonly items: readonly NotificationPreference[];
}

export async function listNotificationPreferences(): Promise<NotificationPreferencesResponse> {
  const res = await apiClient.get<NotificationPreferencesResponse>("/api/notifications/preferences");
  return res.data;
}

export async function updateNotificationPreferences(
  items: readonly Omit<NotificationPreference, "label">[],
): Promise<void> {
  await apiClient.put("/api/notifications/preferences", { items });
}

export async function getVapidPublicKey(): Promise<string | null> {
  const res = await apiClient.get<{ readonly publicKey: string | null }>("/api/push/vapid-public-key");
  return res.data.publicKey;
}

function urlBase64ToArrayBuffer(base64: string): ArrayBuffer {
  const padding = "=".repeat((4 - (base64.length % 4)) % 4);
  const normalized = (base64 + padding).replace(/-/g, "+").replace(/_/g, "/");
  const raw = window.atob(normalized);

  const buffer = new ArrayBuffer(raw.length);
  const view = new Uint8Array(buffer);
  for (let i = 0; i < raw.length; i++) view[i] = raw.charCodeAt(i);
  return buffer;
}

/**
 * Đăng ký Web Push cho trình duyệt này. Gọi CÓ NGỮ CẢNH (lúc user kích việc nền đầu tiên),
 * không hỏi quyền ngay lúc đăng nhập — bị từ chối một lần là mất vĩnh viễn.
 */
export async function enableWebPush(): Promise<boolean> {
  if (!("serviceWorker" in navigator) || !("PushManager" in window)) return false;

  const publicKey = await getVapidPublicKey();
  if (!publicKey) return false; // server chưa cấu hình VAPID — im lặng bỏ qua

  const permission = await Notification.requestPermission();
  if (permission !== "granted") return false;

  const registration = await navigator.serviceWorker.register("/sw.js");
  const subscription = await registration.pushManager.subscribe({
    userVisibleOnly: true,
    applicationServerKey: urlBase64ToArrayBuffer(publicKey),
  });

  const json = subscription.toJSON();
  await apiClient.post("/api/push/subscribe", {
    endpoint: subscription.endpoint,
    p256dh: json.keys?.p256dh ?? "",
    auth: json.keys?.auth ?? "",
  });
  return true;
}

export async function disableWebPush(): Promise<void> {
  if (!("serviceWorker" in navigator)) return;
  const registration = await navigator.serviceWorker.getRegistration();
  const subscription = await registration?.pushManager.getSubscription();
  if (!subscription) return;

  await apiClient.delete("/api/push/subscribe", { params: { endpoint: subscription.endpoint } });
  await subscription.unsubscribe();
}
