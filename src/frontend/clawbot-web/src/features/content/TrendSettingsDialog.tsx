import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Input, Modal, ToggleSwitch } from "@/shared/ui";
import { toUserFriendlyError } from "@/shared/utils/userText";
import { getTrendSettings, updateTrendSettings, type TrendSettings } from "@/shared/api/content";

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

type FormState = {
  geo: string;
  googleEnabled: boolean;
  youTubeEnabled: boolean;
  youTubeApiKey: string;
  clearYouTubeKey: boolean;
  tikTokEnabled: boolean;
  tikTokUrl: string;
  cadence: string;
};

const DEFAULT_FORM: FormState = {
  geo: "VN",
  googleEnabled: true,
  youTubeEnabled: true,
  youTubeApiKey: "",
  clearYouTubeKey: false,
  tikTokEnabled: false,
  tikTokUrl: "",
  cadence: "off",
};

function formFromSettings(settings: TrendSettings): FormState {
  return {
    geo: settings.geo,
    googleEnabled: settings.google.enabled,
    youTubeEnabled: settings.youTube.enabled,
    youTubeApiKey: "",
    clearYouTubeKey: false,
    tikTokEnabled: settings.tikTok.enabled,
    tikTokUrl: settings.tikTok.url ?? "",
    cadence: settings.schedule.cadence,
  };
}

export function TrendSettingsDialog({ open, onClose }: TrendSettingsDialogProps) {
  const queryClient = useQueryClient();
  const settingsQuery = useQuery({
    queryKey: ["content", "trend-settings"],
    queryFn: getTrendSettings,
    enabled: open,
  });

  const [form, setForm] = useState<FormState>(DEFAULT_FORM);
  const [hydratedSettings, setHydratedSettings] = useState<TrendSettings | null>(null);

  const settings = settingsQuery.data;
  if (settings && settings !== hydratedSettings) {
    setHydratedSettings(settings);
    setForm(formFromSettings(settings));
  }

  const saveMutation = useMutation({
    mutationFn: () =>
      updateTrendSettings({
        geo: form.geo.trim() || null,
        google: { enabled: form.googleEnabled },
        youTube: {
          enabled: form.youTubeEnabled,
          // null = giữ key đã lưu; "" = xoá; giá trị mới = thay
          apiKey: form.clearYouTubeKey ? "" : form.youTubeApiKey.trim() || null,
        },
        tikTok: { enabled: form.tikTokEnabled, url: form.tikTokUrl.trim() },
        scheduleCadence: form.cadence,
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
              value={form.geo}
              onChange={(event) => setForm((current) => ({ ...current, geo: event.target.value.toUpperCase() }))}
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
              <ToggleSwitch
                checked={form.googleEnabled}
                onChange={(checked) => setForm((current) => ({ ...current, googleEnabled: checked }))}
              />
            </div>

            <div className="border-t border-outline pt-3">
              <div className="flex items-center justify-between gap-3">
                <div>
                  <p className="text-body-md font-bold text-secondary">YouTube</p>
                  <p className="text-label-sm text-on-surface-variant">
                    Cần API key (YouTube Data API v3, miễn phí 10.000 units/ngày).
                  </p>
                </div>
                <ToggleSwitch
                  checked={form.youTubeEnabled}
                  onChange={(checked) => setForm((current) => ({ ...current, youTubeEnabled: checked }))}
                />
              </div>
              <div className="mt-2 space-y-2">
                <Input
                  type="password"
                  value={form.youTubeApiKey}
                  onChange={(event) => {
                    const value = event.target.value;
                    setForm((current) => ({
                      ...current,
                      youTubeApiKey: value,
                      clearYouTubeKey: value ? false : current.clearYouTubeKey,
                    }));
                  }}
                  placeholder={settings?.youTube.hasApiKey ? "•••••• (đã lưu — nhập để thay)" : "Dán API key"}
                  autoComplete="off"
                />
                {settings?.youTube.hasApiKey ? (
                  <label className="flex items-center gap-2 text-label-sm text-on-surface-variant">
                    <input
                      type="checkbox"
                      checked={form.clearYouTubeKey}
                      onChange={(event) => setForm((current) => ({ ...current, clearYouTubeKey: event.target.checked }))}
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
                <ToggleSwitch
                  checked={form.tikTokEnabled}
                  onChange={(checked) => setForm((current) => ({ ...current, tikTokEnabled: checked }))}
                />
              </div>
              <div className="mt-2">
                <Input
                  value={form.tikTokUrl}
                  onChange={(event) => setForm((current) => ({ ...current, tikTokUrl: event.target.value }))}
                  placeholder="https://..."
                />
              </div>
            </div>
          </div>

          <div>
            <label className="mb-1 block text-body-md font-bold text-secondary" htmlFor="trend-cadence">
              Lịch quét tự động
            </label>
            <select
              id="trend-cadence"
              className={SELECT_CLASS}
              value={form.cadence}
              onChange={(event) => setForm((current) => ({ ...current, cadence: event.target.value }))}
            >
              {CADENCES.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
            <p className="mt-1 text-label-sm text-on-surface-variant">
              {form.cadence === "off"
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
