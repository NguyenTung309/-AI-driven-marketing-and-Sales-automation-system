import type { AgentListItem, UpdateAgentSettingsPayload } from "@/shared/api/agents";

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
}

export interface SandboxMessage {
  readonly id: string;
  readonly side: "bot" | "user";
  readonly text: string;
  readonly time: string;
}

function listToText(values: readonly string[]): string {
  return values.join("\n");
}

function textToList(value: string): readonly string[] {
  return value
    .split(/\r?\n|,/)
    .map((item) => item.trim())
    .filter(Boolean);
}

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

export function AgentConfigDrawer({
  agent,
  form,
  tab,
  settingsLoading,
  saving,
  sandboxInput,
  sandboxMessages,
  sandboxPending,
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
  readonly onClose: () => void;
  readonly onDraftChange: (patch: Partial<UpdateAgentSettingsPayload>) => void;
  readonly onSave: () => void;
  readonly onSandboxInputChange: (value: string) => void;
  readonly onSendSandbox: (message?: string) => void;
  readonly onTabChange: (tab: AgentConfigTab) => void;
}) {
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
          <ConfigTabButton active={tab === "model"} label="Mô hình AI" onClick={() => onTabChange("model")} />
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
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              <label className="space-y-2">
                <span className="text-label-caps uppercase text-tertiary">Tên hiển thị</span>
                <input className="w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary" onChange={(event) => onDraftChange({ displayName: event.target.value })} value={form.displayName} />
              </label>
              <label className="space-y-2">
                <span className="text-label-caps uppercase text-tertiary">Nhà cung cấp AI</span>
                <select className="w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary" onChange={(event) => onDraftChange({ provider: event.target.value })} value={form.provider}>
                  <option value="claude">Claude</option>
                  <option value="openai-compatible">Chuẩn OpenAI</option>
                  <option value="local">Máy nội bộ</option>
                </select>
              </label>
              <label className="space-y-2 md:col-span-2">
                <span className="text-label-caps uppercase text-tertiary">Mô hình AI</span>
                <input className="w-full rounded border border-outline bg-white px-3 py-2 font-mono text-mono-status outline-none focus:border-primary" onChange={(event) => onDraftChange({ model: event.target.value })} value={form.model} />
              </label>
              <label className="space-y-2">
                <span className="text-label-caps uppercase text-tertiary">Độ sáng tạo</span>
                <div className="rounded border border-outline bg-white px-3 py-2">
                  <input className="w-full accent-primary" max={2} min={0} onChange={(event) => onDraftChange({ temperature: Number(event.target.value) })} step={0.1} type="range" value={form.temperature} />
                  <div className="mt-1 font-mono text-mono-status text-secondary">{form.temperature.toFixed(1)}</div>
                </div>
              </label>
              <label className="space-y-2">
                <span className="text-label-caps uppercase text-tertiary">Giới hạn độ dài</span>
                <input className="w-full rounded border border-outline bg-white px-3 py-2 font-mono text-mono-status outline-none focus:border-primary" min={128} onChange={(event) => onDraftChange({ maxTokens: Number(event.target.value) })} step={128} type="number" value={form.maxTokens} />
              </label>
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

          <section className="absolute bottom-6 right-6 flex h-96 w-[calc(100%-3rem)] flex-col overflow-hidden rounded-xl border border-outline-variant bg-surface-container-lowest shadow-2xl lg:w-80">
            <header className="flex items-center justify-between bg-primary-container p-3">
              <span className="text-label-caps uppercase text-on-primary">Thử phản hồi</span>
              <span aria-hidden="true" className="material-symbols-outlined text-sm text-on-primary">expand_more</span>
            </header>
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
    </>
  );
}
