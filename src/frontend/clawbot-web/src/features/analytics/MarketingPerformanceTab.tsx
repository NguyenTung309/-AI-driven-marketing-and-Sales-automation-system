import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  getPostPerformance,
  syncPostPerformance,
  type PostPerformanceDailyPoint,
  type PostPerformancePlatform,
  type PostPerformancePlatformRow,
  type PostPerformanceTargetRow,
  type PostPerformanceTopPost,
} from "@/shared/api/content";
import { PostDetailDialog } from "@/shared/content/PostDetailDialog";
import { platformClasses } from "@/shared/theme/colors";
import { toUserFriendlyError } from "@/shared/utils/userText";
import { Alert, Button, Card, DataTable, StatusPill, type Column, type StatusTone } from "@/shared/ui";

const PLATFORM_LABELS: Record<PostPerformancePlatform, string> = {
  facebook: "Facebook",
  instagram: "Instagram",
};

const EMPTY_PLATFORMS: readonly PostPerformancePlatformRow[] = [];
const EMPTY_TARGETS: readonly PostPerformanceTargetRow[] = [];
const EMPTY_DAILY: readonly PostPerformanceDailyPoint[] = [];
const EMPTY_TOP: readonly PostPerformanceTopPost[] = [];

function formatMetric(value: number | null): string {
  return value === null ? "—" : value.toLocaleString("vi-VN");
}

function formatAverage(value: number | null): string {
  return value === null ? "—" : value.toLocaleString("vi-VN", { maximumFractionDigits: 1 });
}

function formatDayLabel(value: string): string {
  const [, month, day] = value.split("-");
  return month && day ? `${day}/${month}` : value;
}

function formatDateTime(value: string | null): string {
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

function engagement(likes: number | null, comments: number | null): number {
  return (likes ?? 0) + (comments ?? 0);
}

function PlatformTag({ platform }: { readonly platform: PostPerformancePlatform }) {
  return (
    <span className={`inline-flex rounded border px-2 py-0.5 text-label-sm font-medium ${platformClasses(platform)}`}>
      {PLATFORM_LABELS[platform]}
    </span>
  );
}

function MetricCard({
  icon,
  label,
  value,
  meta,
  tone,
}: {
  readonly icon: string;
  readonly label: string;
  readonly value: string;
  readonly meta: string;
  readonly tone: StatusTone;
}) {
  return (
    <Card>
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-label-caps uppercase text-on-surface-variant">{label}</p>
          <p className="mt-2 text-telemetry-data text-secondary">{value}</p>
        </div>
        <span aria-hidden="true" className="material-symbols-outlined rounded bg-primary/10 p-2 text-primary">{icon}</span>
      </div>
      <div className="mt-3">
        <StatusPill tone={tone}>{meta}</StatusPill>
      </div>
    </Card>
  );
}

// Cột dọc theo ngày: MKT cần thấy ngày nào bài chạy tốt để lặp lại lịch đăng đó.
function DailyEngagementChart({ points }: { readonly points: readonly PostPerformanceDailyPoint[] }) {
  const withData = points.filter((point) => point.posts > 0);
  const max = Math.max(1, ...withData.map((point) => engagement(point.likes, point.comments)));

  return (
    <Card>
      <h3 className="text-headline-sm text-secondary">Tương tác theo ngày</h3>
      <p className="mt-1 text-body-md text-on-surface-variant">Tổng lượt thích và bình luận của các bài đăng trong ngày.</p>
      {withData.length === 0 ? (
        <p className="mt-4 text-body-md text-on-surface-variant">Chưa có bài nào đăng trong kỳ này.</p>
      ) : (
        <ul className="mt-4 flex h-48 items-stretch gap-1.5" aria-label="Biểu đồ tương tác theo ngày">
          {withData.map((point) => {
            const total = engagement(point.likes, point.comments);
            const heightPct = Math.max(4, Math.round((total / max) * 100));
            return (
              <li
                key={point.date}
                className="flex h-full min-w-0 flex-1 flex-col items-center gap-1"
                title={`${formatDayLabel(point.date)}: ${point.posts} bài, ${formatMetric(point.likes)} thích, ${formatMetric(point.comments)} bình luận`}
              >
                <span className="text-label-sm text-on-surface-variant">{total}</span>
                {/* Cột neo đáy bằng absolute: chiều cao % chỉ phân giải được khi cha có chiều cao xác định. */}
                <div className="relative w-full flex-1">
                  <span
                    className="absolute bottom-0 left-1/2 w-full max-w-16 -translate-x-1/2 rounded-t bg-primary"
                    style={{ height: `${heightPct}%` }}
                  />
                </div>
                <span className="truncate text-label-sm text-on-surface-variant">{formatDayLabel(point.date)}</span>
              </li>
            );
          })}
        </ul>
      )}
    </Card>
  );
}

export interface MarketingPerformanceTabProps {
  /** Số ngày lấy theo bộ chọn khoảng thời gian chung của dashboard. */
  readonly days: number;
  readonly onOpenItem?: (contentItemId: string) => void;
}

export function MarketingPerformanceTab({ days, onOpenItem }: MarketingPerformanceTabProps) {
  const [openedPostId, setOpenedPostId] = useState<string | null>(null);
  const queryClient = useQueryClient();
  const performanceQuery = useQuery({
    queryKey: ["analytics-report", "post-performance", days],
    queryFn: () => getPostPerformance({ days }),
    refetchInterval: 120_000,
  });

  const syncMutation = useMutation({
    mutationFn: () => syncPostPerformance({ days }),
    onSuccess: (updatedData) => {
      queryClient.setQueryData(["analytics-report", "post-performance", days], updatedData);
      void queryClient.invalidateQueries({ queryKey: ["analytics-report", "post-performance"] });
      void queryClient.invalidateQueries({ queryKey: ["post-performance"] });
    },
  });

  if (performanceQuery.isLoading) {
    return <Card><p className="text-body-md text-on-surface-variant">Đang tải số liệu bài đăng...</p></Card>;
  }

  if (performanceQuery.isError) {
    return <Alert tone="error">{toUserFriendlyError(performanceQuery.error)}</Alert>;
  }

  const performance = performanceQuery.data;
  if (!performance) {
    return <Card><p className="text-body-md text-on-surface-variant">Chưa có số liệu bài đăng.</p></Card>;
  }

  const { totals, freshness } = performance;
  const byPlatform = performance.byPlatform ?? EMPTY_PLATFORMS;
  const byTarget = performance.byTarget ?? EMPTY_TARGETS;
  const daily = performance.daily ?? EMPTY_DAILY;
  const topPosts = performance.topPosts ?? EMPTY_TOP;

  const platformColumns: readonly Column<PostPerformancePlatformRow>[] = [
    { key: "platform", header: "Nền tảng", render: (row) => <PlatformTag platform={row.platform} /> },
    { key: "posts", header: "Bài đăng", className: "text-right font-mono", render: (row) => formatMetric(row.posts) },
    { key: "likes", header: "Lượt thích", className: "text-right font-mono", render: (row) => formatMetric(row.likes) },
    { key: "comments", header: "Bình luận", className: "text-right font-mono", render: (row) => formatMetric(row.comments) },
    { key: "avg", header: "TB/bài", className: "text-right font-mono", render: (row) => formatAverage(row.avgEngagementPerPost) },
  ];

  const targetColumns: readonly Column<PostPerformanceTargetRow>[] = [
    { key: "target", header: "Trang / tài khoản", render: (row) => row.targetName },
    { key: "posts", header: "Bài đăng", className: "text-right font-mono", render: (row) => formatMetric(row.posts) },
    { key: "likes", header: "Lượt thích", className: "text-right font-mono", render: (row) => formatMetric(row.likes) },
    { key: "comments", header: "Bình luận", className: "text-right font-mono", render: (row) => formatMetric(row.comments) },
    { key: "avg", header: "TB/bài", className: "text-right font-mono", render: (row) => formatAverage(row.avgEngagementPerPost) },
  ];

  const topColumns: readonly Column<PostPerformanceTopPost>[] = [
    {
      key: "excerpt",
      header: "Bài đăng",
      render: (post) => (
        <div className="min-w-0">
          <p className="truncate text-body-md text-on-surface">{post.excerpt || "(không có trích đoạn)"}</p>
          <p className="mt-0.5 text-label-sm text-on-surface-variant">{formatDateTime(post.postedAt)}</p>
        </div>
      ),
    },
    { key: "platform", header: "Nền tảng", render: (post) => <PlatformTag platform={post.platform} /> },
    { key: "likes", header: "Lượt thích", className: "text-right font-mono", render: (post) => formatMetric(post.likes) },
    { key: "comments", header: "Bình luận", className: "text-right font-mono", render: (post) => formatMetric(post.comments) },
    {
      key: "link",
      header: "Xem",
      render: (post) => (
        <div className="flex flex-wrap gap-2">
          <button
            className="text-label-md text-primary underline"
            onClick={() => setOpenedPostId(post.scheduleId)}
            type="button"
          >
            Xem bài
          </button>
          {onOpenItem && post.isContentAvailable ? (
            <button className="text-label-md text-primary underline" onClick={() => onOpenItem(post.contentItemId)} type="button">
              Sửa
            </button>
          ) : null}
        </div>
      ),
    },
  ];

  const totalEngagement = engagement(totals.likes, totals.comments);

  return (
    <div className="space-y-gutter">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-body-md text-on-surface-variant">
          Tổng quan số liệu tương tác các bài viết Facebook và Instagram đã đăng.
        </p>
        <Button
          disabled={syncMutation.isPending}
          onClick={() => syncMutation.mutate()}
          size="sm"
          type="button"
          variant="outline"
        >
          <span aria-hidden="true" className="material-symbols-outlined mr-1 text-[18px]">sync</span>
          {syncMutation.isPending ? "Đang đồng bộ..." : "Cập nhật dữ liệu"}
        </Button>
      </div>

      {syncMutation.isError ? (
        <Alert tone="error">{toUserFriendlyError(syncMutation.error, "Đồng bộ tương tác thất bại. Vui lòng thử lại sau.")}</Alert>
      ) : null}

      <section className="grid grid-cols-1 gap-gutter sm:grid-cols-2 xl:grid-cols-4">
        <MetricCard
          icon="article"
          label="Bài đã đăng"
          value={formatMetric(totals.posts)}
          meta={`${performance.windowDays} ngày gần nhất`}
          tone="neutral"
        />
        <MetricCard
          icon="thumb_up"
          label="Lượt thích"
          value={formatMetric(totals.likes)}
          meta={`${totals.syncedPosts}/${totals.posts} bài có số liệu`}
          tone={totals.posts > 0 && totals.syncedPosts === totals.posts ? "success" : "warning"}
        />
        <MetricCard
          icon="mode_comment"
          label="Bình luận"
          value={formatMetric(totals.comments)}
          meta="Từ bài đã đồng bộ"
          tone="neutral"
        />
        <MetricCard
          icon="trending_up"
          label="Tương tác TB/bài"
          value={formatAverage(totals.avgEngagementPerPost)}
          meta={`Tổng ${formatMetric(totalEngagement)} tương tác`}
          tone={totals.posts > 0 ? "success" : "neutral"}
        />
      </section>

      {totals.posts === 0 ? (
        <Alert tone="info">
          Không có bài nào đăng qua hệ thống trong {performance.windowDays} ngày gần nhất. Nếu bên bạn có đăng
          bài trong kỳ này, hãy kiểm tra tab &quot;Lịch xuất bản&quot; xem bài đã được phát hành qua ClawBot chưa —
          bài đăng tay trực tiếp trên Facebook sẽ không xuất hiện ở đây.
        </Alert>
      ) : freshness.unsyncedPosts > 0 ? (
        <Alert tone="warning">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <span>Có {freshness.unsyncedPosts} bài chưa đồng bộ lượt thích/bình luận nên các số trên còn thiếu. </span>
              <span>Lần đồng bộ cũ nhất đang chờ: {formatDateTime(freshness.oldestEngagementAttemptAt)}.</span>
            </div>
            <Button
              disabled={syncMutation.isPending}
              onClick={() => syncMutation.mutate()}
              size="sm"
              type="button"
            >
              <span aria-hidden="true" className="material-symbols-outlined mr-1 text-[18px]">sync</span>
              Đồng bộ ngay
            </Button>
          </div>
        </Alert>
      ) : null}

      <DailyEngagementChart points={daily} />

      <section className="grid grid-cols-1 gap-gutter xl:grid-cols-2">
        <Card>
          <h3 className="text-headline-sm text-secondary">Theo nền tảng</h3>
          <p className="mt-1 text-body-md text-on-surface-variant">So sánh hiệu quả giữa các kênh đang chạy.</p>
          <div className="mt-4">
            <DataTable columns={platformColumns} rows={byPlatform} rowKey={(row) => row.platform} empty="Chưa có bài đăng trong kỳ này." />
          </div>
        </Card>
        <Card>
          <h3 className="text-headline-sm text-secondary">Theo trang / tài khoản</h3>
          <p className="mt-1 text-body-md text-on-surface-variant">Fanpage hoặc tài khoản nào đang kéo tương tác tốt nhất.</p>
          <div className="mt-4">
            <DataTable columns={targetColumns} rows={byTarget} rowKey={(row) => row.metaAssetId ?? row.targetName} empty="Chưa xác định được trang đăng bài." />
          </div>
        </Card>
      </section>

      <PostDetailDialog
        onClose={() => setOpenedPostId(null)}
        open={openedPostId !== null}
        periodAverageEngagement={totals.avgEngagementPerPost}
        post={topPosts.find((post) => post.scheduleId === openedPostId) ?? null}
        rank={openedPostId ? topPosts.findIndex((post) => post.scheduleId === openedPostId) + 1 : null}
      />

      <Card>
        <h3 className="text-headline-sm text-secondary">Top bài đăng</h3>
        <p className="mt-1 text-body-md text-on-surface-variant">
          Xếp theo tổng lượt thích và bình luận hiện có; bài chưa có số liệu nằm cuối bảng.
        </p>
        <div className="mt-4">
          <DataTable columns={topColumns} rows={topPosts} rowKey={(post) => post.scheduleId} empty="Chưa có bài đăng nào trong kỳ này." />
        </div>
      </Card>
    </div>
  );
}
