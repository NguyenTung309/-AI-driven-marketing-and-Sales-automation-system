import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Input, Modal, ToggleSwitch } from "@/shared/ui";
import { toUserFriendlyError } from "@/shared/utils/userText";
import { getTrendSettings, updateTrendSettings } from "@/shared/api/content";

const CADENCES = [
  { value: "off", label: "Tắt" },
  { value: "daily", label: "Hằng ngày" },
  { value: "weekly", label: "Hằng tuần" },
  { value: "monthly", label: "Hằng tháng" },
] as const;

const SELECT_CLASS =
  "bg-surface-container-lowest border border-surface-variant rounded px-3 py-2 text-body-md w-full focus:outline-none focus:ring-2 focus:ring-primary/30";

interface TrendSettingsDialogProps {
  readonly open: boolean;
  readonly onClose: () => void;
}

export function TrendSettingsDialog({ open, onClose }: TrendSettingsDialogProps) {
  const queryClient = useQueryClient();
  const settingsQuery = useQuery({
    queryKey: ["content", "trend-settings"],
    queryFn: getTrendSettings,
    enabled: open,
  });

  const [geo, setGeo] = useState("VN");
  const [googleEnabled, setGoogleEnabled] = useState(true);
  const [youTubeEnabled, setYouTubeEnabled] = useState(true);
  const [youTubeApiKey, setYouTubeApiKey] = useState("");
  const [clearYouTubeKey, setClearYouTubeKey] = useState(false);
  const [tikTokEnabled, setTikTokEnabled] = useState(false);
  const [tikTokUrl, setTikTokUrl] = useState("");
  const [cadence, setCadence] = useState("off");

  const settings = settingsQuery.data;
  useEffect(() => {
    if (!settings) return;
    setGeo(settings.geo);
    setGoogleEnabled(settings.google.enabled);
    setYouTubeEnabled(settings.youTube.enabled);
    setYouTubeApiKey("");
    setClearYouTubeKey(false);
    setTikTokEnabled(settings.tikTok.enabled);
    setTikTokUrl(settings.tikTok.url ?? "");
    setCadence(settings.schedule.cadence);
  }, [settings]);

  const saveMutation = useMutation({
    mutationFn: () =>
      updateTrendSettings({
        geo: geo.trim() || null,
        google: { enabled: googleEnabled },
        youTube: {
          enabled: youTubeEnabled,
          // null = giữ key đã lưu; "" = xoá; giá trị mới = thay
          apiKey: clearYouTubeKey ? "" : youTubeApiKey.trim() || null,
        },
        tikTok: { enabled: tikTokEnabled, url: tikTokUrl.trim() },
        scheduleCadence: cadence,
      }),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["content", "trend-settings"] }),
        queryClient.invalidateQueries({ queryKey: ["content", "trends"] }),
      ]);
      onClose();
    },
  });

  const error = settingsQuery.error ?? saveMutation.error;
  const nextRunAt = settings?.schedule.nextRunAt;

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Cấu hình quét xu hướng"
      maxWidthClass="max-w-lg"
      footer={
        <>
          <Button type="button" variant="outline" onClick={onClose} disabled={saveMutation.isPending}>
            Hủy
          </Button>
          <Button type="button" onClick={() => saveMutation.mutate()} disabled={saveMutation.isPending || settingsQuery.isLoading}>
            {saveMutation.isPending ? "Đang lưu..." : "Lưu cấu hình"}
          </Button>
        </>
      }
    >
      {error ? <Alert tone="error">{toUserFriendlyError(error)}</Alert> : null}
      {settingsQuery.isLoading ? (
        <p className="text-body-md text-on-surface-variant">Đang tải cấu hình...</p>
      ) : (
        <div className="space-y-5">
          <div>
            <label className="mb-1 block text-body-md font-bold text-secondary" htmlFor="trend-geo">
              Thị trường (geo)
            </label>
            <Input
              id="trend-geo"
              value={geo}
              onChange={(event) => setGeo(event.target.value.toUpperCase())}
              maxLength={2}
              placeholder="VN"
            />
          </div>

          <div className="space-y-3 rounded-lg border border-outline bg-surface p-3">
            <div className="flex items-center justify-between gap-3">
              <div>
                <p className="text-body-md font-bold text-secondary">Google Trends</p>
                <p className="text-label-sm text-on-surface-variant">RSS miễn phí, không cần key.</p>
              </div>
              <ToggleSwitch checked={googleEnabled} onChange={setGoogleEnabled} />
            </div>

            <div className="border-t border-outline pt-3">
              <div className="flex items-center justify-between gap-3">
                <div>
                  <p className="text-body-md font-bold text-secondary">YouTube</p>
                  <p className="text-label-sm text-on-surface-variant">
                    Cần API key (YouTube Data API v3, miễn phí 10.000 units/ngày).
                  </p>
                </div>
                <ToggleSwitch checked={youTubeEnabled} onChange={setYouTubeEnabled} />
              </div>
              <div className="mt-2 space-y-2">
                <Input
                  type="password"
                  value={youTubeApiKey}
                  onChange={(event) => {
                    setYouTubeApiKey(event.target.value);
                    if (event.target.value) setClearYouTubeKey(false);
                  }}
                  placeholder={settings?.youTube.hasApiKey ? "•••••• (đã lưu — nhập để thay)" : "Dán API key"}
                  autoComplete="off"
                />
                {settings?.youTube.hasApiKey ? (
                  <label className="flex items-center gap-2 text-label-sm text-on-surface-variant">
                    <input
                      type="checkbox"
                      checked={clearYouTubeKey}
                      onChange={(event) => setClearYouTubeKey(event.target.checked)}
                    />
                    Xóa key đã lưu
                  </label>
                ) : null}
              </div>
            </div>

            <div className="border-t border-outline pt-3">
              <div className="flex items-center justify-between gap-3">
                <div>
                  <p className="text-body-md font-bold text-secondary">TikTok (thử nghiệm)</p>
                  <p className="text-label-sm text-on-surface-variant">
                    Quét HTML tĩnh từ URL công khai (https). Trang render bằng JS sẽ không đọc được.
                  </p>
                </div>
                <ToggleSwitch checked={tikTokEnabled} onChange={setTikTokEnabled} />
              </div>
              <div className="mt-2">
                <Input
                  value={tikTokUrl}
                  onChange={(event) => setTikTokUrl(event.target.value)}
                  placeholder="https://..."
                />
              </div>
            </div>
          </div>

          <div>
            <label className="mb-1 block text-body-md font-bold text-secondary" htmlFor="trend-cadence">
              Lịch quét tự động
            </label>
            <select id="trend-cadence" className={SELECT_CLASS} value={cadence} onChange={(event) => setCadence(event.target.value)}>
              {CADENCES.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
            <p className="mt-1 text-label-sm text-on-surface-variant">
              {cadence === "off"
                ? "Hệ thống vẫn giữ quét nền hằng tuần mặc định."
                : "Lưu xong sẽ chạy lần quét đầu tiên trong khoảng 1 phút."}
              {nextRunAt ? ` Lần chạy kế tiếp: ${new Date(nextRunAt).toLocaleString("vi-VN")}.` : ""}
            </p>
          </div>
        </div>
      )}
    </Modal>
  );
}
