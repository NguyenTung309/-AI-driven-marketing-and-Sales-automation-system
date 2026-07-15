import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Input, Modal } from "@/shared/ui";
import { listLlmConfigs } from "@/shared/api/llmConfigs";
import { ToolPicker } from "./ToolPicker";
import { upsertOrchestrationV2Agent, type OrchestrationV2Agent } from "@/shared/api/orchestrationV2";

const UNBIND_LLM_CONFIG = "00000000-0000-0000-0000-000000000000";

const AGENT_TYPES = [
  { value: "content", label: "Nội dung" },
  { value: "research", label: "Nghiên cứu" },
  { value: "lead", label: "Chấm điểm lead" },
  { value: "report", label: "Báo cáo" },
  { value: "docs", label: "Tài liệu" },
  { value: "chat", label: "Trò chuyện" },
  { value: "custom", label: "Tuỳ chỉnh" },
] as const;

const SELECT_CLASS =
  "bg-surface-container-lowest border border-surface-variant rounded px-3 py-2 text-body-md w-full focus:outline-none focus:ring-2 focus:ring-primary/30";

const DEFAULT_PERSONA =
  "# Vai trò: <mô tả vai trò của agent>\n# Nhiệm vụ: <agent này chịu trách nhiệm việc gì>\n# Nguyên tắc: trả lời ngắn gọn, dùng tool khi cần hành động thật.";

interface CreateSubAgentDialogProps {
  readonly open: boolean;
  readonly editing: OrchestrationV2Agent | null;
  readonly onClose: () => void;
  readonly onSaved: (agent: OrchestrationV2Agent) => void;
}

function parseTools(json: string | undefined): readonly string[] {
  if (!json) return [];
  try {
    const value: unknown = JSON.parse(json);
    return Array.isArray(value) ? value.filter((item): item is string => typeof item === "string") : [];
  } catch {
    return [];
  }
}

type FormState = {
  code: string;
  displayName: string;
  agentType: string;
  personaPrompt: string;
  llmConfigId: string;
  tools: readonly string[];
};

function formFromEditing(editing: OrchestrationV2Agent | null): FormState {
  if (editing) {
    return {
      code: editing.code,
      displayName: editing.displayName,
      agentType: editing.agentType || "custom",
      personaPrompt: editing.personaPrompt || DEFAULT_PERSONA,
      llmConfigId: editing.llmConfigId ?? "",
      tools: parseTools(editing.allowedToolsJson),
    };
  }
  return {
    code: "",
    displayName: "",
    agentType: "content",
    personaPrompt: DEFAULT_PERSONA,
    llmConfigId: "",
    tools: [],
  };
}

export function CreateSubAgentDialog({ open, editing, onClose, onSaved }: CreateSubAgentDialogProps) {
  const queryClient = useQueryClient();
  const llmConfigsQuery = useQuery({ queryKey: ["llm-configs"], queryFn: listLlmConfigs, enabled: open });

  const [formKey, setFormKey] = useState(`${open}:${editing?.code ?? "new"}`);
  const [form, setForm] = useState<FormState>(() => formFromEditing(editing));

  const nextKey = `${open}:${editing?.code ?? "new"}`;
  if (open && nextKey !== formKey) {
    setFormKey(nextKey);
    setForm(formFromEditing(editing));
  }

  const saveMutation = useMutation({
    mutationFn: () =>
      upsertOrchestrationV2Agent({
        code: form.code.trim(),
        displayName: form.displayName.trim(),
        agentType: form.agentType,
        personaPrompt: form.personaPrompt.trim(),
        isOrchestratable: true,
        allowedToolsJson: JSON.stringify(form.tools),
        llmConfigId: form.llmConfigId === "" ? UNBIND_LLM_CONFIG : form.llmConfigId,
      }),
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: ["orchestration-v2", "agents"] });
      onSaved(saved);
      onClose();
    },
  });

  function toggleTool(name: string) {
    setForm((current) => ({
      ...current,
      tools: current.tools.includes(name) ? current.tools.filter((t) => t !== name) : [...current.tools, name],
    }));
  }

  const canSubmit =
    form.code.trim().length >= 2 && form.displayName.trim().length >= 2 && form.personaPrompt.trim().length >= 10;

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? `Sửa sub agent: ${editing.code}` : "Thêm sub agent"}
      maxWidthClass="max-w-2xl"
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={saveMutation.isPending}>
            Hủy
          </Button>
          <Button onClick={() => saveMutation.mutate()} disabled={!canSubmit || saveMutation.isPending}>
            {saveMutation.isPending ? "Đang lưu..." : editing ? "Lưu thay đổi" : "Tạo sub agent"}
          </Button>
        </>
      }
    >
      {saveMutation.error ? (
        <Alert tone="error">{saveMutation.error instanceof Error ? saveMutation.error.message : "Không lưu được sub agent."}</Alert>
      ) : null}
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <div>
          <label className="mb-1 block text-body-md font-bold text-secondary" htmlFor="sub-agent-code">
            Mã (code)
          </label>
          <Input
            id="sub-agent-code"
            value={form.code}
            onChange={(event) =>
              setForm((current) => ({
                ...current,
                code: event.target.value.toLowerCase().replaceAll(/[^a-z0-9-_]/g, ""),
              }))
            }
            placeholder="vd: seo-writer"
            disabled={Boolean(editing)}
          />
        </div>
        <div>
          <label className="mb-1 block text-body-md font-bold text-secondary" htmlFor="sub-agent-name">
            Tên hiển thị
          </label>
          <Input
            id="sub-agent-name"
            value={form.displayName}
            onChange={(event) => setForm((current) => ({ ...current, displayName: event.target.value }))}
            placeholder="vd: SEO Writer"
          />
        </div>
        <div>
          <label className="mb-1 block text-body-md font-bold text-secondary" htmlFor="sub-agent-type">
            Nhóm vai trò
          </label>
          <select
            id="sub-agent-type"
            className={SELECT_CLASS}
            value={form.agentType}
            onChange={(event) => setForm((current) => ({ ...current, agentType: event.target.value }))}
          >
            {AGENT_TYPES.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label className="mb-1 block text-body-md font-bold text-secondary" htmlFor="sub-agent-llm">
            LLM sử dụng
          </label>
          <select
            id="sub-agent-llm"
            className={SELECT_CLASS}
            value={form.llmConfigId}
            onChange={(event) => setForm((current) => ({ ...current, llmConfigId: event.target.value }))}
          >
            <option value="">— Chưa gắn (orchestrator sẽ bỏ qua) —</option>
            {(llmConfigsQuery.data ?? []).map((config) => (
              <option key={config.id} value={config.id}>
                {config.displayName || `${config.provider}/${config.modelId}`}
              </option>
            ))}
          </select>
          <p className="mt-1 text-label-sm text-on-surface-variant">Bắt buộc gắn LLM để orchestrator nhận diện và giao việc.</p>
        </div>
      </div>

      <div>
        <label className="mb-1 block text-body-md font-bold text-secondary" htmlFor="sub-agent-persona">
          Persona / vai trò (system prompt)
        </label>
        <textarea
          id="sub-agent-persona"
          value={form.personaPrompt}
          onChange={(event) => setForm((current) => ({ ...current, personaPrompt: event.target.value }))}
          rows={6}
          className="w-full rounded-lg border border-outline bg-surface-container-lowest p-3 font-mono text-mono-status text-on-surface focus:border-primary focus:outline-none"
        />
      </div>

      <div>
        <p className="mb-2 text-body-md font-bold text-secondary">Công cụ được phép dùng</p>
        <ToolPicker onToggle={toggleTool} selected={form.tools} />
      </div>
    </Modal>
  );
}
