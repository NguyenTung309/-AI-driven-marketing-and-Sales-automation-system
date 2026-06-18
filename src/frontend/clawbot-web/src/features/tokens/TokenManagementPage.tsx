import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert } from "@/shared/ui/Alert";
import { Button } from "@/shared/ui/Button";
import { Card } from "@/shared/ui/Card";
import { MetricCard } from "@/shared/ui/MetricCard";
import { StatusPill, type StatusTone } from "@/shared/ui/StatusPill";
import { AppShell } from "@/shared/layout/AppShell";
import { getTokenUsage, updateTokenSettings, type TokenAgentUsage, type TokenAlertSettings, type TokenQuotaUpdate, type TokenUsageResponse } from "@/shared/api/tokens";

type RouterTier = "flash" | "pro" | "high_effort";

const ROUTER_TIERS: readonly { readonly value: RouterTier; readonly label: string; readonly icon: string; readonly description: string }[] = [
  { value: "flash", label: "Flash Model", icon: "bolt", description: "Mặc định cho phản hồi nhanh và chi phí thấp." },
  { value: "pro", label: "Pro Model", icon: "psychology", description: "Cho nội dung dài, phân tích và biên tập." },
  { value: "high_effort", label: "High Effort Model", icon: "school", description: "Cho VIP, handoff khó và tác vụ cần suy luận sâu." },
];

const MODEL_COLORS = ["bg-primary", "bg-tertiary", "bg-secondary", "bg-surface-variant"];
const AGENT_COLORS = ["#D32F2F", "#10B981", "#545F73", "#E0E3E5", "#F59E0B", "#8F6F6C"];

const EMPTY_AGENTS: readonly TokenAgentUsage[] = [];
const EMPTY_QUOTAS: readonly TokenQuotaUpdate[] = [];
const DEFAULT_ALERT: TokenAlertSettings = { enabled: true, lowBalanceThresholdTokens: 500_000 };

function formatNumber(value: number) {
  return new Intl.NumberFormat("vi-VN").format(Math.round(value));
}

function formatUsd(value: number) {
  return new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", maximumFractionDigits: 2 }).format(value);
}

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : "Không thể tải dữ liệu token.";
}

function statusTone(status: string): StatusTone {
  if (status === "running") return "success";
  if (status === "error") return "error";
  return "neutral";
}

function tierLabel(tier: string) {
  return ROUTER_TIERS.find((item) => item.value === tier)?.label ?? "Flash Model";
}

function normalizeTier(tier: string): RouterTier {
  return tier === "pro" || tier === "high_effort" ? tier : "flash";
}

function toQuotas(data: TokenUsageResponse | undefined): readonly TokenQuotaUpdate[] {
  return (
    data?.agents.map((agent) => ({
      code: agent.code,
      monthlyQuotaTokens: agent.monthlyQuotaTokens,
      alertPercent: agent.alertPercent,
      routerTier: normalizeTier(agent.routerTier),
    })) ?? EMPTY_QUOTAS
  );
}

function exportCsv(data: TokenUsageResponse) {
  const rows = [
    ["Agent", "Module", "Model", "Calls", "Input tokens", "Output tokens", "Total tokens", "USD", "Monthly quota", "Alert percent", "Router tier"],
    ...data.agents.map((agent) => [
      agent.displayName,
      agent.moduleName,
      agent.model,
      String(agent.calls),
      String(agent.inputTokens),
      String(agent.outputTokens),
      String(agent.totalTokens),
      String(agent.usd),
      String(agent.monthlyQuotaTokens),
      String(agent.alertPercent),
      agent.routerTier,
    ]),
  ];
  const csv = rows.map((row) => row.map((cell) => `"${cell.replaceAll('"', '""')}"`).join(",")).join("\n");
  const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = "clawbot-token-usage.csv";
  link.click();
  URL.revokeObjectURL(url);
}

function UsageBar({ percent, tone = "primary" }: { readonly percent: number; readonly tone?: "primary" | "success" | "warning" }) {
  const color = tone === "success" ? "bg-success" : tone === "warning" ? "bg-warning" : "bg-primary";
  return (
    <div className="h-2 overflow-hidden rounded-full bg-surface-container-high">
      <div className={`h-full rounded-full ${color}`} style={{ width: `${Math.min(100, Math.max(0, percent))}%` }} />
    </div>
  );
}

function ModelMix({ data }: { readonly data: TokenUsageResponse }) {
  const rows = data.models.length > 0 ? data.models : [{ model: "Chưa có usage", calls: 0, totalTokens: 0, usd: 0, percent: 0 }];

  return (
    <Card>
      <h2 className="mb-5 text-headline-sm text-secondary">Cơ cấu tiêu thụ theo model</h2>
      <div className="space-y-5">
        {rows.map((model, index) => (
          <div key={model.model} className="grid grid-cols-[112px_minmax(0,1fr)_64px] items-center gap-3">
            <div>
              <p className="truncate font-mono text-mono-status text-secondary">{model.model}</p>
              <p className="text-label-sm text-on-surface-variant">{formatUsd(model.usd)}</p>
            </div>
            <div className="h-4 overflow-hidden rounded-full bg-surface-container-high">
              <div className={`h-full rounded-full ${MODEL_COLORS[index % MODEL_COLORS.length]}`} style={{ width: `${Math.max(2, model.percent)}%` }} />
            </div>
            <p className="text-right font-mono text-mono-status text-secondary">{model.percent.toFixed(1)}%</p>
          </div>
        ))}
      </div>
    </Card>
  );
}

function AgentMix({ agents }: { readonly agents: readonly TokenAgentUsage[] }) {
  const total = agents.reduce((sum, agent) => sum + agent.totalTokens, 0);
  let cursor = 0;
  const segments = agents.slice(0, 6).map((agent, index) => {
    const percent = total === 0 ? 0 : (agent.totalTokens / total) * 100;
    const segment = `${AGENT_COLORS[index % AGENT_COLORS.length]} ${cursor}% ${cursor + percent}%`;
    cursor += percent;
    return segment;
  });
  const chartBackground = total === 0 ? "#E0E3E5" : `conic-gradient(${segments.join(", ")})`;

  return (
    <Card>
      <h2 className="mb-5 text-headline-sm text-secondary">Tiêu thụ theo Agent</h2>
      <div className="flex flex-col gap-6 sm:flex-row sm:items-center">
        <div className="relative size-36 shrink-0 rounded-full" style={{ background: chartBackground }}>
          <div className="absolute inset-4 rounded-full bg-white" />
          <div className="absolute inset-0 flex flex-col items-center justify-center">
            <span className="font-mono text-label-md text-secondary">{total === 0 ? "0%" : "100%"}</span>
            <span className="text-label-sm text-on-surface-variant">Token</span>
          </div>
        </div>
        <div className="min-w-0 flex-1 space-y-3">
          {(agents.length > 0 ? agents : EMPTY_AGENTS).slice(0, 6).map((agent, index) => {
            const percent = total === 0 ? 0 : (agent.totalTokens / total) * 100;
            return (
              <div className="flex items-center justify-between gap-3" key={agent.code}>
                <span className="flex min-w-0 items-center gap-2 text-body-md text-secondary">
                  <span className="size-3 rounded-sm" style={{ backgroundColor: AGENT_COLORS[index % AGENT_COLORS.length] }} />
                  <span className="truncate">{agent.displayName}</span>
                </span>
                <span className="font-mono text-mono-status text-on-surface-variant">{percent.toFixed(1)}%</span>
              </div>
            );
          })}
          {agents.length === 0 ? <p className="text-body-md text-on-surface-variant">Chưa có agent để thống kê.</p> : null}
        </div>
      </div>
    </Card>
  );
}

export default function TokenManagementPage() {
  const queryClient = useQueryClient();
  const [notice, setNotice] = useState<string | null>(null);
  const [dirty, setDirty] = useState(false);
  const [quotaDrafts, setQuotaDrafts] = useState<readonly TokenQuotaUpdate[] | null>(null);
  const [alertDraft, setAlertDraft] = useState<TokenAlertSettings | null>(null);

  const usageQuery = useQuery({
    queryKey: ["tokens", "usage"],
    queryFn: () => getTokenUsage(),
  });

  const data = usageQuery.data;
  const agents = data?.agents ?? EMPTY_AGENTS;
  const effectiveQuotaDrafts = quotaDrafts ?? toQuotas(data);
  const effectiveAlertDraft = alertDraft ?? data?.alert ?? DEFAULT_ALERT;

  const saveMutation = useMutation({
    mutationFn: () =>
      updateTokenSettings({
        quotas: effectiveQuotaDrafts,
        alert: effectiveAlertDraft,
      }),
    onSuccess: (next) => {
      queryClient.setQueryData(["tokens", "usage"], next);
      setQuotaDrafts(null);
      setAlertDraft(null);
      setDirty(false);
      setNotice("Đã lưu cấu hình hạn ngạch và định tuyến model.");
    },
  });

  const totalQuota = effectiveQuotaDrafts.reduce((sum, quota) => sum + quota.monthlyQuotaTokens, 0);
  const projectedDays = data?.estimatedDaysRemaining === null ? "Chưa có usage" : `${data?.estimatedDaysRemaining ?? 0} ngày`;
  const quotaStatus = data && data.usagePercent >= 90 ? "error" : data && data.usagePercent >= 75 ? "warning" : "success";
  const currentError = usageQuery.error ?? saveMutation.error;

  const routerCounts = useMemo(() => {
    const counts = new Map<string, number>();
    effectiveQuotaDrafts.forEach((quota) => counts.set(normalizeTier(quota.routerTier), (counts.get(normalizeTier(quota.routerTier)) ?? 0) + 1));
    return counts;
  }, [effectiveQuotaDrafts]);

  function updateQuota(code: string, patch: Partial<TokenQuotaUpdate>) {
    setDirty(true);
    setQuotaDrafts((current) => {
      const base = current ?? toQuotas(data);
      return base.map((quota) => (quota.code === code ? { ...quota, ...patch } : quota));
    });
  }

  return (
    <AppShell title="Quản lý Token">
      <section className="mb-gutter flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div>
          <h1 className="text-display-lg text-secondary">Quản lý chi phí Token</h1>
          <p className="mt-2 max-w-3xl text-body-md text-on-surface-variant">
            Theo dõi ledger Claude theo agent, đặt hạn ngạch phân hệ và định tuyến model 3 tầng cho tenant hiện tại.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <StatusPill tone={currentError ? "error" : quotaStatus}>{currentError ? "Mất kết nối" : "Đã kết nối"}</StatusPill>
          <Button type="button" variant="outline" disabled={!data} onClick={() => data && exportCsv(data)}>
            <span className="material-symbols-outlined text-[18px]">download</span>
            Xuất báo cáo
          </Button>
        </div>
      </section>

      {notice ? (
        <div className="mb-gutter">
          <Alert tone="success">{notice}</Alert>
        </div>
      ) : null}
      {currentError ? (
        <div className="mb-gutter">
          <Alert tone="error">{errorMessage(currentError)}</Alert>
        </div>
      ) : null}

      <section className="mb-gutter grid grid-cols-1 gap-gutter md:grid-cols-3">
        <MetricCard
          icon="account_balance_wallet"
          label="Số dư token"
          value={data ? `${formatNumber(data.remainingTokens)} Tokens` : "Đang tải"}
          delta={data ? `Dự kiến còn ${projectedDays}` : "Ledger đang đồng bộ"}
          tone={quotaStatus}
        />
        <MetricCard
          icon="memory"
          label="Cache-hit ratio"
          value={data?.cacheHitRatioPercent === null || data?.cacheHitRatioPercent === undefined ? "Chưa có dữ liệu" : `${data.cacheHitRatioPercent.toFixed(1)}%`}
          delta="Chờ telemetry cache từ RAG/runtime"
          tone="neutral"
        />
        <Card>
          <div className="flex items-start justify-between">
            <p className="text-label-caps uppercase text-on-surface-variant">Mức tiêu thụ hiện tại</p>
            <span className="material-symbols-outlined text-[20px] text-on-surface-variant/60">bar_chart</span>
          </div>
          <div className="mt-4 flex h-16 items-end gap-1">
            {[24, 42, 31, 62, 54, Math.max(8, Math.min(100, data?.usagePercent ?? 0))].map((height, index) => (
              <div className={`w-1/6 rounded-t ${index === 5 ? "bg-primary" : "bg-secondary-container"}`} key={`${height}-${index}`} style={{ height: `${height}%` }} />
            ))}
          </div>
          <p className="mt-3 font-mono text-mono-status text-on-surface-variant">{data ? `${data.usagePercent.toFixed(1)}% quota tháng · ${formatUsd(data.usd)}` : "Đang tải usage"}</p>
        </Card>
      </section>

      <section className="mb-gutter grid grid-cols-1 gap-gutter xl:grid-cols-2">
        {data ? <ModelMix data={data} /> : <Card><p className="text-body-md text-on-surface-variant">Đang tải cơ cấu model...</p></Card>}
        <AgentMix agents={agents} />
      </section>

      <section className="grid grid-cols-1 gap-gutter xl:grid-cols-[minmax(0,1.15fr)_minmax(360px,0.85fr)]">
        <Card className="p-0">
          <div className="border-b border-outline p-card-padding">
            <h2 className="text-headline-sm text-secondary">Cấu hình hạn ngạch phân hệ</h2>
            <p className="mt-1 text-body-md text-on-surface-variant">Thay đổi được lưu vào cấu hình agent trên backend.</p>
          </div>
          <div className="overflow-x-auto">
            <table className="min-w-[900px] w-full border-collapse text-left">
              <thead className="bg-surface text-label-sm uppercase text-secondary">
                <tr>
                  <th className="px-4 py-3 font-bold">Phân hệ</th>
                  <th className="px-4 py-3 font-bold">Usage</th>
                  <th className="px-4 py-3 font-bold">Hạn mức token</th>
                  <th className="px-4 py-3 font-bold">Cảnh báo</th>
                  <th className="px-4 py-3 font-bold">Router</th>
                  <th className="px-4 py-3 text-right font-bold">Trạng thái</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-outline bg-white">
                {agents.map((agent) => {
                  const quota = effectiveQuotaDrafts.find((item) => item.code === agent.code);
                  return (
                    <tr className="hover:bg-surface-container-low" key={agent.code}>
                      <td className="px-4 py-4">
                        <p className="font-semibold text-secondary">{agent.moduleName}</p>
                        <p className="font-mono text-mono-status text-on-surface-variant">{agent.displayName}</p>
                      </td>
                      <td className="px-4 py-4">
                        <div className="min-w-[160px]">
                          <div className="mb-2 flex justify-between gap-3 font-mono text-mono-status text-on-surface-variant">
                            <span>{formatNumber(agent.totalTokens)}</span>
                            <span>{agent.usagePercent.toFixed(1)}%</span>
                          </div>
                          <UsageBar percent={agent.usagePercent} tone={agent.usagePercent > 90 ? "warning" : "primary"} />
                        </div>
                      </td>
                      <td className="px-4 py-4">
                        <input
                          className="w-36 rounded border border-outline bg-white px-3 py-2 font-mono text-mono-status outline-none focus:border-primary"
                          min={1000}
                          onChange={(event) => updateQuota(agent.code, { monthlyQuotaTokens: Number(event.target.value) })}
                          step={1000}
                          type="number"
                          value={quota?.monthlyQuotaTokens ?? agent.monthlyQuotaTokens}
                        />
                      </td>
                      <td className="px-4 py-4">
                        <div className="flex items-center gap-2">
                          <input
                            className="w-20 rounded border border-outline bg-white px-3 py-2 font-mono text-mono-status outline-none focus:border-primary"
                            max={100}
                            min={50}
                            onChange={(event) => updateQuota(agent.code, { alertPercent: Number(event.target.value) })}
                            type="number"
                            value={quota?.alertPercent ?? agent.alertPercent}
                          />
                          <span className="font-mono text-mono-status text-on-surface-variant">%</span>
                        </div>
                      </td>
                      <td className="px-4 py-4">
                        <select
                          className="w-40 rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
                          onChange={(event) => updateQuota(agent.code, { routerTier: event.target.value })}
                          value={quota?.routerTier ?? normalizeTier(agent.routerTier)}
                        >
                          {ROUTER_TIERS.map((tier) => (
                            <option key={tier.value} value={tier.value}>
                              {tier.label}
                            </option>
                          ))}
                        </select>
                      </td>
                      <td className="px-4 py-4 text-right">
                        <StatusPill tone={statusTone(agent.status)}>{agent.status === "running" ? "Đang chạy" : agent.status}</StatusPill>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
            {agents.length === 0 ? <div className="p-card-padding text-body-md text-on-surface-variant">Chưa có agent trong tenant hiện tại.</div> : null}
          </div>
          <div className="border-t border-outline p-card-padding">
            <label className="flex items-start gap-3">
              <input
                checked={effectiveAlertDraft.enabled}
                className="mt-1 size-4 rounded border-outline text-primary focus:ring-primary"
                onChange={(event) => {
                  setDirty(true);
                  setAlertDraft((current) => ({ ...(current ?? data?.alert ?? DEFAULT_ALERT), enabled: event.target.checked }));
                }}
                type="checkbox"
              />
              <span className="text-body-md text-on-surface">
                Gửi cảnh báo in-app/SignalR khi số dư xuống dưới
                <input
                  className="mx-2 w-32 rounded border border-outline bg-white px-2 py-1 font-mono text-mono-status outline-none focus:border-primary"
                  min={0}
                  onChange={(event) => {
                    setDirty(true);
                  setAlertDraft((current) => ({ ...(current ?? data?.alert ?? DEFAULT_ALERT), lowBalanceThresholdTokens: Number(event.target.value) }));
                  }}
                  step={1000}
                  type="number"
                  value={effectiveAlertDraft.lowBalanceThresholdTokens}
                />
                token.
              </span>
            </label>
            <Button className="mt-5 w-full py-3 text-headline-sm" disabled={!dirty || saveMutation.isPending || agents.length === 0} onClick={() => saveMutation.mutate()} type="button">
              <span className="material-symbols-outlined">save</span>
              Lưu cấu hình cảnh báo
            </Button>
          </div>
        </Card>

        <Card>
          <div className="mb-5">
            <h2 className="text-headline-sm text-secondary">Trình định tuyến model</h2>
            <p className="mt-1 text-body-md text-on-surface-variant">3-tier router dùng để kiểm soát chi phí theo độ khó tác vụ.</p>
          </div>
          <div className="space-y-4">
            {ROUTER_TIERS.map((tier) => {
              const activeCount = routerCounts.get(tier.value) ?? 0;
              const active = activeCount > 0;
              return (
                <div className={`rounded-lg border p-4 ${active ? "border-primary bg-primary/5" : "border-outline bg-surface"}`} key={tier.value}>
                  <div className="mb-2 flex items-center justify-between gap-3">
                    <div className="flex items-center gap-2">
                      <span className={`material-symbols-outlined ${active ? "text-primary" : "text-on-surface-variant"}`}>{tier.icon}</span>
                      <h3 className={`text-headline-sm ${active ? "text-primary" : "text-secondary"}`}>{tier.label}</h3>
                    </div>
                    <StatusPill tone={active ? "success" : "neutral"}>{activeCount} agent</StatusPill>
                  </div>
                  <p className="text-body-md text-on-surface-variant">{tier.description}</p>
                </div>
              );
            })}
          </div>
          <div className="mt-6 rounded-lg border border-outline bg-surface p-4">
            <p className="text-label-caps uppercase text-on-surface-variant">Tổng quota đang cấu hình</p>
            <p className="mt-2 text-headline-md text-secondary">{formatNumber(totalQuota)} Tokens</p>
            <p className="mt-1 text-body-md text-on-surface-variant">
              Router hiện tại ưu tiên {tierLabel(effectiveQuotaDrafts[0]?.routerTier ?? "flash")} cho agent đầu tiên trong danh sách.
            </p>
          </div>
        </Card>
      </section>
    </AppShell>
  );
}
