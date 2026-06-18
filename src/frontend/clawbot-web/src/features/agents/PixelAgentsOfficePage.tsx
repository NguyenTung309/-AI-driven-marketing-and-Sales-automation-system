import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { AppShell } from "@/shared/layout/AppShell";
import { getAgentTraces, listAgents, type AgentListItem, type AgentStatus, type AgentTraceItem } from "@/shared/api/agents";

const SAMPLE_AGENTS: readonly AgentListItem[] = [
  { code: "chat", displayName: "Chat", agentType: "chat", model: "claude", status: "running", updatedAt: "", lastRunAt: null },
  { code: "sale", displayName: "Sale", agentType: "sale_assist", model: "claude", status: "running", updatedAt: "", lastRunAt: null },
  { code: "content", displayName: "Content", agentType: "content", model: "claude", status: "running", updatedAt: "", lastRunAt: null },
  { code: "research", displayName: "Research", agentType: "research", model: "claude", status: "stopped", updatedAt: "", lastRunAt: null },
  { code: "ads", displayName: "Ads", agentType: "ads", model: "rules", status: "running", updatedAt: "", lastRunAt: null },
  { code: "docs", displayName: "Docs", agentType: "docs", model: "questpdf", status: "stopped", updatedAt: "", lastRunAt: null },
  { code: "report", displayName: "Report", agentType: "report", model: "mlnet", status: "running", updatedAt: "", lastRunAt: null },
  { code: "lead", displayName: "Lead", agentType: "lead", model: "rules", status: "error", updatedAt: "", lastRunAt: null },
];

const SAMPLE_TRACES: readonly AgentTraceItem[] = [
  {
    id: "trace-seed-1",
    sessionId: "demo",
    agentName: "Chat",
    phase: "complete",
    message: "Captured visitor intent and handed context to Sale.",
    occurredAt: new Date().toISOString(),
  },
  {
    id: "trace-seed-2",
    sessionId: "demo",
    agentName: "Content",
    phase: "running",
    message: "Drafting campaign variants for the next HSK intake.",
    occurredAt: new Date(Date.now() - 180_000).toISOString(),
  },
  {
    id: "trace-seed-3",
    sessionId: "demo",
    agentName: "Lead",
    phase: "warning",
    message: "Duplicate contact candidate needs review before scoring.",
    occurredAt: new Date(Date.now() - 420_000).toISOString(),
  },
];

const POSITIONS: readonly { readonly left: string; readonly top: string }[] = [
  { left: "9%", top: "18%" },
  { left: "32%", top: "16%" },
  { left: "56%", top: "18%" },
  { left: "78%", top: "20%" },
  { left: "16%", top: "55%" },
  { left: "39%", top: "58%" },
  { left: "62%", top: "56%" },
  { left: "82%", top: "58%" },
];

function normalize(value: string): string {
  return value.trim().toLowerCase();
}

function statusTone(status: AgentStatus): "running" | "stopped" | "error" | "other" {
  const value = normalize(status);
  if (value === "running") return "running";
  if (value === "stopped") return "stopped";
  if (value === "error") return "error";
  return "other";
}

function statusLabel(status: AgentStatus): string {
  const tone = statusTone(status);
  if (tone === "running") return "Đang chạy";
  if (tone === "stopped") return "Tạm dừng";
  if (tone === "error") return "Cần xử lý";
  return status;
}

function agentLabel(agent: AgentListItem): string {
  const type = normalize(agent.agentType || agent.code);
  if (type === "sale_assist") return "Sale";
  if (type === "lead") return "Lead";
  if (type === "docs") return "Docs";
  if (type === "ads") return "Ads";
  if (type === "report") return "Report";
  if (type === "research") return "Research";
  if (type === "content") return "Content";
  if (type === "chat") return "Chat";
  return agent.displayName || agent.code;
}

function statusClasses(status: AgentStatus): string {
  const tone = statusTone(status);
  if (tone === "running") return "border-emerald-400 bg-emerald-100 text-emerald-900";
  if (tone === "error") return "border-red-400 bg-red-100 text-red-900";
  if (tone === "stopped") return "border-slate-300 bg-slate-100 text-slate-600";
  return "border-amber-400 bg-amber-100 text-amber-900";
}

function timeLabel(value: string | null): string {
  if (!value) return "No run yet";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("vi-VN", { hour: "2-digit", minute: "2-digit" }).format(date);
}

function PixelPerson({ active, error }: { readonly active: boolean; readonly error: boolean }) {
  const shirt = error ? "bg-red-500" : active ? "bg-green-500" : "bg-slate-400";
  return (
    <div className="grid justify-items-center gap-0.5" aria-hidden="true">
      <span className="block size-3 bg-[#f1c28f] shadow-[3px_0_0_#2f2119,-3px_0_0_#2f2119]" />
      <span className={`block h-4 w-5 ${shirt} shadow-[0_3px_0_#1f2937]`} />
      <span className="block h-2 w-7 bg-[#1f2937]" />
    </div>
  );
}

function OfficeAgent({
  agent,
  index,
  selected,
  onSelect,
}: {
  readonly agent: AgentListItem;
  readonly index: number;
  readonly selected: boolean;
  readonly onSelect: () => void;
}) {
  const tone = statusTone(agent.status);
  const active = tone === "running";
  const error = tone === "error";
  const position = POSITIONS[index % POSITIONS.length];

  return (
    <button
      className={[
        "absolute w-[112px] -translate-x-1/2 -translate-y-1/2 text-left transition-transform hover:-translate-y-[54%] focus:outline-none",
        selected ? "z-20 scale-105" : "z-10",
      ].join(" ")}
      onClick={onSelect}
      style={position}
      type="button"
    >
      <span className={`mb-2 inline-flex rounded border px-2 py-0.5 text-[11px] font-bold leading-4 ${statusClasses(agent.status)}`}>
        {agentLabel(agent)}
      </span>
      <span
        className={[
          "block border-2 bg-white p-2 shadow-[6px_6px_0_#243244]",
          selected ? "border-red-600" : error ? "border-red-400" : "border-slate-700",
        ].join(" ")}
      >
        <span className="mb-1 block h-3 bg-slate-200" />
        <span className="mb-2 block h-5 border border-slate-700 bg-[#dbeafe]" />
        <PixelPerson active={active} error={error} />
      </span>
    </button>
  );
}

function Metric({ icon, label, value }: { readonly icon: string; readonly label: string; readonly value: string }) {
  return (
    <div className="border border-slate-200 bg-white px-4 py-3 shadow-sm">
      <div className="flex items-center gap-2 text-[12px] font-bold uppercase tracking-[0] text-slate-500">
        <span className="material-symbols-outlined text-[17px]">{icon}</span>
        {label}
      </div>
      <div className="mt-2 text-[26px] font-black leading-8 text-slate-950">{value}</div>
    </div>
  );
}

function TraceRow({ trace }: { readonly trace: AgentTraceItem }) {
  const tone = normalize(trace.phase);
  const dot = tone.includes("error") ? "bg-red-500" : tone.includes("warn") ? "bg-amber-500" : "bg-green-500";
  return (
    <li className="grid grid-cols-[18px_minmax(0,1fr)] gap-3 border-b border-slate-200 py-3 last:border-b-0">
      <span className={`mt-1 size-3 ${dot}`} />
      <span className="min-w-0">
        <span className="flex flex-wrap items-center gap-2">
          <span className="font-bold text-slate-900">{trace.agentName || "Agent"}</span>
          <span className="font-mono text-[11px] uppercase text-slate-500">{trace.phase || "trace"}</span>
          <span className="ml-auto font-mono text-[11px] text-slate-400">{timeLabel(trace.occurredAt)}</span>
        </span>
        <span className="mt-1 block text-[13px] leading-5 text-slate-600">{trace.message}</span>
      </span>
    </li>
  );
}

export default function PixelAgentsOfficePage() {
  const agentsQuery = useQuery({ queryKey: ["agents", "office"], queryFn: listAgents, refetchInterval: 5_000 });
  const agents = agentsQuery.data?.items?.length ? agentsQuery.data.items : SAMPLE_AGENTS;
  const usingFallback = !agentsQuery.data?.items?.length;
  const [selectedCode, setSelectedCode] = useState<string>(agents[0]?.code ?? "chat");
  const selectedAgent = agents.find((agent) => agent.code === selectedCode) ?? agents[0] ?? null;

  const tracesQuery = useQuery({
    queryKey: ["agents-office", selectedAgent?.code, "traces"],
    queryFn: () => getAgentTraces(selectedAgent?.code ?? "", 1, 16),
    enabled: Boolean(selectedAgent?.code) && !usingFallback,
    refetchInterval: 5_000,
  });

  const traces = tracesQuery.data?.items?.length ? tracesQuery.data.items : SAMPLE_TRACES;
  const runningCount = agents.filter((agent) => statusTone(agent.status) === "running").length;
  const errorCount = agents.filter((agent) => statusTone(agent.status) === "error").length;
  const queue = useMemo(
    () =>
      agents
        .map((agent, index) => ({
          agent,
          task: `${agentLabel(agent)} ${statusTone(agent.status) === "running" ? "đang xử lý tác vụ" : "đang chờ lượt chạy"}`,
          priority: statusTone(agent.status) === "error" ? "P0" : index % 3 === 0 ? "P1" : "P2",
        }))
        .slice(0, 6),
    [agents],
  );

  return (
    <AppShell title="Pixel Agents Office">
      <section className="mb-5 border border-slate-200 bg-white px-5 py-4 shadow-sm">
        <div className="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
          <div>
            <h1 className="text-[32px] font-black leading-10 tracking-[0] text-slate-950">Pixel Agents Office</h1>
            <p className="mt-1 text-[15px] leading-6 text-slate-600">Mặt bằng vận hành agent, hàng đợi tác vụ và trace đang chạy theo thời gian thực.</p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <span className="inline-flex items-center gap-2 border border-red-200 bg-red-50 px-3 py-2 text-[12px] font-bold uppercase text-red-700">
              <span className="size-2 animate-pulse bg-red-600" />
              Đang đồng bộ
            </span>
            <span className="border border-slate-200 px-3 py-2 font-mono text-[12px] uppercase text-slate-500">Polling 5s</span>
          </div>
        </div>
      </section>

      <section className="mb-5 grid grid-cols-1 gap-4 md:grid-cols-4">
        <Metric icon="memory" label="Active agents" value={`${runningCount}/${agents.length}`} />
        <Metric icon="priority_high" label="Attention" value={String(errorCount)} />
        <Metric icon="route" label="Queue lanes" value={String(queue.length)} />
        <Metric icon="history" label="Trace feed" value={String(traces.length)} />
      </section>

      <section className="grid grid-cols-1 gap-5 2xl:grid-cols-[320px_minmax(0,1fr)_340px]">
        <aside className="border border-slate-200 bg-white shadow-sm">
          <div className="border-b border-slate-200 px-4 py-3">
            <h2 className="text-[18px] font-black leading-6 text-slate-950">Task queue</h2>
            <p className="text-[13px] leading-5 text-slate-500">Agent work lanes sorted by current floor state.</p>
          </div>
          <ul className="divide-y divide-slate-200">
            {queue.map((item) => (
              <li key={item.agent.code}>
                <button
                  className={["block w-full px-4 py-4 text-left transition-colors hover:bg-slate-50", selectedAgent?.code === item.agent.code ? "bg-red-50" : ""].join(" ")}
                  onClick={() => setSelectedCode(item.agent.code)}
                  type="button"
                >
                  <span className="flex items-center justify-between gap-3">
                    <span className="font-bold text-slate-950">{agentLabel(item.agent)}</span>
                    <span className="font-mono text-[11px] font-bold text-slate-500">{item.priority}</span>
                  </span>
                  <span className="mt-1 block text-[13px] leading-5 text-slate-600">{item.task}</span>
                  <span className={`mt-2 inline-flex border px-2 py-0.5 text-[11px] font-bold ${statusClasses(item.agent.status)}`}>{statusLabel(item.agent.status)}</span>
                </button>
              </li>
            ))}
          </ul>
        </aside>

        <div className="min-w-0 border border-slate-200 bg-white shadow-sm">
          <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-200 px-4 py-3">
            <div>
              <h2 className="text-[18px] font-black leading-6 text-slate-950">Agent floor</h2>
              <p className="text-[13px] leading-5 text-slate-500">Theo dõi trạng thái vận hành của từng agent.</p>
            </div>
            {usingFallback ? (
              <span className="border border-amber-300 bg-amber-50 px-3 py-1.5 text-[12px] font-bold uppercase text-amber-800">Demo fallback</span>
            ) : (
              <span className="border border-green-300 bg-green-50 px-3 py-1.5 text-[12px] font-bold uppercase text-green-800">Đã kết nối</span>
            )}
          </div>

          <div className="relative min-h-[560px] overflow-hidden bg-[#f8fafc]">
            <div
              className="absolute inset-0 opacity-80"
              style={{
                backgroundImage: "radial-gradient(rgba(100,116,139,.28) 1px, transparent 1px)",
                backgroundSize: "24px 24px",
              }}
            />
            <div className="absolute left-[6%] top-[8%] h-[78%] w-[88%] border border-slate-200 bg-white/65 shadow-sm" />
            <div className="absolute left-[9%] top-[74%] h-5 w-[82%] bg-red-600/10" />
            {agents.map((agent, index) => (
              <OfficeAgent agent={agent} index={index} key={agent.code} onSelect={() => setSelectedCode(agent.code)} selected={selectedAgent?.code === agent.code} />
            ))}
          </div>
        </div>

        <aside className="space-y-5">
          <section className="border border-slate-200 bg-white shadow-sm">
            <div className="border-b border-slate-200 px-4 py-3">
              <h2 className="text-[18px] font-black leading-6 text-slate-950">Health</h2>
              <p className="text-[13px] leading-5 text-slate-500">Selected agent runtime signal.</p>
            </div>
            <div className="space-y-4 px-4 py-4">
              <div>
                <p className="text-[12px] font-bold uppercase text-slate-500">Agent</p>
                <p className="mt-1 text-[24px] font-black leading-8 text-slate-950">{selectedAgent ? agentLabel(selectedAgent) : "--"}</p>
              </div>
              <div className="grid grid-cols-2 gap-3 text-[13px]">
                <div className="border border-slate-200 bg-slate-50 p-3">
                  <p className="font-bold uppercase text-slate-500">Status</p>
                  <p className="mt-1 font-black text-slate-950">{selectedAgent ? statusLabel(selectedAgent.status) : "--"}</p>
                </div>
                <div className="border border-slate-200 bg-slate-50 p-3">
                  <p className="font-bold uppercase text-slate-500">Model</p>
                  <p className="mt-1 truncate font-black text-slate-950">{selectedAgent?.model || "--"}</p>
                </div>
              </div>
              <div className="border border-slate-200 bg-slate-50 p-3">
                <p className="font-bold uppercase text-slate-500">Last run</p>
                <p className="mt-1 font-mono text-[13px] text-slate-700">{timeLabel(selectedAgent?.lastRunAt ?? null)}</p>
              </div>
            </div>
          </section>

          <section className="border border-slate-200 bg-white shadow-sm">
            <div className="border-b border-slate-200 px-4 py-3">
              <h2 className="text-[18px] font-black leading-6 text-slate-950">Trace feed</h2>
              <p className="text-[13px] leading-5 text-slate-500">Recent events for the selected floor.</p>
            </div>
            <ul className="max-h-[410px] overflow-y-auto px-4">
              {traces.map((trace) => (
                <TraceRow key={trace.id} trace={trace} />
              ))}
            </ul>
          </section>
        </aside>
      </section>
    </AppShell>
  );
}
