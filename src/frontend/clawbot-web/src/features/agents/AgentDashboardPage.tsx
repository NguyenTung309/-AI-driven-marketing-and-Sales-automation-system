import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AppShell } from "@/shared/layout/AppShell";
import { Alert } from "@/shared/ui/Alert";
import { Button } from "@/shared/ui/Button";
import { Card } from "@/shared/ui/Card";
import { MetricCard } from "@/shared/ui/MetricCard";
import { StatusPill, type StatusTone } from "@/shared/ui/StatusPill";
import { WorkflowNode } from "@/shared/ui/WorkflowNode";
import { operationalPhaseLabel, toSafeOperationalText } from "@/shared/utils/userText";
import { AgentConfigDrawer } from "./AgentConfigDrawer";
import { OrchestrationPanel } from "./OrchestrationPanel";
import { listLlmConfigs, type LlmConfig } from "@/shared/api/llmConfigs";
import {
  disableAgent,
  enableAgent,
  getAgentSettings,
  getAgentCost,
  getAgentTraces,
  listAgents,
  runAgentSandbox,
  updateAgentSettings,
  type AgentCostItem,
  type AgentListItem,
  type AgentSettings,
  type AgentStatus,
  type AgentTraceItem,
  type UpdateAgentSettingsPayload,
} from "@/shared/api/agents";

const EMPTY_AGENTS: readonly AgentListItem[] = [];
const EMPTY_COSTS: readonly AgentCostItem[] = [];
const EMPTY_TRACES: readonly AgentTraceItem[] = [];

type AgentConfigTab = "prompt" | "model" | "tools";
type TerminalTab = "events" | "queue" | "errors";

interface AgentSettingsForm {
  readonly displayName: string;
  readonly model: string;
  readonly provider: string;
  readonly systemPrompt: string;
  readonly temperature: number;
  readonly maxTokens: number;
  readonly skillFiles: readonly string[];
  readonly kbModules: readonly string[];
  readonly llmConfigId: string;
}

// Backend tri-state: Guid.Empty = unbind. The form always carries the current selection.
const UNBIND_LLM_CONFIG = "00000000-0000-0000-0000-000000000000";

interface SandboxMessage {
  readonly id: string;
  readonly side: "bot" | "user";
  readonly text: string;
  readonly time: string;
}

const DEFAULT_SANDBOX_MESSAGES: readonly SandboxMessage[] = [
  { id: "sandbox-seed", side: "bot", text: "Chào bạn! Tôi có thể giúp gì cho bạn?", time: "now" },
];

const DEFAULT_AGENT_PROMPTS: Record<string, string> = {
  chat: "# Vai trò: Chuyên viên tư vấn khách hàng\n# Nhiệm vụ: Hỗ trợ học viên và phụ huynh về chương trình Học Bá\n# Giọng văn: Chuyên nghiệp, thân thiện",
  content: "# Vai trò: Chuyên viên nội dung giáo dục\n# Nhiệm vụ: Tạo nội dung đa kênh đúng định vị Học Bá\n# Giọng văn: Rõ ràng, hữu ích, truyền cảm hứng",
  lead: "# Vai trò: Chuyên viên đánh giá lead\n# Nhiệm vụ: Phân loại, chấm điểm và đề xuất bước chăm sóc tiếp theo\n# Giọng văn: Chính xác, ngắn gọn",
  report: "# Vai trò: Chuyên viên phân tích hiệu suất\n# Nhiệm vụ: Tổng hợp số liệu, phát hiện bất thường và đề xuất hành động\n# Giọng văn: Súc tích, có bằng chứng",
};

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

function firstNonBlank(...values: readonly (string | null | undefined)[]): string {
  return values.find((value) => value?.trim())?.trim() ?? "";
}

function costForAgent(costs: readonly AgentCostItem[], code: string): AgentCostItem | null {
  return costs.find((item) => item.agentCode.toLowerCase() === code.toLowerCase()) ?? null;
}

function agentTypeLabel(type: string): string {
  const value = normalize(type);
  if (value === "sale_assist") return "Trợ lý tư vấn";
  if (value === "content") return "Nội dung";
  if (value === "lead") return "Chấm điểm lead";
  if (value === "docs") return "Tài liệu";
  if (value === "ads") return "Quảng cáo";
  if (value === "report") return "Báo cáo";
  if (value === "research") return "Nghiên cứu";
  if (value === "chat") return "Trò chuyện";
  if (value === "orchestrator") return "Điều phối viên";
  return type || "Agent";
}

function isOrchestrator(agent: AgentListItem): boolean {
  const code = normalize(agent.code);
  const type = normalize(agent.agentType);
  return code.includes("orchestrator") || type.includes("orchestrator") || type.includes("planner");
}

function defaultPromptFor(agent: AgentListItem | null): string {
  if (!agent) return DEFAULT_AGENT_PROMPTS.chat;
  return DEFAULT_AGENT_PROMPTS[normalize(agent.agentType)] ?? DEFAULT_AGENT_PROMPTS.chat;
}

function nowLabel(): string {
  return new Intl.DateTimeFormat("vi-VN", { hour: "2-digit", minute: "2-digit" }).format(new Date());
}

function buildSettingsPayload(form: AgentSettingsForm): UpdateAgentSettingsPayload {
  return {
    displayName: form.displayName,
    model: form.model,
    provider: form.provider,
    systemPrompt: form.systemPrompt,
    temperature: form.temperature,
    maxTokens: form.maxTokens,
    skillFiles: form.skillFiles,
    kbModules: form.kbModules,
    llmConfigId: form.llmConfigId === "" ? UNBIND_LLM_CONFIG : form.llmConfigId,
  };
}

function exportTraceCsv(agent: AgentListItem | null, traces: readonly AgentTraceItem[]) {
  const rows = [
    ["thoi_gian", "agent", "loai_su_kien", "noi_dung", "ma_phien"],
    ...traces.map((trace) => [
      trace.occurredAt,
      trace.agentName,
      operationalPhaseLabel(trace.phase),
      toSafeOperationalText(trace.message),
      trace.sessionId,
    ]),
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
  onConfigure,
  onToggle,
  pending,
}: {
  readonly agent: AgentListItem;
  readonly cost: AgentCostItem | null;
  readonly selected: boolean;
  readonly onSelect: () => void;
  readonly onConfigure: (tab: AgentConfigTab) => void;
  readonly onToggle: () => void;
  readonly pending: boolean;
}) {
  const running = normalize(agent.status) === "running";
  return (
    <div
      className={[
        "w-fit cursor-pointer text-left transition-transform hover:-translate-y-0.5 focus:outline-none",
        selected ? "rounded-lg ring-2 ring-primary ring-offset-2 ring-offset-surface" : "",
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
          <span>Mô hình</span>
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
        <div className="grid grid-cols-2 gap-2 pt-2">
          {(["prompt", "model", "tools"] as const).map((tab) => (
            <button
              className="w-full rounded border border-outline bg-white px-3 py-1.5 text-body-md font-bold text-secondary transition-colors hover:border-primary hover:text-primary"
              key={tab}
              onClick={(event) => {
                event.stopPropagation();
                onConfigure(tab);
              }}
              type="button"
            >
              {tab === "prompt" ? "Prompt" : tab === "model" ? "LLM" : "Công cụ"}
            </button>
          ))}
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
            {running ? "Dừng" : "Chạy"}
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
  const [activeTab, setActiveTab] = useState<TerminalTab>("events");
  const visibleTraces = traces.filter((trace) => {
    const phase = normalize(trace.phase);
    if (activeTab === "errors") return phase.includes("error") || phase.includes("fail");
    if (activeTab === "queue")
      return phase.includes("queue") || phase.includes("pending") || phase.includes("waiting")
        || phase.includes("planning_started") || phase === "planned" || phase.includes("started") || phase.includes("running");
    return true;
  });
  const emptyText = activeTab === "errors" ? "Chưa có sự kiện lỗi cho agent này." : activeTab === "queue" ? "Chưa có sự kiện hàng đợi cho agent này." : "Chưa có sự kiện vận hành cho agent này. Khi có hoạt động mới, danh sách sẽ tự cập nhật.";
  const tabs: readonly { readonly key: TerminalTab; readonly icon: string; readonly label: string }[] = [
    { key: "events", icon: "terminal", label: "Sự kiện vận hành" },
    { key: "queue", icon: "queue", label: "Hàng đợi" },
    { key: "errors", icon: "bug_report", label: "Sự kiện lỗi" },
  ];

  return (
    <section className="flex min-h-[520px] flex-col overflow-hidden rounded-xl border border-slate-700 bg-[#0f172a] shadow-lg">
      <div className="flex flex-col gap-3 border-b border-slate-700 bg-[#1e293b] px-4 pt-4 lg:flex-row lg:items-end lg:justify-between">
        <div className="flex flex-wrap gap-1">
          {tabs.map((item) => (
            <button
              className={[
                "flex items-center gap-2 rounded-t-lg border-b-2 px-4 py-3 text-label-caps uppercase transition-colors",
                activeTab === item.key ? "border-error bg-[#0f172a] text-white" : "border-transparent text-slate-400 hover:text-white",
              ].join(" ")}
              key={item.key}
              onClick={() => setActiveTab(item.key)}
              type="button"
            >
              <span aria-hidden="true" className="material-symbols-outlined text-[16px]">{item.icon}</span>
              {item.label}
            </button>
          ))}
        </div>
        <div className="flex items-center gap-3 pb-3">
          <div className="hidden items-center gap-2 rounded border border-slate-600 bg-[#0f172a] px-3 py-1.5 font-mono text-mono-status text-slate-300 sm:flex">
            <span aria-hidden="true" className="material-symbols-outlined text-[16px] text-slate-400">tag</span>
            {selectedAgent?.code ?? "Chưa chọn agent"}
          </div>
          <button
            className="flex items-center gap-2 rounded border border-error px-3 py-1.5 text-label-caps uppercase text-white transition-colors hover:bg-error disabled:cursor-not-allowed disabled:opacity-50"
            disabled={!traces.length}
            onClick={onExport}
            type="button"
          >
            <span aria-hidden="true" className="material-symbols-outlined text-[16px]">download</span>
            Tải sự kiện
          </button>
        </div>
      </div>

      <div className="flex-1 space-y-2 overflow-y-auto p-4 font-mono text-mono-status text-slate-300 md:p-6">
        {loading ? (
          <div className="flex items-center gap-3 text-slate-400">
            <span className="size-3 animate-pulse rounded-full bg-slate-400" />
            Đang tải sự kiện vận hành...
          </div>
        ) : visibleTraces.length ? (
          visibleTraces.map((trace) => (
            <div
              className={[
                "grid gap-2 rounded p-1 hover:bg-slate-800/60 md:grid-cols-[150px_92px_minmax(0,1fr)]",
                normalize(trace.phase).includes("error") ? "border-l-2 border-error bg-error/10 pl-3" : "",
              ].join(" ")}
              key={trace.id}
            >
              <span className="text-slate-500">{formatDateTime(trace.occurredAt)}</span>
              <span className={`font-bold uppercase ${phaseTone(trace.phase)}`}>[{operationalPhaseLabel(trace.phase)}]</span>
              <span className="min-w-0 break-words text-slate-300">{toSafeOperationalText(trace.message)}</span>
            </div>
          ))
        ) : (
          <div className="rounded border border-slate-700 bg-slate-900/60 p-4 text-slate-400">
            {emptyText}
          </div>
        )}
      </div>

      <div className="flex items-center justify-between bg-primary px-4 py-1 font-mono text-[11px] uppercase text-on-primary">
        <span className="flex items-center gap-2">
          <span className="size-2 rounded-full bg-green-300" />
          Đã kết nối
        </span>
        <span>Tự cuộn: bật</span>
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
  const [notice, setNotice] = useState<string | null>(null);
  const [configAgentCode, setConfigAgentCode] = useState<string | null>(null);
  const [configTab, setConfigTab] = useState<AgentConfigTab>("prompt");
  const [settingsDraft, setSettingsDraft] = useState<Partial<UpdateAgentSettingsPayload>>({});
  const [sandboxInput, setSandboxInput] = useState("");
  const [sandboxMessages, setSandboxMessages] = useState<readonly SandboxMessage[]>(DEFAULT_SANDBOX_MESSAGES);
  const selectedAgent = useMemo(() => {
    if (!agents.length) return null;
    return agents.find((agent) => agent.code === selectedCode) ?? agents.find(isOrchestrator) ?? agents[0];
  }, [agents, selectedCode]);
  const configAgent = useMemo(() => agents.find((agent) => agent.code === configAgentCode) ?? null, [agents, configAgentCode]);
  const selectedCost = selectedAgent ? costForAgent(costs, selectedAgent.code) : null;

  const settingsQuery = useQuery({
    queryKey: ["agents", configAgentCode, "settings"],
    queryFn: () => getAgentSettings(configAgentCode ?? ""),
    enabled: Boolean(configAgentCode),
  });
  const settings: AgentSettings | undefined = settingsQuery.data;
  const llmConfigsQuery = useQuery({
    queryKey: ["llm-configs"],
    queryFn: listLlmConfigs,
    enabled: Boolean(configAgentCode),
  });
  const llmConfigs: readonly LlmConfig[] = llmConfigsQuery.data ?? [];
  const selectedLlmConfig = llmConfigs.find((config) => config.id === (settingsDraft.llmConfigId ?? settings?.llmConfigId));
  const settingsForm: AgentSettingsForm = useMemo(
    () => ({
      displayName: settingsDraft.displayName ?? settings?.displayName ?? configAgent?.displayName ?? "",
      model: firstNonBlank(settingsDraft.model, selectedLlmConfig?.modelId, settings?.model, configAgent?.model, "claude"),
      provider: firstNonBlank(settingsDraft.provider, selectedLlmConfig?.provider, settings?.provider, "claude"),
      systemPrompt: settingsDraft.systemPrompt ?? settings?.systemPrompt ?? defaultPromptFor(configAgent),
      temperature: settingsDraft.temperature ?? settings?.temperature ?? 0.4,
      maxTokens: settingsDraft.maxTokens ?? settings?.maxTokens ?? 2048,
      skillFiles: settingsDraft.skillFiles ?? settings?.skillFiles ?? [],
      kbModules: settingsDraft.kbModules ?? settings?.kbModules ?? [],
      llmConfigId: settingsDraft.llmConfigId ?? settings?.llmConfigId ?? "",
    }),
    [configAgent, selectedLlmConfig, settings, settingsDraft],
  );

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

  const settingsMutation = useMutation({
    mutationFn: () => {
      if (!configAgentCode) throw new Error("Chưa chọn agent để cấu hình.");
      return updateAgentSettings(configAgentCode, buildSettingsPayload(settingsForm));
    },
    onSuccess: async (saved) => {
      setNotice(`Đã lưu cấu hình ${saved.displayName}.`);
      setSettingsDraft({});
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["agents"] }),
        queryClient.invalidateQueries({ queryKey: ["agents", saved.code, "settings"] }),
      ]);
    },
  });

  const sandboxMutation = useMutation({
    mutationFn: ({ code, message }: { readonly code: string; readonly message: string }) => runAgentSandbox(code, message),
    onSuccess: async (response) => {
      setSandboxMessages((current) => [
        ...current,
        {
          id: response.sessionId,
          side: "bot",
          text: toSafeOperationalText(response.reply, "Đã ghi nhận phản hồi chạy thử."),
          time: formatDateTime(response.sentAt),
        },
      ]);
      await queryClient.invalidateQueries({ queryKey: ["agents", configAgentCode, "traces"] });
    },
    onError: () => {
      setSandboxMessages((current) => [
        ...current,
        { id: `sandbox-error-${Date.now()}`, side: "bot", text: "Không thể chạy thử. Vui lòng kiểm tra quyền truy cập hoặc thử lại sau.", time: nowLabel() },
      ]);
    },
  });

  function openAgentConfig(agent: AgentListItem, tab: AgentConfigTab = "prompt") {
    setSelectedCode(agent.code);
    setConfigAgentCode(agent.code);
    setConfigTab(tab);
    setSettingsDraft({});
    setSandboxInput("");
    setSandboxMessages(DEFAULT_SANDBOX_MESSAGES);
  }

  function closeAgentConfig() {
    setConfigAgentCode(null);
    setSettingsDraft({});
    setSandboxInput("");
    setSandboxMessages(DEFAULT_SANDBOX_MESSAGES);
  }

  function sendSandboxMessage(messageOverride?: string) {
    if (!configAgentCode) return;
    const message = (messageOverride ?? sandboxInput).trim();
    if (!message) return;
    setSandboxMessages((current) => [...current, { id: `sandbox-user-${Date.now()}`, side: "user", text: message, time: nowLabel() }]);
    setSandboxInput("");
    sandboxMutation.mutate({ code: configAgentCode, message });
  }

  const visibleOrchestrator = agents.find(isOrchestrator) ?? null;
  const runningAgents = visibleOrchestrator && normalize(visibleOrchestrator.status) === "running" ? [visibleOrchestrator] : [];
  const errorCount = visibleOrchestrator && normalize(visibleOrchestrator.status) === "error" ? 1 : 0;
  const totalUsd = costs.reduce((sum, item) => sum + item.usd, 0);
  const totalCalls = costs.reduce((sum, item) => sum + item.calls, 0);

  return (
    <AppShell title="Giám sát Agent">
      <div className="mb-stack-lg flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <h1 className="text-display-lg font-black text-on-surface">Giám sát & Cấu hình</h1>
          <p className="mt-1 text-body-lg text-on-surface-variant">
            Quản lý trạng thái agent, chi phí AI và các sự kiện vận hành theo thời gian thực.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <StatusPill tone={agentsQuery.isError ? "error" : "success"}>
            {agentsQuery.isError ? "Mất kết nối dữ liệu" : "Agent đang trực tuyến"}
          </StatusPill>
          <Button
            className="bg-error hover:bg-red-700"
            disabled={!runningAgents.length || stopAllMutation.isPending}
            onClick={() => stopAllMutation.mutate(runningAgents)}
            type="button"
          >
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">warning</span>
            Dừng điều phối viên đang chạy
          </Button>
        </div>
      </div>

      {notice ? (
        <div className="mb-gutter">
          <Alert tone="success">{notice}</Alert>
        </div>
      ) : null}

      <section className="mb-gutter">
        <OrchestrationPanel />
      </section>

      <section className="mb-gutter grid grid-cols-1 gap-gutter md:grid-cols-2 xl:grid-cols-4">
        <MetricCard
          label="Điều phối viên"
          value={visibleOrchestrator ? statusLabel(visibleOrchestrator.status) : "Chưa có"}
          delta=""
          icon="memory"
          tone={visibleOrchestrator ? statusTone(visibleOrchestrator.status) : "neutral"}
        />
        <MetricCard label="Điều phối viên lỗi" value={String(errorCount)} delta="Cần kiểm tra" icon="bug_report" tone={errorCount ? "error" : "success"} />
        <MetricCard label="Chi phí AI" value={formatCurrency(totalUsd)} delta="30 ngày gần nhất" icon="toll" tone="warning" />
        <MetricCard label="Lượt gọi AI" value={totalCalls.toLocaleString("vi-VN")} delta="Theo sổ chi phí" icon="analytics" tone="neutral" />
      </section>

      <section className="grid grid-cols-1 gap-gutter 2xl:grid-cols-[minmax(0,1fr)_360px]">
        <div className="space-y-gutter">
          <Card>
            <div className="mb-4 flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
              <div>
                <h2 className="text-headline-sm font-bold text-secondary">Sơ đồ agent</h2>
              </div>
              <StatusPill tone={costQuery.isError ? "warning" : "success"}>
                {costQuery.isError ? "Chưa có dữ liệu chi phí" : "Dữ liệu chi phí sẵn sàng"}
              </StatusPill>
            </div>

            {agentsQuery.isLoading ? (
              <div className="rounded-lg border border-outline bg-surface p-6 text-body-md text-on-surface-variant">Đang tải danh sách agent...</div>
            ) : agentsQuery.isError ? (
              <div className="rounded-lg border border-error/30 bg-red-50 p-6 text-body-md text-error">
                Không thể tải danh sách agent. Vui lòng thử lại hoặc kiểm tra quyền truy cập.
              </div>
            ) : agents.length ? (
              <div
                className="rounded-lg border border-outline bg-surface p-5"
                style={{
                  backgroundImage: "radial-gradient(#cbd5e1 1px, transparent 1px)",
                  backgroundSize: "18px 18px",
                }}
              >
                {visibleOrchestrator ? (
                  <div className="flex flex-col items-center">
                    <AgentNode
                      agent={visibleOrchestrator}
                      cost={costForAgent(costs, visibleOrchestrator.code)}
                      onConfigure={(tab) => openAgentConfig(visibleOrchestrator, tab)}
                      onSelect={() => setSelectedCode(visibleOrchestrator.code)}
                      onToggle={() => setStatusMutation.mutate(visibleOrchestrator)}
                      pending={setStatusMutation.isPending}
                      selected={selectedAgent?.code === visibleOrchestrator.code}
                    />
                    <div className="h-8 w-px bg-outline" />
                    <div className="h-px w-full max-w-3xl bg-outline" />
                  </div>
                ) : null}
              </div>
            ) : (
              <div className="rounded-lg border border-outline bg-surface p-6 text-body-md text-on-surface-variant">
                Chưa có agent nào trong đơn vị hiện tại.
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
                <span className="text-on-surface-variant">Mã định danh</span>
                <span className="font-mono text-mono-status text-secondary">{selectedAgent?.code ?? "--"}</span>
              </div>
              <div className="flex items-center justify-between gap-3">
                <span className="text-on-surface-variant">Trạng thái</span>
                {selectedAgent ? <StatusPill tone={statusTone(selectedAgent.status)}>{statusLabel(selectedAgent.status)}</StatusPill> : <span>--</span>}
              </div>
              <div className="flex items-center justify-between gap-3">
                <span className="text-on-surface-variant">Mô hình AI</span>
                <span className="font-mono text-mono-status text-secondary">{selectedAgent?.model ?? "--"}</span>
              </div>
              <div className="flex items-center justify-between gap-3">
                <span className="text-on-surface-variant">Lần chạy cuối</span>
                <span className="text-secondary">{formatDateTime(selectedAgent?.lastRunAt ?? null)}</span>
              </div>
            </div>
            <Button className="mt-5 w-full" disabled={!selectedAgent} onClick={() => selectedAgent && openAgentConfig(selectedAgent)} type="button">
              <span aria-hidden="true" className="material-symbols-outlined text-[18px]">settings</span>
              Cấu hình agent
            </Button>
          </Card>

          <Card>
            <p className="text-label-caps uppercase text-on-surface-variant">Chi phí AI</p>
            <p className="mt-2 text-telemetry-data text-secondary">{selectedCost ? formatCurrency(selectedCost.usd) : "$0.00"}</p>
            <p className="mt-1 font-mono text-mono-status text-on-surface-variant">
              {selectedCost ? `${selectedCost.calls.toLocaleString("vi-VN")} lượt · trung bình ${formatCurrency(selectedCost.avgUsdPerCall)}` : "Chưa có dữ liệu chi phí"}
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
                <p className="text-body-md text-on-surface-variant">Chưa có dữ liệu chi phí agent.</p>
              )}
            </div>
          </Card>
        </aside>
      </section>

      {configAgent ? (
        <AgentConfigDrawer
          agent={configAgent}
          form={settingsForm}
          llmConfigs={llmConfigs}
          onClose={closeAgentConfig}
          onDraftChange={(patch) => setSettingsDraft((current) => ({ ...current, ...patch }))}
          onSandboxInputChange={setSandboxInput}
          onSave={() => settingsMutation.mutate()}
          onSendSandbox={sendSandboxMessage}
          onTabChange={setConfigTab}
          sandboxInput={sandboxInput}
          sandboxMessages={sandboxMessages}
          sandboxPending={sandboxMutation.isPending}
          saving={settingsMutation.isPending}
          settingsLoading={settingsQuery.isLoading}
          tab={configTab}
        />
      ) : null}
    </AppShell>
  );
}
