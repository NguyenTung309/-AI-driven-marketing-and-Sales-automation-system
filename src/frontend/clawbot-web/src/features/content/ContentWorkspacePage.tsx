import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AppShell } from "@/shared/layout/AppShell";
import { Alert, Button, Card, Modal, StatusPill, type StatusTone } from "@/shared/ui";
import { toUserFriendlyError } from "@/shared/utils/userText";
import {
  approveContentItem,
  createContentBrief,
  deleteContentBrief,
  deleteContentItem,
  deleteContentSchedule,
  generateContentItems,
  getContentCalendar,
  getContentQueue,
  getContentTrends,
  listContentBriefs,
  rejectContentItem,
  repurposeContentItem,
  scanContentTrends,
  scheduleContentItem,
  updateContentBrief,
  updateContentItem,
  uploadContentAsset,
  type ContentBrief,
  type ContentCalendarItem,
  type ContentItem,
  type Trend,
} from "@/shared/api/content";

type QueueStatusFilter = "all" | "draft" | "approved" | "scheduled" | "published" | "rejected";
type ScheduleMode = "golden" | "specific";
type NoticeTone = "info" | "success" | "warning" | "error";

interface NoticeState {
  readonly tone: NoticeTone;
  readonly message: string;
}

interface ContentAsset {
  readonly type?: string;
  readonly url?: string;
  readonly fileName?: string;
  readonly contentType?: string;
}

interface EditorDraft {
  readonly itemId: string;
  readonly body: string;
  readonly assetsJson: string;
}

interface PlatformConfig {
  readonly value: string;
  readonly label: string;
  readonly icon: string;
  readonly accent: string;
}

const PLATFORMS: readonly PlatformConfig[] = [
  { value: "facebook", label: "Facebook", icon: "thumb_up", accent: "bg-blue-50 text-blue-700 border-blue-100" },
  { value: "zalo", label: "Zalo", icon: "chat", accent: "bg-emerald-50 text-emerald-700 border-emerald-100" },
  { value: "tiktok", label: "TikTok", icon: "music_note", accent: "bg-slate-100 text-slate-800 border-slate-200" },
  { value: "website", label: "Trang web", icon: "language", accent: "bg-amber-50 text-amber-700 border-amber-100" },
];

const STATUS_FILTERS: readonly { readonly value: QueueStatusFilter; readonly label: string }[] = [
  { value: "all", label: "Tất cả" },
  { value: "draft", label: "Chờ duyệt" },
  { value: "approved", label: "Đã duyệt" },
  { value: "scheduled", label: "Đã lên lịch" },
  { value: "published", label: "Đã đăng" },
  { value: "rejected", label: "Từ chối" },
];

const EMPTY_BRIEFS: readonly ContentBrief[] = [];
const EMPTY_ITEMS: readonly ContentItem[] = [];
const EMPTY_CALENDAR: readonly ContentCalendarItem[] = [];
const EMPTY_TRENDS: readonly Trend[] = [];

function normalize(value: string | null | undefined): string {
  return (value ?? "").trim().toLowerCase();
}

function platformConfig(platform: string): PlatformConfig {
  const value = normalize(platform);
  return PLATFORMS.find((item) => item.value === value || value.includes(item.value)) ?? {
    value: platform || "unknown",
    label: platform || "Khác",
    icon: "campaign",
    accent: "bg-surface-container text-on-surface-variant border-outline",
  };
}

function statusLabel(status: string): string {
  const value = normalize(status);
  if (value === "draft") return "Chờ duyệt";
  if (value === "approved") return "Đã duyệt";
  if (value === "scheduled") return "Đã lên lịch";
  if (value === "published") return "Đã đăng";
  if (value === "rejected") return "Từ chối";
  if (value === "pending") return "Yêu cầu mới";
  return status || "Không rõ";
}

function statusTone(status: string): StatusTone {
  const value = normalize(status);
  if (value === "approved" || value === "published") return "success";
  if (value === "scheduled" || value === "pending") return "warning";
  if (value === "rejected" || value === "failed") return "error";
  return "neutral";
}

function formatDateTime(value: string | null | undefined): string {
  if (!value) return "Chưa có";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("vi-VN", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}

function formatShortDate(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("vi-VN", { weekday: "short", day: "2-digit", month: "2-digit" }).format(date);
}

function toInputDate(value: Date): string {
  const year = value.getFullYear();
  const month = String(value.getMonth() + 1).padStart(2, "0");
  const day = String(value.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function defaultScheduleDate(): string {
  const tomorrow = new Date();
  tomorrow.setDate(tomorrow.getDate() + 1);
  return toInputDate(tomorrow);
}

function buildCalendarRange(): { readonly from: string; readonly to: string } {
  const from = new Date();
  from.setHours(0, 0, 0, 0);
  const to = new Date(from);
  to.setDate(to.getDate() + 30);
  return { from: from.toISOString(), to: to.toISOString() };
}

function scheduledAtIso(mode: ScheduleMode, date: string, time: string): string | null {
  if (mode === "golden") return null;
  const local = new Date(`${date}T${time || "09:00"}:00`);
  if (Number.isNaN(local.getTime())) return null;
  return local.toISOString();
}

function errorMessage(error: unknown): string {
  return toUserFriendlyError(error, "Không xử lý được thao tác nội dung. Vui lòng thử lại.");
}

function parseAssets(value: string): readonly ContentAsset[] {
  if (!value || value === "[]") return [];
  try {
    const parsed = JSON.parse(value) as unknown;
    if (!Array.isArray(parsed)) return [];
    return parsed.filter((item): item is ContentAsset => typeof item === "object" && item !== null && "url" in item);
  } catch {
    return [];
  }
}

function assetsSummary(value: string): string {
  const count = parseAssets(value).length;
  return count ? `${count} tệp đính kèm` : "Chưa có tệp đính kèm";
}

function firstImageAsset(value: string): ContentAsset | null {
  return parseAssets(value).find((asset) => asset.url && (!asset.type || asset.type === "image")) ?? null;
}

function groupCalendar(items: readonly ContentCalendarItem[]) {
  const groups = new Map<string, ContentCalendarItem[]>();
  for (const item of items) {
    const day = new Date(item.scheduledAt);
    const key = Number.isNaN(day.getTime()) ? item.scheduledAt : toInputDate(day);
    groups.set(key, [...(groups.get(key) ?? []), item]);
  }
  return Array.from(groups.entries()).sort(([a], [b]) => a.localeCompare(b));
}

function compactBody(body: string, max = 120): string {
  const clean = body.replace(/\s+/g, " ").trim();
  if (clean.length <= max) return clean;
  return `${clean.slice(0, max - 1)}…`;
}

function platformOptions(exclude?: string): readonly PlatformConfig[] {
  const excluded = normalize(exclude);
  return PLATFORMS.filter((item) => item.value !== excluded);
}

function MetricTile({ icon, label, value, meta }: { readonly icon: string; readonly label: string; readonly value: string | number; readonly meta: string }) {
  return (
    <Card>
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-label-caps uppercase text-on-surface-variant">{label}</p>
          <p className="mt-2 text-telemetry-data text-secondary">{value}</p>
          <p className="mt-1 text-label-sm text-on-surface-variant">{meta}</p>
        </div>
        <span aria-hidden="true" className="material-symbols-outlined rounded bg-primary/10 p-2 text-primary">{icon}</span>
      </div>
    </Card>
  );
}

function PlatformBadge({ platform }: { readonly platform: string }) {
  const config = platformConfig(platform);
  return (
    <span className={`inline-flex items-center gap-1.5 rounded border px-2 py-1 font-mono text-mono-status ${config.accent}`}>
      <span aria-hidden="true" className="material-symbols-outlined text-[16px]">{config.icon}</span>
      {config.label}
    </span>
  );
}

function BriefEditor({
  briefs,
  selectedId,
  platform,
  briefText,
  saving,
  deleting,
  generating,
  error,
  onSelect,
  onNew,
  onPlatform,
  onBriefText,
  onSave,
  onDelete,
  onGenerate,
}: {
  readonly briefs: readonly ContentBrief[];
  readonly selectedId: string | null;
  readonly platform: string;
  readonly briefText: string;
  readonly saving: boolean;
  readonly deleting: boolean;
  readonly generating: boolean;
  readonly error: unknown;
  readonly onSelect: (brief: ContentBrief) => void;
  readonly onNew: () => void;
  readonly onPlatform: (value: string) => void;
  readonly onBriefText: (value: string) => void;
  readonly onSave: () => void;
  readonly onDelete: () => void;
  readonly onGenerate: () => void;
}) {
  return (
    <Card>
      <div className="mb-4 flex items-start justify-between gap-3">
        <div>
          <h2 className="text-headline-sm text-secondary">Yêu cầu nội dung</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">Tạo yêu cầu cho agent marketing và sinh bản nháp.</p>
        </div>
        <Button type="button" variant="outline" className="text-nowrap" size="sm" onClick={onNew}>
          <span aria-hidden="true" className="material-symbols-outlined text-[16px]">add</span>
          Yêu cầu mới
        </Button>
      </div>

      {error ? <Alert tone="error">{errorMessage(error)}</Alert> : null}

      <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-[150px_minmax(0,1fr)]">
        <label className="block">
          <span className="mb-1 block text-label-caps uppercase text-secondary">Kênh</span>
          <select
            className="w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
            value={platform}
            onChange={(event) => onPlatform(event.target.value)}
          >
            {PLATFORMS.map((item) => (
              <option key={item.value} value={item.value}>
                {item.label}
              </option>
            ))}
          </select>
        </label>
        <label className="block">
          <span className="mb-1 block text-label-caps uppercase text-secondary">Mục tiêu chiến dịch</span>
          <input
            className="w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
            value={briefText.split("\n")[0] ?? ""}
            onChange={(event) => {
              const tail = briefText.split("\n").slice(1).join("\n");
              onBriefText(tail ? `${event.target.value}\n${tail}` : event.target.value);
            }}
            placeholder="VD: Tuyển sinh lớp HSK 4 cấp tốc tháng 9"
          />
        </label>
      </div>

      <label className="mt-3 block">
        <span className="mb-1 block text-label-caps uppercase text-secondary">Nội dung yêu cầu</span>
        <textarea
          className="min-h-[180px] w-full resize-y rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
          value={briefText}
          onChange={(event) => onBriefText(event.target.value)}
          placeholder="Đối tượng, ưu đãi, thông điệp chính, CTA, giọng văn..."
        />
      </label>

      <div className="mt-4 flex flex-wrap gap-2">
        <Button type="button" onClick={onSave} disabled={saving || !briefText.trim()}>
          <span aria-hidden="true" className="material-symbols-outlined text-[18px]">save</span>
          {saving ? "Đang lưu..." : selectedId ? "Cập nhật yêu cầu" : "Lưu yêu cầu"}
        </Button>
        <Button type="button" variant="outline" onClick={onGenerate} disabled={generating || !briefText.trim()}>
          <span aria-hidden="true" className="material-symbols-outlined text-[18px]">auto_awesome</span>
          {generating ? "Đang sinh..." : "Sinh bài nháp"}
        </Button>
        {selectedId ? (
          <Button type="button" variant="ghost" onClick={onDelete} disabled={deleting}>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">archive</span>
            Lưu trữ
          </Button>
        ) : null}
      </div>

      <div className="mt-5 space-y-2">
        <p className="text-label-caps uppercase text-secondary">Yêu cầu gần đây</p>
        {briefs.length ? (
          briefs.slice(0, 5).map((brief) => (
            <button
              key={brief.id}
              className={`w-full rounded-lg border p-3 text-left transition-colors ${
                brief.id === selectedId ? "border-primary bg-red-50" : "border-outline bg-surface hover:border-primary/40"
              }`}
              type="button"
              onClick={() => onSelect(brief)}
            >
              <div className="mb-2 flex items-center justify-between gap-2">
                <PlatformBadge platform={brief.platform} />
                <StatusPill tone={statusTone(brief.status)}>{statusLabel(brief.status)}</StatusPill>
              </div>
              <p className="text-body-md font-semibold text-secondary">{compactBody(brief.brief, 92)}</p>
              <p className="mt-1 text-label-sm text-on-surface-variant">Cập nhật {formatDateTime(brief.updatedAt)}</p>
            </button>
          ))
        ) : (
          <div className="rounded-lg border border-dashed border-outline bg-surface p-4 text-body-md text-on-surface-variant">
            Chưa có yêu cầu nội dung nào.
          </div>
        )}
      </div>
    </Card>
  );
}

function TrendPanel({
  trends,
  loading,
  scanning,
  error,
  onScan,
  onUseIdea,
}: {
  readonly trends: readonly Trend[];
  readonly loading: boolean;
  readonly scanning: boolean;
  readonly error: unknown;
  readonly onScan: () => void;
  readonly onUseIdea: (idea: string) => void;
}) {
  return (
    <Card>
      <div className="mb-4 flex items-start justify-between gap-3">
        <div>
          <h2 className="text-headline-sm text-secondary">Xu hướng tuần</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">Nguồn từ hệ thống xu hướng và agent nghiên cứu.</p>
        </div>
        <Button type="button" variant="outline" size="sm" onClick={onScan} disabled={scanning}>
          <span aria-hidden="true" className="material-symbols-outlined text-[16px]">travel_explore</span>
          {scanning ? "Đang quét" : "Quét"}
        </Button>
      </div>
      {error ? <Alert tone="error">{errorMessage(error)}</Alert> : null}
      {loading ? (
        <p className="text-body-md text-on-surface-variant">Đang tải xu hướng...</p>
      ) : trends.length ? (
        <div className="space-y-3">
          {trends.slice(0, 4).map((trend) => (
            <article key={`${trend.source}-${trend.topic}`} className="rounded-lg border border-outline bg-surface p-3">
              <div className="mb-2 flex items-start justify-between gap-3">
                <div>
                  <p className="text-body-md font-bold text-secondary">{trend.topic}</p>
                  <p className="text-label-sm text-on-surface-variant">
                    {trend.source} · {trend.metric}
                  </p>
                </div>
                <span className="rounded bg-primary/10 px-2 py-1 font-mono text-mono-status text-primary">
                  {Math.round(trend.relevanceScore * 100)}%
                </span>
              </div>
              <div className="space-y-2">
                {trend.contentIdeas.slice(0, 2).map((idea) => (
                  <button
                    key={idea}
                    type="button"
                    onClick={() => onUseIdea(idea)}
                    className="block w-full rounded border border-outline bg-white px-3 py-2 text-left text-label-sm text-on-surface hover:border-primary"
                  >
                    {idea}
                  </button>
                ))}
              </div>
            </article>
          ))}
        </div>
      ) : (
        <div className="rounded-lg border border-dashed border-outline bg-surface p-4 text-body-md text-on-surface-variant">
          Chưa có xu hướng được quét. Bấm Quét để gọi agent nghiên cứu.
        </div>
      )}
    </Card>
  );
}

function QueueList({
  items,
  selectedId,
  onSelect,
}: {
  readonly items: readonly ContentItem[];
  readonly selectedId: string | null;
  readonly onSelect: (item: ContentItem) => void;
}) {
  if (!items.length) {
    return (
      <div className="flex min-h-[320px] items-center justify-center rounded-lg border border-dashed border-outline bg-surface p-6 text-center text-body-md text-on-surface-variant">
        Hàng đợi đang trống với bộ lọc hiện tại.
      </div>
    );
  }

  return (
    <div className="space-y-2">
      {items.map((item) => (
        <button
          key={item.id}
          type="button"
          onClick={() => onSelect(item)}
          className={`w-full rounded-lg border p-3 text-left transition-colors ${
            selectedId === item.id ? "border-primary bg-red-50" : "border-outline bg-white hover:border-primary/40"
          }`}
        >
          <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
            <PlatformBadge platform={item.platform} />
            <StatusPill tone={statusTone(item.status)}>{statusLabel(item.status)}</StatusPill>
          </div>
          <p className="text-body-md font-semibold text-secondary">{compactBody(item.body, 96)}</p>
          <div className="mt-2 flex flex-wrap items-center gap-2 text-label-sm text-on-surface-variant">
            <span>{assetsSummary(item.assetsJson)}</span>
            <span>·</span>
            <span>{formatDateTime(item.updatedAt)}</span>
          </div>
        </button>
      ))}
    </div>
  );
}

function SocialPreview({ item, body, assetsJson }: { readonly item: ContentItem; readonly body: string; readonly assetsJson: string }) {
  const config = platformConfig(item.platform);
  const image = firstImageAsset(assetsJson);
  return (
    <div className="rounded-lg border border-outline bg-white">
      <div className="flex items-center justify-between border-b border-outline p-4">
        <div className="flex items-center gap-3">
          <div className="flex size-10 items-center justify-center rounded-full bg-primary text-label-sm font-bold text-white">HB</div>
          <div>
            <p className="text-body-md font-bold text-secondary">Học Bá AI</p>
            <p className="text-label-sm text-on-surface-variant">{config.label} · Agent nội dung</p>
          </div>
        </div>
        <span aria-hidden="true" className="material-symbols-outlined text-on-surface-variant">more_horiz</span>
      </div>
      <div className="space-y-3 p-4">
        <p className="whitespace-pre-wrap text-body-md text-on-surface">{body || "Nội dung bản nháp sẽ hiển thị ở đây."}</p>
        {image?.url ? (
          <img className="max-h-[320px] w-full rounded-lg object-cover" src={image.url} alt={image.fileName || "Ảnh bài viết"} />
        ) : (
          <div className="flex min-h-[180px] items-center justify-center rounded-lg border border-dashed border-outline bg-surface">
            <div className="text-center">
              <span aria-hidden="true" className="material-symbols-outlined text-[36px] text-primary">image</span>
              <p className="mt-2 text-label-sm text-on-surface-variant">{assetsSummary(assetsJson)}</p>
            </div>
          </div>
        )}
      </div>
      <div className="grid grid-cols-3 border-t border-outline text-label-sm text-on-surface-variant">
        <button className="flex items-center justify-center gap-2 py-3 hover:bg-surface" type="button">
          <span aria-hidden="true" className="material-symbols-outlined text-[16px]">thumb_up</span>
          Thích
        </button>
        <button className="flex items-center justify-center gap-2 py-3 hover:bg-surface" type="button">
          <span aria-hidden="true" className="material-symbols-outlined text-[16px]">comment</span>
          Bình luận
        </button>
        <button className="flex items-center justify-center gap-2 py-3 hover:bg-surface" type="button">
          <span aria-hidden="true" className="material-symbols-outlined text-[16px]">share</span>
          Chia sẻ
        </button>
      </div>
    </div>
  );
}

function QueueEditor({
  item,
  body,
  assetsJson,
  saving,
  uploading,
  acting,
  onBody,
  onUploadAsset,
  onSave,
  onApprove,
  onReject,
  onSchedule,
  onRepurpose,
  onDelete,
}: {
  readonly item: ContentItem | null;
  readonly body: string;
  readonly assetsJson: string;
  readonly saving: boolean;
  readonly uploading: boolean;
  readonly acting: boolean;
  readonly onBody: (value: string) => void;
  readonly onUploadAsset: (file: File) => void;
  readonly onSave: () => void;
  readonly onApprove: () => void;
  readonly onReject: () => void;
  readonly onSchedule: () => void;
  readonly onRepurpose: (targets: readonly string[]) => void;
  readonly onDelete: () => void;
}) {
  const [repurposeTargets, setRepurposeTargets] = useState<readonly string[]>(() =>
    platformOptions(item?.platform).slice(0, 2).map((option) => option.value)
  );
  const assets = parseAssets(assetsJson);

  if (!item) {
    return (
      <div className="flex min-h-[520px] items-center justify-center rounded-lg border border-dashed border-outline bg-surface p-6 text-center text-body-md text-on-surface-variant">
        Chọn một bài trong hàng đợi để chỉnh sửa trực tiếp.
      </div>
    );
  }

  const canSchedule = normalize(item.status) === "approved";
  const canApprove = !["approved", "scheduled", "published"].includes(normalize(item.status));

  function toggleTarget(value: string) {
    setRepurposeTargets((old) => (old.includes(value) ? old.filter((itemValue) => itemValue !== value) : [...old, value]));
  }

  return (
    <div className="grid grid-cols-1 gap-4 2xl:grid-cols-[minmax(0,1fr)_minmax(360px,0.9fr)]">
      <div className="space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-2">
            <PlatformBadge platform={item.platform} />
            <StatusPill tone={statusTone(item.status)}>{statusLabel(item.status)}</StatusPill>
          </div>
          <span className="font-mono text-mono-status text-on-surface-variant">#{item.id.slice(0, 8)}</span>
        </div>

        <label className="block">
          <span className="mb-1 block text-label-caps uppercase text-secondary">Nội dung bài viết</span>
          <textarea
            className="min-h-[260px] w-full resize-y rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
            value={body}
            onChange={(event) => onBody(event.target.value)}
          />
        </label>
        <div className="rounded-lg border border-outline bg-surface p-3">
          <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
            <div>
              <p className="text-label-caps uppercase text-secondary">Hình ảnh bài đăng</p>
              <p className="text-label-sm text-on-surface-variant">Tải ảnh PNG, JPG, WebP hoặc GIF.</p>
            </div>
            <label className="inline-flex cursor-pointer items-center gap-2 rounded border border-primary bg-white px-3 py-2 text-label-sm font-semibold text-primary hover:bg-primary/5">
              <span aria-hidden="true" className="material-symbols-outlined text-[18px]">upload</span>
              {uploading ? "Đang tải..." : "Tải ảnh"}
              <input
                className="sr-only"
                type="file"
                accept="image/png,image/jpeg,image/webp,image/gif"
                disabled={uploading}
                onChange={(event) => {
                  const file = event.target.files?.[0];
                  event.currentTarget.value = "";
                  if (file) onUploadAsset(file);
                }}
              />
            </label>
          </div>
          {assets.length ? (
            <div className="grid grid-cols-2 gap-3 md:grid-cols-3">
              {assets.map((asset) => (
                <a key={asset.url} className="group block overflow-hidden rounded border border-outline bg-white" href={asset.url} target="_blank" rel="noreferrer">
                  <img className="h-28 w-full object-cover transition-transform group-hover:scale-[1.03]" src={asset.url} alt={asset.fileName || "Ảnh bài viết"} />
                  <span className="block truncate px-2 py-1 text-label-sm text-on-surface-variant">{asset.fileName || asset.url}</span>
                </a>
              ))}
            </div>
          ) : (
            <div className="rounded border border-dashed border-outline bg-white p-4 text-body-md text-on-surface-variant">
              Chưa có ảnh. Bấm Tải ảnh để gắn media vào bài.
            </div>
          )}
        </div>

        <div className="flex flex-wrap gap-2">
          <Button type="button" onClick={onSave} disabled={saving || !body.trim()}>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">save</span>
            {saving ? "Đang lưu..." : "Lưu sửa đổi"}
          </Button>
          <Button type="button" variant="outline" onClick={onApprove} disabled={acting || !canApprove}>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">verified</span>
            Duyệt đăng
          </Button>
          <Button type="button" variant="outline" onClick={onSchedule} disabled={acting || !canSchedule}>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">event</span>
            Lên lịch
          </Button>
          <Button type="button" variant="ghost" onClick={onReject} disabled={acting || normalize(item.status) === "rejected"}>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">block</span>
            Từ chối
          </Button>
          <Button type="button" variant="ghost" onClick={onDelete} disabled={acting}>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">delete</span>
            Xóa
          </Button>
        </div>

        <div className="rounded-lg border border-outline bg-surface p-3">
          <p className="mb-2 text-label-caps uppercase text-secondary">Biến thể kênh khác</p>
          <div className="mb-3 flex flex-wrap gap-2">
            {platformOptions(item.platform).map((option) => (
              <button
                key={option.value}
                type="button"
                onClick={() => toggleTarget(option.value)}
                className={`rounded border px-3 py-1.5 text-label-sm font-semibold ${
                  repurposeTargets.includes(option.value)
                    ? "border-primary bg-primary text-on-primary"
                    : "border-outline bg-white text-on-surface-variant"
                }`}
              >
                {option.label}
              </button>
            ))}
          </div>
          <Button type="button" size="sm" variant="outline" onClick={() => onRepurpose(repurposeTargets)} disabled={acting || repurposeTargets.length === 0}>
            <span aria-hidden="true" className="material-symbols-outlined text-[16px]">dynamic_feed</span>
            Tạo biến thể
          </Button>
        </div>
      </div>
      <SocialPreview item={item} body={body} assetsJson={assetsJson} />
    </div>
  );
}

function CalendarPanel({
  items,
  loading,
  cancelingId,
  onCancel,
}: {
  readonly items: readonly ContentCalendarItem[];
  readonly loading: boolean;
  readonly cancelingId: string | null;
  readonly onCancel: (id: string) => void;
}) {
  const groups = groupCalendar(items);
  return (
    <Card>
      <div className="mb-4 flex items-start justify-between gap-3">
        <div>
          <h2 className="text-headline-sm text-secondary">Lịch xuất bản</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">Lịch xuất bản trong 30 ngày tới.</p>
        </div>
        <StatusPill tone={items.length ? "success" : "neutral"}>{items.length} lịch</StatusPill>
      </div>
      {loading ? (
        <p className="text-body-md text-on-surface-variant">Đang tải lịch...</p>
      ) : groups.length ? (
        <div className="grid grid-cols-1 gap-3 lg:grid-cols-3">
          {groups.map(([day, rows]) => (
            <div key={day} className="rounded-lg border border-outline bg-surface p-3">
              <p className="mb-3 text-label-caps uppercase text-secondary">{formatShortDate(day)}</p>
              <div className="space-y-2">
                {rows.map((row) => (
                  <div key={row.scheduleId} className="rounded border border-outline bg-white p-3">
                    <div className="mb-2 flex items-center justify-between gap-2">
                      <PlatformBadge platform={row.platform} />
                      <StatusPill tone={statusTone(row.status)}>{statusLabel(row.status)}</StatusPill>
                    </div>
                    <p className="text-body-md font-semibold text-secondary">{compactBody(row.body, 88)}</p>
                    <p className="mt-1 text-label-sm text-on-surface-variant">{formatDateTime(row.scheduledAt)}</p>
                    {normalize(row.status) === "pending" ? (
                      <button
                        type="button"
                        className="mt-2 inline-flex items-center gap-1 text-label-sm font-semibold text-error hover:underline"
                        onClick={() => onCancel(row.scheduleId)}
                        disabled={cancelingId === row.scheduleId}
                      >
                        <span aria-hidden="true" className="material-symbols-outlined text-[16px]">event_busy</span>
                        Hủy lịch
                      </button>
                    ) : null}
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>
      ) : (
        <div className="rounded-lg border border-dashed border-outline bg-surface p-4 text-body-md text-on-surface-variant">
          Chưa có bài nào được lên lịch trong 30 ngày tới.
        </div>
      )}
    </Card>
  );
}

function ScheduleDialog({
  item,
  mode,
  date,
  time,
  saving,
  error,
  onMode,
  onDate,
  onTime,
  onClose,
  onSubmit,
}: {
  readonly item: ContentItem;
  readonly mode: ScheduleMode;
  readonly date: string;
  readonly time: string;
  readonly saving: boolean;
  readonly error: unknown;
  readonly onMode: (value: ScheduleMode) => void;
  readonly onDate: (value: string) => void;
  readonly onTime: (value: string) => void;
  readonly onClose: () => void;
  readonly onSubmit: () => void;
}) {
  return (
    <Modal
      open
      title="Lên lịch xuất bản nội dung"
      onClose={onClose}
      footer={
        <>
          <Button type="button" variant="ghost" onClick={onClose} disabled={saving}>
            Hủy bỏ
          </Button>
          <Button type="button" onClick={onSubmit} disabled={saving}>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">event_available</span>
            {saving ? "Đang lên lịch..." : "Xác nhận lên lịch"}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {error ? <Alert tone="error">{errorMessage(error)}</Alert> : null}
        <div>
          <p className="mb-2 text-label-caps uppercase text-secondary">Kênh đăng tải</p>
          <PlatformBadge platform={item.platform} />
        </div>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <label className="block">
            <span className="mb-1 block text-label-caps uppercase text-secondary">Ngày</span>
            <input
              className="w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary disabled:opacity-60"
              type="date"
              value={date}
              disabled={mode === "golden"}
              onChange={(event) => onDate(event.target.value)}
            />
          </label>
          <label className="block">
            <span className="mb-1 block text-label-caps uppercase text-secondary">Giờ</span>
            <input
              className="w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary disabled:opacity-60"
              type="time"
              value={time}
              disabled={mode === "golden"}
              onChange={(event) => onTime(event.target.value)}
            />
          </label>
        </div>
        <div className="space-y-2">
          <button
            type="button"
            onClick={() => onMode("golden")}
            className={`w-full rounded-lg border p-3 text-left ${
              mode === "golden" ? "border-primary bg-red-50" : "border-outline bg-white"
            }`}
          >
            <div className="flex items-center gap-2">
              <span aria-hidden="true" className="material-symbols-outlined text-[18px] text-primary">auto_awesome</span>
              <span className="text-body-md font-bold text-secondary">Chọn giờ vàng</span>
            </div>
            <p className="mt-1 text-label-sm text-on-surface-variant">Hệ thống tự gợi ý khung giờ phù hợp kế tiếp theo từng kênh.</p>
          </button>
          <button
            type="button"
            onClick={() => onMode("specific")}
            className={`w-full rounded-lg border p-3 text-left ${
              mode === "specific" ? "border-primary bg-red-50" : "border-outline bg-white"
            }`}
          >
            <div className="flex items-center gap-2">
              <span aria-hidden="true" className="material-symbols-outlined text-[18px] text-primary">schedule</span>
              <span className="text-body-md font-bold text-secondary">Chọn thời điểm riêng</span>
            </div>
            <p className="mt-1 text-label-sm text-on-surface-variant">Chọn chính xác thời điểm muốn lên lịch.</p>
          </button>
        </div>
      </div>
    </Modal>
  );
}

export default function ContentWorkspacePage() {
  const queryClient = useQueryClient();
  const calendarRange = useMemo(() => buildCalendarRange(), []);
  const [selectedBriefId, setSelectedBriefId] = useState<string | null>(null);
  const [briefPlatform, setBriefPlatform] = useState(PLATFORMS[0].value);
  const [briefText, setBriefText] = useState("");
  const [queueStatus, setQueueStatus] = useState<QueueStatusFilter>("all");
  const [queuePlatform, setQueuePlatform] = useState("all");
  const [selectedItemId, setSelectedItemId] = useState<string | null>(null);
  const [editorDraft, setEditorDraft] = useState<EditorDraft | null>(null);
  const [scheduleItem, setScheduleItem] = useState<ContentItem | null>(null);
  const [scheduleMode, setScheduleMode] = useState<ScheduleMode>("golden");
  const [scheduleDate, setScheduleDate] = useState(defaultScheduleDate);
  const [scheduleTime, setScheduleTime] = useState("09:00");
  const [notice, setNotice] = useState<NoticeState | null>(null);

  const briefsQuery = useQuery({ queryKey: ["content", "briefs"], queryFn: () => listContentBriefs() });
  const queueQuery = useQuery({
    queryKey: ["content", "queue", queueStatus, queuePlatform],
    queryFn: () =>
      getContentQueue({
        status: queueStatus === "all" ? undefined : queueStatus,
        platform: queuePlatform === "all" ? undefined : queuePlatform,
        page: 1,
        pageSize: 80,
      }),
  });
  const calendarQuery = useQuery({
    queryKey: ["content", "calendar", calendarRange],
    queryFn: () => getContentCalendar(calendarRange),
  });
  const trendsQuery = useQuery({
    queryKey: ["content", "trends"],
    queryFn: () => getContentTrends(),
    staleTime: 60_000,
  });

  const briefs = Array.isArray(briefsQuery.data) ? briefsQuery.data : EMPTY_BRIEFS;
  const queueItems = Array.isArray(queueQuery.data?.items) ? queueQuery.data.items : EMPTY_ITEMS;
  const calendarItems = Array.isArray(calendarQuery.data?.items) ? calendarQuery.data.items : EMPTY_CALENDAR;
  const trends = Array.isArray(trendsQuery.data?.trends) ? trendsQuery.data.trends : EMPTY_TRENDS;
  const selectedItem = queueItems.find((item) => item.id === selectedItemId) ?? queueItems[0] ?? null;
  const matchingDraft = editorDraft && selectedItem && editorDraft.itemId === selectedItem.id ? editorDraft : null;
  const editorBody = matchingDraft?.body ?? selectedItem?.body ?? "";
  const editorAssets = (matchingDraft?.assetsJson ?? selectedItem?.assetsJson) || "[]";
  const draftCount = queueItems.filter((item) => normalize(item.status) === "draft").length;
  const readyCount = queueItems.filter((item) => normalize(item.status) === "approved").length;
  const activeError = briefsQuery.error ?? queueQuery.error ?? calendarQuery.error;

  const invalidateContent = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["content", "briefs"] }),
      queryClient.invalidateQueries({ queryKey: ["content", "queue"] }),
      queryClient.invalidateQueries({ queryKey: ["content", "calendar"] }),
      queryClient.invalidateQueries({ queryKey: ["content", "trends"] }),
    ]);
  };

  const saveBriefMutation = useMutation({
    mutationFn: () =>
      selectedBriefId
        ? updateContentBrief(selectedBriefId, { platform: briefPlatform, brief: briefText.trim() })
        : createContentBrief({ platform: briefPlatform, brief: briefText.trim() }),
    onSuccess: async (brief) => {
      setSelectedBriefId(brief.id);
      setBriefPlatform(brief.platform);
      setBriefText(brief.brief);
      setNotice({ tone: "success", message: "Đã lưu yêu cầu nội dung." });
      await queryClient.invalidateQueries({ queryKey: ["content", "briefs"] });
    },
  });

  const deleteBriefMutation = useMutation({
    mutationFn: (id: string) => deleteContentBrief(id),
    onSuccess: async () => {
      setSelectedBriefId(null);
      setBriefText("");
      setNotice({ tone: "success", message: "Đã lưu trữ yêu cầu nội dung." });
      await queryClient.invalidateQueries({ queryKey: ["content", "briefs"] });
    },
  });

  const generateMutation = useMutation({
    mutationFn: () =>
      selectedBriefId
        ? generateContentItems({ briefId: selectedBriefId })
        : generateContentItems({ platform: briefPlatform, briefText: briefText.trim() }),
    onSuccess: async (response) => {
      const first = response.items[0];
      if (first) {
        setSelectedItemId(first.id);
        setEditorDraft({ itemId: first.id, body: first.body, assetsJson: first.assetsJson || "[]" });
      }
      setNotice({ tone: "success", message: `Đã sinh ${response.items.length || 1} bài nháp từ agent nội dung.` });
      await queryClient.invalidateQueries({ queryKey: ["content", "queue"] });
    },
  });

  const updateItemMutation = useMutation({
    mutationFn: (item: ContentItem) =>
      updateContentItem(item.id, {
        body: editorBody.trim(),
        assetsJson: editorAssets.trim() || "[]",
      }),
    onSuccess: async (item) => {
      setEditorDraft({ itemId: item.id, body: item.body, assetsJson: item.assetsJson || "[]" });
      setNotice({ tone: "success", message: "Đã cập nhật nội dung bài viết." });
      await queryClient.invalidateQueries({ queryKey: ["content", "queue"] });
    },
  });

  const uploadAssetMutation = useMutation({
    mutationFn: ({ item, file }: { readonly item: ContentItem; readonly file: File }) => uploadContentAsset(item.id, file),
    onSuccess: async (response) => {
      if (selectedItem) setEditorDraft({ itemId: selectedItem.id, body: editorBody, assetsJson: response.assetsJson });
      setNotice({ tone: "success", message: "Đã tải ảnh lên bài viết." });
      await queryClient.invalidateQueries({ queryKey: ["content", "queue"] });
    },
  });

  const approveMutation = useMutation({
    mutationFn: (id: string) => approveContentItem(id),
    onSuccess: async () => {
      setNotice({ tone: "success", message: "Đã duyệt bài, có thể lên lịch xuất bản." });
      await queryClient.invalidateQueries({ queryKey: ["content", "queue"] });
    },
  });

  const rejectMutation = useMutation({
    mutationFn: (id: string) => rejectContentItem(id, "Từ chối trong màn hình quản lý nội dung"),
    onSuccess: async () => {
      setNotice({ tone: "warning", message: "Đã chuyển bài sang trạng thái từ chối." });
      await queryClient.invalidateQueries({ queryKey: ["content", "queue"] });
    },
  });

  const deleteItemMutation = useMutation({
    mutationFn: (id: string) => deleteContentItem(id),
    onSuccess: async () => {
      setNotice({ tone: "success", message: "Đã xóa bài khỏi hàng đợi." });
      await queryClient.invalidateQueries({ queryKey: ["content", "queue"] });
    },
  });

  const repurposeMutation = useMutation({
    mutationFn: ({ id, targets }: { readonly id: string; readonly targets: readonly string[] }) => repurposeContentItem(id, targets),
    onSuccess: async (response) => {
      setNotice({ tone: "success", message: `Đã tạo ${response.items.length} biến thể nội dung.` });
      await queryClient.invalidateQueries({ queryKey: ["content", "queue"] });
    },
  });

  const scheduleMutation = useMutation({
    mutationFn: (item: ContentItem) => scheduleContentItem(item.id, scheduledAtIso(scheduleMode, scheduleDate, scheduleTime)),
    onSuccess: async () => {
      setScheduleItem(null);
      setNotice({ tone: "success", message: "Đã lên lịch xuất bản nội dung." });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["content", "queue"] }),
        queryClient.invalidateQueries({ queryKey: ["content", "calendar"] }),
      ]);
    },
  });

  const cancelScheduleMutation = useMutation({
    mutationFn: (id: string) => deleteContentSchedule(id),
    onSuccess: async () => {
      setNotice({ tone: "success", message: "Đã hủy lịch xuất bản." });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["content", "queue"] }),
        queryClient.invalidateQueries({ queryKey: ["content", "calendar"] }),
      ]);
    },
  });

  const scanMutation = useMutation({
    mutationFn: () => scanContentTrends(),
    onSuccess: async () => {
      setNotice({ tone: "success", message: "Đã quét xu hướng mới từ agent nghiên cứu." });
      await queryClient.invalidateQueries({ queryKey: ["content", "trends"] });
    },
  });

  function selectBrief(brief: ContentBrief) {
    setSelectedBriefId(brief.id);
    setBriefPlatform(brief.platform);
    setBriefText(brief.brief);
  }

  function selectItem(item: ContentItem) {
    setSelectedItemId(item.id);
    setEditorDraft({ itemId: item.id, body: item.body, assetsJson: item.assetsJson || "[]" });
  }

  function updateEditorBody(value: string) {
    if (!selectedItem) return;
    setEditorDraft({ itemId: selectedItem.id, body: value, assetsJson: editorAssets });
  }

  function newBrief() {
    setSelectedBriefId(null);
    setBriefPlatform(PLATFORMS[0].value);
    setBriefText("");
  }

  function applyTrendIdea(idea: string) {
    setSelectedBriefId(null);
    setBriefText((old) => (old.trim() ? `${old.trim()}\n\nÝ tưởng xu hướng: ${idea}` : idea));
    setNotice({ tone: "info", message: "Đã đưa ý tưởng xu hướng vào yêu cầu nội dung." });
  }

  return (
    <AppShell title="Quản lý nội dung">
      <section className="mb-gutter rounded-lg border border-primary/20 bg-primary/5 p-4">
        <div className="flex flex-col gap-3 xl:flex-row xl:items-center xl:justify-between">
          <div>
            <h1 className="text-headline-md text-secondary">Quản lý bài viết & nội dung</h1>
            <p className="mt-1 text-body-md text-on-surface-variant">
              Quản lý yêu cầu nội dung, hàng đợi duyệt, lịch đăng và xu hướng nội dung.
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            <StatusPill tone={activeError ? "error" : "success"}>{activeError ? "Mất kết nối" : "Đã kết nối"}</StatusPill>
            <Button type="button" variant="outline" onClick={() => void invalidateContent()}>
              <span aria-hidden="true" className="material-symbols-outlined text-[18px]">refresh</span>
              Làm mới
            </Button>
            <Button type="button" onClick={() => generateMutation.mutate()} disabled={generateMutation.isPending || !briefText.trim()}>
              <span aria-hidden="true" className="material-symbols-outlined text-[18px]">add_circle</span>
              Tạo bài viết mới
            </Button>
          </div>
        </div>
      </section>

      {notice ? (
        <div className="mb-gutter">
          <Alert tone={notice.tone}>{notice.message}</Alert>
        </div>
      ) : null}

      <section className="mb-gutter grid grid-cols-1 gap-gutter sm:grid-cols-2 xl:grid-cols-4">
        <MetricTile icon="description" label="Yêu cầu đang mở" value={briefs.length} meta="Không tính yêu cầu đã lưu trữ" />
        <MetricTile icon="rate_review" label="Chờ duyệt" value={draftCount} meta={`${queueQuery.data?.total ?? 0} bài trong hàng đợi`} />
        <MetricTile icon="verified" label="Sẵn sàng lịch" value={readyCount} meta="Bài đã được duyệt" />
        <MetricTile icon="event" label="Lịch 30 ngày" value={calendarItems.length} meta={`${trends.length} xu hướng đang lưu`} />
      </section>

      <section className="grid grid-cols-1 gap-gutter xl:grid-cols-[390px_minmax(0,1fr)]">
        <div className="space-y-gutter">
          <BriefEditor
            briefs={briefs}
            selectedId={selectedBriefId}
            platform={briefPlatform}
            briefText={briefText}
            saving={saveBriefMutation.isPending}
            deleting={deleteBriefMutation.isPending}
            generating={generateMutation.isPending}
            error={saveBriefMutation.error ?? deleteBriefMutation.error ?? generateMutation.error ?? briefsQuery.error}
            onSelect={selectBrief}
            onNew={newBrief}
            onPlatform={setBriefPlatform}
            onBriefText={setBriefText}
            onSave={() => saveBriefMutation.mutate()}
            onDelete={() => {
              if (selectedBriefId) deleteBriefMutation.mutate(selectedBriefId);
            }}
            onGenerate={() => generateMutation.mutate()}
          />
          <TrendPanel
            trends={trends}
            loading={trendsQuery.isLoading}
            scanning={scanMutation.isPending}
            error={trendsQuery.error ?? scanMutation.error}
            onScan={() => scanMutation.mutate()}
            onUseIdea={applyTrendIdea}
          />
        </div>

        <div className="space-y-gutter">
          <Card>
            <div className="mb-4 flex flex-col gap-3 lg:flex-row lg:items-end lg:justify-between">
              <div>
                <h2 className="text-headline-sm text-secondary">Hàng đợi duyệt bài</h2>
                <p className="mt-1 text-body-md text-on-surface-variant">Soạn thảo trực tiếp cho bài chờ duyệt, duyệt bài và tạo biến thể sang kênh khác.</p>
              </div>
              <div className="flex flex-wrap gap-2">
                <select
                  className="rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
                  value={queuePlatform}
                  onChange={(event) => setQueuePlatform(event.target.value)}
                >
                  <option value="all">Tất cả kênh</option>
                  {PLATFORMS.map((item) => (
                    <option key={item.value} value={item.value}>
                      {item.label}
                    </option>
                  ))}
                </select>
                <select
                  className="rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
                  value={queueStatus}
                  onChange={(event) => setQueueStatus(event.target.value as QueueStatusFilter)}
                >
                  {STATUS_FILTERS.map((item) => (
                    <option key={item.value} value={item.value}>
                      {item.label}
                    </option>
                  ))}
                </select>
              </div>
            </div>

            {(updateItemMutation.error ||
              uploadAssetMutation.error ||
              approveMutation.error ||
              rejectMutation.error ||
              deleteItemMutation.error ||
              repurposeMutation.error ||
              queueQuery.error) ? (
              <div className="mb-4">
                <Alert tone="error">
                  {errorMessage(
                    updateItemMutation.error ??
                      uploadAssetMutation.error ??
                      approveMutation.error ??
                      rejectMutation.error ??
                      deleteItemMutation.error ??
                      repurposeMutation.error ??
                      queueQuery.error
                  )}
                </Alert>
              </div>
            ) : null}

            <div className="grid grid-cols-1 gap-4 2xl:grid-cols-[320px_minmax(0,1fr)]">
              <div>
                <QueueList items={queueItems} selectedId={selectedItemId} onSelect={selectItem} />
              </div>
              <QueueEditor
                key={selectedItem?.id ?? "empty"}
                item={selectedItem}
                body={editorBody}
                assetsJson={editorAssets}
                saving={updateItemMutation.isPending}
                uploading={uploadAssetMutation.isPending}
                acting={
                  approveMutation.isPending ||
                  rejectMutation.isPending ||
                  deleteItemMutation.isPending ||
                  repurposeMutation.isPending
                }
                onBody={updateEditorBody}
                onUploadAsset={(file) => {
                  if (selectedItem) uploadAssetMutation.mutate({ item: selectedItem, file });
                }}
                onSave={() => {
                  if (selectedItem) updateItemMutation.mutate(selectedItem);
                }}
                onApprove={() => {
                  if (selectedItem) approveMutation.mutate(selectedItem.id);
                }}
                onReject={() => {
                  if (selectedItem) rejectMutation.mutate(selectedItem.id);
                }}
                onSchedule={() => {
                  if (selectedItem) setScheduleItem(selectedItem);
                }}
                onRepurpose={(targets) => {
                  if (selectedItem) repurposeMutation.mutate({ id: selectedItem.id, targets });
                }}
                onDelete={() => {
                  if (selectedItem) deleteItemMutation.mutate(selectedItem.id);
                }}
              />
            </div>
          </Card>

          <CalendarPanel
            items={calendarItems}
            loading={calendarQuery.isLoading}
            cancelingId={cancelScheduleMutation.isPending ? cancelScheduleMutation.variables ?? null : null}
            onCancel={(id) => cancelScheduleMutation.mutate(id)}
          />
        </div>
      </section>

      {scheduleItem ? (
        <ScheduleDialog
          item={scheduleItem}
          mode={scheduleMode}
          date={scheduleDate}
          time={scheduleTime}
          saving={scheduleMutation.isPending}
          error={scheduleMutation.error}
          onMode={setScheduleMode}
          onDate={setScheduleDate}
          onTime={setScheduleTime}
          onClose={() => setScheduleItem(null)}
          onSubmit={() => scheduleMutation.mutate(scheduleItem)}
        />
      ) : null}
    </AppShell>
  );
}
