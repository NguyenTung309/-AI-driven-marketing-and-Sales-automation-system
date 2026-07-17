import { apiClient } from "./client";
import type { TestLlmConfigResult } from "./llmConfigs";

export type EmbeddingProvider = "openai" | "openai-compatible" | "hash";

export interface EmbeddingConfig {
  readonly id: string;
  readonly provider: EmbeddingProvider;
  readonly modelId: string;
  readonly displayName: string | null;
  readonly hasApiKey: boolean;
  readonly baseUrl: string | null;
  readonly dimension: number;
  readonly isActive: boolean;
  readonly createdAt: string;
  readonly updatedAt: string;
}

export interface EmbeddingStatus {
  readonly configured: boolean;
  readonly provider: string;
  readonly modelId: string;
  readonly dimension: number;
  readonly source: string;
  readonly isFallback: boolean;
  readonly displayName: string | null;
  /** "llm" = KB truy xuất bằng LLM của tenant (mặc định); "vector" = có embedding config, dùng Qdrant. */
  readonly retrievalMode: "vector" | "llm";
}

export interface CreateEmbeddingConfigPayload {
  readonly provider: EmbeddingProvider;
  readonly modelId: string;
  readonly dimension: number;
  readonly apiKey?: string | null;
  readonly displayName?: string | null;
  readonly baseUrl?: string | null;
}

export type UpdateEmbeddingConfigPayload = Omit<CreateEmbeddingConfigPayload, "apiKey">;

const BASE = "/api/embedding-configs";

export async function listEmbeddingConfigs(): Promise<readonly EmbeddingConfig[]> {
  const res = await apiClient.get<readonly EmbeddingConfig[]>(BASE);
  return res.data;
}

export async function getEmbeddingStatus(): Promise<EmbeddingStatus> {
  const res = await apiClient.get<EmbeddingStatus>(`${BASE}/status`);
  return res.data;
}

export async function createEmbeddingConfig(payload: CreateEmbeddingConfigPayload): Promise<EmbeddingConfig> {
  const res = await apiClient.post<EmbeddingConfig>(BASE, payload);
  return res.data;
}

export async function updateEmbeddingConfig(id: string, payload: UpdateEmbeddingConfigPayload): Promise<EmbeddingConfig> {
  const res = await apiClient.put<EmbeddingConfig>(`${BASE}/${id}`, payload);
  return res.data;
}

export async function rotateEmbeddingKey(id: string, apiKey: string): Promise<void> {
  await apiClient.post(`${BASE}/${id}/rotate-key`, { apiKey });
}

export async function setEmbeddingConfigActive(id: string, active: boolean): Promise<EmbeddingConfig> {
  const res = await apiClient.post<EmbeddingConfig>(`${BASE}/${id}/${active ? "activate" : "deactivate"}`);
  return res.data;
}

export async function deleteEmbeddingConfig(id: string): Promise<void> {
  await apiClient.delete(`${BASE}/${id}`);
}

export async function testEmbeddingConfig(id: string): Promise<TestLlmConfigResult> {
  const res = await apiClient.post<TestLlmConfigResult>(`${BASE}/${id}/test`);
  return res.data;
}
