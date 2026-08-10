import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { getPostPerformance, type PostPerformanceDailyPoint, type PostPerformancePlatform, type PostPerformanceTopPost } from "@/shared/api/content";
import { platformClasses } from "@/shared/theme/colors";
import { toUserFriendlyError } from "@/shared/utils/userText";
import { Alert, Button, Card, DataTable, MetricCard, StatusPill, type Column } from "@/shared/ui";

interface PostPerformancePanelProps {
  readonly onOpenCalendar: () => void;
  readonly onOpenItem: (contentItemId: string) => void;
}

type PerformancePlatformFilter = "all" | PostPerformancePlatform;
type PerformanceWindowDays = 7 | 30 | 90;

const PLATFORM_LABELS: Record<PostPerformancePlatform, string> = {
  facebook: "Facebook",
  instagram: "Instagram",
};

const TRUSTED_SOCIAL_POST_HOSTS = new Set([
  "facebook.com",
  "www.facebook.com",
  "m.facebook.com",
  "instagram.com",
  "www.instagram.com",
]);

function formatMetric(value: number | null): string {
  return value === null ? "—" : value.toLocaleString("vi-VN");
}

function formatAverage(value: number | null): string {
  return value === null ? "—" : value.toLocaleString("vi-VN", { maximumFractionDigits: 1 });
}

function formatDate(value: string): string {
  const [year, month, day] = value.split("-");
  return year && month && day ? `${day}/${month}` : value;
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

function isSafeExternalPostUrl(value: string | null): value is string {
  if (!value) return false;
  try {
    const url = new URL(value);
    return url.protocol === "https:"
      && url.port === ""
      && url.username === ""
      && url.password === ""
      && TRUSTED_SOCIAL_POST_HOSTS.has(url.hostname);
  } catch {
    return false;
  }
}

function PlatformLabel({ platform }: { readonly platform: PostPerformancePlatform }) {
  return (
    <span className={`inline-flex rounded border px-2 py-0.5 text-label-sm font-medium ${platformClasses(platform)}`}>
      {PLATFORM_LABELS[platform]}
    </span>
  );
}

function DailyBarStrip({
  points,
  label,
  valueFor,
  className,
}: {
  readonly points: readonly PostPerformanceDailyPoint[];
  readonly label: string;
  readonly valueFor: (point: PostPerformanceDailyPoint) => number;
  readonly className: string;
}) {
  const values = points.map(valueFor);
  const maximum = Math.max(...values, 1);
  const midIndex = Math.floor((points.length - 1) / 2);

  return (
    <div className="overflow-x-auto">
      <div aria-label={label} className="grid min-w-[520px] grid-flow-col auto-cols-fr items-end gap-1" role="list">
        {points.map((point, index) => {
          const value = valueFor(point);
          const height = Math.max(value > 0 ? 8 : 2, Math.round((value / maximum) * 112));
          const shouldLabelDate = index === 0 || index === midIndex || index === points.length - 1;
          return (
            <div key={point.date} className="flex min-w-0 flex-col items-center gap-2" role="listitem">
              <div className="flex h-28 w-full items-end rounded-sm bg-surface-container-low">
                <div
                  aria-hidden="true"
                  className={`w-full rounded-sm ${className}`}
                  style={{ height: `${height}px` }}
                  title={`${formatDate(point.date)}: ${value.toLocaleString("vi-VN")} ${label.toLowerCase()}`}
                />
              </div>
              <span className="min-h-4 text-center text-[10px] text-on-surface-variant">
                {shouldLabelDate ? formatDate(point.date) : ""}
              </span>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function DailyTrend({ points }: { readonly points: readonly PostPerformanceDailyPoint[] }) {
  const dailyColumns: readonly Column<PostPerformanceDailyPoint>[] = [
    { key: "date", header: "Ngày đăng", render: (point) => formatDate(point.date) },
    { key: "posts", header: "Bài đăng", className: "text-right font-mono", render: (point) => formatMetric(point.posts) },
    { key: "likes", header: "Thích", className: "text-right font-mono", render: (point) => formatMetric(point.likes) },
    { key: "comments", header: "Bình luận", className: "text-right font-mono", render: (point) => formatMetric(point.comments) },
  ];

  return (
    <Card>
      <div className="mb-4">
        <h2 className="text-headline-sm text-secondary">Xu hướng theo ngày đăng bài</h2>
        <p className="mt-1 text-body-md text-on-surface-variant">
          Tổng tương tác hiện tại của các bài đã đăng trong từng ngày, không phải tăng trưởng tương tác theo ngày đo.
        </p>
      </div>
      {points.length ? (
        <div className="space-y-6">
          <section aria-labelledby="post-count-trend-heading">
            <div className="mb-2 flex items-center justify-between gap-3">
              <h3 id="post-count-trend-heading" className="text-label-caps uppercase text-on-surface-variant">Số bài đăng</h3>
              <span className="text-label-sm text-on-surface-variant">Tối đa {formatMetric(Math.max(...points.map((point) => point.posts)))}</span>
            </div>
            <DailyBarStrip points={points} label="Bài đăng" valueFor={(point) => point.posts} className="bg-primary" />
          </section>
          <section aria-labelledby="engagement-trend-heading">
            <div className="mb-2 flex items-center justify-between gap-3">
              <h3 id="engagement-trend-heading" className="text-label-caps uppercase text-on-surface-variant">Tổng tương tác hiện tại</h3>
              <span className="text-label-sm text-on-surface-variant">
                Tối đa {formatMetric(Math.max(...points.map((point) => (point.likes ?? 0) + (point.comments ?? 0))))}
              </span>
            </div>
            <DailyBarStrip
              points={points}
              label="Tương tác"
              valueFor={(point) => (point.likes ?? 0) + (point.comments ?? 0)}
              className="bg-secondary"
            />
          </section>
          <details className="rounded-lg border border-outline bg-surface-container-lowest p-3">
            <summary className="cursor-pointer text-body-md font-medium text-secondary">Bảng số liệu theo ngày</summary>
            <div className="mt-3">
              <DataTable columns={dailyColumns} rows={points} rowKey={(point) => point.date} />
            </div>
          </details>
        </div>
      ) : null}
    </Card>
  );
}

export function PostPerformancePanel({ onOpenCalendar, onOpenItem }: PostPerformancePanelProps) {
  const [days, setDays] = useState<PerformanceWindowDays>(30);
  const [platform, setPlatform] = useState<PerformancePlatformFilter>("all");
  const performanceQuery = useQuery({
    queryKey: ["content", "post-performance", days, platform],
    queryFn: () => getPostPerformance({ days, platform: platform === "all" ? undefined : platform }),
  });
  const performance = performanceQuery.data;

  const topPostColumns: readonly Column<PostPerformanceTopPost>[] = [
    {
      key: "post",
      header: "Bài đăng",
      render: (post) => (
        <div className="min-w-56">
          {post.isContentAvailable ? (
            <button type="button" className="text-left font-medium text-primary hover:underline" onClick={() => onOpenItem(post.contentItemId)}>
              {post.excerpt}
            </button>
          ) : (
            <span className="font-medium text-on-surface">{post.excerpt}</span>
          )}
          <p className="mt-1 text-label-sm text-on-surface-variant">{formatDateTime(post.postedAt)}</p>
        </div>
      ),
    },
    { key: "platform", header: "Kênh", render: (post) => <PlatformLabel platform={post.platform} /> },
    { key: "likes", header: "Thích", className: "text-right font-mono", render: (post) => formatMetric(post.likes) },
    { key: "comments", header: "Bình luận", className: "text-right font-mono", render: (post) => formatMetric(post.comments) },
    { key: "total", header: "Tổng", className: "text-right font-mono", render: (post) => formatMetric(post.total) },
    {
      key: "external",
      header: "Liên kết",
      render: (post) => isSafeExternalPostUrl(post.postUrl) ? (
        <a className="text-primary hover:underline" href={post.postUrl} target="_blank" rel="noreferrer noopener">Mở bài</a>
      ) : "—",
    },
  ];

  return (
    <div className="space-y-gutter">
      <Card>
        <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
          <div>
            <h2 className="text-headline-sm text-secondary">Hiệu quả bài đăng</h2>
            <p className="mt-1 text-body-md text-on-surface-variant">
              Theo dõi lượt thích và bình luận của bài Facebook, Instagram đã xuất bản.
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            <label className="flex flex-col gap-1 text-label-sm text-on-surface-variant">
              Khoảng thời gian
              <select className="rounded border border-outline bg-white px-3 py-2 text-body-md text-on-surface outline-none focus:border-primary" value={days} onChange={(event) => setDays(Number(event.target.value) as PerformanceWindowDays)}>
                <option value={7}>7 ngày</option>
                <option value={30}>30 ngày</option>
                <option value={90}>90 ngày</option>
              </select>
            </label>
            <label className="flex flex-col gap-1 text-label-sm text-on-surface-variant">
              Kênh
              <select className="rounded border border-outline bg-white px-3 py-2 text-body-md text-on-surface outline-none focus:border-primary" value={platform} onChange={(event) => setPlatform(event.target.value as PerformancePlatformFilter)}>
                <option value="all">Facebook và Instagram</option>
                <option value="facebook">Facebook</option>
                <option value="instagram">Instagram</option>
              </select>
            </label>
          </div>
        </div>
      </Card>

      {performanceQuery.isError ? (
        <Alert tone="error">{toUserFriendlyError(performanceQuery.error, "Không tải được hiệu quả bài đăng. Vui lòng thử lại.")}</Alert>
      ) : performanceQuery.isLoading ? (
        <Card><p className="text-body-md text-on-surface-variant">Đang tải hiệu quả bài đăng...</p></Card>
      ) : !performance || performance.totals.posts === 0 ? (
        <Card>
          <div className="flex min-h-52 flex-col items-center justify-center text-center">
            <span aria-hidden="true" className="material-symbols-outlined text-[36px] text-on-surface-variant">insights</span>
            <h3 className="mt-3 text-title-lg text-secondary">Chưa có bài Facebook hoặc Instagram trong kỳ này</h3>
            <p className="mt-1 max-w-xl text-body-md text-on-surface-variant">Hãy kiểm tra lịch xuất bản hoặc đổi khoảng thời gian để xem số liệu của các bài đã đăng.</p>
            <Button className="mt-4" type="button" variant="outline" onClick={onOpenCalendar}>Mở lịch xuất bản</Button>
          </div>
        </Card>
      ) : (
        <>
          <div className="grid grid-cols-2 gap-3 xl:grid-cols-4">
            <MetricCard label="Bài đã đăng" value={formatMetric(performance.totals.posts)} delta={`${performance.windowDays} ngày`} icon="article" />
            <MetricCard label="Lượt thích" value={formatMetric(performance.totals.likes)} delta={`${performance.totals.syncedPosts}/${performance.totals.posts} bài có số liệu`} icon="thumb_up" />
            <MetricCard label="Bình luận" value={formatMetric(performance.totals.comments)} delta="Từ bài đã có số liệu" icon="mode_comment" />
            <MetricCard label="TB tương tác/bài" value={formatAverage(performance.totals.avgEngagementPerPost)} delta="Chỉ tính bài đã có số liệu" icon="trending_up" />
          </div>

          <Alert tone={performance.freshness.unsyncedPosts ? "warning" : "info"}>
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
              <span>Đã có số liệu {performance.freshness.syncedPosts}/{performance.totals.posts} bài.</span>
              <span>Lần thử đồng bộ cũ nhất: {formatDateTime(performance.freshness.oldestEngagementAttemptAt)}.</span>
              {performance.freshness.unsyncedPosts ? <StatusPill tone="warning">{performance.freshness.unsyncedPosts} bài chưa có số liệu</StatusPill> : null}
            </div>
          </Alert>

          <Card>
            <div className="mb-4">
              <h2 className="text-headline-sm text-secondary">Bài đăng hiệu quả nhất</h2>
              <p className="mt-1 text-body-md text-on-surface-variant">Xếp theo tổng lượt thích và bình luận hiện có; bài chưa có số liệu nằm cuối bảng.</p>
            </div>
            <DataTable columns={topPostColumns} rows={performance.topPosts} rowKey={(post) => post.scheduleId} />
          </Card>

          <div className="grid gap-gutter xl:grid-cols-2">
            <Card>
              <div className="mb-4">
                <h2 className="text-headline-sm text-secondary">Theo kênh</h2>
                <p className="mt-1 text-body-md text-on-surface-variant">So sánh các bài đã đăng có số liệu.</p>
              </div>
              <DataTable
                columns={[
                  { key: "platform", header: "Kênh", render: (row) => <PlatformLabel platform={row.platform} /> },
                  { key: "posts", header: "Bài", className: "text-right font-mono", render: (row) => formatMetric(row.posts) },
                  { key: "likes", header: "Thích", className: "text-right font-mono", render: (row) => formatMetric(row.likes) },
                  { key: "comments", header: "Bình luận", className: "text-right font-mono", render: (row) => formatMetric(row.comments) },
                  { key: "average", header: "TB/bài", className: "text-right font-mono", render: (row) => formatAverage(row.avgEngagementPerPost) },
                ]}
                rows={performance.byPlatform}
                rowKey={(row) => row.platform}
              />
            </Card>

            <Card>
              <div className="mb-4">
                <h2 className="text-headline-sm text-secondary">Theo Page</h2>
                <p className="mt-1 text-body-md text-on-surface-variant">Bài Instagram độc lập hoặc bài cũ thiếu đích đăng được nhóm là Không xác định Page.</p>
              </div>
              <DataTable
                columns={[
                  { key: "target", header: "Page", render: (row) => row.targetName },
                  { key: "posts", header: "Bài", className: "text-right font-mono", render: (row) => formatMetric(row.posts) },
                  { key: "likes", header: "Thích", className: "text-right font-mono", render: (row) => formatMetric(row.likes) },
                  { key: "comments", header: "Bình luận", className: "text-right font-mono", render: (row) => formatMetric(row.comments) },
                ]}
                rows={performance.byTarget}
                rowKey={(row) => row.metaAssetId ?? "unknown-target"}
              />
            </Card>
          </div>

          <DailyTrend points={performance.daily} />
        </>
      )}
    </div>
  );
}
