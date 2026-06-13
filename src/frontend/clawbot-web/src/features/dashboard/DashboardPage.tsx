import { useQuery } from "@tanstack/react-query";
import { AppShell } from "@/shared/layout/AppShell";
import { Card, MetricCard, StatusPill, type MetricCardProps } from "@/shared/ui";
import { getOmnichannel, type OmniChannelRow } from "@/shared/api/analytics";

function aggregate(rows: readonly OmniChannelRow[]) {
  const sum = (sel: (r: OmniChannelRow) => number) => rows.reduce((acc, r) => acc + sel(r), 0);
  const resp = rows.map((r) => r.avgResponseTimeSec).filter((v): v is number => v != null);
  return {
    leads: sum((r) => r.leads),
    dms: sum((r) => r.dms),
    conversions: sum((r) => r.conversions),
    avgResp: resp.length ? resp.reduce((a, b) => a + b, 0) / resp.length : null,
  };
}

export default function DashboardPage() {
  const { data, isError, isLoading } = useQuery({
    queryKey: ["analytics", "omnichannel"],
    queryFn: () => getOmnichannel(),
  });
  const agg = data ? aggregate(data.rows) : null;
  const num = (n: number) => n.toLocaleString("vi-VN");

  const metrics: readonly MetricCardProps[] = [
    { label: "Hội thoại (DM)", value: agg ? num(agg.dms) : "—", icon: "forum", tone: "success" },
    { label: "Lead", value: agg ? num(agg.leads) : "—", icon: "local_fire_department", tone: "success" },
    { label: "Chuyển đổi", value: agg ? num(agg.conversions) : "—", icon: "trending_up", tone: "success" },
    {
      label: "Phản hồi TB",
      value: agg?.avgResp != null ? `${agg.avgResp.toFixed(1)}s` : "—",
      delta: "SLA < 3s",
      tone: "success",
      icon: "bolt",
    },
  ];

  return (
    <AppShell title="Dashboard tổng quan">
      <section className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-gutter">
        {metrics.map((m) => (
          <MetricCard key={m.label} {...m} />
        ))}
      </section>

      <Card className="mt-stack-lg">
        <div className="flex items-center justify-between">
          <h2 className="text-headline-sm">Trạng thái hệ thống</h2>
          <StatusPill tone={isError ? "error" : data?.stale ? "warning" : "success"}>
            {isError ? "Mất kết nối API" : data?.stale ? "Dữ liệu cũ" : "Trực tuyến"}
          </StatusPill>
        </div>
        <p className="text-body-md text-on-surface-variant mt-2">
          {isLoading
            ? "Đang tải KPI omnichannel…"
            : data
              ? `KPI tổng hợp ${data.from} → ${data.to} (nguồn: /api/analytics/omnichannel).`
              : "Không tải được KPI — kiểm tra kết nối backend."}
        </p>
      </Card>
    </AppShell>
  );
}
