import { apiClient } from "./client";
import type { JobAccepted } from "./jobs";

export interface OrchestrationV2Agent {
  readonly id: string;
  readonly code: string;
  readonly displayName: string;
  readonly agentType: string;
  readonly isOrchestratable: boolean;
  readonly version: number;
  readonly kbModuleCode?: string | null;
  readonly personaPrompt?: string;
  // SPEC-16 P1-7: tool allow-list + input schema for the ReAct worker.
  readonly allowedToolsJson?: string;
  readonly inputSchemaJson?: string;
  // Bound LLM config; null/undefined = unbound (orchestrator planner skips unbound agents).
  readonly llmConfigId?: string | null;
}

export interface OrchestrationV2Schedule {
  readonly id: string;
  readonly name: string;
  readonly goalTemplate: string;
  readonly cadence: string;
  readonly timezoneId: string;
  readonly nextRunAt: string;
  readonly lastRunAt: string | null;
  readonly isActive: boolean;
  readonly requiresApproval: boolean;
  /** "cadence" (mặc định) hoặc "event" — lịch sự kiện ngủ tới khi hệ thống phát event tương ứng. */
  readonly triggerType?: string;
  readonly eventKey?: string | null;
  readonly lastRunStatus?: string | null;
  readonly lastRunError?: string | null;
}

export interface OrchestrationV2RunNowResponse {
  readonly status: string;
  readonly sessionId: string | null;
  readonly nextRunAt: string;
  readonly lastRunAt: string;
}

export type OrchestrationV2Status =
  | "draft"
  | "pending_approval"
  | "running"
  | "pause_requested"
  | "paused"
  | "cancelling"
  | "failing"
  | "completed"
  | "failed"
  | "cancelled"
  | string;

export type OrchestrationV2ControlAction = "pause" | "resume" | "cancel";

/** Can thiệp thủ công vào một task khi phiên đang tạm dừng — không gọi planner nên không tốn LLM. */
export type OrchestrationV2TaskAction = "edit_output" | "retry" | "skip";

export interface OrchestrationV2RunSummary {
  readonly sessionId: string;
  readonly status: OrchestrationV2Status;
  readonly goal: string | null;
  readonly startedAt: string;
  readonly finishedAt: string | null;
  readonly userId?: string | null;
}

export interface OrchestrationV2Trace {
  readonly taskId: string;
  readonly agentName: string;
  readonly phase: string;
  readonly message: string;
  readonly occurredAt: string;
}

export interface OrchestrationV2Message {
  readonly id: string;
  readonly taskId: string;
  readonly intent: string;
  readonly status: string;
  readonly payloadJson: string;
  readonly error: string | null;
  readonly createdAt: string;
  readonly processedAt: string | null;
}

export interface OrchestrationV2TaskDto {
  readonly id: string;
  readonly agent: string;
  readonly description: string;
  readonly dependsOn: readonly string[];
  readonly input: Readonly<Record<string, string>>;
  readonly status: string;
  readonly output: string | null;
  readonly error: string | null;
  readonly useCount?: number;
  readonly currentTaskId?: string | null;
}

export interface OrchestrationV2Plan {
  readonly sessionId: string;
  readonly status: OrchestrationV2Status;
  readonly goal: string;
  readonly requiresApproval: boolean;
  readonly costBlocked: boolean;
  readonly costReason: string | null;
  readonly replanCount: number;
  readonly etag: string;
  readonly planJson: string;
  readonly tasks: readonly OrchestrationV2TaskDto[];
}

export interface OrchestrationV2RunDetail extends OrchestrationV2Plan {
  readonly startedAt: string;
  readonly finishedAt: string | null;
  readonly archivedAt: string | null;
  readonly traces: readonly OrchestrationV2Trace[];
  readonly messages: readonly OrchestrationV2Message[];
  /** Chi phí LLM thực của phiên (USD), tổng từ ledger gắn sessionId. */
  readonly actualCostUsd: number;
}

interface ListResponse<T> {
  readonly items: readonly T[];
}

export async function listOrchestrationV2Agents(): Promise<readonly OrchestrationV2Agent[]> {
  const res = await apiClient.get<ListResponse<OrchestrationV2Agent>>("/api/orchestration/v2/agents");
  return res.data.items;
}

// SPEC-16 P1-7: upsert a data-defined agent (allowedTools/inputSchema now editable).
export async function upsertOrchestrationV2Agent(payload: {
  readonly code: string;
  readonly displayName: string;
  readonly agentType: string;
  readonly personaPrompt: string;
  readonly isOrchestratable: boolean;
  readonly kbModuleCode?: string | null;
  readonly allowedToolsJson?: string;
  readonly inputSchemaJson?: string;
  /** null/omit = giữ binding hiện tại; Guid rỗng = gỡ bind; giá trị = bind LLM config. */
  readonly llmConfigId?: string | null;
}): Promise<OrchestrationV2Agent> {
  const res = await apiClient.post<OrchestrationV2Agent>("/api/orchestration/v2/agents", payload);
  return res.data;
}

export async function listOrchestrationV2Schedules(): Promise<readonly OrchestrationV2Schedule[]> {
  const res = await apiClient.get<ListResponse<OrchestrationV2Schedule>>("/api/orchestration/v2/schedules");
  return res.data.items;
}

export async function createOrchestrationV2Schedule(payload: {
  readonly name: string;
  readonly goalTemplate: string;
  readonly cadence: string;
  readonly timezoneId: string;
  readonly triggerType?: string;
  readonly eventKey?: string | null;
}): Promise<OrchestrationV2Schedule> {
  const res = await apiClient.post<OrchestrationV2Schedule>("/api/orchestration/v2/schedules", payload);
  return res.data;
}

export async function runOrchestrationV2ScheduleNow(id: string): Promise<OrchestrationV2RunNowResponse> {
  const res = await apiClient.post<OrchestrationV2RunNowResponse>(`/api/orchestration/v2/schedules/${encodeURIComponent(id)}/run-now`);
  return res.data;
}

export async function deleteOrchestrationV2Schedule(id: string): Promise<void> {
  await apiClient.delete(`/api/orchestration/v2/schedules/${encodeURIComponent(id)}`);
}

// `mine` filters to the current user's runs; `archived` switches to the archived list.
export async function listOrchestrationV2Runs(mine = false, archived = false): Promise<readonly OrchestrationV2RunSummary[]> {
  const params = new URLSearchParams();
  if (mine) params.set("mine", "true");
  if (archived) params.set("archived", "true");
  const suffix = params.toString() ? `?${params}` : "";
  const res = await apiClient.get<ListResponse<OrchestrationV2RunSummary>>(`/api/orchestration/v2/runs${suffix}`);
  return res.data.items;
}

export async function createOrchestrationV2Run(goal: string, dryRun = false): Promise<{ readonly sessionId: string; readonly status: string }> {
  const res = await apiClient.post<{ readonly sessionId: string; readonly status: string }>("/api/orchestration/v2/runs", { goal, dryRun });
  return res.data;
}

export async function unarchiveOrchestrationV2Run(
  sessionId: string,
): Promise<{ readonly sessionId: string; readonly status: string; readonly archivedAt: string | null }> {
  const res = await apiClient.post<{ readonly sessionId: string; readonly status: string; readonly archivedAt: string | null }>(
    `/api/orchestration/v2/runs/${encodeURIComponent(sessionId)}/unarchive`,
  );
  return res.data;
}

export async function pauseOrchestrationV2Schedule(id: string): Promise<OrchestrationV2Schedule> {
  const res = await apiClient.post<OrchestrationV2Schedule>(`/api/orchestration/v2/schedules/${encodeURIComponent(id)}/pause`);
  return res.data;
}

export async function activateOrchestrationV2Schedule(id: string): Promise<OrchestrationV2Schedule> {
  const res = await apiClient.post<OrchestrationV2Schedule>(`/api/orchestration/v2/schedules/${encodeURIComponent(id)}/activate`);
  return res.data;
}

// "Tự động xây dựng kế hoạch": orchestrator quét snapshot hệ thống, đề xuất kế hoạch định kỳ chưa trùng.
export interface OrchestrationPlanSuggestion {
  readonly name: string;
  readonly goal: string;
  readonly cadence: string;
  readonly reason: string;
}

export interface OrchestrationPlanSuggestionsResponse {
  readonly items: readonly OrchestrationPlanSuggestion[];
  readonly skippedDuplicates: number;
}

// Chạy ngầm: trả jobId; kết quả (checklist) nằm trong resultSummary của job dưới dạng JSON.
export async function suggestOrchestrationPlans(): Promise<JobAccepted> {
  const res = await apiClient.post<JobAccepted>("/api/orchestration/v2/plan-suggestions");
  return res.data;
}

export interface OrchestrationCostSummary {
  readonly monthToDateUsd: number;
  readonly capUsd: number;
}

export async function getOrchestrationV2CostSummary(): Promise<OrchestrationCostSummary> {
  const res = await apiClient.get<OrchestrationCostSummary>("/api/orchestration/v2/cost-summary");
  return res.data;
}

export async function getOrchestrationV2Run(id: string): Promise<OrchestrationV2RunDetail> {
  const res = await apiClient.get<OrchestrationV2RunDetail>(`/api/orchestration/v2/runs/${encodeURIComponent(id)}`);
  return res.data;
}

export async function updateOrchestrationV2Plan(sessionId: string, planJson: string, etag: string): Promise<OrchestrationV2Plan> {
  const res = await apiClient.put<OrchestrationV2Plan>(
    `/api/orchestration/v2/runs/${encodeURIComponent(sessionId)}/plan`,
    { planJson, etag },
  );
  return res.data;
}

/**
 * Can thiệp một bước của phiên đang tạm dừng. Server tự sửa plan (redact + validate) nên FE không
 * phải tự vá JSON. `rerunDownstream` đặt lại các bước phía sau về chờ chạy — bắt buộc khi chúng đã
 * chạy với kết quả cũ, nếu không output vừa sửa sẽ không đi tới đâu.
 */
export async function interveneOrchestrationV2Task(
  sessionId: string,
  taskId: string,
  payload: {
    readonly action: OrchestrationV2TaskAction;
    readonly output?: string;
    readonly rerunDownstream: boolean;
    readonly etag: string;
  },
): Promise<OrchestrationV2Plan> {
  const res = await apiClient.post<OrchestrationV2Plan>(
    `/api/orchestration/v2/runs/${encodeURIComponent(sessionId)}/tasks/${encodeURIComponent(taskId)}/intervene`,
    payload,
  );
  return res.data;
}

export async function approveOrchestrationV2Run(sessionId: string, etag: string): Promise<OrchestrationV2Plan> {
  const res = await apiClient.post<OrchestrationV2Plan>(`/api/orchestration/v2/runs/${encodeURIComponent(sessionId)}/approve`, { etag });
  return res.data;
}

// `etag` is optional because the recent-runs list (OrchestrationV2RunSummary) doesn't carry one; omitting it
// preserves today's behavior for that path (control still requires a match, so an etag-less call there 409s).
export async function controlOrchestrationV2Run(
  sessionId: string,
  action: OrchestrationV2ControlAction,
  etag?: string,
): Promise<{ readonly sessionId: string; readonly status: string }> {
  const res = await apiClient.post<{ readonly sessionId: string; readonly status: string }>(
    `/api/orchestration/v2/runs/${encodeURIComponent(sessionId)}/control`,
    { action, etag },
  );
  return res.data;
}

export async function archiveOrchestrationV2Run(
  sessionId: string,
): Promise<{ readonly sessionId: string; readonly status: string; readonly archivedAt: string | null }> {
  const res = await apiClient.post<{ readonly sessionId: string; readonly status: string; readonly archivedAt: string | null }>(
    `/api/orchestration/v2/runs/${encodeURIComponent(sessionId)}/archive`,
  );
  return res.data;
}
