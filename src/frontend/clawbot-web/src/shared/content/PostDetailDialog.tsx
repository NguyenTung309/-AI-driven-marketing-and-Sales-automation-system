import { useQuery } from "@tanstack/react-query";
import { getContentItem, getScheduleComments, type PostPerformanceTopPost } from "@/shared/api/content";
import { platformClasses } from "@/shared/theme/colors";
import { isSafeExternalPostUrl } from "@/shared/utils/socialPostUrl";
import { toUserFriendlyError } from "@/shared/utils/userText";
import { Alert, Modal, StatusPill } from "@/shared/ui";
import { ContentAssetImage } from "./ContentAssetImage";
import { imageAssets } from "./assets";

const PLATFORM_LABELS: Record<string, string> = {
  facebook: "Facebook",
  instagram: "Instagram",
};

const REACTION_TYPES = [
  { key: "reactionLove", emoji: "❤️", label: "Yêu thích" },
  { key: "reactionHaha", emoji: "😆", label: "Haha" },
  { key: "reactionWow", emoji: "😮", label: "Wow" },
  { key: "reactionSad", emoji: "😢", label: "Buồn" },
  { key: "reactionAngry", emoji: "😡", label: "Phẫn nộ" },
  { key: "reactionCare", emoji: "🤗", label: "Thương thương" },
] as const;

const COMMENT_UNAVAILABLE_REASONS: Record<string, string> = {
  platform_not_supported: "Nền tảng này chưa hỗ trợ đọc bình luận trong app.",
  post_id_unavailable: "Bài chưa lưu được mã bài trên Facebook nên không đọc được bình luận.",
  no_page_credential: "Chưa có quyền truy cập trang đăng bài để đọc bình luận.",
  graph_unavailable: "Không gọi được Facebook để lấy bình luận. Thử lại sau.",
};

function platformLabel(platform: string): string {
  return PLATFORM_LABELS[platform] ?? platform;
}

function formatMetric(value: number | null | undefined): string {
  return value === null || value === undefined ? "—" : value.toLocaleString("vi-VN");
}

function formatDateTime(value: string | null | undefined): string {
  if (!value) return "Chưa có";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("vi-VN", {
    weekday: "long",
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}

function daysSince(value: string): number | null {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return null;
  return Math.max(0, Math.floor((Date.now() - date.getTime()) / 86_400_000));
}

/** So tương tác của bài với trung bình kỳ. null khi chưa đủ dữ liệu để so. */
function compareToAverage(total: number | null, average: number | null | undefined): number | null {
  if (total === null || average === null || average === undefined || average <= 0) return null;
  return Math.round(((total - average) / average) * 100);
}

function InfoRow({ label, value }: { readonly label: string; readonly value: string }) {
  return (
    <div className="flex items-start justify-between gap-4 border-b border-outline py-2 last:border-b-0">
      <span className="text-label-sm text-on-surface-variant">{label}</span>
      <span className="text-right text-body-md text-on-surface">{value}</span>
    </div>
  );
}

export interface PostDetailDialogProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly post: PostPerformanceTopPost | null;
  /** Tương tác TB/bài của kỳ đang xem, để so bài này cao hay thấp hơn mặt bằng. */
  readonly periodAverageEngagement?: number | null;
  /** Thứ hạng trong bảng top (1 = cao nhất). */
  readonly rank?: number | null;
}

// Xem bài ngay trong app: người dùng không phải rời sang Facebook (và không bị chặn khi
// chưa đăng nhập hoặc page chưa publish). Kèm luôn số liệu marketing của chính bài đó.
export function PostDetailDialog({
  open,
  onClose,
  post,
  periodAverageEngagement,
  rank,
}: PostDetailDialogProps) {
  const itemQuery = useQuery({
    queryKey: ["content", "item", post?.contentItemId],
    queryFn: () => getContentItem(post!.contentItemId),
    enabled: open && !!post && post.isContentAvailable,
  });
  // Bình luận không nằm trong DB: gọi Graph lúc mở dialog nên chỉ chạy khi thực sự mở.
  const commentsQuery = useQuery({
    queryKey: ["content", "schedule-comments", post?.scheduleId],
    queryFn: () => getScheduleComments(post!.scheduleId),
    enabled: open && !!post,
    staleTime: 60_000,
  });

  if (!post) return null;

  const item = itemQuery.data ?? null;
  const body = item?.body ?? post.excerpt;
  const images = imageAssets(item?.assetsJson);
  const total = post.total ?? null;
  const vsAverage = compareToAverage(total, periodAverageEngagement);
  const ageDays = daysSince(post.postedAt);
  const isSynced = post.likes !== null && post.comments !== null;
  const reactionBreakdown = REACTION_TYPES
    .map((reaction) => ({ ...reaction, value: post[reaction.key] ?? null }))
    .filter((reaction) => reaction.value !== null && reaction.value > 0);
  const comments = commentsQuery.data?.items ?? [];
  const commentUnavailable = commentsQuery.isError
    ? "Không tải được bình luận."
    : commentsQuery.data?.unavailableReason
      ? COMMENT_UNAVAILABLE_REASONS[commentsQuery.data.unavailableReason] ?? "Không đọc được bình luận."
      : null;

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={`Bài đăng ${platformLabel(post.platform)}`}
      maxWidthClass="max-w-4xl"
    >
      <div className="grid grid-cols-1 gap-gutter lg:grid-cols-[minmax(0,1fr)_300px]">
        {/* Bản xem giống bài trên mạng xã hội */}
        <section aria-label="Nội dung bài đăng" className="rounded-lg border border-outline bg-white">
          <div className="flex items-center gap-3 border-b border-outline p-4">
            <div className="flex size-10 items-center justify-center rounded-full bg-primary text-label-sm font-bold text-white">
              {(post.targetName ?? "HB").slice(0, 2).toUpperCase()}
            </div>
            <div className="min-w-0">
              <p className="truncate text-body-md font-bold text-secondary">{post.targetName ?? "Trang chưa xác định"}</p>
              <p className="text-label-sm text-on-surface-variant">
                <span className={`mr-2 inline-flex rounded border px-1.5 py-0.5 ${platformClasses(post.platform)}`}>
                  {platformLabel(post.platform)}
                </span>
                {formatDateTime(post.postedAt)}
              </p>
            </div>
          </div>

          <div className="space-y-3 p-4">
            {itemQuery.isLoading ? (
              <p className="text-body-md text-on-surface-variant">Đang tải nội dung bài...</p>
            ) : itemQuery.isError ? (
              <Alert tone="error">{toUserFriendlyError(itemQuery.error)}</Alert>
            ) : null}

            {!post.isContentAvailable ? (
              <Alert tone="warning">
                Nội dung gốc đã bị xoá khỏi hệ thống nên chỉ còn trích đoạn đã lưu.
              </Alert>
            ) : null}

            <p className="whitespace-pre-wrap text-body-md text-on-surface">{body || "(Bài không có nội dung văn bản)"}</p>

            {images.length > 0 ? (
              <div className={images.length > 1 ? "grid grid-cols-2 gap-2" : ""}>
                {images.map((asset) => (
                  <ContentAssetImage
                    key={asset.url}
                    className="max-h-[360px] w-full rounded-lg object-cover"
                    url={asset.url!}
                    alt={asset.fileName || "Ảnh bài viết"}
                  />
                ))}
              </div>
            ) : null}
          </div>

          {/* Thanh tương tác: số thật lấy từ Meta, không phải nút bấm được */}
          <div className="grid grid-cols-3 border-t border-outline text-center">
            <div className="border-r border-outline py-3">
              <p className="text-telemetry-data text-secondary">
                {formatMetric(post.reactionsTotal ?? post.likes)}
              </p>
              <p className="text-label-sm text-on-surface-variant">
                {post.reactionsTotal === null || post.reactionsTotal === undefined ? "Lượt thích" : "Cảm xúc"}
              </p>
            </div>
            <div className="border-r border-outline py-3">
              <p className="text-telemetry-data text-secondary">{formatMetric(post.comments)}</p>
              <p className="text-label-sm text-on-surface-variant">Bình luận</p>
            </div>
            <div className="py-3">
              <p className="text-telemetry-data text-secondary">{formatMetric(total)}</p>
              <p className="text-label-sm text-on-surface-variant">Tổng tương tác</p>
            </div>
          </div>

          {reactionBreakdown.length > 0 ? (
            <div className="border-t border-outline p-4">
              <h3 className="text-label-caps uppercase text-on-surface-variant">Phân loại cảm xúc</h3>
              <ul className="mt-2 flex flex-wrap gap-2">
                <li className="rounded border border-outline px-2 py-1 text-label-sm text-on-surface">
                  👍 Thích <span className="font-mono">{formatMetric(post.likes)}</span>
                </li>
                {reactionBreakdown.map((reaction) => (
                  <li key={reaction.key} className="rounded border border-outline px-2 py-1 text-label-sm text-on-surface">
                    {reaction.emoji} {reaction.label} <span className="font-mono">{formatMetric(reaction.value)}</span>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}

          <div className="border-t border-outline p-4">
            <h3 className="text-label-caps uppercase text-on-surface-variant">
              Bình luận{comments.length > 0 ? ` (${formatMetric(commentsQuery.data?.totalCount ?? comments.length)})` : ""}
            </h3>
            {commentsQuery.isLoading ? (
              <p className="mt-2 text-body-md text-on-surface-variant">Đang tải bình luận từ Facebook...</p>
            ) : commentUnavailable ? (
              <Alert tone="info">{commentUnavailable}</Alert>
            ) : comments.length === 0 ? (
              <p className="mt-2 text-body-md text-on-surface-variant">Bài này chưa có bình luận nào.</p>
            ) : (
              <>
                <ul className="mt-2 space-y-3">
                  {comments.map((comment) => (
                    <li key={comment.id} className="rounded-lg bg-surface p-3">
                      <div className="flex items-baseline justify-between gap-3">
                        <span className="text-body-md font-medium text-on-surface">{comment.authorName}</span>
                        <span className="shrink-0 text-label-sm text-on-surface-variant">{formatDateTime(comment.createdAt)}</span>
                      </div>
                      <p className="mt-1 whitespace-pre-wrap text-body-md text-on-surface">
                        {comment.message || "(bình luận không có văn bản)"}
                      </p>
                      {comment.likeCount > 0 || comment.replyCount > 0 ? (
                        <p className="mt-1 text-label-sm text-on-surface-variant">
                          👍 {formatMetric(comment.likeCount)} · 💬 {formatMetric(comment.replyCount)} trả lời
                        </p>
                      ) : null}
                    </li>
                  ))}
                </ul>
                {commentsQuery.data?.isTruncated ? (
                  <p className="mt-2 text-label-sm text-on-surface-variant">
                    Đang hiển thị {comments.length} bình luận mới nhất.
                  </p>
                ) : null}
              </>
            )}
          </div>
        </section>

        {/* Khối số liệu phục vụ marketing */}
        <aside aria-label="Thông tin marketing" className="space-y-4">
          <div className="rounded-lg border border-outline p-4">
            <h3 className="text-label-caps uppercase text-on-surface-variant">Hiệu suất</h3>
            <p className="mt-2 text-telemetry-data text-secondary">{formatMetric(total)}</p>
            <p className="text-label-sm text-on-surface-variant">tương tác</p>
            <div className="mt-3 flex flex-wrap gap-2">
              {rank ? <StatusPill tone="neutral">{`Hạng ${rank} trong kỳ`}</StatusPill> : null}
              {vsAverage !== null ? (
                <StatusPill tone={vsAverage >= 0 ? "success" : "warning"}>
                  {`${vsAverage >= 0 ? "+" : ""}${vsAverage}% so với TB/bài`}
                </StatusPill>
              ) : null}
            </div>
          </div>

          <div className="rounded-lg border border-outline p-4">
            <h3 className="text-label-caps uppercase text-on-surface-variant">Chi tiết</h3>
            <div className="mt-2">
              <InfoRow label="Kênh" value={platformLabel(post.platform)} />
              <InfoRow label="Trang / tài khoản" value={post.targetName ?? "Chưa xác định"} />
              <InfoRow label="Đăng lúc" value={formatDateTime(post.postedAt)} />
              <InfoRow label="Đã đăng" value={ageDays === null ? "—" : `${ageDays} ngày trước`} />
              <InfoRow
                label="Đồng bộ tương tác"
                value={isSynced ? formatDateTime(post.engagementSyncedAt) : "Chưa đồng bộ"}
              />
            </div>
          </div>

          {!isSynced ? (
            <Alert tone="warning">
              Bài này chưa đồng bộ được lượt thích/bình luận từ Meta nên số liệu ở trên còn thiếu.
            </Alert>
          ) : null}

          {isSafeExternalPostUrl(post.postUrl) ? (
            <a
              className="block rounded-lg border border-outline px-4 py-2 text-center text-label-md text-primary hover:bg-surface"
              href={post.postUrl}
              rel="noreferrer noopener"
              target="_blank"
            >
              Mở bản gốc trên {platformLabel(post.platform)}
            </a>
          ) : null}
          <p className="text-label-sm text-on-surface-variant">
            Bản gốc chỉ mở được khi bạn đang đăng nhập tài khoản có quyền xem trang
            {post.targetName ? ` "${post.targetName}"` : ""}.
          </p>
        </aside>
      </div>
    </Modal>
  );
}
