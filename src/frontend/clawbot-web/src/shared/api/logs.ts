import { apiClient } from "./client";

export interface TaskRunStats {
  readonly totalSessions: number;
  readonly runningSessions: number;
  readonly completedSessions: number;
  readonly traceEvents: number;
  readonly auditEvents: number;
  readonly tokensLast30Days: number;
}

export interface TaskRunListItem {
  readonly id: string;
  readonly agentCode: string | null;
  readonly agentName: string;
  readonly agentType: string;
  readonly goal: string;
  readonly status: string;
  readonly startedAt: string;
  readonly finishedAt: string | null;
  readonly durationMs: number;
  readonly traceCount: number;
  readonly lastPhase: string | null;
  readonly lastMessage: string | null;
  readonly totalTokens: number;
  readonly usd: number;
}

export interface TaskRunTrace {
  readonly id: string;
  readonly sessionId: string;
  readonly taskId: string | null;
  readonly agentName: string;
  readonly phase: string;
  readonly message: string;
  readonly occurredAt: string;
}

export interface TaskRunAudit {
  readonly id: string;
  readonly action: string;
  readonly resourceType: string;
  readonly resourceId: string | null;
  readonly diffJson: string | null;
  readonly ipAddress: string | null;
  readonly userAgent: string | null;
  readonly occurredAt: string;
}

export interface TaskRunListResponse {
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
  readonly stats: TaskRunStats;
  readonly items: readonly TaskRunListItem[];
}

export interface TaskRunDetailResponse {
  readonly run: TaskRunListItem;
  readonly traces: readonly TaskRunTrace[];
  readonly auditEvents: readonly TaskRunAudit[];
}

export interface AuditLogListResponse {
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
  readonly items: readonly TaskRunAudit[];
}

export interface ListTaskRunsParams {
  readonly agentCode?: string;
  readonly status?: string;
  readonly q?: string;
  readonly page?: number;
  readonly pageSize?: number;
}

export interface ListAuditParams {
  readonly action?: string;
  readonly resourceType?: string;
  readonly page?: number;
  readonly pageSize?: number;
}

export async function listTaskRuns(params?: ListTaskRunsParams): Promise<TaskRunListResponse> {
  const res = await apiClient.get<TaskRunListResponse>("/api/logs/task-runs", { params });
  return res.data;
}

export async function getTaskRunDetail(id: string): Promise<TaskRunDetailResponse> {
  const res = await apiClient.get<TaskRunDetailResponse>(`/api/logs/task-runs/${id}`);
  return res.data;
}

export async function listLogAudit(params?: ListAuditParams): Promise<AuditLogListResponse> {
  const res = await apiClient.get<AuditLogListResponse>("/api/logs/audit", { params });
  return res.data;
}
