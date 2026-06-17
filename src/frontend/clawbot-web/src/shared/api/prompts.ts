import { apiClient } from "./client";

export interface PromptConfigStats {
  readonly totalConfigs: number;
  readonly runningConfigs: number;
  readonly promptConfigured: number;
  readonly tokensLast7Days: number;
  readonly usdLast7Days: number;
}

export interface PromptUsageLog {
  readonly id: string;
  readonly agentCode: string;
  readonly model: string;
  readonly inputTokens: number;
  readonly outputTokens: number;
  readonly totalTokens: number;
  readonly usd: number;
  readonly createdAt: string;
}

export interface PromptConfig {
  readonly code: string;
  readonly displayName: string;
  readonly agentType: string;
  readonly model: string;
  readonly status: string;
  readonly provider: string;
  readonly systemPrompt: string;
  readonly temperature: number;
  readonly maxTokens: number;
  readonly skillFiles: readonly string[];
  readonly kbModules: readonly string[];
  readonly updatedAt: string;
  readonly lastRunAt: string | null;
  readonly callsLast7Days: number;
  readonly inputTokensLast7Days: number;
  readonly outputTokensLast7Days: number;
  readonly totalTokensLast7Days: number;
  readonly usdLast7Days: number;
  readonly recentUsage: readonly PromptUsageLog[];
}

export interface PromptConfigListResponse {
  readonly stats: PromptConfigStats;
  readonly items: readonly PromptConfig[];
}

export interface UpdatePromptConfigPayload {
  readonly displayName?: string;
  readonly model?: string;
  readonly provider?: string;
  readonly systemPrompt?: string;
  readonly temperature?: number;
  readonly maxTokens?: number;
  readonly skillFiles?: readonly string[];
  readonly kbModules?: readonly string[];
}

export interface PromptSandboxPayload {
  readonly message: string;
  readonly systemPrompt?: string;
}

export interface PromptSandboxResponse {
  readonly sessionId: string;
  readonly reply: string;
  readonly sentAt: string;
  readonly estimatedTokens: number;
}

export async function listPromptConfigs(): Promise<PromptConfigListResponse> {
  const res = await apiClient.get<PromptConfigListResponse>("/api/prompts/configs");
  return res.data;
}

export async function getPromptConfig(code: string): Promise<PromptConfig> {
  const res = await apiClient.get<PromptConfig>(`/api/prompts/configs/${encodeURIComponent(code)}`);
  return res.data;
}

export async function updatePromptConfig(code: string, payload: UpdatePromptConfigPayload): Promise<PromptConfig> {
  const res = await apiClient.put<PromptConfig>(`/api/prompts/configs/${encodeURIComponent(code)}`, payload);
  return res.data;
}

export async function runPromptSandbox(code: string, payload: PromptSandboxPayload): Promise<PromptSandboxResponse> {
  const res = await apiClient.post<PromptSandboxResponse>(`/api/prompts/configs/${encodeURIComponent(code)}/sandbox`, payload);
  return res.data;
}
