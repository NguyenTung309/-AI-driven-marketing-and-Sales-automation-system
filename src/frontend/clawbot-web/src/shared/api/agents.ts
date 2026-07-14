import { apiClient } from "./client";
import type { JobAccepted } from "./jobs";

export type AgentStatus = "running" | "stopped" | "error" | string;

export interface AgentListItem {
  readonly code: string;
  readonly displayName: string;
  readonly agentType: string;
  readonly model: string;
  readonly status: AgentStatus;
  readonly updatedAt: string;
  readonly lastRunAt: string | null;
  /** LLM config đã gắn; null = chưa gắn (planner sẽ bỏ qua agent này). */
  readonly llmConfigId?: string | null;
}

export interface AgentListResponse {
  readonly items: readonly AgentListItem[];
}

export interface AgentStatusResponse {
  readonly code: string;
  readonly status: AgentStatus;
}

export interface AgentTraceItem {
  readonly id: string;
  readonly sessionId: string;
  readonly agentName: string;
  readonly phase: string;
  readonly message: string;
  readonly occurredAt: string;
}

export interface AgentTraceResponse {
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
  readonly items: readonly AgentTraceItem[];
}

export interface AgentCostItem {
  readonly agentCode: string;
  readonly calls: number;
  readonly inputTokens: number;
  readonly outputTokens: number;
  readonly usd: number;
  readonly avgUsdPerCall: number;
}

export interface AgentCostResponse {
  readonly from: string;
  readonly to: string;
  readonly items: readonly AgentCostItem[];
}

export interface AgentSettings {
  readonly code: string;
  readonly displayName: string;
  readonly agentType: string;
  readonly model: string;
  readonly status: AgentStatus;
  readonly provider: string;
  readonly systemPrompt: string;
  readonly temperature: number;
  readonly maxTokens: number;
  readonly skillFiles: readonly string[];
  readonly kbModules: readonly string[];
  /** Tools the orchestrator may invoke for this agent. Empty = text-only (no system actions). */
  readonly allowedTools: readonly string[];
  /** Bound LLM provider config id (null = unconfigured → agent hard-errors at runtime). */
  readonly llmConfigId: string | null;
  readonly updatedAt: string;
}

export interface UpdateAgentSettingsPayload {
  readonly displayName?: string;
  readonly model?: string;
  readonly provider?: string;
  readonly systemPrompt?: string;
  readonly temperature?: number;
  readonly maxTokens?: number;
  readonly skillFiles?: readonly string[];
  readonly kbModules?: readonly string[];
  /** Replace the agent's tool grants. Omit = unchanged. */
  readonly allowedTools?: readonly string[];
  /** Tri-state: omit = unchanged, empty-guid = unbind, otherwise bind to that config id. */
  readonly llmConfigId?: string | null;
}

export interface AgentToolInfo {
  readonly name: string;
  readonly description: string;
  /** "Low" | "High" — High = irreversible/outward-facing (publish, ad spend, customer messages). */
  readonly risk: string;
  readonly permission: string;
}

export interface AgentSandboxResponse {
  readonly sessionId: string;
  readonly reply: string;
  readonly sentAt: string;
}

export async function listAgents(): Promise<AgentListResponse> {
  const res = await apiClient.get<AgentListResponse>("/api/agents");
  return res.data;
}

export async function enableAgent(code: string): Promise<AgentStatusResponse> {
  const res = await apiClient.post<AgentStatusResponse>(`/api/agents/${encodeURIComponent(code)}/enable`);
  return res.data;
}

export async function disableAgent(code: string): Promise<AgentStatusResponse> {
  const res = await apiClient.post<AgentStatusResponse>(`/api/agents/${encodeURIComponent(code)}/disable`);
  return res.data;
}

export async function getAgentTraces(code: string, page = 1, pageSize = 50): Promise<AgentTraceResponse> {
  const res = await apiClient.get<AgentTraceResponse>(`/api/agents/${encodeURIComponent(code)}/traces`, {
    params: { page, pageSize },
  });
  return res.data;
}

export async function getAgentSettings(code: string): Promise<AgentSettings> {
  const res = await apiClient.get<AgentSettings>(`/api/agents/${encodeURIComponent(code)}/settings`);
  return res.data;
}

export async function updateAgentSettings(code: string, payload: UpdateAgentSettingsPayload): Promise<AgentSettings> {
  const res = await apiClient.put<AgentSettings>(`/api/agents/${encodeURIComponent(code)}/settings`, payload);
  return res.data;
}

export async function runAgentSandbox(code: string, message: string): Promise<JobAccepted> {
  const res = await apiClient.post<JobAccepted>(`/api/agents/${encodeURIComponent(code)}/sandbox`, { message });
  return res.data;
}

export async function getAgentCost(): Promise<AgentCostResponse> {
  const res = await apiClient.get<AgentCostResponse>("/api/analytics/agent-cost");
  return res.data;
}

export async function listAgentTools(): Promise<readonly AgentToolInfo[]> {
  const res = await apiClient.get<{ items: readonly AgentToolInfo[] }>("/api/agents/tools");
  return res.data.items;
}
