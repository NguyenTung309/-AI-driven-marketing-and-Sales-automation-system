import { useEffect, useState } from "react";
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

export function CreateSubAgentDialog({ open, editing, onClose, onSaved }: CreateSubAgentDialogProps) {
  const queryClient = useQueryClient();
  const llmConfigsQuery = useQuery({ queryKey: ["llm-configs"], queryFn: listLlmConfigs, enabled: open });

  const [code, setCode] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [agentType, setAgentType] = useState<string>("content");
  const [personaPrompt, setPersonaPrompt] = useState(DEFAULT_PERSONA);
  const [llmConfigId, setLlmConfigId] = useState("");
  const [tools, setTools] = useState<readonly string[]>([]);

  useEffect(() => {
    if (!open) return;
    if (editing) {
      setCode(editing.code);
      setDisplayName(editing.displayName);
      setAgentType(editing.agentType || "custom");
      setPersonaPrompt(editing.personaPrompt || DEFAULT_PERSONA);
      setLlmConfigId(editing.llmConfigId ?? "");
      setTools(parseTools(editing.allowedToolsJson));
    } else {
      setCode("");
      setDisplayName("");
      setAgentType("content");
      setPersonaPrompt(DEFAULT_PERSONA);
      setLlmConfigId("");
      setTools([]);
    }
  }, [open, editing]);

  const saveMutation = useMutation({
    mutationFn: () =>
      upsertOrchestrationV2Agent({
        code: code.trim(),
        displayName: displayName.trim(),
        agentType,
        personaPrompt: personaPrompt.trim(),
        isOrchestratable: true,
        allowedToolsJson: JSON.stringify(tools),
        llmConfigId: llmConfigId === "" ? UNBIND_LLM_CONFIG : llmConfigId,
      }),
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: ["orchestration-v2", "agents"] });
      onSaved(saved);
      onClose();
    },
  });

  function toggleTool(name: string) {
    setTools((current) => (current.includes(name) ? current.filter((t) => t !== name) : [...current, name]));
  }

  const canSubmit = code.trim().length >= 2 && displayName.trim().length >= 2 && personaPrompt.trim().length >= 10;

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
            value={code}
            onChange={(event) => setCode(event.target.value.toLowerCase().replaceAll(/[^a-z0-9-_]/g, ""))}
            placeholder="vd: seo-writer"
            disabled={Boolean(editing)}
          />
        </div>
        <div>
          <label className="mb-1 block text-body-md font-bold text-secondary" htmlFor="sub-agent-name">
            Tên hiển thị
          </label>
          <Input id="sub-agent-name" value={displayName} onChange={(event) => setDisplayName(event.target.value)} placeholder="vd: SEO Writer" />
        </div>
        <div>
          <label className="mb-1 block text-body-md font-bold text-secondary" htmlFor="sub-agent-type">
            Nhóm vai trò
          </label>
          <select id="sub-agent-type" className={SELECT_CLASS} value={agentType} onChange={(event) => setAgentType(event.target.value)}>
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
          <select id="sub-agent-llm" className={SELECT_CLASS} value={llmConfigId} onChange={(event) => setLlmConfigId(event.target.value)}>
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
          value={personaPrompt}
          onChange={(event) => setPersonaPrompt(event.target.value)}
          rows={6}
          className="w-full rounded-lg border border-outline bg-surface-container-lowest p-3 font-mono text-mono-status text-on-surface focus:border-primary focus:outline-none"
        />
      </div>

      <div>
        <p className="mb-2 text-body-md font-bold text-secondary">Công cụ được phép dùng</p>
        <ToolPicker onToggle={toggleTool} selected={tools} />
      </div>
    </Modal>
  );
}
