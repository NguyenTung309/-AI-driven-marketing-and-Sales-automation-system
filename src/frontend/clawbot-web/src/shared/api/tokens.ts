import { apiClient } from "./client";

export interface TokenAgentUsage {
  readonly code: string;
  readonly displayName: string;
  readonly agentType: string;
  readonly moduleName: string;
  readonly status: string;
  readonly model: string;
  readonly routerTier: "flash" | "pro" | "high_effort" | string;
  readonly calls: number;
  readonly inputTokens: number;
  readonly outputTokens: number;
  readonly totalTokens: number;
  readonly usd: number;
  readonly monthlyQuotaTokens: number;
  readonly alertPercent: number;
  readonly usagePercent: number;
  /** Có ít nhất một lượt gọi token/cost do hệ thống ước lượng (provider không trả usage). */
  readonly hasEstimated: boolean;
}

export interface TokenModelUsage {
  readonly model: string;
  readonly calls: number;
  readonly totalTokens: number;
  readonly usd: number;
  readonly percent: number;
  readonly hasEstimated: boolean;
}

export interface TokenAlertSettings {
  readonly enabled: boolean;
  readonly lowBalanceThresholdTokens: number;
}

export interface TokenUsageResponse {
  readonly from: string;
  readonly to: string;
  readonly totalTokens: number;
  readonly inputTokens: number;
  readonly outputTokens: number;
  readonly usd: number;
  readonly monthlyQuotaTokens: number;
  readonly remainingTokens: number;
  readonly usagePercent: number;
  readonly estimatedDaysRemaining: number | null;
  readonly cacheHitRatioPercent: number | null;
  readonly agents: readonly TokenAgentUsage[];
  readonly models: readonly TokenModelUsage[];
  readonly alert: TokenAlertSettings;
  /** Phần chi phí provider báo thật (usd = measuredUsd + estimatedUsd). */
  readonly measuredUsd: number;
  /** Phần chi phí hệ thống ước lượng cục bộ — thấp hơn hóa đơn thật, phải gắn nhãn khi hiển thị. */
  readonly estimatedUsd: number;
  readonly hasEstimated: boolean;
}

export interface TokenQuotaUpdate {
  readonly code: string;
  readonly monthlyQuotaTokens: number;
  readonly alertPercent: number;
  readonly routerTier: string;
}

export interface UpdateTokenSettingsPayload {
  readonly quotas: readonly TokenQuotaUpdate[];
  readonly alert: TokenAlertSettings;
}

export async function getTokenUsage(params?: { readonly from?: string; readonly to?: string }): Promise<TokenUsageResponse> {
  const res = await apiClient.get<TokenUsageResponse>("/api/tokens/usage", { params });
  return res.data;
}

export async function updateTokenSettings(payload: UpdateTokenSettingsPayload): Promise<TokenUsageResponse> {
  const res = await apiClient.put<TokenUsageResponse>("/api/tokens/settings", payload);
  return res.data;
}
