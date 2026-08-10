import { useState } from "react";
import { Alert } from "@/shared/ui/Alert";
import { Button } from "@/shared/ui/Button";
import { Modal } from "@/shared/ui/Modal";
import { StatusPill } from "@/shared/ui/StatusPill";
import { joinToolResults, splitToolResults } from "@/shared/utils/userText";
import { taskStatusLabel, taskTone, transitiveDependents } from "./orchestrationStatus";
import type { OrchestrationV2TaskAction, OrchestrationV2TaskDto } from "@/shared/api/orchestrationV2";

// Khớp OrchestratorGrpcService.MaxInterveneOutputChars (= OrchestrationPlanValidator.MaxTaskInputChars).
const MAX_OUTPUT_CHARS = 8192;

export interface TaskInterventionPayload {
  readonly action: OrchestrationV2TaskAction;
  readonly output?: string;
  readonly rerunDownstream: boolean;
  /** Chạy tiếp phiên ngay sau khi lưu, thay vì giữ nguyên trạng thái tạm dừng. */
  readonly resumeAfter: boolean;
}

interface TaskInterventionDialogProps {
  readonly open: boolean;
  readonly task: OrchestrationV2TaskDto | null;
  readonly tasks: readonly OrchestrationV2TaskDto[];
  readonly busy: boolean;
  readonly error: string | null;
  readonly onClose: () => void;
  readonly onSubmit: (payload: TaskInterventionPayload) => void;
}

interface DraftState {
  /** Bước + output đã gieo nháp. Đổi = server có kết quả mới (chạy lại, bỏ qua) → gieo lại, đừng giữ bản cũ. */
  readonly source: string;
  readonly text: string;
  readonly toolResultsJson: string;
  readonly rerunDownstream: boolean;
}

function draftSourceOf(task: OrchestrationV2TaskDto): string {
  return `${task.id}:${task.status}:${task.output ?? ""}`;
}

function seedDraft(task: OrchestrationV2TaskDto, forceRerun: boolean): DraftState {
  const { text, toolResults } = splitToolResults(task.output);
  return {
    source: draftSourceOf(task),
    text,
    toolResultsJson: toolResults ? JSON.stringify(toolResults, null, 2) : "",
    rerunDownstream: forceRerun,
  };
}

function parseToolResults(raw: string): { readonly value: Record<string, string> | null; readonly error: string | null } {
  const trimmed = raw.trim();
  if (!trimmed) return { value: null, error: null };
  let parsed: unknown;
  try {
    parsed = JSON.parse(trimmed);
  } catch {
    return { value: null, error: "JSON không hợp lệ — kiểm tra lại dấu ngoặc và dấu phẩy." };
  }
  if (!parsed || typeof parsed !== "object" || Array.isArray(parsed))
    return { value: null, error: "Khối bàn giao phải là một object JSON, ví dụ {\"content_id\": \"...\"}." };
  const flat: Record<string, string> = {};
  for (const [key, val] of Object.entries(parsed as Record<string, unknown>))
    flat[key] = typeof val === "string" ? val : JSON.stringify(val);
  return { value: flat, error: null };
}

/**
 * Sửa tay kết quả một bước khi phiên đang tạm dừng, rồi chạy tiếp — thay cho việc để orchestrator lập lại
 * kế hoạch (tốn thêm một lượt LLM và chạy lại cả những bước đã xong).
 */
export function TaskInterventionDialog({
  open,
  task,
  tasks,
  busy,
  error,
  onClose,
  onSubmit,
}: TaskInterventionDialogProps) {
  const [draft, setDraft] = useState<DraftState | null>(null);

  const dependents = task ? transitiveDependents(tasks, task.id) : [];
  // Bước phía sau đã chạy thì kết quả của nó dựa trên output cũ — bắt buộc đặt lại, không cho bỏ tick.
  const staleDependents = dependents.filter((t) => t.status !== "pending");
  const mustRerunDownstream = staleDependents.length > 0;

  // Gieo lại nháp khi mở dialog cho bước khác hoặc khi server trả kết quả mới (adjust-state-on-prop-change).
  if (open && task && draft?.source !== draftSourceOf(task)) {
    setDraft(seedDraft(task, mustRerunDownstream));
  }

  if (!task || !draft) {
    return <Modal open={open} onClose={onClose} title="Can thiệp bước" maxWidthClass="max-w-3xl"><p className="text-label-sm text-on-surface-variant">Chưa chọn bước nào.</p></Modal>;
  }

  const toolResults = parseToolResults(draft.toolResultsJson);
  const nextOutput = joinToolResults(draft.text, toolResults.value);
  const tooLong = nextOutput.length > MAX_OUTPUT_CHARS;
  const canSaveEdit = !busy && toolResults.error === null && !tooLong && nextOutput.trim().length > 0;
  const rerunDownstream = mustRerunDownstream || draft.rerunDownstream;

  const update = (patch: Partial<DraftState>): void => setDraft((current) => (current ? { ...current, ...patch } : current));
  const submitEdit = (resumeAfter: boolean): void =>
    onSubmit({ action: "edit_output", output: nextOutput, rerunDownstream, resumeAfter });

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={`Can thiệp bước: ${task.id}`}
      maxWidthClass="max-w-3xl"
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={busy}>
            Đóng
          </Button>
          <Button
            variant="ghost"
            onClick={() => onSubmit({ action: "skip", rerunDownstream, resumeAfter: true })}
            disabled={busy}
            title="Đánh dấu bỏ qua — các bước sau vẫn chạy, chỉ không nhận đầu vào từ bước này."
          >
            Bỏ qua bước này
          </Button>
          {task.status !== "pending" && (
            <Button
              variant="outline"
              onClick={() => onSubmit({ action: "retry", rerunDownstream, resumeAfter: true })}
              disabled={busy}
              title="Chạy lại đúng bước này bằng agent, không lập lại kế hoạch."
            >
              Chạy lại bước này
            </Button>
          )}
          <Button variant="outline" onClick={() => submitEdit(false)} disabled={!canSaveEdit}>
            Lưu, giữ tạm dừng
          </Button>
          <Button onClick={() => submitEdit(true)} disabled={!canSaveEdit}>
            Lưu &amp; chạy tiếp
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <span className="font-mono text-mono-status text-secondary">{task.agent}</span>
          <StatusPill tone={taskTone(task.status)}>{taskStatusLabel(task.status)}</StatusPill>
          {task.dependsOn.length > 0 && (
            <span className="text-label-sm text-on-surface-variant">Nhận đầu vào từ: {task.dependsOn.join(", ")}</span>
          )}
        </div>
        <p className="text-body-sm text-on-surface">{task.description}</p>
        {task.error && <Alert tone="error">{task.error}</Alert>}
        {error && <Alert tone="error">{error}</Alert>}

        <div className="flex flex-col gap-1">
          <label className="text-label-sm text-on-surface-variant" htmlFor="intervene-text">
            Kết quả bàn giao cho bước sau
          </label>
          <textarea
            id="intervene-text"
            value={draft.text}
            onChange={(event) => update({ text: event.target.value })}
            rows={12}
            className="w-full resize-y rounded-lg border border-outline bg-surface-container-lowest p-3 text-body-sm text-on-surface focus:border-primary focus:outline-none"
            disabled={busy}
            spellCheck={false}
          />
        </div>

        <details className="rounded border border-outline bg-surface-container-low p-2" open={draft.toolResultsJson.length > 0}>
          <summary className="cursor-pointer text-label-sm text-on-surface-variant">
            Dữ liệu định danh kèm theo (tool_results)
          </summary>
          <p className="mt-2 text-label-sm text-on-surface-variant">
            Object JSON chứa các id thao tác thật (content_id, schedule_id, post_url…). Để trống nếu bước này không tạo ra gì.
          </p>
          <textarea
            value={draft.toolResultsJson}
            onChange={(event) => update({ toolResultsJson: event.target.value })}
            rows={6}
            placeholder={'{\n  "content_id": "..."\n}'}
            className="mt-2 w-full resize-y rounded-lg border border-outline bg-surface-container-lowest p-3 font-mono text-mono-status text-on-surface focus:border-primary focus:outline-none"
            disabled={busy}
            spellCheck={false}
          />
          {toolResults.error && <p className="mt-1 text-label-sm text-error">{toolResults.error}</p>}
        </details>

        <p className={`text-label-sm ${tooLong ? "font-bold text-error" : "text-on-surface-variant"}`}>
          {nextOutput.length.toLocaleString("vi-VN")} / {MAX_OUTPUT_CHARS.toLocaleString("vi-VN")} ký tự
          {tooLong ? " — vượt giới hạn, hãy rút gọn trước khi lưu." : ""}
        </p>

        {dependents.length > 0 && (
          <div className="rounded border border-outline p-2">
            <label className="flex items-start gap-2 text-label-sm text-on-surface">
              <input
                checked={rerunDownstream}
                disabled={busy || mustRerunDownstream}
                onChange={(event) => update({ rerunDownstream: event.target.checked })}
                type="checkbox"
              />
              <span>
                Chạy lại {dependents.length} bước phía sau
                {mustRerunDownstream && (
                  <span className="text-on-surface-variant">
                    {" "}
                    — bắt buộc vì {staleDependents.length} bước đã chạy với kết quả cũ.
                  </span>
                )}
              </span>
            </label>
            <ul className="mt-2 flex flex-wrap gap-1.5">
              {dependents.map((dependent) => (
                <li
                  className="inline-flex items-center gap-1 rounded-full border border-outline px-2 py-0.5 text-label-sm"
                  key={dependent.id}
                >
                  <span className="font-mono text-on-surface-variant">{dependent.id}</span>
                  <span className="text-on-surface-variant">{taskStatusLabel(dependent.status)}</span>
                </li>
              ))}
            </ul>
          </div>
        )}

        <p className="text-label-sm text-on-surface-variant">
          Sửa tay không gọi lại bộ lập kế hoạch nên không phát sinh chi phí LLM; chỉ những bước chạy lại mới tốn thêm.
        </p>
      </div>
    </Modal>
  );
}
