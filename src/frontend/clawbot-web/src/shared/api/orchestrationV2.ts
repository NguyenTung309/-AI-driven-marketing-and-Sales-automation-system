import { apiClient } from "./client";

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
}

export interface OrchestrationV2RunNowResponse {
  readonly status: string;
  readonly nextRunAt: string;
}

export interface OrchestrationV2RunSummary {
  readonly sessionId: string;
  readonly status: string;
  readonly goal: string | null;
  readonly startedAt: string;
  readonly finishedAt: string | null;
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

export interface OrchestrationV2RunDetail extends OrchestrationV2RunSummary {
  readonly traces: readonly OrchestrationV2Trace[];
  readonly messages: readonly OrchestrationV2Message[];
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
  readonly requiresApproval: boolean;
}): Promise<OrchestrationV2Schedule> {
  const res = await apiClient.post<OrchestrationV2Schedule>("/api/orchestration/v2/schedules", payload);
  return res.data;
}

export async function runOrchestrationV2ScheduleNow(id: string): Promise<OrchestrationV2RunNowResponse> {
  const res = await apiClient.post<OrchestrationV2RunNowResponse>(`/api/orchestration/v2/schedules/${encodeURIComponent(id)}/run-now`);
  return res.data;
}

export async function listOrchestrationV2Runs(): Promise<readonly OrchestrationV2RunSummary[]> {
  const res = await apiClient.get<ListResponse<OrchestrationV2RunSummary>>("/api/orchestration/v2/runs");
  return res.data.items;
}

export async function createOrchestrationV2Run(goal: string): Promise<{ readonly sessionId: string; readonly status: string }> {
  const res = await apiClient.post<{ readonly sessionId: string; readonly status: string }>("/api/orchestration/v2/runs", { goal });
  return res.data;
}

export async function getOrchestrationV2Run(id: string): Promise<OrchestrationV2RunDetail> {
  const res = await apiClient.get<OrchestrationV2RunDetail>(`/api/orchestration/v2/runs/${encodeURIComponent(id)}`);
  return res.data;
}
