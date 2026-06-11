import { AppShell } from "@/shared/layout/AppShell";
import { Card, MetricCard, StatusPill, type MetricCardProps } from "@/shared/ui";

const METRICS: readonly MetricCardProps[] = [
  { label: "Hội thoại hôm nay", value: "1,284", delta: "+12.4%", tone: "success", icon: "forum" },
  { label: "Lead nóng", value: "47", delta: "+5 trong 1h", tone: "success", icon: "local_fire_department" },
  { label: "Hạn ngạch Token", value: "82%", delta: "Sắp chạm ngưỡng", tone: "warning", icon: "toll" },
  { label: "Phản hồi (p95)", value: "2.8s", delta: "Đạt SLA < 3s", tone: "success", icon: "bolt" },
];

export default function DashboardPage() {
  return (
    <AppShell title="Dashboard tổng quan">
      <section className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-gutter">
        {METRICS.map((m) => (
          <MetricCard key={m.label} {...m} />
        ))}
      </section>

      <Card className="mt-stack-lg">
        <div className="flex items-center justify-between">
          <h2 className="text-headline-sm">Trạng thái Agent</h2>
          <StatusPill tone="success">Đang chạy</StatusPill>
        </div>
        <p className="text-body-md text-on-surface-variant mt-2">
          Bảng điều khiển orchestrator — KPI theo thời gian thực, sơ đồ tiến trình và telemetry hệ thống.
        </p>
      </Card>
    </AppShell>
  );
}
