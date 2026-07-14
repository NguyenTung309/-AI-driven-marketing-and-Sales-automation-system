import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Card } from "@/shared/ui";
import {
  disableWebPush,
  enableWebPush,
  listNotificationPreferences,
  updateNotificationPreferences,
  type NotificationPreference,
} from "@/shared/api/notificationPreferences";

type Channel = "inApp" | "push" | "email";

const CHANNEL_LABELS: readonly { readonly key: Channel; readonly label: string }[] = [
  { key: "inApp", label: "Trong ứng dụng" },
  { key: "push", label: "Đẩy về máy" },
  { key: "email", label: "Email" },
];

export function NotificationSettingsCard() {
  const queryClient = useQueryClient();
  // Bản nháp chỉ giữ phần user đã chỉnh; phần chưa chạm lấy thẳng từ server (không copy state vào effect).
  const [edits, setEdits] = useState<Readonly<Record<string, NotificationPreference>>>({});
  const [notice, setNotice] = useState<string | null>(null);

  const { data } = useQuery({
    queryKey: ["notification-preferences"],
    queryFn: listNotificationPreferences,
  });

  const draft: readonly NotificationPreference[] = (data?.items ?? []).map(
    (item) => edits[item.type] ?? item,
  );

  const save = useMutation({
    mutationFn: () =>
      updateNotificationPreferences(
        draft.map(({ type, inApp, push, email }) => ({ type, inApp, push, email })),
      ),
    onSuccess: async () => {
      setNotice("Đã lưu tuỳ chọn thông báo.");
      setEdits({});
      await queryClient.invalidateQueries({ queryKey: ["notification-preferences"] });
    },
  });

  const pushToggle = useMutation({
    mutationFn: async (enable: boolean) => (enable ? enableWebPush() : disableWebPush().then(() => false)),
    onSuccess: (enabled) =>
      setNotice(
        enabled
          ? "Đã bật thông báo đẩy trên trình duyệt này."
          : "Đã tắt thông báo đẩy trên trình duyệt này (hoặc trình duyệt từ chối quyền).",
      ),
  });

  function toggle(type: string, channel: Channel) {
    const current = draft.find((item) => item.type === type);
    if (!current) return;
    setEdits((prev) => ({ ...prev, [type]: { ...current, [channel]: !current[channel] } }));
  }

  return (
    <Card>
      <div className="flex flex-col gap-4">
        <div>
          <h2 className="text-title-lg text-on-surface">Thông báo</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">
            Chọn việc nào của AI cần báo cho bạn. Cảnh báo lỗi (agent hỏng, mất kết nối kênh) luôn được
            đẩy và không tắt được — tắt là hệ thống hỏng mà không ai biết.
          </p>
        </div>

        {notice ? <Alert tone="success">{notice}</Alert> : null}

        <div className="overflow-x-auto">
          <table className="w-full min-w-[520px] border-collapse text-body-md">
            <thead>
              <tr className="border-b border-outline text-left text-label-sm uppercase text-on-surface-variant">
                <th className="py-2">Loại việc</th>
                {CHANNEL_LABELS.map((channel) => (
                  <th className="w-28 py-2 text-center" key={channel.key}>
                    {channel.label}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {draft.map((item) => (
                <tr className="border-b border-surface-variant" key={item.type}>
                  <td className="py-2 pr-3 text-on-surface">{item.label}</td>
                  {CHANNEL_LABELS.map((channel) => (
                    <td className="py-2 text-center" key={channel.key}>
                      <input
                        aria-label={`${item.label} — ${channel.label}`}
                        checked={item[channel.key]}
                        onChange={() => toggle(item.type, channel.key)}
                        type="checkbox"
                      />
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="flex flex-wrap gap-2">
          <Button disabled={save.isPending || draft.length === 0} onClick={() => save.mutate()}>
            Lưu tuỳ chọn
          </Button>
          <Button
            disabled={pushToggle.isPending}
            onClick={() => pushToggle.mutate(true)}
            variant="outline"
          >
            Bật thông báo đẩy trên trình duyệt này
          </Button>
          <Button
            disabled={pushToggle.isPending}
            onClick={() => pushToggle.mutate(false)}
            variant="ghost"
          >
            Tắt
          </Button>
        </div>
      </div>
    </Card>
  );
}
