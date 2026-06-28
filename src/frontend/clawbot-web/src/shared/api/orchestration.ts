import { apiClient } from "./client";

export type OrchestrationStatus =
  | "draft"
  | "pending_approval"
  | "running"
  | "paused"
  | "completed"
  | "failed"
  | "cancelled"
  | string;

export type OrchestrationControlAction = "pause" | "resume" | "cancel";

export interface OrchestrationTaskDto {
  readonly id: string;
  readonly agent: string;
  readonly description: string;
  readonly dependsOn: readonly string[];
  readonly input: Readonly<Record<string, string>>;
  readonly status: string;
  readonly output: string | null;
  readonly error: string | null;
  // SPEC-16 P3-2: per-agent usage count + the in-progress task id for that agent (agent graph).
  readonly useCount?: number;
  readonly currentTaskId?: string | null;
}

export interface OrchestrationSessionDto {
  readonly sessionId: string;
  readonly status: OrchestrationStatus;
  readonly requiresApproval: boolean;
  readonly goal: string;
  readonly costBlocked: boolean;
  readonly costReason: string | null;
  readonly replanCount: number;
  readonly etag: string;
  readonly planJson: string;
  readonly tasks: readonly OrchestrationTaskDto[];
}

export interface OrchestrationTraceDto {
  readonly taskId: string;
  readonly agentName: string;
  readonly phase: string;
  readonly message: string;
  readonly occurredAt: string;
}

export interface OrchestrationTraceResponse {
  readonly items: readonly OrchestrationTraceDto[];
}

export async function submitOrchestration(goal: string): Promise<OrchestrationSessionDto> {
  const res = await apiClient.post<OrchestrationSessionDto>("/api/orchestration/submit", { goal });
  return res.data;
}

// SPEC-16 P3-6: recent orchestration runs (URL-independent list). `mine=true` filters to the current user.
export interface OrchestrationRunListItem {
  readonly sessionId: string;
  readonly status: OrchestrationStatus;
  readonly goal: string;
  readonly startedAt: string;
  readonly finishedAt: string | null;
  readonly userId?: string | null;
}

export async function listOrchestrationRuns(mine = false): Promise<readonly OrchestrationRunListItem[]> {
  const res = await apiClient.get<{ items: readonly OrchestrationRunListItem[] }>(
    `/api/orchestration/v2/runs${mine ? "?mine=true" : ""}`,
  );
  return res.data.items;
}

export async function getOrchestrationPlan(sessionId: string): Promise<OrchestrationSessionDto> {
  const res = await apiClient.get<OrchestrationSessionDto>(`/api/orchestration/${encodeURIComponent(sessionId)}`);
  return res.data;
}

export async function getOrchestrationTrace(sessionId: string): Promise<readonly OrchestrationTraceDto[]> {
  const res = await apiClient.get<OrchestrationTraceResponse>(`/api/orchestration/${encodeURIComponent(sessionId)}/trace`);
  return res.data.items;
}

export async function updateOrchestrationPlan(
  sessionId: string,
  planJson: string,
  etag: string,
): Promise<OrchestrationSessionDto> {
  const res = await apiClient.put<OrchestrationSessionDto>(
    `/api/orchestration/${encodeURIComponent(sessionId)}/plan`,
    { planJson, etag },
  );
  return res.data;
}

export async function approveOrchestration(sessionId: string, etag: string): Promise<OrchestrationSessionDto> {
  const res = await apiClient.post<OrchestrationSessionDto>(`/api/orchestration/${encodeURIComponent(sessionId)}/approve`, {
    etag,
  });
  return res.data;
}

export async function controlOrchestration(
  sessionId: string,
  action: OrchestrationControlAction,
  etag: string,
): Promise<OrchestrationSessionDto> {
  const res = await apiClient.post<OrchestrationSessionDto>(
    `/api/orchestration/${encodeURIComponent(sessionId)}/${action}`,
    { etag },
  );
  return res.data;
}
