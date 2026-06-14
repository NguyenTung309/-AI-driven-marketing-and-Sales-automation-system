import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AppShell } from "@/shared/layout/AppShell";
import { Button } from "@/shared/ui/Button";
import { Card } from "@/shared/ui/Card";
import { MetricCard } from "@/shared/ui/MetricCard";
import { StatusPill, type StatusTone } from "@/shared/ui/StatusPill";
import { WorkflowNode } from "@/shared/ui/WorkflowNode";
import {
  disableAgent,
  enableAgent,
  getAgentCost,
  getAgentTraces,
  listAgents,
  type AgentCostItem,
  type AgentListItem,
  type AgentStatus,
  type AgentTraceItem,
} from "@/shared/api/agents";

const EMPTY_AGENTS: readonly AgentListItem[] = [];
const EMPTY_COSTS: readonly AgentCostItem[] = [];
const EMPTY_TRACES: readonly AgentTraceItem[] = [];

function normalize(value: string): string {
  return value.trim().toLowerCase();
}

function statusTone(status: AgentStatus): StatusTone {
  const value = normalize(status);
  if (value === "running") return "success";
  if (value === "error") return "error";
  if (value === "stopped") return "neutral";
  return "warning";
}

function statusLabel(status: AgentStatus): string {
  const value = normalize(status);
  if (value === "running") return "Đang chạy";
  if (value === "stopped") return "Đã dừng";
  if (value === "error") return "Lỗi";
  return status;
}

function phaseTone(phase: string): string {
  const value = normalize(phase);
  if (value.includes("error") || value.includes("fail")) return "text-red-300";
  if (value.includes("warn")) return "text-yellow-300";
  if (value.includes("complete") || value.includes("success")) return "text-green-300";
  return "text-slate-300";
}

function formatDateTime(value: string | null): string {
  if (!value) return "Chưa chạy";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("vi-VN", {
    day: "2-digit",
    month: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}

function formatCurrency(value: number): string {
  return new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", maximumFractionDigits: 2 }).format(value);
}

function costForAgent(costs: readonly AgentCostItem[], code: string): AgentCostItem | null {
  return costs.find((item) => item.agentCode.toLowerCase() === code.toLowerCase()) ?? null;
}

function agentTypeLabel(type: string): string {
  const value = normalize(type);
  if (value === "sale_assist") return "Sale Assist";
  if (value === "content") return "Content";
  if (value === "lead") return "Lead Scoring";
  if (value === "docs") return "Docs";
  if (value === "ads") return "Ads";
  if (value === "report") return "Report";
  if (value === "research") return "Research";
  if (value === "chat") return "Chat";
  return type || "Agent";
}

function exportTraceCsv(agent: AgentListItem | null, traces: readonly AgentTraceItem[]) {
  const rows = [
    ["occurred_at", "agent", "phase", "message", "session_id"],
    ...traces.map((trace) => [trace.occurredAt, trace.agentName, trace.phase, trace.message, trace.sessionId]),
  ];
  const csv = rows
    .map((row) => row.map((cell) => `"${String(cell).replaceAll('"', '""')}"`).join(","))
    .join("\n");
  const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = `${agent?.code ?? "agent"}-traces.csv`;
  link.click();
  URL.revokeObjectURL(url);
}

function AgentNode({
  agent,
  cost,
  selected,
  onSelect,
  onToggle,
  pending,
}: {
  readonly agent: AgentListItem;
  readonly cost: AgentCostItem | null;
  readonly selected: boolean;
  readonly onSelect: () => void;
  readonly onToggle: () => void;
  readonly pending: boolean;
}) {
  const running = normalize(agent.status) === "running";
  return (
    <div
      className={[
        "cursor-pointer text-left transition-transform hover:-translate-y-0.5 focus:outline-none",
        selected ? "ring-2 ring-primary ring-offset-2 ring-offset-surface" : "",
      ].join(" ")}
      onClick={onSelect}
      onKeyDown={(event) => {
        if (event.key === "Enter" || event.key === " ") {
          event.preventDefault();
          onSelect();
        }
      }}
      role="button"
      tabIndex={0}
    >
      <WorkflowNode title={agent.displayName || agent.code} subtitle={agentTypeLabel(agent.agentType)} status={statusTone(agent.status)}>
        <div className="flex items-center justify-between gap-2">
          <span>Model</span>
          <span className="truncate text-secondary">{agent.model || "n/a"}</span>
        </div>
        <div className="flex items-center justify-between gap-2">
          <span>Lần chạy</span>
          <span>{formatDateTime(agent.lastRunAt)}</span>
        </div>
        <div className="flex items-center justify-between gap-2">
          <span>Chi phí</span>
          <span>{cost ? formatCurrency(cost.usd) : "$0.00"}</span>
        </div>
        <div className="pt-2">
          <button
            className={[
              "w-full rounded px-3 py-1.5 text-body-md font-bold text-white transition-colors disabled:cursor-not-allowed disabled:opacity-60",
              running ? "bg-secondary hover:bg-slate-700" : "bg-primary hover:bg-primary-hover",
            ].join(" ")}
            disabled={pending}
            onClick={(event) => {
              event.stopPropagation();
              onToggle();
            }}
            type="button"
          >
            {running ? "Dừng agent" : "Chạy agent"}
          </button>
        </div>
      </WorkflowNode>
    </div>
  );
}

function TerminalLog({
  selectedAgent,
  traces,
  loading,
  onExport,
}: {
  readonly selectedAgent: AgentListItem | null;
  readonly traces: readonly AgentTraceItem[];
  readonly loading: boolean;
  readonly onExport: () => void;
}) {
  return (
    <section className="flex min-h-[520px] flex-col overflow-hidden rounded-xl border border-slate-700 bg-[#0f172a] shadow-lg">
      <div className="flex flex-col gap-3 border-b border-slate-700 bg-[#1e293b] px-4 pt-4 lg:flex-row lg:items-end lg:justify-between">
        <div className="flex flex-wrap gap-1">
          <button className="flex items-center gap-2 rounded-t-lg border-b-2 border-error bg-[#0f172a] px-4 py-3 text-label-caps uppercase text-white">
            <span className="material-symbols-outlined text-[16px]">terminal</span>
            Log Dịch vụ
          </button>
          <button className="flex items-center gap-2 rounded-t-lg px-4 py-3 text-label-caps uppercase text-slate-400">
            <span className="material-symbols-outlined text-[16px]">queue</span>
            Hàng đợi
          </button>
          <button className="flex items-center gap-2 rounded-t-lg px-4 py-3 text-label-caps uppercase text-slate-400">
            <span className="material-symbols-outlined text-[16px]">bug_report</span>
            Error traces
          </button>
        </div>
        <div className="flex items-center gap-3 pb-3">
          <div className="hidden items-center gap-2 rounded border border-slate-600 bg-[#0f172a] px-3 py-1.5 font-mono text-mono-status text-slate-300 sm:flex">
            <span className="material-symbols-outlined text-[16px] text-slate-400">tag</span>
            {selectedAgent?.code ?? "NO_AGENT"}
          </div>
          <button
            className="flex items-center gap-2 rounded border border-error px-3 py-1.5 text-label-caps uppercase text-white transition-colors hover:bg-error disabled:cursor-not-allowed disabled:opacity-50"
            disabled={!traces.length}
            onClick={onExport}
            type="button"
          >
            <span className="material-symbols-outlined text-[16px]">download</span>
            Xuất CSV
          </button>
        </div>
      </div>

      <div className="flex-1 space-y-2 overflow-y-auto p-4 font-mono text-mono-status text-slate-300 md:p-6">
        {loading ? (
          <div className="flex items-center gap-3 text-slate-400">
            <span className="size-3 animate-pulse rounded-full bg-slate-400" />
            Đang tải trace từ backend...
          </div>
        ) : traces.length ? (
          traces.map((trace) => (
            <div
              className={[
                "grid gap-2 rounded p-1 hover:bg-slate-800/60 md:grid-cols-[150px_92px_minmax(0,1fr)]",
                normalize(trace.phase).includes("error") ? "border-l-2 border-error bg-error/10 pl-3" : "",
              ].join(" ")}
              key={trace.id}
            >
              <span className="text-slate-500">{formatDateTime(trace.occurredAt)}</span>
              <span className={`font-bold uppercase ${phaseTone(trace.phase)}`}>[{trace.phase || "info"}]</span>
              <span className="min-w-0 break-words text-slate-300">{trace.message}</span>
            </div>
          ))
        ) : (
          <div className="rounded border border-slate-700 bg-slate-900/60 p-4 text-slate-400">
            Chưa có trace cho agent này. Khi backend ghi `AgentTrace`, log sẽ xuất hiện ở đây.
          </div>
        )}
      </div>

      <div className="flex items-center justify-between bg-primary px-4 py-1 font-mono text-[11px] uppercase text-on-primary">
        <span className="flex items-center gap-2">
          <span className="size-2 rounded-full bg-green-300" />
          API CONNECTED
        </span>
        <span>AUTO-SCROLL: ON</span>
      </div>
    </section>
  );
}

export default function AgentDashboardPage() {
  const queryClient = useQueryClient();
  const agentsQuery = useQuery({ queryKey: ["agents"], queryFn: listAgents });
  const costQuery = useQuery({ queryKey: ["analytics", "agent-cost"], queryFn: getAgentCost, staleTime: 60_000 });
  const agents = agentsQuery.data?.items ?? EMPTY_AGENTS;
  const costs = costQuery.data?.items ?? EMPTY_COSTS;
  const [selectedCode, setSelectedCode] = useState<string | null>(null);
  const selectedAgent = useMemo(() => {
    if (!agents.length) return null;
    return agents.find((agent) => agent.code === selectedCode) ?? agents[0];
  }, [agents, selectedCode]);
  const selectedCost = selectedAgent ? costForAgent(costs, selectedAgent.code) : null;

  const tracesQuery = useQuery({
    queryKey: ["agents", selectedAgent?.code, "traces"],
    queryFn: () => getAgentTraces(selectedAgent?.code ?? "", 1, 50),
    enabled: Boolean(selectedAgent?.code),
  });
  const traces = tracesQuery.data?.items ?? EMPTY_TRACES;

  const setStatusMutation = useMutation({
    mutationFn: (agent: AgentListItem) => (normalize(agent.status) === "running" ? disableAgent(agent.code) : enableAgent(agent.code)),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["agents"] });
    },
  });

  const stopAllMutation = useMutation({
    mutationFn: async (runningAgents: readonly AgentListItem[]) => {
      await Promise.all(runningAgents.map((agent) => disableAgent(agent.code)));
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["agents"] });
    },
  });

  const runningAgents = agents.filter((agent) => normalize(agent.status) === "running");
  const errorCount = agents.filter((agent) => normalize(agent.status) === "error").length;
  const totalUsd = costs.reduce((sum, item) => sum + item.usd, 0);
  const totalCalls = costs.reduce((sum, item) => sum + item.calls, 0);

  return (
    <AppShell title="Giám sát Agent">
      <div className="mb-stack-lg flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <h1 className="text-display-lg font-black text-on-surface">Giám sát & Cấu hình</h1>
          <p className="mt-1 text-body-lg text-on-surface-variant">
            Quản lý trạng thái agent, chi phí Claude và log trace từ backend theo thời gian vận hành.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <StatusPill tone={agentsQuery.isError ? "error" : "success"}>
            {agentsQuery.isError ? "Mất kết nối API" : "API: /api/agents"}
          </StatusPill>
          <Button
            className="bg-error hover:bg-red-700"
            disabled={!runningAgents.length || stopAllMutation.isPending}
            onClick={() => stopAllMutation.mutate(runningAgents)}
            type="button"
          >
            <span className="material-symbols-outlined text-[18px]">warning</span>
            Dừng agent đang chạy
          </Button>
        </div>
      </div>

      <section className="mb-gutter grid grid-cols-1 gap-gutter md:grid-cols-2 xl:grid-cols-4">
        <MetricCard
          label="Agent đang chạy"
          value={`${runningAgents.length}/${agents.length}`}
          delta="Start/stop qua /api/agents"
          icon="memory"
          tone={runningAgents.length ? "success" : "neutral"}
        />
        <MetricCard label="Agent lỗi" value={String(errorCount)} delta="Trace cần xử lý" icon="bug_report" tone={errorCount ? "error" : "success"} />
        <MetricCard label="Claude cost" value={formatCurrency(totalUsd)} delta="30 ngày gần nhất" icon="toll" tone="warning" />
        <MetricCard label="LLM calls" value={totalCalls.toLocaleString("vi-VN")} delta="agent-cost ledger" icon="analytics" tone="neutral" />
      </section>

      <section className="grid grid-cols-1 gap-gutter 2xl:grid-cols-[minmax(0,1fr)_360px]">
        <div className="space-y-gutter">
          <Card>
            <div className="mb-4 flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
              <div>
                <h2 className="text-headline-sm font-bold text-secondary">Agent Matrix</h2>
                <p className="mt-1 text-body-md text-on-surface-variant">
                  Mỗi node đọc từ `AgentConfig`, chọn node để xem trace gần nhất và chi phí tương ứng.
                </p>
              </div>
              <StatusPill tone={costQuery.isError ? "warning" : "success"}>
                {costQuery.isError ? "Thiếu cost ledger" : "Cost ledger online"}
              </StatusPill>
            </div>

            {agentsQuery.isLoading ? (
              <div className="rounded-lg border border-outline bg-surface p-6 text-body-md text-on-surface-variant">Đang tải danh sách agent...</div>
            ) : agentsQuery.isError ? (
              <div className="rounded-lg border border-error/30 bg-red-50 p-6 text-body-md text-error">
                Không thể tải `/api/agents`. Kiểm tra backend và quyền truy cập.
              </div>
            ) : agents.length ? (
              <div
                className="grid gap-5 rounded-lg border border-outline bg-surface p-5 sm:grid-cols-2 xl:grid-cols-3"
                style={{
                  backgroundImage: "radial-gradient(#cbd5e1 1px, transparent 1px)",
                  backgroundSize: "18px 18px",
                }}
              >
                {agents.map((agent) => (
                  <AgentNode
                    agent={agent}
                    cost={costForAgent(costs, agent.code)}
                    key={agent.code}
                    onSelect={() => setSelectedCode(agent.code)}
                    onToggle={() => setStatusMutation.mutate(agent)}
                    pending={setStatusMutation.isPending}
                    selected={selectedAgent?.code === agent.code}
                  />
                ))}
              </div>
            ) : (
              <div className="rounded-lg border border-outline bg-surface p-6 text-body-md text-on-surface-variant">
                Chưa có AgentConfig trong tenant hiện tại.
              </div>
            )}
          </Card>

          <TerminalLog
            loading={tracesQuery.isLoading}
            onExport={() => exportTraceCsv(selectedAgent, traces)}
            selectedAgent={selectedAgent}
            traces={traces}
          />
        </div>

        <aside className="space-y-gutter">
          <Card>
            <p className="text-label-caps uppercase text-on-surface-variant">Agent đang chọn</p>
            <h2 className="mt-2 text-headline-sm font-bold text-secondary">{selectedAgent?.displayName ?? "Chưa chọn agent"}</h2>
            <div className="mt-4 space-y-3 text-body-md">
              <div className="flex items-center justify-between gap-3">
                <span className="text-on-surface-variant">Mã agent</span>
                <span className="font-mono text-mono-status text-secondary">{selectedAgent?.code ?? "--"}</span>
              </div>
              <div className="flex items-center justify-between gap-3">
                <span className="text-on-surface-variant">Trạng thái</span>
                {selectedAgent ? <StatusPill tone={statusTone(selectedAgent.status)}>{statusLabel(selectedAgent.status)}</StatusPill> : <span>--</span>}
              </div>
              <div className="flex items-center justify-between gap-3">
                <span className="text-on-surface-variant">Model</span>
                <span className="font-mono text-mono-status text-secondary">{selectedAgent?.model ?? "--"}</span>
              </div>
              <div className="flex items-center justify-between gap-3">
                <span className="text-on-surface-variant">Lần chạy cuối</span>
                <span className="text-secondary">{formatDateTime(selectedAgent?.lastRunAt ?? null)}</span>
              </div>
            </div>
          </Card>

          <Card>
            <p className="text-label-caps uppercase text-on-surface-variant">Claude cost</p>
            <p className="mt-2 text-telemetry-data text-secondary">{selectedCost ? formatCurrency(selectedCost.usd) : "$0.00"}</p>
            <p className="mt-1 font-mono text-mono-status text-on-surface-variant">
              {selectedCost ? `${selectedCost.calls.toLocaleString("vi-VN")} calls · avg ${formatCurrency(selectedCost.avgUsdPerCall)}` : "Chưa có cost row"}
            </p>
          </Card>

          <Card>
            <p className="text-label-caps uppercase text-on-surface-variant">Chi phí theo agent</p>
            <div className="mt-4 space-y-3">
              {costs.length ? (
                costs.slice(0, 6).map((item) => (
                  <div className="space-y-1" key={item.agentCode}>
                    <div className="flex items-center justify-between gap-3 text-body-md">
                      <span className="font-semibold text-secondary">{item.agentCode}</span>
                      <span className="font-mono text-mono-status text-primary">{formatCurrency(item.usd)}</span>
                    </div>
                    <div className="h-2 overflow-hidden rounded-full bg-surface-container">
                      <div
                        className="h-full rounded-full bg-primary"
                        style={{ width: `${Math.min(100, totalUsd ? (item.usd / totalUsd) * 100 : 0)}%` }}
                      />
                    </div>
                  </div>
                ))
              ) : (
                <p className="text-body-md text-on-surface-variant">Chưa có dữ liệu `/api/analytics/agent-cost`.</p>
              )}
            </div>
          </Card>
        </aside>
      </section>
    </AppShell>
  );
}
