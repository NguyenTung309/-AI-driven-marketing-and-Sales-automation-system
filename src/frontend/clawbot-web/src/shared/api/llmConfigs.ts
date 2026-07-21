import { apiClient } from "./client";

// openai = Chat Completions (/chat/completions); openai-responses = chuẩn OpenAI v2 (Responses API,
// /responses) cho gateway không expose chat/completions.
export type LlmProvider = "anthropic" | "openai" | "openai-responses";

export interface LlmConfig {
  readonly id: string;
  readonly provider: LlmProvider;
  readonly modelId: string;
  readonly displayName: string | null;
  readonly hasApiKey: boolean;
  readonly baseUrl: string | null;
  readonly isActive: boolean;
  readonly inputUsdPer1M: number | null;
  readonly outputUsdPer1M: number | null;
  readonly timeoutSeconds: number | null;
  readonly maxOutputTokens: number | null;
  readonly supportsVision: boolean | null;
  readonly createdAt: string;
  readonly updatedAt: string;
}

export interface CreateLlmConfigPayload {
  readonly provider: LlmProvider;
  readonly modelId: string;
  readonly apiKey: string;
  readonly displayName?: string | null;
  readonly baseUrl?: string | null;
  readonly inputUsdPer1M?: number | null;
  readonly outputUsdPer1M?: number | null;
  readonly timeoutSeconds?: number | null;
  readonly maxOutputTokens?: number | null;
  readonly supportsVision?: boolean | null;
}

export type UpdateLlmConfigPayload = Omit<CreateLlmConfigPayload, "apiKey">;

export interface TestLlmConfigResult {
  readonly ok: boolean;
  readonly latencyMs: number;
  readonly error?: string | null;
}

const BASE = "/api/llm-configs";

export async function listLlmConfigs(): Promise<readonly LlmConfig[]> {
  const res = await apiClient.get<readonly LlmConfig[]>(BASE);
  return res.data;
}

export async function createLlmConfig(payload: CreateLlmConfigPayload): Promise<LlmConfig> {
  const res = await apiClient.post<LlmConfig>(BASE, payload);
  return res.data;
}

export async function updateLlmConfig(id: string, payload: UpdateLlmConfigPayload): Promise<LlmConfig> {
  const res = await apiClient.put<LlmConfig>(`${BASE}/${id}`, payload);
  return res.data;
}

export async function rotateLlmKey(id: string, apiKey: string): Promise<void> {
  await apiClient.post(`${BASE}/${id}/rotate-key`, { apiKey });
}

export async function setLlmConfigActive(id: string, active: boolean): Promise<LlmConfig> {
  const res = await apiClient.post<LlmConfig>(`${BASE}/${id}/${active ? "activate" : "deactivate"}`);
  return res.data;
}

export async function deleteLlmConfig(id: string): Promise<void> {
  await apiClient.delete(`${BASE}/${id}`);
}

export async function testLlmConfig(id: string): Promise<TestLlmConfigResult> {
  const res = await apiClient.post<TestLlmConfigResult>(`${BASE}/${id}/test`);
  return res.data;
}
