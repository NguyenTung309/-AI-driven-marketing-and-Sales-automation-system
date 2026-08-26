import { useEffect, useMemo, useRef, useState } from "react";
import { isAxiosError } from "axios";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSearchParams } from "react-router-dom";
import { AppShell } from "@/shared/layout/AppShell";
import {
  Alert,
  Button,
  Card,
  Modal,
  StatusPill,
  type StatusTone,
} from "@/shared/ui";
import { useAuthStore } from "@/shared/auth/authStore";
import { useJobWatcher } from "@/features/jobs/useJobWatcher";
import { TrendSettingsDialog } from "./TrendSettingsDialog";
import { ContentPublishingPolicyControl } from "./ContentPublishingPolicyControl";
import { PostPerformancePanel } from "./PostPerformancePanel";
import { ContentAssetImage } from "@/shared/content/ContentAssetImage";
import { assetsSummary, firstImageAsset, parseAssets } from "@/shared/content/assets";
import { platformClasses } from "@/shared/theme/colors";
import { contentErrorMessage, toUserFriendlyError } from "@/shared/utils/userText";
import {
  approveContentItem,
  createContentBrief,
  deleteContentBrief,
  deleteContentItem,
  deleteContentSchedule,
  generateContentItems,
  getContentCalendar,
  getContentChainMetrics,
  getContentItem,
  getContentItemHooks,
  getContentPublishTargets,
  getContentQueue,
  getContentTrends,
  listContentBriefs,
  regenerateContentHook,
  rejectContentItem,
  repurposeContentItem,
  retryAgentReview,
  retryContentSchedule,
  scanContentTrends,
  scheduleContentItem,
  updateContentBrief,
  updateContentItem,
  uploadContentAsset,
  type ContentBrief,
  type ContentCalendarItem,
  type ContentHookOption,
  type ContentItem,
  type ContentPublishTarget,
  type ContentPublishTargetMode,
  type ScheduleContentItemPayload,
  type RawTrend,
  type Trend,
} from "@/shared/api/content";

type QueueStatusFilter = "all" | "draft" | "approved" | "scheduled" | "published" | "rejected";
type ContentWorkspaceTab = "queue" | "calendar" | "metrics" | "performance";
type ScheduleMode = "golden" | "specific";
type NoticeTone = "info" | "success" | "warning" | "error";

interface NoticeState {
  readonly tone: NoticeTone;
  readonly message: string;
}

interface ScheduleTargetState {
  readonly isExistingSchedule: boolean;
  readonly originalMetaAssetId: string | null;
  readonly explicitMetaAssetId: string | null;
  readonly requiresInstagramAccountConfirmation: boolean;
  readonly confirmInstagramAccount: boolean;
}

interface ScheduleMutationVariables {
  readonly item: ContentItem;
  readonly session: number;
  readonly mode: ScheduleMode;
  readonly payload: ScheduleContentItemPayload;
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
  { value: "facebook", label: "Facebook", icon: "thumb_up", accent: platformClasses("facebook") },
  { value: "zalo", label: "Zalo", icon: "chat", accent: platformClasses("zalo") },
  { value: "instagram", label: "Instagram", icon: "photo_camera", accent: platformClasses("instagram") },
];

const LEGACY_PLATFORM_METADATA: readonly PlatformConfig[] = [
  { value: "tiktok", label: "TikTok", icon: "music_note", accent: platformClasses("tiktok") },
  { value: "youtube", label: "YouTube", icon: "play_circle", accent: platformClasses("youtube") },
  { value: "website", label: "Trang web", icon: "language", accent: platformClasses("website") },
];

const STATUS_FILTERS: readonly { readonly value: QueueStatusFilter; readonly label: string }[] = [
  { value: "all", label: "Tất cả" },
  { value: "draft", label: "Chờ duyệt" },
  { value: "approved", label: "Đã duyệt" },
  { value: "scheduled", label: "Đã lên lịch" },
  { value: "published", label: "Đã đăng" },
  { value: "rejected", label: "Từ chối" },
];

const QUEUE_PAGE_SIZE = 8;

function generatePageNumbers(current: number, total: number): (number | "...")[] {
  if (total <= 5) {
    return Array.from({ length: total }, (_, i) => i + 1);
  }
  if (current <= 3) {
    return [1, 2, 3, "...", total];
  }
  if (current >= total - 2) {
    return [1, "...", total - 3, total - 2, total - 1, total];
  }
  return [1, "...", current - 1, current, current + 1, "...", total];
}

const EMPTY_BRIEFS: readonly ContentBrief[] = [];
const EMPTY_ITEMS: readonly ContentItem[] = [];
const EMPTY_CALENDAR: readonly ContentCalendarItem[] = [];
const EMPTY_TRENDS: readonly Trend[] = [];
const EMPTY_HOOKS: readonly ContentHookOption[] = [];
const CHAIN_STEP_LABELS: Readonly<Record<string, string>> = {
  plan: "Lập kế hoạch (L1)",
  outline: "Dàn ý + bằng chứng (L2)",
  write: "Viết nội dung (L3)",
  package: "Đóng gói + hashtag (L4)",
};
const EMPTY_SCHEDULE_TARGET: ScheduleTargetState = {
  isExistingSchedule: false,
  originalMetaAssetId: null,
  explicitMetaAssetId: null,
  requiresInstagramAccountConfirmation: false,
  confirmInstagramAccount: false,
};

function normalize(value: string | null | undefined): string {
  return (value ?? "").trim().toLowerCase();
}

function isWritablePlatform(platform: string): boolean {
  const value = normalize(platform);
  return PLATFORMS.some((item) => item.value === value);
}

function platformConfig(platform: string): PlatformConfig {
  const value = normalize(platform);
  return [...PLATFORMS, ...LEGACY_PLATFORM_METADATA].find(
    (item) => item.value === value,
  ) ?? {
    value: platform || "unknown",
    label: platform || "Khác",
    icon: "campaign",
    accent: "bg-surface-container text-on-surface-variant border-outline",
  };
}

function statusLabel(status: string, lastError?: string | null, scheduledAt?: string | null): string {
  const value = normalize(status);
  if (value === "draft") return "Chờ duyệt";
  if (value === "approved") return "Đã duyệt";
  if (value === "scheduled") return "Đã lên lịch";
  if (value === "published" || value === "posted") return "Đã đăng";
  if (value === "rejected") return "Từ chối";
  if (value === "failed") return "Thất bại";
  if (value === "canceled") return "Đã hủy";
  if (value === "held") return "Đang giữ";
  if (value === "publishing") return "Đang đăng";
  if (value === "outcome_unknown") return "Chờ đối soát";
  if (value === "pending") {
    if (normalize(lastError) === "held_for_review") return "Chờ review";
    if (isOverdue(scheduledAt)) return "Quá hạn";
    return "Chờ đăng";
  }
  return status || "Không rõ";
}

function statusTone(status: string, lastError?: string | null, scheduledAt?: string | null): StatusTone {
  const value = normalize(status);
  if (value === "approved" || value === "published" || value === "posted") return "success";
  if (value === "rejected" || value === "failed" || value === "canceled" || value === "outcome_unknown") return "error";
  if (value === "pending" && (isOverdue(scheduledAt) || normalize(lastError) === "held_for_review")) return "error";
  if (value === "scheduled" || value === "pending" || value === "held" || value === "publishing") return "warning";
  return "neutral";
}

function isOverdue(scheduledAt?: string | null): boolean {
  if (!scheduledAt) return false;
  const at = new Date(scheduledAt);
  if (Number.isNaN(at.getTime())) return false;
  return at.getTime() < Date.now();
}

function canRetrySchedule(status: string): boolean {
  const value = normalize(status);
  return value === "pending" || value === "failed" || value === "held";
}

function requiresMetaTarget(
  platform: string | null | undefined,
  targetMode: ContentPublishTargetMode | undefined = "linked_meta",
): boolean {
  const value = normalize(platform);
  return value === "facebook" || (value === "instagram" && targetMode === "linked_meta");
}

function lastErrorLabel(lastError: string | null | undefined): string | null {
  if (!lastError) return null;
  const value = normalize(lastError);
  if (value === "held_for_review") return "Đang giữ: cần chữ ký agent review / duyệt phát hành trước khi đăng.";
  if (value.startsWith("stale_item_status:")) return `Lịch đã hủy vì bài không còn trạng thái scheduled (${lastError.slice("stale_item_status:".length)}).`;
  if (value === "canceled_by_user") return "Đã hủy bởi người dùng.";
  if (value === "item_missing") return "Bài gắn với lịch không còn tồn tại.";
  if (value === "facebook_not_configured" || value === "publisher_not_configured") return "Chưa cấu hình kênh đăng Facebook.";
  if (value === "facebook_reconnect_required") return "Token Facebook hết hạn — cần kết nối lại Meta.";
  if (value === "instagram_not_configured") return "Đăng Instagram theo lịch đang tạm khóa cho đến khi kênh media-native được cấu hình.";
  if (value === "instagram_publishing_disabled") return "Tính năng đăng Instagram trực tiếp đang bị tắt.";
  if (value === "instagram_media_required") return "Instagram cần ít nhất một ảnh trước khi đăng.";
  if (value === "instagram_media_invalid") return "Ảnh Instagram không hợp lệ hoặc không thể được Meta truy cập.";
  if (value === "instagram_credentials_invalid") return "Thông tin Instagram độc lập không hợp lệ. Hãy sửa hoặc tắt ghi đè trong Quản trị hệ thống.";
  if (value === "instagram_target_required") return "Hãy chọn Meta Page đã liên kết Instagram trước khi đăng.";
  if (value === "instagram_target_unavailable") return "Meta Page hoặc tài khoản Instagram đã chọn không còn khả dụng. Hãy chọn lại đích đăng.";
  if (value === "instagram_permissions_missing") return "Kết nối Meta thiếu quyền đăng Instagram. Hãy cấp lại quyền cần thiết.";
  if (value === "instagram_not_linked") return "Meta Page đã chọn chưa liên kết tài khoản Instagram chuyên nghiệp.";
  if (value === "instagram_reconnect_required") return "Phiên Meta đã hết hạn hoặc không hợp lệ. Hãy kết nối lại Meta.";
  if (value === "instagram_meta_unavailable") return "Kết nối Meta chưa sẵn sàng để đăng Instagram.";
  if (value === "instagram_unavailable") return "Meta tạm thời không phản hồi. Trạng thái đăng cần được kiểm tra trước khi thử lại.";
  if (value === "instagram_timeout") return "Yêu cầu Instagram hết thời gian chờ. Hệ thống giữ trạng thái chưa xác định để tránh đăng trùng.";
  if (value.startsWith("instagram_graph_")) return "Meta từ chối yêu cầu đăng Instagram. Hãy kiểm tra quyền, media và kết nối Meta.";
  return lastError;
}

function workflowLabel(workflowState: string | null | undefined, fallbackStatus: string): string {
  const value = normalize(workflowState);
  if (value === "awaiting_agent_review") return "Chờ agent review";
  if (value === "agent_review_running") return "Agent đang review";
  if (value === "agent_review_non_pass") return "Agent không pass";
  if (value === "review_failed") return "Review lỗi";
  if (value === "awaiting_human_approval") return "Chờ duyệt phát hành";
  if (value === "approved_for_publish") return "Đã duyệt phát hành";
  if (value === "scheduled") return "Đã lên lịch giờ vàng";
  if (value === "published") return "Đã đăng";
  if (value === "rejected") return "Từ chối phát hành";
  return statusLabel(fallbackStatus);
}

function workflowTone(workflowState: string | null | undefined, fallbackStatus: string): StatusTone {
  const value = normalize(workflowState);
  if (value === "published" || value === "approved_for_publish" || value === "scheduled") return "success";
  if (value === "rejected" || value === "review_failed" || value === "agent_review_non_pass") return "error";
  if (value === "awaiting_human_approval" || value === "agent_review_running" || value === "awaiting_agent_review") return "warning";
  return statusTone(fallbackStatus);
}

function agentReviewLabel(status: string | null | undefined): string {
  const value = normalize(status);
  if (value === "passed") return "Agent: đạt";
  if (value === "rejected") return "Agent: không đạt";
  if (value === "needs_human") return "Agent: cần người";
  if (value === "failed") return "Agent: lỗi";
  if (value === "running") return "Agent: đang chạy";
  if (value === "pending") return "Agent: chờ";
  return "Agent: chưa review";
}

// Mã hệ thống (không phải câu giải thích của LLM) khi review không chạy được hoặc bị chặn trước khi
// tới bước LLM chấm — dịch sang tiếng Việt để không hiện mã thô ra màn hình. Câu do LLM tự viết (verdict
// "reason") thì giữ nguyên, không qua bảng này.
const AGENT_REVIEW_REASON_LABELS: Record<string, string> = {
  reviewer_unavailable: "Chưa cấu hình agent duyệt bài (reviewer-agent) cho tài khoản này.",
  reviewer_independence: "Agent duyệt đang trùng với agent đã viết bài — cần agent khác để duyệt độc lập.",
  reviewer_not_configured: "Agent duyệt bài chưa được cấu hình.",
  reviewer_error: "Agent duyệt gặp lỗi kỹ thuật (không gọi được mô hình AI), chưa có kết quả duyệt thật.",
  content_review_attempt_limit_reached: "Đã thử duyệt tối đa số lần cho phép, cần người duyệt thủ công.",
  empty_content: "Nội dung bài viết đang trống.",
  suspicious_embedded_instructions: "Nội dung hoặc dữ liệu tham chiếu chứa chỉ dẫn đáng ngờ, cần người kiểm tra lại.",
  review_timeout: "Agent duyệt xử lý quá lâu và bị hủy giữa chừng.",
};

function agentReviewReasonLabel(reason: string | null | undefined): string | null {
  const trimmed = reason?.trim();
  if (!trimmed) return null;
  const mapped = AGENT_REVIEW_REASON_LABELS[trimmed];
  if (mapped) return mapped;
  // Mã hệ thống khác chưa có bản dịch riêng (vd review_parse_failed, review_refused...): vẫn báo rõ
  // đây là lỗi hệ thống thay vì hiện mã trần trụi, kèm mã gốc để báo hỗ trợ kỹ thuật.
  if (/^[a-z0-9_]+$/.test(trimmed) && !trimmed.includes(" ")) {
    return `Agent duyệt gặp sự cố kỹ thuật (mã: ${trimmed}).`;
  }
  // Không khớp pattern mã hệ thống -> coi là câu giải thích tự do LLM đã viết, hiển thị nguyên văn.
  return trimmed;
}

function publishingApprovalLabel(status: string | null | undefined): string {
  const value = normalize(status);
  if (value === "approved") return "Phát hành: đã duyệt";
  if (value === "pending") return "Phát hành: chờ người";
  if (value === "rejected") return "Phát hành: từ chối";
  return "Phát hành: chưa sẵn sàng";
}

function needsOverrideReason(item: ContentItem): boolean {
  const review = normalize(item.agentReview?.status);
  return review === "rejected" || review === "needs_human" || review === "failed";
}

function itemRevision(item: ContentItem): number {
  return item.contentRevision && item.contentRevision > 0 ? item.contentRevision : 1;
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

function toInputTime(value: Date): string {
  const hours = String(value.getHours()).padStart(2, "0");
  const minutes = String(value.getMinutes()).padStart(2, "0");
  return `${hours}:${minutes}`;
}

function defaultScheduleDate(): string {
  const tomorrow = new Date();
  tomorrow.setDate(tomorrow.getDate() + 1);
  return toInputDate(tomorrow);
}

// Lịch đang hiệu lực của 1 bài — dùng để nạp lại dialog "Đổi lịch" và hiển thị giờ đăng thật trên panel.
function findActiveSchedule(
  calendarItems: readonly ContentCalendarItem[],
  contentItemId: string,
): ContentCalendarItem | null {
  return calendarItems.find((schedule) =>
    schedule.contentItemId === contentItemId
    && (normalize(schedule.status) === "pending" || normalize(schedule.status) === "held"),
  ) ?? null;
}

function buildCalendarRange(): { readonly from: string; readonly to: string } {
  // Include 7 days past so overdue / failed schedules still appear with retry actions.
  const from = new Date();
  from.setHours(0, 0, 0, 0);
  from.setDate(from.getDate() - 7);
  const to = new Date();
  to.setHours(0, 0, 0, 0);
  to.setDate(to.getDate() + 30);
  return { from: from.toISOString(), to: to.toISOString() };
}

function scheduledAtIso(mode: ScheduleMode, date: string, time: string): string | null {
  if (mode === "golden") return null;
  const local = new Date(`${date}T${time || "09:00"}:00`);
  if (Number.isNaN(local.getTime())) return null;
  return local.toISOString();
}

const INSTAGRAM_TARGET_RESELECTION_ERROR_CODE = "content.instagram_target_reselection_required";
const INSTAGRAM_TARGET_RESELECTION_GUIDANCE = "Lịch Instagram này cần chọn lại đích đăng. Hãy xác nhận tài khoản Instagram độc lập hiện đang cấu hình hoặc chọn Meta Page liên kết rồi thử lại.";

function isInstagramTargetReselectionError(error: unknown): boolean {
  if (!isAxiosError(error) || error.response?.status !== 409) return false;
  const data: unknown = error.response.data;
  if (!data || typeof data !== "object") return false;
  return (data as { readonly errorCode?: unknown }).errorCode === INSTAGRAM_TARGET_RESELECTION_ERROR_CODE;
}

function errorMessage(error: unknown): string {
  if (isInstagramTargetReselectionError(error)) return INSTAGRAM_TARGET_RESELECTION_GUIDANCE;
  return toUserFriendlyError(error, "Không xử lý được thao tác nội dung. Vui lòng thử lại.");
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
  const hasWritablePlatform = isWritablePlatform(platform);
  const currentPlatform = platformConfig(platform);

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
            {!hasWritablePlatform ? (
              <option value={platform} disabled>
                {currentPlatform.label} (lịch sử — chọn kênh mới để sinh bài)
              </option>
            ) : null}
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
          className="min-h-[104px] w-full resize-y rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
          rows={4}
          value={briefText}
          onChange={(event) => onBriefText(event.target.value)}
          placeholder="Đối tượng, ưu đãi, thông điệp chính, CTA, giọng văn..."
        />
      </label>

      <div className="mt-4 flex flex-wrap gap-2">
        <Button type="button" onClick={onSave} disabled={saving || !briefText.trim() || (!selectedId && !hasWritablePlatform)}>
          <span aria-hidden="true" className="material-symbols-outlined text-[18px]">save</span>
          {saving ? "Đang lưu..." : selectedId ? "Cập nhật yêu cầu" : "Lưu yêu cầu"}
        </Button>
        <Button type="button" variant="outline" onClick={onGenerate} disabled={generating || !briefText.trim() || !hasWritablePlatform}>
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

// ISO week (giờ VN ~ local): dùng để lọc xu hướng theo tuần, khớp weekOf backend
function isoWeekOf(date: Date): string {
  const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
  const day = d.getUTCDay() || 7;
  d.setUTCDate(d.getUTCDate() + 4 - day);
  const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1));
  const week = Math.ceil(((d.getTime() - yearStart.getTime()) / 86400000 + 1) / 7);
  return `${d.getUTCFullYear()}-W${String(week).padStart(2, "0")}`;
}

// Compact launcher trong cot 390px: tranh header wrap doc, mo modal rong de xem/dung xu huong.
function TrendLauncherCard({
  trends,
  loading,
  scanning,
  onOpen,
  onScan,
}: {
  readonly trends: readonly Trend[];
  readonly loading: boolean;
  readonly scanning: boolean;
  readonly onOpen: () => void;
  readonly onScan: () => void;
}) {
  const top = trends[0];
  return (
    <Card>
      <div className="flex items-start justify-between gap-3">
        <div className="flex items-center gap-2">
          <span aria-hidden="true" className="material-symbols-outlined text-[22px] text-primary">trending_up</span>
          <div>
            <h2 className="text-headline-sm text-secondary">Xu hướng tuần</h2>
            <p className="mt-0.5 text-label-sm text-on-surface-variant">
              {loading ? "Đang tải..." : `${trends.length} chủ đề đã quét`}
            </p>
          </div>
        </div>
        <span className="shrink-0 rounded-full bg-primary/10 px-2.5 py-1 font-mono text-mono-status text-primary">
          {trends.length}
        </span>
      </div>

      {top ? (
        <button
          type="button"
          onClick={onOpen}
          className="mt-3 block w-full rounded-lg border border-outline bg-surface p-3 text-left transition-colors hover:border-primary"
        >
          <p className="text-label-sm text-on-surface-variant">Nổi bật nhất</p>
          <p className="mt-0.5 line-clamp-1 text-body-md font-bold text-secondary">{top.topic}</p>
          <p className="mt-0.5 text-label-sm text-on-surface-variant">{top.source} · {top.relevanceScore.toFixed(1)} điểm</p>
        </button>
      ) : (
        <p className="mt-3 rounded-lg border border-dashed border-outline bg-surface p-3 text-label-sm text-on-surface-variant">
          Chưa có xu hướng. Bấm Quét để gọi agent nghiên cứu.
        </p>
      )}

      <div className="mt-3 flex gap-2">
        <Button type="button" variant="outline" size="sm" className="flex-1" onClick={onOpen}>
          <span aria-hidden="true" className="material-symbols-outlined text-[16px]">visibility</span>
          Xem tất cả
        </Button>
        <Button type="button" size="sm" className="flex-1" onClick={onScan} disabled={scanning}>
          <span aria-hidden="true" className="material-symbols-outlined text-[16px]">travel_explore</span>
          {scanning ? "Đang quét" : "Quét"}
        </Button>
      </div>
    </Card>
  );
}

function TrendModal({
  open,
  trends,
  rawTrends,
  loading,
  scanning,
  error,
  week,
  weekOptions,
  onClose,
  onWeekChange,
  onScan,
  onOpenSettings,
  onUseIdea,
}: {
  readonly open: boolean;
  readonly trends: readonly Trend[];
  readonly rawTrends: readonly RawTrend[];
  readonly loading: boolean;
  readonly scanning: boolean;
  readonly error: unknown;
  readonly week: string;
  readonly weekOptions: readonly { value: string; label: string }[];
  readonly onClose: () => void;
  readonly onWeekChange: (week: string) => void;
  readonly onScan: () => void;
  readonly onOpenSettings: () => void;
  readonly onUseIdea: (idea: string) => void;
}) {
  return (
    <Modal open={open} onClose={onClose} title="Xu hướng tuần" maxWidthClass="max-w-4xl">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-body-md text-on-surface-variant">Nguồn từ hệ thống xu hướng và agent nghiên cứu.</p>
        <div className="flex shrink-0 items-center gap-2">
          <select
            className="rounded border border-outline bg-surface-container-lowest px-2 py-1.5 text-label-sm text-secondary"
            value={week}
            onChange={(e) => onWeekChange(e.target.value)}
            aria-label="Chọn tuần xu hướng"
          >
            {weekOptions.map((opt) => (
              <option key={opt.value} value={opt.value}>{opt.label}</option>
            ))}
          </select>
          <Button type="button" variant="outline" size="sm" onClick={onOpenSettings} aria-label="Cấu hình quét xu hướng">
            <span aria-hidden="true" className="material-symbols-outlined text-[16px]">settings</span>
          </Button>
          <Button type="button" variant="outline" size="sm" onClick={onScan} disabled={scanning}>
            <span aria-hidden="true" className="material-symbols-outlined text-[16px]">travel_explore</span>
            {scanning ? "Đang quét" : "Quét"}
          </Button>
        </div>
      </div>

      {error ? <Alert tone="error">{errorMessage(error)}</Alert> : null}
      {loading ? (
        <p className="text-body-md text-on-surface-variant">Đang tải xu hướng...</p>
      ) : trends.length ? (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          {trends.map((trend) => (
            <article key={`${trend.source}-${trend.topic}`} className="flex flex-col rounded-lg border border-outline bg-surface p-3">
              <div className="mb-2 flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="text-body-md font-bold text-secondary">{trend.topic}</p>
                  <p className="text-label-sm text-on-surface-variant">
                    {trend.source} · {trend.metric}
                    {week === "all" && trend.weekOf ? ` · ${trend.weekOf}` : ""}
                  </p>
                </div>
                <span className="shrink-0 rounded bg-primary/10 px-2 py-1 font-mono text-mono-status text-primary">
                  {trend.relevanceScore.toFixed(1)} điểm
                </span>
              </div>
              <div className="mt-auto space-y-2">
                {trend.contentIdeas.slice(0, 2).map((idea) => (
                  <button
                    key={idea}
                    type="button"
                    onClick={() => onUseIdea(idea)}
                    className="block w-full rounded border border-outline bg-white px-3 py-2 text-left text-label-sm text-on-surface transition-colors hover:border-primary"
                  >
                    {idea}
                  </button>
                ))}
              </div>
            </article>
          ))}
        </div>
      ) : (
        <div className="rounded-lg border border-dashed border-outline bg-surface p-6 text-center text-body-md text-on-surface-variant">
          Chưa có xu hướng được quét. Bấm Quét để gọi agent nghiên cứu.
        </div>
      )}
      {rawTrends.length ? (
        <section className="mt-5">
          <p className="mb-2 text-label-caps uppercase text-secondary">Tất cả từ khóa đã quét ({rawTrends.length})</p>
          <div className="max-h-56 overflow-auto rounded border border-outline bg-surface">
            {rawTrends.map((trend) => (
              <button
                className="flex w-full items-center justify-between gap-3 border-b border-outline px-3 py-2 text-left last:border-0 hover:bg-surface-container-low"
                key={`${trend.source}-${trend.topic}`}
                onClick={() => onUseIdea(trend.topic)}
                type="button"
              >
                <span className="text-body-sm text-secondary">{trend.topic}</span>
                <span className="text-label-sm text-on-surface-variant">{trend.source} · {trend.metric}</span>
              </button>
            ))}
          </div>
        </section>
      ) : null}
    </Modal>
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
            <StatusPill tone={workflowTone(item.workflowState, item.status)}>
              {workflowLabel(item.workflowState, item.status)}
            </StatusPill>
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
          <ContentAssetImage className="max-h-[320px] w-full rounded-lg object-cover" url={image.url} alt={image.fileName || "Ảnh bài viết"} />
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

// P5 §4.5: đổi hook mở bài. Tự chứa (query hooks + mutation + job watcher) để không phình props QueueEditor.
// Chỉ hiện khi bài có L1/L2 đã lưu (available=true); bài single-shot cũ / chain tắt thì ẩn hẳn.
function HookSwitcher({ item, disabled }: { readonly item: ContentItem; readonly disabled: boolean }) {
  const queryClient = useQueryClient();
  const [jobId, setJobId] = useState<string | null>(null);
  const hooksQuery = useQuery({
    queryKey: ["content", "hooks", item.id],
    queryFn: () => getContentItemHooks(item.id),
  });
  const regenMutation = useMutation({
    mutationFn: (hookIndex: number) => regenerateContentHook(item.id, hookIndex),
    onSuccess: (job) => setJobId(job.jobId),
  });
  useJobWatcher(jobId, () => {
    setJobId(null);
    void queryClient.invalidateQueries({ queryKey: ["content", "queue"] });
    void queryClient.invalidateQueries({ queryKey: ["content", "hooks", item.id] });
  });

  const hooks = hooksQuery.data?.hooks ?? EMPTY_HOOKS;
  if (!hooksQuery.data?.available || hooks.length === 0) return null;

  const busy = disabled || regenMutation.isPending || jobId !== null;
  return (
    <div className="rounded-lg border border-outline bg-surface p-3">
      <p className="mb-1 text-label-caps uppercase text-secondary">Đổi hook mở bài</p>
      <p className="mb-3 text-label-sm text-on-surface-variant">
        Chạy lại phần viết + đóng gói với câu mở bài bạn chọn. Bài sẽ lên revision mới và chờ duyệt lại.
      </p>
      <div className="space-y-2">
        {hooks.map((hook) => (
          <div
            key={hook.index}
            className={`flex items-start justify-between gap-3 rounded border px-3 py-2 ${
              hook.selected ? "border-primary bg-primary/5" : "border-outline bg-white"
            }`}
          >
            <span className="text-body-sm text-on-surface">
              {hook.selected ? <span className="mr-1 font-semibold text-primary">Đang dùng:</span> : null}
              {hook.text}
            </span>
            <Button
              type="button"
              size="sm"
              variant="outline"
              onClick={() => regenMutation.mutate(hook.index)}
              disabled={busy || hook.selected}
            >
              {hook.selected ? "Hiện tại" : "Dùng hook này"}
            </Button>
          </div>
        ))}
      </div>
      {regenMutation.error ? (
        <div className="mt-2"><Alert tone="error">{errorMessage(regenMutation.error)}</Alert></div>
      ) : null}
      {jobId ? <p className="mt-2 text-label-sm text-on-surface-variant">Đang đổi hook...</p> : null}
    </div>
  );
}

function QueueEditor({
  item,
  schedule,
  body,
  assetsJson,
  saving,
  uploading,
  acting,
  canApprovePerm,
  canWritePerm,
  bodyDirty,
  onBody,
  onUploadAsset,
  onSave,
  onApprove,
  onReject,
  onRetryReview,
  onSchedule,
  onRepurpose,
  onDelete,
}: {
  readonly item: ContentItem | null;
  readonly schedule: ContentCalendarItem | null;
  readonly body: string;
  readonly assetsJson: string;
  readonly saving: boolean;
  readonly uploading: boolean;
  readonly acting: boolean;
  readonly canApprovePerm: boolean;
  readonly canWritePerm: boolean;
  readonly bodyDirty: boolean;
  readonly onBody: (value: string) => void;
  readonly onUploadAsset: (file: File) => void;
  readonly onSave: () => void;
  readonly onApprove: () => void;
  readonly onReject: () => void;
  readonly onRetryReview: () => void;
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

  const canSchedule = Boolean(item.canSchedule);
  const scheduleBlockedMessage = contentErrorMessage(item.scheduleBlockedReason);
  const canApprove = Boolean(item.canApprove) && canApprovePerm && !bodyDirty;
  const canReject = Boolean(item.canReject ?? item.canApprove) && canApprovePerm;
  const canRetryReview = Boolean(item.canRetryReview) && canWritePerm;
  const reviewReason = agentReviewReasonLabel(item.agentReview?.reason);
  const approvalReason =
    item.publishingApproval?.reason?.trim()
    || item.publishingApproval?.requirementReason?.trim()
    || null;
  const publishedLocked = normalize(item.status) === "published";

  function toggleTarget(value: string) {
    setRepurposeTargets((old) => (old.includes(value) ? old.filter((itemValue) => itemValue !== value) : [...old, value]));
  }

  return (
    <div className="grid grid-cols-1 gap-4 2xl:grid-cols-[minmax(0,1fr)_minmax(360px,0.9fr)]">
      <div className="space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-2">
            <PlatformBadge platform={item.platform} />
            <StatusPill tone={workflowTone(item.workflowState, item.status)}>
              {workflowLabel(item.workflowState, item.status)}
            </StatusPill>
            {schedule ? (
              <StatusPill tone="neutral">
                Đăng lúc {formatDateTime(schedule.scheduledAt)}
              </StatusPill>
            ) : null}
            <StatusPill tone="neutral">{agentReviewLabel(item.agentReview?.status)}</StatusPill>
            <StatusPill tone="neutral">{publishingApprovalLabel(item.publishingApproval?.status)}</StatusPill>
          </div>
          <span className="font-mono text-mono-status text-on-surface-variant">
            rev {itemRevision(item)} · #{item.id.slice(0, 8)}
          </span>
        </div>

        {(reviewReason || approvalReason) ? (
          <div className="space-y-2 rounded-lg border border-outline bg-surface p-3 text-body-sm text-on-surface-variant">
            {reviewReason ? (
              <p>
                <span className="font-semibold text-secondary">Lý do agent review: </span>
                {reviewReason}
              </p>
            ) : null}
            {approvalReason ? (
              <p>
                <span className="font-semibold text-secondary">Lý do duyệt phát hành: </span>
                {approvalReason}
              </p>
            ) : null}
          </div>
        ) : null}

        {bodyDirty ? (
          <Alert tone="warning">
            Bạn đang sửa nội dung. Lưu bài sẽ tăng revision, hủy lịch cũ và đưa bài quay lại agent review.
          </Alert>
        ) : null}

        {!canSchedule && scheduleBlockedMessage ? (
          <Alert tone="info">{scheduleBlockedMessage}</Alert>
        ) : null}

        {publishedLocked ? (
          <Alert tone="info">Bài đã đăng không thể sửa. Tạo bản repurpose nếu cần đăng lại trên kênh khác.</Alert>
        ) : null}

        <label className="block">
          <span className="mb-1 block text-label-caps uppercase text-secondary">Nội dung bài viết</span>
          <textarea
            className="min-h-[260px] w-full resize-y rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary disabled:bg-surface-container-low"
            value={body}
            disabled={publishedLocked}
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
              {assets.map((asset) => asset.url ? (
                <div key={asset.url} className="group overflow-hidden rounded border border-outline bg-white">
                  <ContentAssetImage className="h-28 w-full object-cover transition-transform group-hover:scale-[1.03]" url={asset.url} alt={asset.fileName || "Ảnh bài viết"} />
                  <span className="block truncate px-2 py-1 text-label-sm text-on-surface-variant">{asset.fileName || asset.url}</span>
                </div>
              ) : null)}
            </div>
          ) : (
            <div className="rounded border border-dashed border-outline bg-white p-4 text-body-md text-on-surface-variant">
              Chưa có ảnh. Bấm Tải ảnh để gắn media vào bài.
            </div>
          )}
        </div>

        <div className="flex flex-wrap gap-2">
          <Button type="button" onClick={onSave} disabled={saving || publishedLocked || !body.trim() || !bodyDirty}>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">save</span>
            {saving ? "Đang lưu..." : "Lưu sửa đổi"}
          </Button>
          <Button type="button" variant="outline" onClick={onApprove} disabled={acting || !canApprove}>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">verified</span>
            Duyệt phát hành
          </Button>
          {canRetryReview ? (
            <Button type="button" variant="outline" onClick={onRetryReview} disabled={acting}>
              <span aria-hidden="true" className="material-symbols-outlined text-[18px]">replay</span>
              Thử agent review lại
            </Button>
          ) : null}
          <Button type="button" variant="outline" onClick={onSchedule} disabled={acting || !canSchedule}>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">event</span>
            Đổi lịch (tuỳ chọn)
          </Button>
          <Button type="button" variant="ghost" onClick={onReject} disabled={acting || !canReject}>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">block</span>
            Từ chối phát hành
          </Button>
          <Button type="button" variant="danger" onClick={onDelete} disabled={acting || publishedLocked}>
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

        <HookSwitcher item={item} disabled={acting || publishedLocked} />
      </div>
      <SocialPreview item={item} body={body} assetsJson={assetsJson} />
    </div>
  );
}

// P5 §6: chỉ số vận hành chuỗi sinh nội dung (fallback, gate fail mỗi mắt xích, token/độ trễ, reviewer approve).
// Tự chứa query theo cửa sổ ngày. Chuỗi tắt / chưa có trace => totalRuns=0, hiện trạng thái rỗng.
function ChainMetricsPanel() {
  const [windowDays, setWindowDays] = useState(7);
  const metricsQuery = useQuery({
    queryKey: ["content", "chain-metrics", windowDays],
    queryFn: () => getContentChainMetrics(windowDays),
  });
  const metrics = metricsQuery.data;
  const pct = (value: number) => `${(value * 100).toFixed(1)}%`;

  return (
    <Card>
      <div className="mb-4 flex flex-col gap-3 lg:flex-row lg:items-end lg:justify-between">
        <div>
          <h2 className="text-headline-sm text-secondary">Chỉ số chuỗi sinh nội dung AI</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">
            Theo dõi tỉ lệ fallback, lỗi cổng từng mắt xích, token và độ trễ. Dữ liệu giữ 30 ngày.
          </p>
        </div>
        <select
          className="rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
          value={windowDays}
          onChange={(event) => setWindowDays(Number(event.target.value))}
        >
          <option value={7}>7 ngày</option>
          <option value={14}>14 ngày</option>
          <option value={30}>30 ngày</option>
        </select>
      </div>

      {metricsQuery.isError ? (
        <Alert tone="error">{errorMessage(metricsQuery.error)}</Alert>
      ) : metricsQuery.isLoading ? (
        <p className="text-body-md text-on-surface-variant">Đang tải chỉ số...</p>
      ) : !metrics || metrics.totalRuns === 0 ? (
        <div className="rounded-lg border border-dashed border-outline bg-surface p-6 text-center text-body-md text-on-surface-variant">
          Chưa có lượt chạy chuỗi nào trong {windowDays} ngày qua. Chuỗi prompt chaining có thể đang tắt cho tenant này.
        </div>
      ) : (
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
            <MetricTile icon="dataset" label="Lượt chạy" value={metrics.totalRuns} meta={`${windowDays} ngày`} />
            <MetricTile icon="undo" label="Fallback single-shot" value={pct(metrics.fallbackRate)} meta={`${metrics.fallbackRuns} lượt`} />
            <MetricTile icon="token" label="Token TB/lượt" value={Math.round(metrics.avgTokensPerRun)} meta={`~$${metrics.avgUsdCostPerRun.toFixed(4)}/lượt`} />
            <MetricTile icon="verified" label="Reviewer duyệt" value={pct(metrics.reviewApproveRate)} meta={`${metrics.reviewApproved}/${metrics.reviewTotal}`} />
          </div>

          <div className="overflow-x-auto rounded-lg border border-outline">
            <table className="w-full text-body-sm">
              <thead className="bg-surface text-label-caps uppercase text-secondary">
                <tr>
                  <th className="px-3 py-2 text-left">Mắt xích</th>
                  <th className="px-3 py-2 text-right">Lượt gọi</th>
                  <th className="px-3 py-2 text-right">Lỗi cổng</th>
                  <th className="px-3 py-2 text-right">Tỉ lệ lỗi</th>
                  <th className="px-3 py-2 text-right">p95 độ trễ</th>
                </tr>
              </thead>
              <tbody>
                {metrics.steps.map((step) => (
                  <tr key={step.stepId} className="border-t border-outline">
                    <td className="px-3 py-2 text-on-surface">{CHAIN_STEP_LABELS[step.stepId] ?? step.stepId}</td>
                    <td className="px-3 py-2 text-right font-mono">{step.attempts}</td>
                    <td className="px-3 py-2 text-right font-mono">{step.gateFailures}</td>
                    <td className="px-3 py-2 text-right font-mono">{pct(step.gateFailRate)}</td>
                    <td className="px-3 py-2 text-right font-mono">{step.p95LatencyMs} ms</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </Card>
  );
}

function CalendarPanel({
  items,
  loading,
  cancelingId,
  retryingId,
  canPublish,
  onCancel,
  onRetry,
  onSelectItem,
}: {
  readonly items: readonly ContentCalendarItem[];
  readonly loading: boolean;
  readonly cancelingId: string | null;
  readonly retryingId: string | null;
  readonly canPublish: boolean;
  readonly onCancel: (id: string) => void;
  readonly onRetry: (id: string) => void;
  readonly onSelectItem: (id: string) => void;
}) {
  const groups = groupCalendar(items);
  return (
    <Card>
      <div className="mb-4 flex items-start justify-between gap-3">
        <div>
          <h2 className="text-headline-sm text-secondary">Lịch xuất bản</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">Lịch xuất bản trong 30 ngày tới (kể cả quá hạn / thất bại).</p>
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
                {rows.map((row) => {
                  const errorText = lastErrorLabel(row.lastError);
                  const busy = cancelingId === row.scheduleId || retryingId === row.scheduleId;
                  return (
                  <div key={row.scheduleId} className="rounded border border-outline bg-white p-3">
                    <div className="mb-2 flex items-center justify-between gap-2">
                      <PlatformBadge platform={row.platform} />
                      <StatusPill tone={statusTone(row.status, row.lastError, row.scheduledAt)}>
                        {statusLabel(row.status, row.lastError, row.scheduledAt)}
                      </StatusPill>
                    </div>
                    <p className="text-body-md font-semibold text-secondary">{compactBody(row.body, 88)}</p>
                    <p className="mt-1 text-label-sm text-on-surface-variant">{formatDateTime(row.scheduledAt)}</p>
                    {errorText ? (
                      <p className="mt-1 text-label-sm text-error" title={row.lastError ?? undefined}>{errorText}</p>
                    ) : null}
                    {normalize(row.status) === "posted" && (row.likeCount !== null || row.commentCount !== null) ? (
                      <div className="mt-2 flex items-center gap-3 text-label-sm text-on-surface-variant">
                        <span className="inline-flex items-center gap-1">
                          <span aria-hidden="true" className="material-symbols-outlined text-[16px]">thumb_up</span>
                          {row.likeCount ?? 0}
                        </span>
                        <span className="inline-flex items-center gap-1">
                          <span aria-hidden="true" className="material-symbols-outlined text-[16px]">mode_comment</span>
                          {row.commentCount ?? 0}
                        </span>
                      </div>
                    ) : null}
                    <div className="mt-2 flex flex-wrap items-center gap-3">
                      <button
                        type="button"
                        className="inline-flex items-center gap-1 text-label-sm font-semibold text-primary hover:underline"
                        onClick={() => onSelectItem(row.contentItemId)}
                      >
                        <span aria-hidden="true" className="material-symbols-outlined text-[16px]">visibility</span>
                        Xem bài
                      </button>
                      {canRetrySchedule(row.status) && canPublish ? (
                        <button
                          type="button"
                          className="inline-flex items-center gap-1 text-label-sm font-semibold text-primary hover:underline disabled:opacity-50"
                          onClick={() => onRetry(row.scheduleId)}
                          disabled={busy}
                          title="Yêu cầu content:publish — chỉ reset trạng thái durable, Hangfire mới gửi provider."
                        >
                          <span aria-hidden="true" className="material-symbols-outlined text-[16px]">publish</span>
                          {normalize(row.status) === "failed" || isOverdue(row.scheduledAt) ? "Xếp thử đăng lại" : "Xếp đăng lại"}
                        </button>
                      ) : null}
                      {normalize(row.status) === "pending" ? (
                        <button
                          type="button"
                          className="inline-flex items-center gap-1 text-label-sm font-semibold text-error hover:underline disabled:opacity-50"
                          onClick={() => onCancel(row.scheduleId)}
                          disabled={busy}
                        >
                          <span aria-hidden="true" className="material-symbols-outlined text-[16px]">event_busy</span>
                          Hủy lịch
                        </button>
                      ) : null}
                    </div>
                  </div>
                  );
                })}
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
  targets,
  targetsLoading,
  targetMode,
  selectedTargetId,
  preserveExistingTarget,
  originalTargetUnavailable,
  requiresStandaloneConfirmation,
  standaloneAccountConfirmed,
  onMode,
  onDate,
  onTime,
  onTarget,
  onStandaloneAccountConfirmation,
  onClose,
  onSubmit,
}: {
  readonly item: ContentItem;
  readonly mode: ScheduleMode;
  readonly date: string;
  readonly time: string;
  readonly saving: boolean;
  readonly error: unknown;
  readonly targets: readonly ContentPublishTarget[];
  readonly targetsLoading: boolean;
  readonly targetMode: ContentPublishTargetMode | undefined;
  readonly selectedTargetId: string | null;
  readonly preserveExistingTarget: boolean;
  readonly originalTargetUnavailable: boolean;
  readonly requiresStandaloneConfirmation: boolean;
  readonly standaloneAccountConfirmed: boolean;
  readonly onMode: (value: ScheduleMode) => void;
  readonly onDate: (value: string) => void;
  readonly onTime: (value: string) => void;
  readonly onTarget: (value: string) => void;
  readonly onStandaloneAccountConfirmation: (value: boolean) => void;
  readonly onClose: () => void;
  readonly onSubmit: () => void;
}) {
  const normalizedPlatform = normalize(item.platform);
  const isTargetModeInvalid = normalizedPlatform === "instagram"
    && targetMode === "invalid"
    && !preserveExistingTarget;
  const isTargetResolutionPending = requiresMetaTarget(item.platform)
    && !preserveExistingTarget
    && (targetsLoading || !targetMode);
  const showsTargetSelector = requiresMetaTarget(item.platform, targetMode) || isTargetResolutionPending;
  const needsSelectedTarget = requiresMetaTarget(item.platform, targetMode) && !preserveExistingTarget;
  const showsStandaloneConfirmation = normalizedPlatform === "instagram"
    && targetMode === "standalone"
    && requiresStandaloneConfirmation;
  const isStandaloneConfirmationMissing = showsStandaloneConfirmation && !standaloneAccountConfirmed;

  return (
    <Modal
      open
      title="Lên lịch xuất bản nội dung"
      onClose={onClose}
      dismissible={!saving}
      footer={
        <>
          <Button type="button" variant="ghost" onClick={onClose} disabled={saving}>
            Hủy bỏ
          </Button>
          <Button
            type="button"
            onClick={onSubmit}
            disabled={saving
              || isTargetModeInvalid
              || isTargetResolutionPending
              || isStandaloneConfirmationMissing
              || (needsSelectedTarget && !selectedTargetId)}
          >
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
        {normalizedPlatform === "instagram" && targetMode === "standalone" ? (
          showsStandaloneConfirmation ? (
            <div className="space-y-3">
              <Alert tone="warning">
                Lịch Instagram cũ cần xác nhận lại tài khoản Instagram độc lập đang cấu hình trước khi đổi lịch.
              </Alert>
              <label className="flex cursor-pointer items-start gap-3 rounded border border-outline bg-surface px-3 py-3 text-body-md text-on-surface-variant">
                <input
                  type="checkbox"
                  className="mt-0.5 h-4 w-4 accent-primary"
                  checked={standaloneAccountConfirmed}
                  onChange={(event) => onStandaloneAccountConfirmation(event.target.checked)}
                />
                <span>Tôi xác nhận dùng tài khoản Instagram độc lập hiện đang cấu hình</span>
              </label>
            </div>
          ) : (
            <Alert tone="info">
              {preserveExistingTarget
                ? "Lịch hiện tại sẽ giữ nguyên đích Instagram đã khóa; tài khoản độc lập chỉ áp dụng khi chọn lại đích hoặc tạo lịch mới."
                : "Bài sẽ dùng tài khoản Instagram độc lập đang bật trong Quản trị hệ thống."}
            </Alert>
          )
        ) : null}
        {isTargetModeInvalid ? (
          <Alert tone="error">Thông tin Instagram độc lập đang lỗi. Hãy sửa hoặc tắt ghi đè trước khi lên lịch.</Alert>
        ) : null}
        {showsTargetSelector ? (
          <label className="block">
            <span className="mb-1 block text-label-caps uppercase text-secondary">
              {normalizedPlatform === "instagram" ? "Meta Page liên kết Instagram" : "Facebook Page"}
            </span>
            {targetsLoading || !targetMode ? (
              <p className="rounded border border-outline bg-surface px-3 py-2 text-body-md text-on-surface-variant">Đang tải danh sách Page...</p>
            ) : targets.length ? (
              <div className="space-y-2">
                <select
                  className="w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
                  value={selectedTargetId ?? ""}
                  onChange={(event) => onTarget(event.target.value)}
                >
                  <option value="" disabled>
                    {preserveExistingTarget ? "Giữ Page đã gắn với lịch hiện tại" : "Chọn Page sẽ đăng"}
                  </option>
                  {targets.map((target) => (
                    <option key={target.id} value={target.id}>{target.name}{target.isDefault ? " (mặc định)" : ""}</option>
                  ))}
                </select>
                {originalTargetUnavailable ? (
                  <Alert tone="info">
                    Không tải được Page đã gắn với lịch hiện tại. Nếu chỉ đổi thời gian, hệ thống sẽ giữ nguyên đích đăng đã khóa.
                  </Alert>
                ) : null}
              </div>
            ) : (
              <Alert tone={preserveExistingTarget ? "info" : "warning"}>
                {preserveExistingTarget
                  ? "Không tải được Page đã gắn với lịch hiện tại. Nếu chỉ đổi thời gian, hệ thống sẽ giữ nguyên đích đăng đã khóa."
                  : "Chưa có Meta Page khả dụng. Hãy kết nối Meta trong phần Quản trị hệ thống."}
              </Alert>
            )}
          </label>
        ) : null}
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <label className="block">
            <span className="mb-1 block text-label-caps uppercase text-secondary">Ngày</span>
            <input
              className="w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary disabled:opacity-60"
              type="date"
              min={toInputDate(new Date())}
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
  const permissions = useAuthStore((state) => state.permissions);
  const canApprovePerm = permissions.includes("content:approve");
  const canWritePerm = permissions.includes("content:write");
  const canPublishPerm = permissions.includes("content:publish");
  const [searchParams, setSearchParams] = useSearchParams();
  const calendarRange = useMemo(() => buildCalendarRange(), []);
  const requestedItemId = searchParams.get("itemId");
  const tabParam = searchParams.get("tab");
  const activeTab: ContentWorkspaceTab = requestedItemId
    ? "queue"
    : tabParam === "calendar" || tabParam === "metrics" || tabParam === "performance"
      ? tabParam
      : "queue";
  const [selectedBriefId, setSelectedBriefId] = useState<string | null>(null);
  const [briefPlatform, setBriefPlatform] = useState(PLATFORMS[0].value);
  const [briefText, setBriefText] = useState("");
  const [queueStatus, setQueueStatus] = useState<QueueStatusFilter>("all");
  const [queuePlatform, setQueuePlatform] = useState("all");
  const [queuePage, setQueuePage] = useState(1);
  const [selectedItemId, setSelectedItemId] = useState<string | null>(null);
  const [editorDraft, setEditorDraft] = useState<EditorDraft | null>(null);
  const [scheduleItem, setScheduleItem] = useState<ContentItem | null>(null);
  const [scheduleMode, setScheduleMode] = useState<ScheduleMode>("golden");
  const [scheduleDate, setScheduleDate] = useState(defaultScheduleDate);
  const [scheduleTime, setScheduleTime] = useState("09:00");
  const [scheduleTarget, setScheduleTarget] = useState<ScheduleTargetState>(EMPTY_SCHEDULE_TARGET);
  const scheduleDialogSessionCounterRef = useRef(0);
  const activeScheduleDialogSessionRef = useRef<number | null>(null);
  const [activeScheduleDialogSession, setActiveScheduleDialogSession] = useState<number | null>(null);
  const [overrideItem, setOverrideItem] = useState<ContentItem | null>(null);
  const [overrideReason, setOverrideReason] = useState("");
  const [rejectItem, setRejectItem] = useState<ContentItem | null>(null);
  const [rejectReason, setRejectReason] = useState("Từ chối trong màn hình quản lý nội dung");
  const [notice, setNotice] = useState<NoticeState | null>(null);
  const noticeRef = useRef<HTMLDivElement | null>(null);
  // Trang dài (queue + editor + calendar...) nên banner thông báo ở đầu trang dễ bị khuất khi người
  // dùng đang cuộn sâu (vd vừa đổi lịch trong panel bài viết) — tự cuộn tới để giống 1 toast thật sự.
  useEffect(() => {
    if (notice) noticeRef.current?.scrollIntoView({ behavior: "smooth", block: "center" });
  }, [notice]);

  const briefsQuery = useQuery({ queryKey: ["content", "briefs"], queryFn: () => listContentBriefs() });
  const queueQuery = useQuery({
    queryKey: ["content", "queue", queueStatus, queuePlatform, queuePage],
    queryFn: () =>
      getContentQueue({
        status: queueStatus === "all" ? undefined : queueStatus,
        platform: queuePlatform === "all" ? undefined : queuePlatform,
        page: queuePage,
        pageSize: QUEUE_PAGE_SIZE,
      }),
  });
  const calendarQuery = useQuery({
    queryKey: ["content", "calendar", calendarRange],
    queryFn: () => getContentCalendar(calendarRange),
  });
  const linkedItemQuery = useQuery({
    queryKey: ["content", "item", requestedItemId],
    queryFn: () => getContentItem(requestedItemId!),
    enabled: Boolean(requestedItemId),
    retry: false,
  });
  const scheduleTargetPlatform = scheduleItem && requiresMetaTarget(scheduleItem.platform)
    ? normalize(scheduleItem.platform)
    : null;
  const publishTargetsQuery = useQuery({
    queryKey: ["content", "publish-targets", scheduleTargetPlatform],
    queryFn: () => getContentPublishTargets(scheduleTargetPlatform!),
    enabled: Boolean(scheduleTargetPlatform),
  });
  const calendarItems = Array.isArray(calendarQuery.data?.items) ? calendarQuery.data.items : EMPTY_CALENDAR;
  const scheduleTargets = publishTargetsQuery.data?.items ?? [];
  const defaultScheduleTarget = scheduleTargets.find((target) => target.isDefault)
    ?? scheduleTargets[0]
    ?? null;
  const activeDialogSchedule = scheduleItem
    ? calendarItems.find((schedule) =>
        schedule.contentItemId === scheduleItem.id
        && (normalize(schedule.status) === "pending" || normalize(schedule.status) === "held"),
      )
    : null;
  const isExistingDialogSchedule = scheduleTarget.isExistingSchedule || Boolean(activeDialogSchedule);
  const requiresInstagramAccountConfirmation = activeDialogSchedule?.requiresInstagramAccountConfirmation === true
    || scheduleTarget.requiresInstagramAccountConfirmation;
  const originalScheduleTargetId = scheduleTarget.originalMetaAssetId
    ?? activeDialogSchedule?.metaAssetId
    ?? null;
  const isScheduleTargetRequired = requiresMetaTarget(scheduleItem?.platform, publishTargetsQuery.data?.mode);
  const isOriginalScheduleTargetAvailable = Boolean(
    originalScheduleTargetId
    && scheduleTargets.some((target) => target.id === originalScheduleTargetId),
  );
  const isPreservingScheduleTarget = isExistingDialogSchedule
    && scheduleTarget.explicitMetaAssetId === null;
  const selectedScheduleTargetId = isScheduleTargetRequired
    ? scheduleTarget.explicitMetaAssetId
      ?? (isExistingDialogSchedule
        ? isOriginalScheduleTargetAvailable ? originalScheduleTargetId : null
        : defaultScheduleTarget?.id ?? null)
    : null;
  const submittedScheduleTargetId = isScheduleTargetRequired
    ? isExistingDialogSchedule ? scheduleTarget.explicitMetaAssetId : selectedScheduleTargetId
    : null;
  const submittedInstagramAccountConfirmation = normalize(scheduleItem?.platform) === "instagram"
    && publishTargetsQuery.data?.mode === "standalone"
    && requiresInstagramAccountConfirmation
    && scheduleTarget.confirmInstagramAccount;
  const isOriginalScheduleTargetUnavailable = isScheduleTargetRequired
    && isPreservingScheduleTarget
    && !isOriginalScheduleTargetAvailable;
  // Mac dinh chi xem tuan hien tai; "all" = xem lai cac tuan cu (card se kem nhan tuan)
  const [trendWeeks] = useState(() => {
    const now = new Date();
    return {
      current: isoWeekOf(now),
      previous: isoWeekOf(new Date(now.getTime() - 7 * 86400000)),
    };
  });
  const currentWeek = trendWeeks.current;
  const previousWeek = trendWeeks.previous;
  const [trendWeek, setTrendWeek] = useState<string>(currentWeek);
  const trendsQuery = useQuery({
    queryKey: ["content", "trends", trendWeek],
    queryFn: () => getContentTrends(trendWeek === "all" ? undefined : trendWeek, true),
    staleTime: 60_000,
  });

  const briefs = Array.isArray(briefsQuery.data?.items) ? briefsQuery.data.items : EMPTY_BRIEFS;
  const queueItems = Array.isArray(queueQuery.data?.items) ? queueQuery.data.items : EMPTY_ITEMS;
  const queueTotal = typeof queueQuery.data?.total === "number" ? queueQuery.data.total : queueItems.length;
  const queueTotalPages = Math.max(1, Math.ceil(queueTotal / QUEUE_PAGE_SIZE));
  const trends = Array.isArray(trendsQuery.data?.trends) ? trendsQuery.data.trends : EMPTY_TRENDS;
  const rawTrends = Array.isArray(trendsQuery.data?.rawTrends) ? trendsQuery.data.rawTrends : [];
  const linkedItem = linkedItemQuery.data ?? null;
  const displayedQueueItems = linkedItem && !queueItems.some((item) => item.id === linkedItem.id)
    ? [linkedItem, ...queueItems]
    : queueItems;
  // itemId phải mở đúng bài hoặc hiện trạng thái unavailable; tuyệt đối không fallback qua bài đầu tiên.
  const selectedItem = requestedItemId
    ? linkedItem ?? queueItems.find((item) => item.id === requestedItemId) ?? null
    : queueItems.find((item) => item.id === selectedItemId) ?? queueItems[0] ?? null;

  useEffect(() => {
    if (queuePage > queueTotalPages) {
      setQueuePage(queueTotalPages);
    }
  }, [queuePage, queueTotalPages]);
  const matchingDraft = editorDraft && selectedItem && editorDraft.itemId === selectedItem.id ? editorDraft : null;
  const editorBody = matchingDraft?.body ?? selectedItem?.body ?? "";
  const editorAssets = (matchingDraft?.assetsJson ?? selectedItem?.assetsJson) || "[]";
  const bodyDirty = Boolean(
    selectedItem
    && matchingDraft
    && (matchingDraft.body !== selectedItem.body || matchingDraft.assetsJson !== (selectedItem.assetsJson || "[]")),
  );
  const draftCount = queueItems.filter((item) => {
    const workflow = normalize(item.workflowState);
    return normalize(item.status) === "draft"
      || workflow === "awaiting_agent_review"
      || workflow === "agent_review_running"
      || workflow === "awaiting_human_approval"
      || workflow === "agent_review_non_pass"
      || workflow === "review_failed";
  }).length;
  const readyCount = queueItems.filter((item) => {
    const workflow = normalize(item.workflowState);
    return normalize(item.status) === "approved"
      || workflow === "approved_for_publish"
      || workflow === "scheduled";
  }).length;
  const activeError = briefsQuery.error ?? queueQuery.error ?? calendarQuery.error;

  const invalidateLinkedItem = async () => {
    if (requestedItemId) await queryClient.invalidateQueries({ queryKey: ["content", "item", requestedItemId] });
  };

  const invalidateContent = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["content", "briefs"] }),
      queryClient.invalidateQueries({ queryKey: ["content", "queue"] }),
      queryClient.invalidateQueries({ queryKey: ["content", "calendar"] }),
      queryClient.invalidateQueries({ queryKey: ["content", "trends"] }),
      queryClient.invalidateQueries({ queryKey: ["content", "post-performance"] }),
      invalidateLinkedItem(),
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

  // Sinh nội dung chạy ngầm: không giữ màn hình, xong sẽ có thông báo kèm link tới bài.
  const [generateJobId, setGenerateJobId] = useState<string | null>(null);
  const generateMutation = useMutation({
    mutationFn: () =>
      selectedBriefId
        ? generateContentItems({ briefId: selectedBriefId, platform: briefPlatform })
        : generateContentItems({ platform: briefPlatform, briefText: briefText.trim() }),
    onSuccess: (job) => {
      setGenerateJobId(job.jobId);
      setNotice({
        tone: "info",
        message: "Agent đang sinh nội dung ở chế độ nền. Xong sẽ có thông báo — bấm vào là mở đúng bài.",
      });
    },
  });
  useJobWatcher(generateJobId, (job) => {
    setGenerateJobId(null);
    if (job.status === "succeeded") {
      setNotice({ tone: "success", message: "Đã sinh xong bài nháp, xem trong hàng đợi." });
      void queryClient.invalidateQueries({ queryKey: ["content", "queue"] });
    } else if (job.status === "failed") {
      setNotice({ tone: "error", message: job.error ?? "Sinh nội dung thất bại." });
    }
  });

  const updateItemMutation = useMutation({
    mutationFn: (item: ContentItem) =>
      updateContentItem(item.id, {
        body: editorBody.trim(),
        assetsJson: editorAssets.trim() || "[]",
      }),
    onSuccess: async (item) => {
      setEditorDraft({ itemId: item.id, body: item.body, assetsJson: item.assetsJson || "[]" });
      setNotice({
        tone: "success",
        message: "Đã cập nhật bài viết. Review/duyệt cũ đã bị vô hiệu — agent sẽ review lại revision mới.",
      });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["content", "queue"] }),
        queryClient.invalidateQueries({ queryKey: ["content", "calendar"] }),
        invalidateLinkedItem(),
      ]);
    },
  });

  const uploadAssetMutation = useMutation({
    mutationFn: ({ item, file }: { readonly item: ContentItem; readonly file: File }) => uploadContentAsset(item.id, file),
    onSuccess: async (response, variables) => {
      if (selectedItem?.id === variables.item.id) {
        setEditorDraft({ itemId: variables.item.id, body: editorBody, assetsJson: response.assetsJson });
      }
      setNotice({
        tone: "success",
        message: "Đã tải ảnh. Revision tăng và agent sẽ review lại trước khi duyệt phát hành.",
      });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["content", "queue"] }),
        queryClient.invalidateQueries({ queryKey: ["content", "calendar"] }),
        invalidateLinkedItem(),
      ]);
    },
  });

  const approveMutation = useMutation({
    mutationFn: ({ item, reason }: { readonly item: ContentItem; readonly reason?: string | null }) =>
      approveContentItem(item.id, {
        expectedRevision: itemRevision(item),
        overrideReason: reason?.trim() || null,
      }),
    onSuccess: async (item) => {
      setOverrideItem(null);
      setOverrideReason("");
      const scheduled = normalize(item.workflowState) === "scheduled" || normalize(item.status) === "scheduled";
      setNotice({
        tone: "success",
        message: scheduled
          ? "Đã duyệt phát hành. Hệ thống đã tạo lịch giờ vàng — kiểm tra lịch để xem thời điểm."
          : "Đã duyệt phát hành. Hệ thống sẽ tạo lịch giờ vàng khi điều kiện còn lại đủ.",
      });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["content", "queue"] }),
        queryClient.invalidateQueries({ queryKey: ["content", "calendar"] }),
        invalidateLinkedItem(),
      ]);
    },
  });

  const rejectMutation = useMutation({
    mutationFn: ({ item, reason }: { readonly item: ContentItem; readonly reason: string }) =>
      rejectContentItem(item.id, {
        expectedRevision: itemRevision(item),
        reason,
      }),
    onSuccess: async () => {
      setRejectItem(null);
      setRejectReason("Từ chối trong màn hình quản lý nội dung");
      setNotice({ tone: "warning", message: "Đã từ chối phát hành bài viết." });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["content", "queue"] }),
        queryClient.invalidateQueries({ queryKey: ["content", "calendar"] }),
        invalidateLinkedItem(),
      ]);
    },
  });

  const retryReviewMutation = useMutation({
    mutationFn: (item: ContentItem) =>
      retryAgentReview(item.id, { expectedRevision: itemRevision(item) }),
    onSuccess: async () => {
      setNotice({
        tone: "info",
        message: "Đã xếp lại agent review. Hệ thống chạy nền — không gọi LLM ngay trên trình duyệt.",
      });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["content", "queue"] }),
        invalidateLinkedItem(),
      ]);
    },
  });

  const deleteItemMutation = useMutation({
    mutationFn: (id: string) => deleteContentItem(id),
    onSuccess: async () => {
      setNotice({ tone: "success", message: "Đã xóa bài khỏi hàng đợi." });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["content", "queue"] }),
        invalidateLinkedItem(),
      ]);
    },
  });

  const [repurposeJobId, setRepurposeJobId] = useState<string | null>(null);
  const repurposeMutation = useMutation({
    mutationFn: ({ id, targets }: { readonly id: string; readonly targets: readonly string[] }) => repurposeContentItem(id, targets),
    onSuccess: (job) => {
      setRepurposeJobId(job.jobId);
      setNotice({ tone: "info", message: "Đang chuyển thể nội dung ở chế độ nền. Xong sẽ có thông báo." });
    },
  });
  useJobWatcher(repurposeJobId, (job) => {
    setRepurposeJobId(null);
    if (job.status === "succeeded") {
      setNotice({ tone: "success", message: job.resultSummary ?? "Đã tạo xong biến thể nội dung." });
      void queryClient.invalidateQueries({ queryKey: ["content", "queue"] });
    } else if (job.status === "failed") {
      setNotice({ tone: "error", message: job.error ?? "Chuyển thể nội dung thất bại." });
    }
  });

  const scheduleMutation = useMutation({
    mutationFn: ({ item, payload }: ScheduleMutationVariables) => scheduleContentItem(item.id, payload),
    onSuccess: async (_schedule, variables) => {
      if (activeScheduleDialogSessionRef.current === variables.session) {
        closeScheduleDialog(variables.session);
        setNotice({
          tone: "success",
          message: variables.mode === "golden"
            ? "Đã tạo/cập nhật lịch giờ vàng cho bài viết."
            : "Đã đổi lịch xuất bản theo thời điểm bạn chọn.",
        });
      }
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["content", "queue"] }),
        queryClient.invalidateQueries({ queryKey: ["content", "calendar"] }),
        invalidateLinkedItem(),
      ]);
    },
    onError: (error, variables) => {
      if (activeScheduleDialogSessionRef.current !== variables.session
        || !isInstagramTargetReselectionError(error)) return;
      setScheduleTarget((current) => ({
        ...current,
        requiresInstagramAccountConfirmation: true,
        confirmInstagramAccount: false,
      }));
    },
  });
  const isActiveScheduleMutation = scheduleMutation.variables?.session === activeScheduleDialogSession;
  const isActiveScheduleSaving = isActiveScheduleMutation && scheduleMutation.isPending;
  const activeScheduleError = isActiveScheduleMutation ? scheduleMutation.error : null;

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

  const retryScheduleMutation = useMutation({
    mutationFn: (id: string) => retryContentSchedule(id),
    onSuccess: async (schedule) => {
      setNotice({
        tone: "success",
        message: normalize(schedule.status) === "posted"
          ? "Lich da o trang thai dang xong."
          : "Da xep lai lich de Hangfire thu dang (khong goi provider ngay tu trinh duyet).",
      });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["content", "queue"] }),
        queryClient.invalidateQueries({ queryKey: ["content", "calendar"] }),
      ]);
    },
    onError: (error) => {
      setNotice({ tone: "error", message: errorMessage(error) });
      void queryClient.invalidateQueries({ queryKey: ["content", "calendar"] });
    },
  });

  const [scanJobId, setScanJobId] = useState<string | null>(null);
  const scanMutation = useMutation({
    mutationFn: () => scanContentTrends(),
    onSuccess: (job) => {
      setScanJobId(job.jobId);
      setNotice({ tone: "info", message: "Agent nghiên cứu đang quét xu hướng ở chế độ nền." });
    },
  });
  useJobWatcher(scanJobId, (job) => {
    setScanJobId(null);
    if (job.status === "succeeded") {
      setNotice({ tone: "success", message: job.resultSummary ?? "Đã quét xong xu hướng." });
      // Scan luon ghi vao tuan hien tai -> nhay ve tuan nay de thay ket qua moi
      setTrendWeek(currentWeek);
      void queryClient.invalidateQueries({ queryKey: ["content", "trends"] });
    } else if (job.status === "failed") {
      setNotice({ tone: "error", message: job.error ?? "Quét xu hướng thất bại." });
    }
  });

  const [trendSettingsOpen, setTrendSettingsOpen] = useState(false);
  const [trendModalOpen, setTrendModalOpen] = useState(false);

  function selectBrief(brief: ContentBrief) {
    setSelectedBriefId(brief.id);
    setBriefPlatform(brief.platform);
    setBriefText(brief.brief);
  }

  function openContentItem(itemId: string) {
    const next = new URLSearchParams(searchParams);
    next.set("tab", "queue");
    next.set("itemId", itemId);
    setSearchParams(next, { replace: true });
  }

  function selectItem(item: ContentItem) {
    setSelectedItemId(item.id);
    setEditorDraft({ itemId: item.id, body: item.body, assetsJson: item.assetsJson || "[]" });
    openContentItem(item.id);
  }

  function handleQueuePageChange(newPage: number) {
    setQueuePage(newPage);
    setSelectedItemId(null);
    if (searchParams.has("itemId")) {
      const next = new URLSearchParams(searchParams);
      next.delete("itemId");
      setSearchParams(next, { replace: true });
    }
  }

  function selectContentTab(tab: ContentWorkspaceTab) {
    const next = new URLSearchParams(searchParams);
    next.set("tab", tab);
    if (tab !== "queue") next.delete("itemId");
    setSearchParams(next, { replace: true });
  }

  function updateEditorBody(value: string) {
    if (!selectedItem) return;
    setEditorDraft({ itemId: selectedItem.id, body: value, assetsJson: editorAssets });
  }

  function resetScheduleDialogInputs() {
    setScheduleMode("golden");
    setScheduleDate(defaultScheduleDate());
    setScheduleTime("09:00");
  }

  function closeScheduleDialog(expectedSession = activeScheduleDialogSessionRef.current) {
    if (expectedSession === null || activeScheduleDialogSessionRef.current !== expectedSession) return;
    activeScheduleDialogSessionRef.current = null;
    setActiveScheduleDialogSession(null);
    scheduleMutation.reset();
    setScheduleItem(null);
    resetScheduleDialogInputs();
    setScheduleTarget(EMPTY_SCHEDULE_TARGET);
  }

  function openScheduleDialog(item: ContentItem) {
    const activeSchedule = findActiveSchedule(calendarItems, item.id);
    const isExistingSchedule = Boolean(activeSchedule)
      || normalize(item.status) === "scheduled"
      || normalize(item.workflowState) === "scheduled";
    const session = scheduleDialogSessionCounterRef.current + 1;
    scheduleDialogSessionCounterRef.current = session;
    activeScheduleDialogSessionRef.current = session;
    setActiveScheduleDialogSession(session);
    scheduleMutation.reset();
    // Bug 2026-08-23: reset luôn về "Chọn giờ vàng" bất kể bài đã có lịch cụ thể — mở lại dialog để đổi
    // lịch làm mất thời điểm riêng vừa cấu hình. Bài đã có scheduledAt hợp lệ thì nạp lại làm "specific".
    const existingScheduledAt = activeSchedule?.scheduledAt ? new Date(activeSchedule.scheduledAt) : null;
    if (existingScheduledAt && !Number.isNaN(existingScheduledAt.getTime())) {
      setScheduleMode("specific");
      setScheduleDate(toInputDate(existingScheduledAt));
      setScheduleTime(toInputTime(existingScheduledAt));
    } else {
      resetScheduleDialogInputs();
    }
    setScheduleTarget({
      isExistingSchedule,
      originalMetaAssetId: activeSchedule?.metaAssetId ?? null,
      explicitMetaAssetId: null,
      // Seed from the persisted schedule's typed flag so a reopened hold keeps requiring account
      // confirmation, instead of matching the fragile lastError string or silently resetting to false.
      requiresInstagramAccountConfirmation: activeSchedule?.requiresInstagramAccountConfirmation === true,
      confirmInstagramAccount: false,
    });
    setScheduleItem(item);
  }

  function submitScheduleDialog() {
    const session = activeScheduleDialogSessionRef.current;
    if (!scheduleItem || session === null) return;
    const payload: ScheduleContentItemPayload = Object.freeze({
      scheduledAt: scheduledAtIso(scheduleMode, scheduleDate, scheduleTime),
      metaAssetId: submittedScheduleTargetId,
      confirmInstagramAccount: submittedInstagramAccountConfirmation,
    });
    const variables: ScheduleMutationVariables = Object.freeze({
      item: scheduleItem,
      session,
      mode: scheduleMode,
      payload,
    });
    scheduleMutation.mutate(variables);
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
            <Button type="button" onClick={() => generateMutation.mutate()} disabled={generateMutation.isPending || !briefText.trim() || !isWritablePlatform(briefPlatform)}>
              <span aria-hidden="true" className="material-symbols-outlined text-[18px]">add_circle</span>
              Tạo bài viết mới
            </Button>
          </div>
        </div>
      </section>

      {notice ? (
        <div className="mb-gutter" ref={noticeRef}>
          <Alert tone={notice.tone}>{notice.message}</Alert>
        </div>
      ) : null}

      <div className="mb-gutter">
        <ContentPublishingPolicyControl />
      </div>

      <section className="mb-gutter grid grid-cols-1 gap-gutter sm:grid-cols-2 xl:grid-cols-4">
        <MetricTile icon="description" label="Yêu cầu đang mở" value={briefs.length} meta="Không tính yêu cầu đã lưu trữ" />
        <MetricTile icon="rate_review" label="Đang trong workflow" value={draftCount} meta={`${queueTotal} bài trong hàng đợi`} />
        <MetricTile icon="verified" label="Đã duyệt phát hành" value={readyCount} meta="Sẵn sàng / đã có lịch giờ vàng" />
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
          <TrendLauncherCard
            trends={trends}
            loading={trendsQuery.isLoading}
            scanning={scanMutation.isPending}
            onOpen={() => setTrendModalOpen(true)}
            onScan={() => scanMutation.mutate()}
          />
          <TrendModal
            open={trendModalOpen}
            trends={trends}
            rawTrends={rawTrends}
            loading={trendsQuery.isLoading}
            scanning={scanMutation.isPending}
            error={trendsQuery.error ?? scanMutation.error}
            week={trendWeek}
            weekOptions={[
              { value: currentWeek, label: `Tuần này (${currentWeek})` },
              { value: previousWeek, label: `Tuần trước (${previousWeek})` },
              { value: "all", label: "Tất cả các tuần" },
            ]}
            onClose={() => setTrendModalOpen(false)}
            onWeekChange={setTrendWeek}
            onScan={() => scanMutation.mutate()}
            onOpenSettings={() => setTrendSettingsOpen(true)}
            onUseIdea={(idea) => { applyTrendIdea(idea); setTrendModalOpen(false); }}
          />
          <TrendSettingsDialog open={trendSettingsOpen} onClose={() => setTrendSettingsOpen(false)} />
        </div>

        <div className="space-y-gutter">
          <nav className="flex gap-2 border-b border-outline" aria-label="Nội dung" role="tablist">
            <button
              id="content-queue-tab"
              type="button"
              role="tab"
              aria-selected={activeTab === "queue"}
              aria-controls="content-queue-panel"
              onClick={() => selectContentTab("queue")}
              className={`border-b-2 px-4 py-3 text-body-md font-semibold ${activeTab === "queue" ? "border-primary text-primary" : "border-transparent text-on-surface-variant hover:text-secondary"}`}
            >
              Hàng đợi duyệt bài
            </button>
            <button
              id="content-calendar-tab"
              type="button"
              role="tab"
              aria-selected={activeTab === "calendar"}
              aria-controls="content-calendar-panel"
              onClick={() => selectContentTab("calendar")}
              className={`border-b-2 px-4 py-3 text-body-md font-semibold ${activeTab === "calendar" ? "border-primary text-primary" : "border-transparent text-on-surface-variant hover:text-secondary"}`}
            >
              Lịch xuất bản
            </button>
            {/* Tab "Chỉ số chuỗi AI" ẩn khỏi thanh tab theo yêu cầu vận hành; panel vẫn giữ để deep link ?tab=metrics dùng được. */}
            {/* <button
              id="content-performance-tab"
              type="button"
              role="tab"
              aria-selected={activeTab === "performance"}
              aria-controls="content-performance-panel"
              onClick={() => selectContentTab("performance")}
              className={`border-b-2 px-4 py-3 text-body-md font-semibold ${activeTab === "performance" ? "border-primary text-primary" : "border-transparent text-on-surface-variant hover:text-secondary"}`}
            >
              Hiệu quả bài đăng
            </button> */}
          </nav>

          {activeTab === "queue" ? (
            <div id="content-queue-panel" role="tabpanel" aria-labelledby="content-queue-tab">
            <Card>
              <div className="mb-4 flex flex-col gap-3 lg:flex-row lg:items-end lg:justify-between">
                <div>
                  <h2 className="text-headline-sm text-secondary">Hàng đợi duyệt bài</h2>
                  <p className="mt-1 text-body-md text-on-surface-variant">Soạn thảo, duyệt và lên lịch bài viết.</p>
                </div>
                <div className="flex flex-wrap gap-2">
                  <select
                    className="rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
                    value={queuePlatform}
                    onChange={(event) => {
                      setQueuePlatform(event.target.value);
                      setQueuePage(1);
                    }}
                  >
                    <option value="all">Tất cả kênh</option>
                    {PLATFORMS.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}
                  </select>
                  <select
                    className="rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
                    value={queueStatus}
                    onChange={(event) => {
                      setQueueStatus(event.target.value as QueueStatusFilter);
                      setQueuePage(1);
                    }}
                  >
                    {STATUS_FILTERS.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}
                  </select>
                </div>
              </div>

              {(updateItemMutation.error || uploadAssetMutation.error || approveMutation.error || rejectMutation.error || deleteItemMutation.error || repurposeMutation.error || queueQuery.error || linkedItemQuery.error) ? (
                <div className="mb-4"><Alert tone="error">{errorMessage(updateItemMutation.error ?? uploadAssetMutation.error ?? approveMutation.error ?? rejectMutation.error ?? deleteItemMutation.error ?? repurposeMutation.error ?? queueQuery.error ?? linkedItemQuery.error)}</Alert></div>
              ) : null}

              <div className="grid grid-cols-1 gap-4 2xl:grid-cols-[320px_minmax(0,1fr)]">
                <div className="min-w-0 flex flex-col gap-3">
                  <QueueList items={displayedQueueItems} selectedId={selectedItem?.id ?? selectedItemId} onSelect={selectItem} />
                  {queueTotal > 0 ? (
                    <div className="flex flex-col gap-2 rounded-lg border border-outline bg-surface p-2.5">
                      <div className="flex items-center justify-between text-label-sm text-on-surface-variant">
                        <span>
                          Trang <span className="font-semibold text-secondary">{queuePage}</span> / <span className="font-semibold text-secondary">{queueTotalPages}</span>
                        </span>
                        <span>
                          Tổng <span className="font-semibold text-secondary">{queueTotal.toLocaleString("vi-VN")}</span> bài
                        </span>
                      </div>
                      <div className="flex items-center justify-between gap-1">
                        <button
                          type="button"
                          disabled={queuePage <= 1 || queueQuery.isFetching}
                          onClick={() => handleQueuePageChange(Math.max(1, queuePage - 1))}
                          className="inline-flex items-center gap-0.5 rounded border border-outline bg-white px-2 py-1 text-label-sm font-semibold text-secondary hover:bg-surface-variant disabled:cursor-not-allowed disabled:opacity-40"
                          aria-label="Trang trước"
                          title="Trang trước"
                        >
                          <span aria-hidden="true" className="material-symbols-outlined text-[16px]">chevron_left</span>
                        </button>

                        <div className="flex items-center gap-1">
                          {generatePageNumbers(queuePage, queueTotalPages).map((p, idx) =>
                            p === "..." ? (
                              <span key={`ellipsis-${idx}`} className="px-1 text-label-sm text-on-surface-variant">...</span>
                            ) : (
                              <button
                                key={p}
                                type="button"
                                onClick={() => handleQueuePageChange(Number(p))}
                                disabled={queueQuery.isFetching}
                                className={`min-w-[28px] h-7 rounded px-1.5 text-label-sm font-bold transition-colors ${
                                  queuePage === p
                                    ? "bg-primary text-on-primary"
                                    : "border border-outline bg-white text-secondary hover:bg-surface-variant"
                                }`}
                              >
                                {p}
                              </button>
                            )
                          )}
                        </div>

                        <button
                          type="button"
                          disabled={queuePage >= queueTotalPages || queueQuery.isFetching}
                          onClick={() => handleQueuePageChange(Math.min(queueTotalPages, queuePage + 1))}
                          className="inline-flex items-center gap-0.5 rounded border border-outline bg-white px-2 py-1 text-label-sm font-semibold text-secondary hover:bg-surface-variant disabled:cursor-not-allowed disabled:opacity-40"
                          aria-label="Trang sau"
                          title="Trang sau"
                        >
                          <span aria-hidden="true" className="material-symbols-outlined text-[16px]">chevron_right</span>
                        </button>
                      </div>
                    </div>
                  ) : null}
                </div>
                {requestedItemId && linkedItemQuery.isLoading ? (
                  <div className="flex min-h-[520px] items-center justify-center rounded-lg border border-dashed border-outline bg-surface p-6 text-body-md text-on-surface-variant">Đang mở bài viết...</div>
                ) : requestedItemId && linkedItemQuery.isError && !selectedItem ? (
                  <div className="flex min-h-[520px] items-center justify-center rounded-lg border border-dashed border-outline bg-surface p-6 text-center text-body-md text-on-surface-variant">Không tìm thấy bài viết từ liên kết này.</div>
                ) : (
                  <QueueEditor
                    key={selectedItem?.id ?? "empty"}
                    item={selectedItem}
                    schedule={selectedItem ? findActiveSchedule(calendarItems, selectedItem.id) : null}
                    body={editorBody}
                    assetsJson={editorAssets}
                    saving={updateItemMutation.isPending}
                    uploading={uploadAssetMutation.isPending}
                    acting={
                      approveMutation.isPending
                      || rejectMutation.isPending
                      || deleteItemMutation.isPending
                      || repurposeMutation.isPending
                      || retryReviewMutation.isPending
                    }
                    canApprovePerm={canApprovePerm}
                    canWritePerm={canWritePerm}
                    bodyDirty={bodyDirty}
                    onBody={updateEditorBody}
                    onUploadAsset={(file) => { if (selectedItem) uploadAssetMutation.mutate({ item: selectedItem, file }); }}
                    onSave={() => { if (selectedItem) updateItemMutation.mutate(selectedItem); }}
                    onApprove={() => {
                      if (!selectedItem) return;
                      if (needsOverrideReason(selectedItem)) {
                        setOverrideItem(selectedItem);
                        setOverrideReason("");
                        return;
                      }
                      approveMutation.mutate({ item: selectedItem });
                    }}
                    onReject={() => {
                      if (!selectedItem) return;
                      setRejectItem(selectedItem);
                      setRejectReason("Từ chối trong màn hình quản lý nội dung");
                    }}
                    onRetryReview={() => { if (selectedItem) retryReviewMutation.mutate(selectedItem); }}
                    onSchedule={() => {
                      if (selectedItem) openScheduleDialog(selectedItem);
                    }}
                    onRepurpose={(targets) => { if (selectedItem) repurposeMutation.mutate({ id: selectedItem.id, targets }); }}
                    onDelete={() => { if (selectedItem) deleteItemMutation.mutate(selectedItem.id); }}
                  />
                )}
              </div>
            </Card>
            </div>
          ) : activeTab === "metrics" ? (
            <div id="content-metrics-panel" role="tabpanel" aria-labelledby="content-metrics-tab">
              <ChainMetricsPanel />
            </div>
          ) : activeTab === "performance" ? (
            <div id="content-performance-panel" role="tabpanel" aria-labelledby="content-performance-tab">
              <PostPerformancePanel
                onOpenCalendar={() => selectContentTab("calendar")}
                onOpenItem={openContentItem}
              />
            </div>
          ) : (
            <div id="content-calendar-panel" role="tabpanel" aria-labelledby="content-calendar-tab">
              <CalendarPanel
                items={calendarItems}
                loading={calendarQuery.isLoading}
                cancelingId={cancelScheduleMutation.isPending ? cancelScheduleMutation.variables ?? null : null}
                retryingId={retryScheduleMutation.isPending ? retryScheduleMutation.variables ?? null : null}
                canPublish={canPublishPerm}
                onCancel={(id) => cancelScheduleMutation.mutate(id)}
                onRetry={(id) => retryScheduleMutation.mutate(id)}
                onSelectItem={openContentItem}
              />
            </div>
          )}
        </div>
      </section>

      {overrideItem ? (
        <Modal
          open
          onClose={() => {
            if (!approveMutation.isPending) {
              setOverrideItem(null);
              setOverrideReason("");
            }
          }}
          title="Duyệt phát hành (override)"
          maxWidthClass="max-w-lg"
          footer={
            <>
              <Button
                type="button"
                variant="ghost"
                disabled={approveMutation.isPending}
                onClick={() => {
                  setOverrideItem(null);
                  setOverrideReason("");
                }}
              >
                Hủy
              </Button>
              <Button
                type="button"
                disabled={approveMutation.isPending || overrideReason.trim().length < 3}
                onClick={() => approveMutation.mutate({ item: overrideItem, reason: overrideReason })}
              >
                {approveMutation.isPending ? "Đang duyệt..." : "Xác nhận override"}
              </Button>
            </>
          }
        >
          <p className="text-body-sm text-on-surface-variant">
            Agent review chưa đạt (non-pass/error). Cần lý do override để duyệt phát hành revision {itemRevision(overrideItem)}.
          </p>
          {agentReviewReasonLabel(overrideItem.agentReview?.reason) ? (
            <Alert tone="warning">Lý do agent: {agentReviewReasonLabel(overrideItem.agentReview?.reason)}</Alert>
          ) : null}
          <label className="block">
            <span className="mb-1 block text-label-caps uppercase text-secondary">Lý do override</span>
            <textarea
              className="min-h-[120px] w-full resize-y rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
              value={overrideReason}
              onChange={(event) => setOverrideReason(event.target.value)}
              placeholder="Ví dụ: đã kiểm tra brand/legal, chấp nhận rủi ro..."
            />
          </label>
          {approveMutation.isError ? <Alert tone="error">{errorMessage(approveMutation.error)}</Alert> : null}
        </Modal>
      ) : null}

      {rejectItem ? (
        <Modal
          open
          onClose={() => {
            if (!rejectMutation.isPending) {
              setRejectItem(null);
            }
          }}
          title="Từ chối phát hành"
          maxWidthClass="max-w-lg"
          footer={
            <>
              <Button
                type="button"
                variant="ghost"
                disabled={rejectMutation.isPending}
                onClick={() => setRejectItem(null)}
              >
                Hủy
              </Button>
              <Button
                type="button"
                disabled={rejectMutation.isPending || rejectReason.trim().length < 3}
                onClick={() => rejectMutation.mutate({ item: rejectItem, reason: rejectReason })}
              >
                {rejectMutation.isPending ? "Đang từ chối..." : "Xác nhận từ chối"}
              </Button>
            </>
          }
        >
          <p className="text-body-sm text-on-surface-variant">
            Từ chối phát hành sẽ hủy lịch đang chờ cho revision hiện tại.
          </p>
          <label className="block">
            <span className="mb-1 block text-label-caps uppercase text-secondary">Lý do từ chối</span>
            <textarea
              className="min-h-[120px] w-full resize-y rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
              value={rejectReason}
              onChange={(event) => setRejectReason(event.target.value)}
            />
          </label>
          {rejectMutation.isError ? <Alert tone="error">{errorMessage(rejectMutation.error)}</Alert> : null}
        </Modal>
      ) : null}

      {scheduleItem ? (
        <ScheduleDialog
          item={scheduleItem}
          mode={scheduleMode}
          date={scheduleDate}
          time={scheduleTime}
          saving={isActiveScheduleSaving}
          error={activeScheduleError ?? (requiresMetaTarget(scheduleItem.platform) ? publishTargetsQuery.error : null)}
          targets={publishTargetsQuery.data?.items ?? []}
          targetsLoading={publishTargetsQuery.isLoading}
          targetMode={publishTargetsQuery.data?.mode}
          selectedTargetId={selectedScheduleTargetId}
          preserveExistingTarget={isPreservingScheduleTarget}
          originalTargetUnavailable={isOriginalScheduleTargetUnavailable}
          requiresStandaloneConfirmation={requiresInstagramAccountConfirmation}
          standaloneAccountConfirmed={scheduleTarget.confirmInstagramAccount}
          onMode={setScheduleMode}
          onDate={setScheduleDate}
          onTime={setScheduleTime}
          onTarget={(metaAssetId) => setScheduleTarget((current) => ({
            ...current,
            explicitMetaAssetId: metaAssetId,
            confirmInstagramAccount: false,
          }))}
          onStandaloneAccountConfirmation={(confirmInstagramAccount) => setScheduleTarget((current) => ({
            ...current,
            confirmInstagramAccount,
          }))}
          onClose={() => closeScheduleDialog()}
          onSubmit={submitScheduleDialog}
        />
      ) : null}
    </AppShell>
  );
}
