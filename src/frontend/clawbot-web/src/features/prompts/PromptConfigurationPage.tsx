import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert } from "@/shared/ui/Alert";
import { Button } from "@/shared/ui/Button";
import { Card } from "@/shared/ui/Card";
import { MetricCard } from "@/shared/ui/MetricCard";
import { StatusPill, type StatusTone } from "@/shared/ui/StatusPill";
import { AppShell } from "@/shared/layout/AppShell";
import {
  getPromptConfig,
  listPromptConfigs,
  runPromptSandbox,
  updatePromptConfig,
  type PromptConfig,
  type PromptSandboxResponse,
  type PromptUsageLog,
  type UpdatePromptConfigPayload,
} from "@/shared/api/prompts";

const EMPTY_CONFIGS: readonly PromptConfig[] = [];
const EMPTY_USAGE: readonly PromptUsageLog[] = [];
const DEFAULT_SANDBOX_INPUT = "Giới thiệu ngắn về lộ trình học tiếng Trung cho học viên mới.";

const inputClass = "w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary";
const textAreaClass = `${inputClass} min-h-36 resize-y leading-relaxed`;

interface PromptDraft {
  readonly displayName?: string;
  readonly provider?: string;
  readonly model?: string;
  readonly temperature?: number;
  readonly maxTokens?: number;
  readonly systemPrompt?: string;
}

function normalize(value: string | null | undefined): string {
  return (value ?? "").trim().toLowerCase();
}

function formatNumber(value: number): string {
  return new Intl.NumberFormat("vi-VN").format(Math.round(value));
}

function formatUsd(value: number): string {
  return new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", maximumFractionDigits: 2 }).format(value);
}

function formatDateTime(value: string | null | undefined): string {
  if (!value) return "Chưa chạy";
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

function statusTone(config: PromptConfig): StatusTone {
  if (!config.provider || !config.model) return "error";
  if (normalize(config.status) === "running") return "success";
  if (normalize(config.status) === "error") return "error";
  if (!config.systemPrompt.trim()) return "warning";
  return "neutral";
}

function statusLabel(config: PromptConfig): string {
  if (!config.provider || !config.model) return "Thiếu runtime";
  if (!config.systemPrompt.trim()) return "Thiếu prompt";
  if (normalize(config.status) === "running") return "Runtime hợp lệ";
  if (normalize(config.status) === "error") return "Agent lỗi";
  return "Sẵn sàng cấu hình";
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

function providerInitial(provider: string): string {
  const value = provider.trim();
  return value ? value.slice(0, 1).toUpperCase() : "A";
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : "Không xử lý được cấu hình prompt.";
}

function toPayload(config: PromptConfig, draft: PromptDraft): UpdatePromptConfigPayload {
  return {
    displayName: draft.displayName ?? config.displayName,
    provider: draft.provider ?? config.provider,
    model: draft.model ?? config.model,
    temperature: draft.temperature ?? config.temperature,
    maxTokens: draft.maxTokens ?? config.maxTokens,
    systemPrompt: draft.systemPrompt ?? config.systemPrompt,
    skillFiles: config.skillFiles,
    kbModules: config.kbModules,
  };
}

function usagePercent(config: PromptConfig): number {
  const maxTokens = Math.max(1, config.maxTokens);
  return Math.min(100, (config.totalTokensLast7Days / maxTokens) * 100);
}

function PromptConfigCard({
  config,
  selected,
  onSelect,
  onSandbox,
}: {
  readonly config: PromptConfig;
  readonly selected: boolean;
  readonly onSelect: () => void;
  readonly onSandbox: () => void;
}) {
  return (
    <Card className={`flex min-h-[220px] flex-col transition-colors ${selected ? "border-primary ring-2 ring-primary/20" : ""}`}>
      <div className="mb-4 flex items-start justify-between gap-3">
        <button className="flex min-w-0 items-center gap-4 text-left" onClick={onSelect} type="button">
          <span className="flex size-12 shrink-0 items-center justify-center rounded-lg border border-outline bg-surface text-display-lg font-black text-primary">
            {providerInitial(config.provider || config.displayName)}
          </span>
          <span className="min-w-0">
            <span className="block truncate text-headline-sm text-secondary">{config.displayName}</span>
            <span className="mt-1 block truncate font-mono text-mono-status text-on-surface-variant">{config.model || "Chưa có model"}</span>
          </span>
        </button>
        <button aria-label="Xem chi tiết cấu hình" className="rounded p-1 text-on-surface-variant hover:bg-surface hover:text-secondary" onClick={onSelect} type="button">
          <span className="material-symbols-outlined">more_vert</span>
        </button>
      </div>

      <div className={`mb-4 flex items-center gap-2 rounded-lg border p-3 ${statusTone(config) === "error" ? "border-error/30 bg-error/10" : "border-outline bg-surface"}`}>
        <span className={`size-2.5 rounded-full ${statusTone(config) === "error" ? "bg-error" : statusTone(config) === "warning" ? "bg-warning" : "bg-success"}`} />
        <span className={`font-mono text-mono-status ${statusTone(config) === "error" ? "text-error" : "text-secondary"}`}>[{statusLabel(config)}]</span>
      </div>

      <div className="mb-4 grid grid-cols-2 gap-3 text-body-md">
        <div>
          <p className="text-label-sm uppercase text-on-surface-variant">Provider</p>
          <p className="mt-1 font-semibold text-secondary">{config.provider || "n/a"}</p>
        </div>
        <div>
          <p className="text-label-sm uppercase text-on-surface-variant">7 ngày</p>
          <p className="mt-1 font-mono text-mono-status text-secondary">{formatNumber(config.totalTokensLast7Days)} tokens</p>
        </div>
      </div>

      <div className="mt-auto grid grid-cols-2 gap-2 border-t border-outline pt-4">
        <button className="rounded-lg py-2 text-label-caps uppercase text-on-surface-variant hover:bg-surface hover:text-primary" onClick={onSelect} type="button">
          <span className="material-symbols-outlined mr-1 align-middle text-[18px]">edit</span>
          Sửa Prompt
        </button>
        <button className="rounded-lg py-2 text-label-caps uppercase text-on-surface-variant hover:bg-surface hover:text-primary" onClick={onSandbox} type="button">
          <span className="material-symbols-outlined mr-1 align-middle text-[18px]">bolt</span>
          Thử Sandbox
        </button>
      </div>
    </Card>
  );
}

function UsageBars({ config }: { readonly config: PromptConfig }) {
  const total = Math.max(1, config.inputTokensLast7Days + config.outputTokensLast7Days);
  const inputPct = Math.max(4, (config.inputTokensLast7Days / total) * 100);
  const outputPct = Math.max(4, (config.outputTokensLast7Days / total) * 100);
  const barHeights = [38, 56, 34, 78, 64, 44, Math.max(16, usagePercent(config))];

  return (
    <Card>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h3 className="text-headline-sm text-secondary">Mức tiêu thụ Token</h3>
          <p className="mt-1 text-body-md text-on-surface-variant">7 ngày gần nhất từ Claude cost ledger.</p>
        </div>
        <StatusPill tone="neutral">{formatUsd(config.usdLast7Days)}</StatusPill>
      </div>
      <div className="mb-4 flex h-36 items-end gap-2 rounded-lg border border-outline bg-surface p-4">
        {barHeights.map((height, index) => (
          <div className="flex flex-1 items-end gap-1" key={`${height}-${index}`}>
            <div className="w-1/2 rounded-t bg-success" style={{ height: `${Math.min(100, height * (inputPct / 100))}%` }} />
            <div className="w-1/2 rounded-t bg-primary" style={{ height: `${Math.min(100, height * (outputPct / 100))}%` }} />
          </div>
        ))}
      </div>
      <div className="flex flex-wrap gap-4 text-body-md text-on-surface-variant">
        <span className="inline-flex items-center gap-2">
          <span className="size-3 rounded-full bg-success" />
          Input {formatNumber(config.inputTokensLast7Days)}
        </span>
        <span className="inline-flex items-center gap-2">
          <span className="size-3 rounded-full bg-primary" />
          Output {formatNumber(config.outputTokensLast7Days)}
        </span>
      </div>
    </Card>
  );
}

function UsageTable({ rows }: { readonly rows: readonly PromptUsageLog[] }) {
  if (rows.length === 0) {
    return (
      <Card>
        <h3 className="mb-3 text-headline-sm text-secondary">Nhật ký sử dụng gần nhất</h3>
        <div className="rounded-lg border border-dashed border-outline bg-surface p-6 text-center text-body-md text-on-surface-variant">
          Chưa có token ledger cho cấu hình này trong 7 ngày qua.
        </div>
      </Card>
    );
  }

  return (
    <Card className="overflow-hidden">
      <h3 className="mb-4 text-headline-sm text-secondary">Nhật ký 5 lần sử dụng gần nhất</h3>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[560px] text-left">
          <thead className="border-b border-outline text-label-caps uppercase text-on-surface-variant">
            <tr>
              <th className="py-3 pr-4">ID tác vụ</th>
              <th className="px-4 py-3">Thời gian</th>
              <th className="px-4 py-3">Model</th>
              <th className="py-3 pl-4">Tokens</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-outline">
            {rows.map((row) => (
              <tr className="hover:bg-surface" key={row.id}>
                <td className="py-4 pr-4 font-mono text-mono-status text-secondary">#{row.id.slice(0, 8)}</td>
                <td className="px-4 py-4 text-body-md text-secondary">{formatDateTime(row.createdAt)}</td>
                <td className="px-4 py-4 font-mono text-mono-status text-on-surface-variant">{row.model}</td>
                <td className="py-4 pl-4 font-mono text-mono-status text-primary">{formatNumber(row.totalTokens)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  );
}

function SandboxModal({
  config,
  prompt,
  input,
  result,
  pending,
  error,
  onClose,
  onInputChange,
  onSubmit,
}: {
  readonly config: PromptConfig;
  readonly prompt: string;
  readonly input: string;
  readonly result: PromptSandboxResponse | null;
  readonly pending: boolean;
  readonly error: unknown;
  readonly onClose: () => void;
  readonly onInputChange: (value: string) => void;
  readonly onSubmit: () => void;
}) {
  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-black/50 p-gutter backdrop-blur-sm" onClick={onClose} role="presentation">
      <div className="flex max-h-[90vh] w-full max-w-5xl flex-col overflow-hidden rounded-xl bg-surface-container-lowest shadow-2xl" onClick={(event) => event.stopPropagation()} role="dialog" aria-modal="true" aria-label="LLM Test Sandbox">
        <div className="flex items-center justify-between border-b border-outline bg-surface px-gutter py-4">
          <div>
            <h3 className="text-headline-md text-secondary">LLM Test Sandbox</h3>
            <p className="mt-1 text-body-md text-on-surface-variant">{config.displayName} · {config.model}</p>
          </div>
          <button className="rounded-full p-2 text-on-surface-variant hover:bg-error/10 hover:text-error" onClick={onClose} type="button" aria-label="Đóng sandbox">
            <span className="material-symbols-outlined">close</span>
          </button>
        </div>

        <div className="grid flex-1 grid-cols-1 overflow-hidden md:grid-cols-2">
          <div className="space-y-4 overflow-y-auto border-r border-outline p-gutter">
            <label className="block">
              <span className="mb-1 block text-label-caps uppercase text-on-surface-variant">System Prompt</span>
              <textarea className={textAreaClass} readOnly value={prompt || "Chưa có system prompt tùy chỉnh."} />
            </label>
            <label className="block">
              <span className="mb-1 block text-label-caps uppercase text-on-surface-variant">User Input</span>
              <textarea className={`${textAreaClass} min-h-48`} onChange={(event) => onInputChange(event.target.value)} value={input} />
            </label>
            <Button className="w-full py-3 text-label-caps uppercase" disabled={pending || !input.trim()} onClick={onSubmit} type="button">
              <span className="material-symbols-outlined">bolt</span>
              Gửi yêu cầu test
            </Button>
          </div>

          <div className="flex flex-col overflow-hidden bg-surface p-gutter">
            <span className="mb-2 block text-label-caps uppercase text-on-surface-variant">Kết quả Output</span>
            <div className="min-h-64 flex-1 overflow-y-auto rounded-lg border border-outline bg-white p-4 text-body-md leading-relaxed text-secondary">
              {pending ? <p className="border-r-2 border-primary pr-1">Đang kiểm thử prompt...</p> : null}
              {error ? <Alert tone="error">{errorMessage(error)}</Alert> : null}
              {!pending && !error && result ? <p>{result.reply}</p> : null}
              {!pending && !error && !result ? <p className="text-on-surface-variant">Kết quả sandbox sẽ xuất hiện tại đây.</p> : null}
            </div>
            <div className="mt-4 flex flex-wrap gap-3">
              <span className="rounded-full bg-secondary-container px-3 py-1.5 font-mono text-mono-status text-secondary">
                {result ? formatDateTime(result.sentAt) : "Chưa chạy"}
              </span>
              <span className="rounded-full border border-tertiary/20 bg-tertiary/10 px-3 py-1.5 font-mono text-mono-status text-tertiary">
                {result ? `${formatNumber(result.estimatedTokens)} tokens` : "0 tokens"}
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

export default function PromptConfigurationPage() {
  const queryClient = useQueryClient();
  const [selectedCode, setSelectedCode] = useState<string | null>(null);
  const [draft, setDraft] = useState<PromptDraft>({});
  const [notice, setNotice] = useState<string | null>(null);
  const [sandboxOpen, setSandboxOpen] = useState(false);
  const [sandboxInput, setSandboxInput] = useState(DEFAULT_SANDBOX_INPUT);
  const [sandboxResult, setSandboxResult] = useState<PromptSandboxResponse | null>(null);

  const listQuery = useQuery({
    queryKey: ["prompts", "configs"],
    queryFn: listPromptConfigs,
  });

  const configs = listQuery.data?.items ?? EMPTY_CONFIGS;
  const effectiveSelectedCode = selectedCode && configs.some((config) => config.code === selectedCode) ? selectedCode : configs[0]?.code ?? null;

  const detailQuery = useQuery({
    queryKey: ["prompts", "configs", effectiveSelectedCode],
    queryFn: () => getPromptConfig(effectiveSelectedCode ?? ""),
    enabled: Boolean(effectiveSelectedCode),
  });

  const selectedConfig = detailQuery.data ?? configs.find((config) => config.code === effectiveSelectedCode) ?? null;
  const effectiveForm = selectedConfig
    ? {
        displayName: draft.displayName ?? selectedConfig.displayName,
        provider: draft.provider ?? selectedConfig.provider,
        model: draft.model ?? selectedConfig.model,
        temperature: draft.temperature ?? selectedConfig.temperature,
        maxTokens: draft.maxTokens ?? selectedConfig.maxTokens,
        systemPrompt: draft.systemPrompt ?? selectedConfig.systemPrompt,
      }
    : null;

  const saveMutation = useMutation({
    mutationFn: () => {
      if (!selectedConfig) throw new Error("Chưa chọn cấu hình prompt.");
      return updatePromptConfig(selectedConfig.code, toPayload(selectedConfig, draft));
    },
    onSuccess: (next) => {
      queryClient.setQueryData(["prompts", "configs", next.code], next);
      queryClient.setQueryData(["prompts", "configs"], (current: { readonly stats: unknown; readonly items: readonly PromptConfig[] } | undefined) =>
        current
          ? {
              ...current,
              items: current.items.map((item) => (item.code === next.code ? { ...item, ...next, recentUsage: item.recentUsage } : item)),
            }
          : current
      );
      setDraft({});
      setNotice("Đã lưu cấu hình prompt vào AgentConfig.");
    },
  });

  const sandboxMutation = useMutation({
    mutationFn: () => {
      if (!selectedConfig || !effectiveForm) throw new Error("Chưa chọn cấu hình prompt.");
      return runPromptSandbox(selectedConfig.code, {
        message: sandboxInput,
        systemPrompt: effectiveForm.systemPrompt,
      });
    },
    onSuccess: (result) => {
      setSandboxResult(result);
      void queryClient.invalidateQueries({ queryKey: ["prompts", "configs", selectedConfig?.code] });
    },
  });

  const currentError = listQuery.error ?? detailQuery.error ?? saveMutation.error;
  const stats = listQuery.data?.stats;
  const recentUsage = selectedConfig?.recentUsage ?? EMPTY_USAGE;
  const dirty = Object.keys(draft).length > 0;

  const providerMix = useMemo(() => {
    const counts = new Map<string, number>();
    configs.forEach((config) => counts.set(config.provider || "unknown", (counts.get(config.provider || "unknown") ?? 0) + 1));
    return Array.from(counts.entries()).sort((a, b) => b[1] - a[1]);
  }, [configs]);

  function selectConfig(code: string) {
    setSelectedCode(code);
    setDraft({});
    setNotice(null);
    setSandboxResult(null);
  }

  function openSandbox(config: PromptConfig) {
    selectConfig(config.code);
    setSandboxInput(DEFAULT_SANDBOX_INPUT);
    setSandboxResult(null);
    setSandboxOpen(true);
  }

  return (
    <AppShell title="Cấu hình Prompt gốc">
      <section className="mb-gutter flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div>
          <h1 className="text-display-lg text-secondary">Cấu hình Prompt gốc</h1>
          <p className="mt-2 max-w-3xl text-body-md text-on-surface-variant">
            Quản lý provider, model và system prompt đang lưu trong AgentConfig cho các AI agent của tenant hiện tại.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <StatusPill tone={currentError ? "error" : "success"}>{currentError ? "Mất kết nối" : "Đã kết nối"}</StatusPill>
          <Button type="button" variant="outline" onClick={() => void listQuery.refetch()}>
            <span className="material-symbols-outlined text-[18px]">refresh</span>
            Đồng bộ cấu hình
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

      <section className="mb-gutter grid grid-cols-1 gap-gutter md:grid-cols-4">
        <MetricCard icon="settings_suggest" label="Cấu hình LLM" value={stats ? formatNumber(stats.totalConfigs) : "Đang tải"} delta={`${stats?.runningConfigs ?? 0} runtime đang chạy`} tone="neutral" />
        <MetricCard icon="terminal" label="Prompt đã cấu hình" value={stats ? formatNumber(stats.promptConfigured) : "Đang tải"} delta="System prompt có nội dung" tone="success" />
        <MetricCard icon="toll" label="Token 7 ngày" value={stats ? formatNumber(stats.tokensLast7Days) : "Đang tải"} delta={stats ? formatUsd(stats.usdLast7Days) : "Ledger đang đồng bộ"} tone="neutral" />
        <Card>
          <p className="text-label-caps uppercase text-on-surface-variant">Provider mix</p>
          <div className="mt-3 space-y-2">
            {(providerMix.length ? providerMix : [["none", 0] as const]).slice(0, 3).map(([provider, count]) => (
              <div className="flex items-center justify-between gap-3" key={provider}>
                <span className="truncate text-body-md text-secondary">{provider}</span>
                <span className="font-mono text-mono-status text-on-surface-variant">{count}</span>
              </div>
            ))}
          </div>
        </Card>
      </section>

      <section className="mb-gutter grid grid-cols-1 gap-gutter lg:grid-cols-2 xl:grid-cols-3">
        {configs.map((config) => (
          <PromptConfigCard
            config={config}
            key={config.code}
            onSandbox={() => openSandbox(config)}
            onSelect={() => selectConfig(config.code)}
            selected={config.code === effectiveSelectedCode}
          />
        ))}
        {configs.length === 0 ? (
          <Card className="lg:col-span-2 xl:col-span-3">
            <div className="rounded-lg border border-dashed border-outline bg-surface p-6 text-center text-body-md text-on-surface-variant">
              Chưa có AgentConfig nào trong tenant hiện tại.
            </div>
          </Card>
        ) : null}
      </section>

      {selectedConfig && effectiveForm ? (
        <section className="grid grid-cols-1 gap-gutter xl:grid-cols-[minmax(0,1.05fr)_minmax(420px,0.95fr)]">
          <Card>
            <div className="mb-5 flex flex-wrap items-start justify-between gap-3">
              <div>
                <p className="text-label-caps uppercase text-on-surface-variant">Chi tiết cấu hình</p>
                <h2 className="mt-1 text-headline-md text-secondary">{selectedConfig.displayName}</h2>
                <p className="mt-1 text-body-md text-on-surface-variant">
                  {agentTypeLabel(selectedConfig.agentType)} · cập nhật {formatDateTime(selectedConfig.updatedAt)}
                </p>
              </div>
              <StatusPill tone={statusTone(selectedConfig)}>{statusLabel(selectedConfig)}</StatusPill>
            </div>

            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              <label>
                <span className="mb-1 block text-label-caps uppercase text-on-surface-variant">Tên cấu hình</span>
                <input className={inputClass} value={effectiveForm.displayName} onChange={(event) => setDraft((current) => ({ ...current, displayName: event.target.value }))} />
              </label>
              <label>
                <span className="mb-1 block text-label-caps uppercase text-on-surface-variant">Provider</span>
                <input className={inputClass} value={effectiveForm.provider} onChange={(event) => setDraft((current) => ({ ...current, provider: event.target.value }))} />
              </label>
              <label>
                <span className="mb-1 block text-label-caps uppercase text-on-surface-variant">Model</span>
                <input className={inputClass} value={effectiveForm.model} onChange={(event) => setDraft((current) => ({ ...current, model: event.target.value }))} />
              </label>
              <div className="grid grid-cols-2 gap-3">
                <label>
                  <span className="mb-1 block text-label-caps uppercase text-on-surface-variant">Temperature</span>
                  <input
                    className={inputClass}
                    max={2}
                    min={0}
                    onChange={(event) => setDraft((current) => ({ ...current, temperature: Number(event.target.value) }))}
                    step={0.1}
                    type="number"
                    value={effectiveForm.temperature}
                  />
                </label>
                <label>
                  <span className="mb-1 block text-label-caps uppercase text-on-surface-variant">Max tokens</span>
                  <input
                    className={inputClass}
                    max={32000}
                    min={128}
                    onChange={(event) => setDraft((current) => ({ ...current, maxTokens: Number(event.target.value) }))}
                    step={128}
                    type="number"
                    value={effectiveForm.maxTokens}
                  />
                </label>
              </div>
            </div>

            <label className="mt-4 block">
              <span className="mb-1 block text-label-caps uppercase text-on-surface-variant">System Prompt</span>
              <textarea className={`${textAreaClass} min-h-64 font-mono text-mono-status`} value={effectiveForm.systemPrompt} onChange={(event) => setDraft((current) => ({ ...current, systemPrompt: event.target.value }))} />
            </label>

            <div className="mt-5 grid grid-cols-1 gap-3 md:grid-cols-2">
              <Button disabled={!dirty || saveMutation.isPending} onClick={() => saveMutation.mutate()} type="button">
                <span className="material-symbols-outlined text-[18px]">save</span>
                Lưu cấu hình
              </Button>
              <Button variant="outline" onClick={() => setSandboxOpen(true)} type="button">
                <span className="material-symbols-outlined text-[18px]">bolt</span>
                Thử Sandbox
              </Button>
            </div>
          </Card>

          <div className="space-y-gutter">
            <UsageBars config={selectedConfig} />
            <UsageTable rows={recentUsage} />
          </div>
        </section>
      ) : null}

      {sandboxOpen && selectedConfig && effectiveForm ? (
        <SandboxModal
          config={selectedConfig}
          error={sandboxMutation.error}
          input={sandboxInput}
          onClose={() => setSandboxOpen(false)}
          onInputChange={setSandboxInput}
          onSubmit={() => sandboxMutation.mutate()}
          pending={sandboxMutation.isPending}
          prompt={effectiveForm.systemPrompt}
          result={sandboxResult}
        />
      ) : null}
    </AppShell>
  );
}
