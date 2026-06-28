import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { AgentListItem, UpdateAgentSettingsPayload } from "@/shared/api/agents";
import {
  createLlmConfig,
  updateLlmConfig,
  deleteLlmConfig,
  setLlmConfigActive,
  rotateLlmKey,
  type CreateLlmConfigPayload,
  type LlmConfig,
  type LlmProvider,
} from "@/shared/api/llmConfigs";

export type AgentConfigTab = "prompt" | "model" | "tools";

export interface AgentSettingsForm {
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

export interface SandboxMessage {
  readonly id: string;
  readonly side: "bot" | "user";
  readonly text: string;
  readonly time: string;
}

interface LlmConfigDraft {
  readonly provider: LlmProvider;
  readonly modelId: string;
  readonly displayName: string;
  readonly apiKey: string;
  readonly baseUrl: string;
  readonly inputUsdPer1M: string;
  readonly outputUsdPer1M: string;
}

const EMPTY_LLM_CONFIG_DRAFT: LlmConfigDraft = {
  provider: "openai",
  modelId: "",
  displayName: "",
  apiKey: "",
  baseUrl: "",
  inputUsdPer1M: "",
  outputUsdPer1M: "",
};

function listToText(values: readonly string[]): string {
  return values.join("\n");
}

function textToList(value: string): readonly string[] {
  return value
    .split(/\r?\n|,/)
    .map((item) => item.trim())
    .filter(Boolean);
}

function toNullableNumber(value: string): number | null {
  const trimmed = value.trim();
  if (!trimmed) return null;
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}

function toCreateLlmPayload(draft: LlmConfigDraft): CreateLlmConfigPayload {
  return {
    provider: draft.provider,
    modelId: draft.modelId.trim(),
    apiKey: draft.apiKey,
    displayName: draft.displayName.trim() || null,
    baseUrl: draft.baseUrl.trim() || null,
    inputUsdPer1M: toNullableNumber(draft.inputUsdPer1M),
    outputUsdPer1M: toNullableNumber(draft.outputUsdPer1M),
  };
}

const CREATIVITY_PRESETS = [
  {
    label: "Bám sát",
    description: "Ổn định, ưu tiên đúng hướng dẫn.",
    value: 0.4,
  },
  {
    label: "Cân bằng",
    description: "Linh hoạt vừa đủ cho hội thoại thường ngày.",
    value: 1,
  },
  {
    label: "Sáng tạo",
    description: "Nhiều biến tấu hơn cho nội dung và ý tưởng.",
    value: 1.5,
  },
] as const;

function ConfigTabButton({
  active,
  label,
  onClick,
}: {
  readonly active: boolean;
  readonly label: string;
  readonly onClick: () => void;
}) {
  return (
    <button
      className={[
        "px-4 py-3 text-label-caps uppercase transition-colors",
        active ? "border-b-2 border-primary text-primary" : "border-b-2 border-transparent text-on-surface-variant hover:text-on-surface",
      ].join(" ")}
      onClick={onClick}
      type="button"
    >
      {label}
    </button>
  );
}

function providerLabel(provider: string): string {
  const normalized = provider.toLowerCase();
  if (normalized === "anthropic" || normalized === "claude") return "Claude";
  if (normalized === "openai" || normalized === "openai-compatible") return "Chuẩn OpenAI";
  return provider;
}

function configLabel(config: LlmConfig): string {
  return `${config.displayName || config.modelId} · ${providerLabel(config.provider)} · ${config.modelId}${config.isActive ? "" : " (tắt)"}`;
}

export function AgentConfigDrawer({
  agent,
  form,
  tab,
  settingsLoading,
  saving,
  sandboxInput,
  sandboxMessages,
  sandboxPending,
  llmConfigs,
  onClose,
  onDraftChange,
  onSave,
  onSandboxInputChange,
  onSendSandbox,
  onTabChange,
}: {
  readonly agent: AgentListItem;
  readonly form: AgentSettingsForm;
  readonly tab: AgentConfigTab;
  readonly settingsLoading: boolean;
  readonly saving: boolean;
  readonly sandboxInput: string;
  readonly sandboxMessages: readonly SandboxMessage[];
  readonly sandboxPending: boolean;
  readonly llmConfigs: readonly LlmConfig[];
  readonly onClose: () => void;
  readonly onDraftChange: (patch: Partial<UpdateAgentSettingsPayload>) => void;
  readonly onSave: () => void;
  readonly onSandboxInputChange: (value: string) => void;
  readonly onSendSandbox: (message?: string) => void;
  readonly onTabChange: (tab: AgentConfigTab) => void;
}) {
  const queryClient = useQueryClient();
  const [modelPickerOpen, setModelPickerOpen] = useState(false);
  const [sandboxMinimized, setSandboxMinimized] = useState(false);
  const [llmDraft, setLlmDraft] = useState<LlmConfigDraft>(EMPTY_LLM_CONFIG_DRAFT);
  const [llmError, setLlmError] = useState<string | null>(null);
  const boundConfig = llmConfigs.find((c) => c.id === form.llmConfigId);
  const isUnbound = !boundConfig || !boundConfig.isActive;
  const createLlmMutation = useMutation({
    mutationFn: () => createLlmConfig(toCreateLlmPayload(llmDraft)),
    onSuccess: async (config) => {
      onDraftChange({ llmConfigId: config.id, model: config.modelId, provider: config.provider });
      setLlmDraft(EMPTY_LLM_CONFIG_DRAFT);
      setLlmError(null);
      setModelPickerOpen(false);
      await queryClient.invalidateQueries({ queryKey: ["llm-configs"] });
    },
    onError: (error) => setLlmError(error instanceof Error ? error.message : "Không tạo được cấu hình LLM."),
  });

  const [editingId, setEditingId] = useState<string | null>(null);
  const [editDraft, setEditDraft] = useState<LlmConfigDraft>(EMPTY_LLM_CONFIG_DRAFT);
  const onLlmError = (error: unknown) =>
    setLlmError(error instanceof Error ? error.message : "Thao tác cấu hình LLM thất bại.");
  const refetchConfigs = () => queryClient.invalidateQueries({ queryKey: ["llm-configs"] });

  const updateLlmMutation = useMutation({
    mutationFn: async () => {
      await updateLlmConfig(editingId!, {
        provider: editDraft.provider,
        modelId: editDraft.modelId.trim(),
        displayName: editDraft.displayName.trim() || null,
        baseUrl: editDraft.baseUrl.trim() || null,
        inputUsdPer1M: toNullableNumber(editDraft.inputUsdPer1M),
        outputUsdPer1M: toNullableNumber(editDraft.outputUsdPer1M),
      });
      if (editDraft.apiKey.trim()) await rotateLlmKey(editingId!, editDraft.apiKey);
    },
    onSuccess: async () => {
      setEditingId(null);
      setLlmError(null);
      await refetchConfigs();
    },
    onError: onLlmError,
  });

  const toggleLlmMutation = useMutation({
    mutationFn: ({ id, active }: { id: string; active: boolean }) => setLlmConfigActive(id, active),
    onSuccess: async () => {
      setLlmError(null);
      await refetchConfigs();
    },
    onError: onLlmError,
  });

  const deleteLlmMutation = useMutation({
    mutationFn: (id: string) => deleteLlmConfig(id),
    onSuccess: async (_data, id) => {
      if (form.llmConfigId === id) onDraftChange({ llmConfigId: "" });
      setLlmError(null);
      await refetchConfigs();
    },
    onError: onLlmError,
  });

  const startEdit = (config: LlmConfig) => {
    setEditingId(config.id);
    setLlmError(null);
    setEditDraft({
      provider: config.provider,
      modelId: config.modelId,
      displayName: config.displayName ?? "",
      apiKey: "",
      baseUrl: config.baseUrl ?? "",
      inputUsdPer1M: config.inputUsdPer1M?.toString() ?? "",
      outputUsdPer1M: config.outputUsdPer1M?.toString() ?? "",
    });
  };

  return (
    <>
      <button aria-label="Đóng cấu hình agent" className="fixed inset-0 z-[60] bg-black/20 backdrop-blur-[8px]" onClick={onClose} type="button" />
      <aside className="fixed inset-y-0 right-0 z-[70] flex w-full max-w-[760px] flex-col border-l border-outline-variant bg-surface-container-lowest shadow-2xl xl:w-1/2 xl:max-w-none">
        <header className="flex h-[64px] shrink-0 items-center justify-between border-b border-outline-variant bg-surface-container-low px-6">
          <div className="flex items-center gap-3">
            <span aria-hidden="true" className="material-symbols-outlined text-primary">settings</span>
            <h2 className="text-headline-sm font-bold text-on-surface">Cấu hình Agent: {agent.displayName || agent.code}</h2>
          </div>
          <button aria-label="Đóng cấu hình agent" className="rounded-full p-2 transition-colors hover:bg-surface-variant" onClick={onClose} type="button">
            <span aria-hidden="true" className="material-symbols-outlined">close</span>
          </button>
        </header>

        <div className="flex shrink-0 overflow-x-auto border-b border-outline-variant bg-surface-container-low px-6">
          <ConfigTabButton active={tab === "prompt"} label="Hướng dẫn trả lời" onClick={() => onTabChange("prompt")} />
          <ConfigTabButton active={tab === "model"} label="Cấu hình LLM" onClick={() => onTabChange("model")} />
          <ConfigTabButton active={tab === "tools"} label="Công cụ & kết nối" onClick={() => onTabChange("tools")} />
        </div>

        <div className="relative flex-1 overflow-y-auto p-6 pb-[430px] lg:pb-6">
          {settingsLoading ? (
            <div className="rounded-lg border border-outline bg-surface p-4 text-body-md text-on-surface-variant">Đang tải cấu hình agent...</div>
          ) : null}

          {tab === "prompt" ? (
            <div className="flex flex-col gap-3">
              <label className="text-label-caps uppercase text-tertiary" htmlFor="agent-system-prompt">
                Hướng dẫn gốc
              </label>
              <textarea
                className="min-h-[300px] resize-y rounded-lg border border-outline-variant bg-[#1e1e1e] p-4 font-mono text-mono-status leading-6 text-green-400 outline-none focus:border-primary"
                id="agent-system-prompt"
                onChange={(event) => onDraftChange({ systemPrompt: event.target.value })}
                spellCheck={false}
                value={form.systemPrompt}
              />
              <div className="flex justify-end">
                <button
                  className="rounded border border-primary px-4 py-2 text-body-md text-primary transition-colors hover:bg-primary/5 disabled:cursor-not-allowed disabled:opacity-50"
                  disabled={sandboxPending}
                  onClick={() => onSendSandbox("Kiểm tra kết nối")}
                  type="button"
                >
                  Thử hướng dẫn
                </button>
              </div>
            </div>
          ) : null}

          {tab === "model" ? (
            <div className="flex flex-col gap-4">
              {isUnbound ? (
                <div className="rounded-lg border border-amber-300 bg-amber-50 px-4 py-3 text-body-md text-amber-800">
                  Agent chưa gắn cấu hình nhà cung cấp đang hoạt động — agent sẽ báo lỗi khi chạy cho đến khi được gắn cấu hình.
                </div>
              ) : null}
              <div className="space-y-2">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="text-label-caps uppercase text-tertiary">Cấu hình nhà cung cấp (LLM)</span>
                  <button className="rounded border border-outline bg-white px-3 py-1.5 text-body-md font-bold text-secondary transition-colors hover:border-primary hover:text-primary" onClick={() => setModelPickerOpen(true)} type="button">
                    Chọn model/provider
                  </button>
                </div>
                <select
                  className="w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
                  onChange={(event) => {
                    const selected = llmConfigs.find((config) => config.id === event.target.value);
                    onDraftChange({
                      llmConfigId: event.target.value,
                      model: selected?.modelId ?? form.model,
                      provider: selected?.provider ?? form.provider,
                    });
                  }}
                  value={form.llmConfigId}
                >
                  <option value="">— Chưa gắn —</option>
                  {llmConfigs.map((config) => (
                    <option key={config.id} value={config.id}>
                      {configLabel(config)}
                    </option>
                  ))}
                </select>
              </div>
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                <label className="space-y-2 md:col-span-2">
                  <span className="text-label-caps uppercase text-tertiary">Tên hiển thị</span>
                  <input className="w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary" onChange={(event) => onDraftChange({ displayName: event.target.value })} value={form.displayName} />
                </label>
                <fieldset className="space-y-2 md:col-span-2">
                  <legend className="text-label-caps uppercase text-tertiary">Phong cách trả lời</legend>
                  <div className="grid gap-2 md:grid-cols-3">
                    {CREATIVITY_PRESETS.map((preset) => {
                      const selected = Math.abs(form.temperature - preset.value) < 0.01;
                      return (
                        <button
                          className={[
                            "rounded-lg border p-3 text-left transition-colors focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2",
                            selected ? "border-primary bg-primary/5 text-primary" : "border-outline bg-white text-on-surface hover:border-primary/60",
                          ].join(" ")}
                          key={preset.label}
                          onClick={() => onDraftChange({ temperature: preset.value })}
                          type="button"
                        >
                          <span className="block font-bold">{preset.label}</span>
                          <span className="mt-1 block text-body-md text-on-surface-variant">{preset.description}</span>
                        </button>
                      );
                    })}
                  </div>
                </fieldset>
              </div>
            </div>
          ) : null}

          {tab === "tools" ? (
            <div className="grid grid-cols-1 gap-4">
              <label className="space-y-2">
                <span className="text-label-caps uppercase text-tertiary">Tệp kỹ năng</span>
                <textarea className="min-h-32 w-full resize-y rounded border border-outline bg-white px-3 py-2 font-mono text-mono-status outline-none focus:border-primary" onChange={(event) => onDraftChange({ skillFiles: textToList(event.target.value) })} placeholder="ky-nang-tu-van.md" value={listToText(form.skillFiles)} />
              </label>
              <label className="space-y-2">
                <span className="text-label-caps uppercase text-tertiary">Kho tri thức liên kết</span>
                <textarea className="min-h-32 w-full resize-y rounded border border-outline bg-white px-3 py-2 font-mono text-mono-status outline-none focus:border-primary" onChange={(event) => onDraftChange({ kbModules: textToList(event.target.value) })} placeholder="tu-van-hsk&#10;lo-trinh-hsk" value={listToText(form.kbModules)} />
              </label>
            </div>
          ) : null}

          <div className="mt-6 rounded-lg border border-outline bg-surface p-4 text-body-md text-on-surface-variant lg:max-w-[calc(100%-360px)]">
            Các thiết lập này áp dụng cho agent đang chọn. Khu vực thử phản hồi giúp kiểm tra cách agent trả lời trước khi đưa vào vận hành.
          </div>

          <section className={["absolute bottom-6 right-6 flex w-[calc(100%-3rem)] flex-col overflow-hidden rounded-xl border border-outline-variant bg-surface-container-lowest shadow-2xl lg:w-80", sandboxMinimized ? "h-12" : "h-96"].join(" ")}>
            <button className="flex items-center justify-between bg-primary-container p-3 text-left" onClick={() => setSandboxMinimized((value) => !value)} type="button">
              <span className="text-label-caps uppercase text-on-primary">Thử phản hồi</span>
              <span aria-hidden="true" className="material-symbols-outlined text-sm text-on-primary">{sandboxMinimized ? "expand_less" : "expand_more"}</span>
            </button>
            {!sandboxMinimized ? (
              <>
                <div className="flex-1 space-y-3 overflow-y-auto bg-surface-container-low p-4">
                  {sandboxMessages.map((message) => (
                    <div className={["max-w-[82%] rounded-lg p-2 text-body-md", message.side === "user" ? "ml-auto rounded-tr-none bg-primary-container text-on-primary" : "mr-auto rounded-tl-none bg-white text-on-surface shadow-sm"].join(" ")} key={message.id}>
                      {message.text}
                    </div>
                  ))}
                  {sandboxPending ? <div className="text-body-md text-on-surface-variant">Đang kiểm tra hướng dẫn...</div> : null}
                </div>
                <footer className="flex gap-2 border-t border-outline-variant p-3">
                  <input className="min-w-0 flex-1 bg-transparent text-body-md outline-none placeholder:text-on-surface-variant/60" onChange={(event) => onSandboxInputChange(event.target.value)} onKeyDown={(event) => { if (event.key === "Enter") { event.preventDefault(); onSendSandbox(); } }} placeholder="Nhập tin nhắn thử nghiệm..." value={sandboxInput} />
                  <button aria-label="Gửi tin nhắn thử nghiệm" className="text-primary disabled:opacity-50" disabled={sandboxPending} onClick={() => onSendSandbox()} type="button">
                    <span aria-hidden="true" className="material-symbols-outlined">send</span>
                  </button>
                </footer>
              </>
            ) : null}
          </section>
        </div>

        <footer className="flex shrink-0 justify-end gap-3 border-t border-outline-variant bg-surface-container-low p-6">
          <button className="rounded border border-outline px-6 py-2 text-body-md text-on-surface" onClick={onClose} type="button">
            Hủy
          </button>
          <button className="rounded bg-primary-container px-6 py-2 text-body-md text-on-primary shadow-md transition-colors hover:bg-primary-hover disabled:cursor-not-allowed disabled:opacity-60" disabled={saving} onClick={onSave} type="button">
            Lưu cấu hình
          </button>
        </footer>
      </aside>
      {modelPickerOpen ? (
        <div className="fixed inset-0 z-[80] flex items-center justify-center bg-black/30 p-4">
          <div className="max-h-[80vh] w-full max-w-3xl overflow-y-auto rounded-xl border border-outline bg-surface-container-lowest shadow-2xl">
            <header className="flex items-center justify-between border-b border-outline-variant px-5 py-4">
              <div>
                <h3 className="text-headline-sm font-bold text-on-surface">Chọn model/provider cho {agent.displayName || agent.code}</h3>
                <p className="mt-1 text-body-md text-on-surface-variant">Chọn cấu hình LLM đã khai báo rồi bấm “Lưu cấu hình”.</p>
              </div>
              <button aria-label="Đóng chọn model/provider" className="rounded-full p-2 transition-colors hover:bg-surface-variant" onClick={() => setModelPickerOpen(false)} type="button">
                <span aria-hidden="true" className="material-symbols-outlined">close</span>
              </button>
            </header>
            <div className="grid gap-3 p-5">
              <div className="grid gap-3 rounded-lg border border-outline bg-surface p-4">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <h4 className="text-title-sm font-bold text-secondary">Khai báo LLM config mới</h4>
                  <button
                    className="rounded bg-primary-container px-3 py-1.5 text-body-md font-bold text-on-primary disabled:cursor-not-allowed disabled:opacity-60"
                    disabled={createLlmMutation.isPending || !llmDraft.modelId.trim() || !llmDraft.apiKey.trim()}
                    onClick={() => {
                      setLlmError(null);
                      createLlmMutation.mutate();
                    }}
                    type="button"
                  >
                    Tạo & chọn
                  </button>
                </div>
                {llmError ? <div className="rounded border border-red-300 bg-red-50 px-3 py-2 text-mono-status text-red-700">{llmError}</div> : null}
                <div className="grid gap-3 md:grid-cols-2">
                  <label className="space-y-1">
                    <span className="text-label-caps uppercase text-tertiary">Tên hiển thị</span>
                    <input className="w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary" onChange={(event) => setLlmDraft((current) => ({ ...current, displayName: event.target.value }))} placeholder="gpt local" value={llmDraft.displayName} />
                  </label>
                  <label className="space-y-1">
                    <span className="text-label-caps uppercase text-tertiary">Nhà cung cấp</span>
                    <select className="w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary" onChange={(event) => setLlmDraft((current) => ({ ...current, provider: event.target.value as LlmProvider }))} value={llmDraft.provider}>
                      <option value="anthropic">Anthropic</option>
                      <option value="openai">Chuẩn OpenAI</option>
                    </select>
                  </label>
                  <label className="space-y-1">
                    <span className="text-label-caps uppercase text-tertiary">Model</span>
                    <input className="w-full rounded border border-outline bg-white px-3 py-2 font-mono text-mono-status outline-none focus:border-primary" onChange={(event) => setLlmDraft((current) => ({ ...current, modelId: event.target.value }))} placeholder="cx/gpt-5.5-review" value={llmDraft.modelId} />
                  </label>
                  <label className="space-y-1">
                    <span className="text-label-caps uppercase text-tertiary">API key</span>
                    <input autoComplete="off" className="w-full rounded border border-outline bg-white px-3 py-2 font-mono text-mono-status outline-none focus:border-primary" onChange={(event) => setLlmDraft((current) => ({ ...current, apiKey: event.target.value }))} placeholder="sk-..." type="password" value={llmDraft.apiKey} />
                  </label>
                  <label className="space-y-1 md:col-span-2">
                    <span className="text-label-caps uppercase text-tertiary">Base URL</span>
                    <input className="w-full rounded border border-outline bg-white px-3 py-2 font-mono text-mono-status outline-none focus:border-primary" onChange={(event) => setLlmDraft((current) => ({ ...current, baseUrl: event.target.value }))} placeholder="http://localhost:20128/v1" value={llmDraft.baseUrl} />
                  </label>
                </div>
              </div>
              {llmConfigs.map((config) => {
                const selected = config.id === form.llmConfigId;
                const isEditing = editingId === config.id;
                const busy =
                  (toggleLlmMutation.isPending && toggleLlmMutation.variables?.id === config.id) ||
                  (deleteLlmMutation.isPending && deleteLlmMutation.variables === config.id);
                return (
                  <div
                    className={[
                      "rounded-lg border p-4 transition-colors",
                      selected ? "border-primary bg-primary/5" : "border-outline bg-white",
                    ].join(" ")}
                    key={config.id}
                  >
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <span className="text-title-sm font-bold text-secondary">{config.displayName || config.modelId}</span>
                      <span className={config.isActive ? "text-mono-status text-green-700" : "text-mono-status text-amber-700"}>
                        {config.isActive ? "Đang bật" : "Đang tắt"}
                      </span>
                    </div>
                    <div className="mt-2 grid gap-2 text-body-md text-on-surface-variant md:grid-cols-3">
                      <span>{providerLabel(config.provider)}</span>
                      <span className="font-mono text-mono-status">{config.modelId}</span>
                      <span className="truncate font-mono text-mono-status">{config.baseUrl || "endpoint mặc định"}</span>
                    </div>

                    {isEditing ? (
                      <div className="mt-3 grid gap-3 rounded border border-outline bg-surface p-3 md:grid-cols-2">
                        <label className="space-y-1">
                          <span className="text-label-caps uppercase text-tertiary">Tên hiển thị</span>
                          <input className="w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary" onChange={(event) => setEditDraft((current) => ({ ...current, displayName: event.target.value }))} value={editDraft.displayName} />
                        </label>
                        <label className="space-y-1">
                          <span className="text-label-caps uppercase text-tertiary">Nhà cung cấp</span>
                          <select className="w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary" onChange={(event) => setEditDraft((current) => ({ ...current, provider: event.target.value as LlmProvider }))} value={editDraft.provider}>
                            <option value="anthropic">Anthropic</option>
                            <option value="openai">Chuẩn OpenAI</option>
                          </select>
                        </label>
                        <label className="space-y-1">
                          <span className="text-label-caps uppercase text-tertiary">Model</span>
                          <input className="w-full rounded border border-outline bg-white px-3 py-2 font-mono text-mono-status outline-none focus:border-primary" onChange={(event) => setEditDraft((current) => ({ ...current, modelId: event.target.value }))} value={editDraft.modelId} />
                        </label>
                        <label className="space-y-1">
                          <span className="text-label-caps uppercase text-tertiary">API key (để trống = giữ nguyên)</span>
                          <input autoComplete="off" className="w-full rounded border border-outline bg-white px-3 py-2 font-mono text-mono-status outline-none focus:border-primary" onChange={(event) => setEditDraft((current) => ({ ...current, apiKey: event.target.value }))} placeholder="••• đổi key" type="password" value={editDraft.apiKey} />
                        </label>
                        <label className="space-y-1 md:col-span-2">
                          <span className="text-label-caps uppercase text-tertiary">Base URL</span>
                          <input className="w-full rounded border border-outline bg-white px-3 py-2 font-mono text-mono-status outline-none focus:border-primary" onChange={(event) => setEditDraft((current) => ({ ...current, baseUrl: event.target.value }))} placeholder="http://localhost:20128/v1" value={editDraft.baseUrl} />
                        </label>
                        <div className="flex justify-end gap-2 md:col-span-2">
                          <button className="rounded border border-outline px-3 py-1.5 text-body-md font-bold text-on-surface-variant hover:bg-surface-variant" onClick={() => setEditingId(null)} type="button">
                            Hủy
                          </button>
                          <button className="rounded bg-primary px-3 py-1.5 text-body-md font-bold text-white disabled:opacity-60" disabled={updateLlmMutation.isPending || !editDraft.modelId.trim()} onClick={() => updateLlmMutation.mutate()} type="button">
                            {updateLlmMutation.isPending ? "Đang lưu" : "Lưu thay đổi"}
                          </button>
                        </div>
                      </div>
                    ) : (
                      <div className="mt-3 flex flex-wrap items-center gap-2">
                        <button
                          className="rounded border border-primary px-3 py-1.5 text-body-md font-bold text-primary hover:bg-primary/5 disabled:opacity-60"
                          disabled={selected}
                          onClick={() => {
                            onDraftChange({ llmConfigId: config.id, model: config.modelId, provider: config.provider });
                            setModelPickerOpen(false);
                          }}
                          type="button"
                        >
                          {selected ? "Đang chọn" : "Chọn"}
                        </button>
                        <button className="rounded border border-outline px-3 py-1.5 text-body-md font-bold text-secondary hover:border-primary hover:text-primary" onClick={() => startEdit(config)} type="button">
                          Sửa
                        </button>
                        <button className="rounded border border-outline px-3 py-1.5 text-body-md font-bold text-secondary hover:border-primary hover:text-primary disabled:opacity-60" disabled={busy} onClick={() => toggleLlmMutation.mutate({ id: config.id, active: !config.isActive })} type="button">
                          {config.isActive ? "Tắt" : "Bật"}
                        </button>
                        <button
                          className="ml-auto rounded border border-red-300 px-3 py-1.5 text-body-md font-bold text-red-700 hover:bg-red-50 disabled:opacity-60"
                          disabled={busy}
                          onClick={() => {
                            if (window.confirm(`Xoá cấu hình "${config.displayName || config.modelId}"?`)) deleteLlmMutation.mutate(config.id);
                          }}
                          type="button"
                        >
                          Xoá
                        </button>
                      </div>
                    )}
                  </div>
                );
              })}
              {llmConfigs.length === 0 ? (
                <div className="rounded-lg border border-dashed border-outline p-6 text-center text-body-md text-on-surface-variant">
                  Chưa có cấu hình LLM. Tạo nhà cung cấp ở phần quản trị hệ thống trước khi gắn agent.
                </div>
              ) : null}
            </div>
          </div>
        </div>
      ) : null}
    </>
  );
}
