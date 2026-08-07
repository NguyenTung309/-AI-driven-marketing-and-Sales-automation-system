import { apiClient } from "./client";

export interface OmniChannelRow {
  readonly platform: string;
  readonly leads: number;
  readonly dms: number;
  readonly replies: number;
  readonly repliedDms: number;
  readonly conversions: number;
  readonly avgResponseTimeSec: number | null;
  readonly adSpend: number | null;
  readonly cpl: number | null;
  readonly revenue?: number | null;
}

export interface OmniChannelResponse {
  readonly from: string;
  readonly to: string;
  readonly rows: readonly OmniChannelRow[];
  readonly stale: boolean;
}

export interface MetricDelta {
  readonly metric: string;
  readonly current: number;
  readonly previous: number;
  readonly deltaPct: number | null;
}

export interface OmniChannelDeltaResponse {
  readonly from: string;
  readonly to: string;
  readonly compare: string;
  readonly prevFrom: string;
  readonly prevTo: string;
  readonly metrics: readonly MetricDelta[];
}

export interface FunnelResponse {
  readonly platform: string;
  readonly leads: number;
  readonly dms: number;
  readonly replies: number;
  readonly conversions: number;
  readonly dmRate: number;
  readonly replyRate: number;
  readonly conversionRate: number;
}

export interface AgentPerformance {
  readonly agentId: string | null;
  readonly agentName: string;
  readonly sessions: number;
  readonly completedSessions: number;
  readonly traceCount: number;
  readonly completionRate: number;
  readonly qualitySamples: number;
  readonly passedQualitySamples: number;
  readonly qualityPassRate: number;
  readonly averageQualityScore: number | null;
}

export interface AnomalyPoint {
  readonly date: string;
  readonly platform: string;
  readonly metric: string;
  readonly value: number;
  readonly zScore: number;
  readonly isAnomaly: boolean;
}

export interface ForecastPoint {
  readonly date: string;
  readonly platform: string;
  readonly metric: string;
  readonly value: number;
  readonly lowerBound: number;
  readonly upperBound: number;
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

export interface AnalyticsRangeParams {
  readonly from?: string;
  readonly to?: string;
}

// GET /api/analytics/omnichannel?from=&to=
export async function getOmnichannel(params?: AnalyticsRangeParams): Promise<OmniChannelResponse> {
  const res = await apiClient.get<OmniChannelResponse>("/api/analytics/omnichannel", { params });
  return res.data;
}

export async function getOmnichannelDelta(
  params?: AnalyticsRangeParams & { readonly compare?: "dod" | "wow" }
): Promise<OmniChannelDeltaResponse> {
  const res = await apiClient.get<OmniChannelDeltaResponse>("/api/analytics/omnichannel-delta", { params });
  return res.data;
}

export async function getFunnel(params?: AnalyticsRangeParams & { readonly platform?: string }): Promise<FunnelResponse> {
  const res = await apiClient.get<FunnelResponse>("/api/analytics/funnel", { params });
  return res.data;
}

export async function getAgentPerformance(params?: AnalyticsRangeParams): Promise<readonly AgentPerformance[]> {
  const res = await apiClient.get<readonly AgentPerformance[]>("/api/analytics/agent-performance", { params });
  return res.data;
}

export async function getAnomalies(params: {
  readonly metric: string;
  readonly platform?: string;
  readonly zThreshold?: number;
  readonly lookbackDays?: number;
}): Promise<readonly AnomalyPoint[]> {
  const res = await apiClient.get<readonly AnomalyPoint[]>("/api/analytics/anomalies", { params });
  return res.data;
}

export async function getForecast(params: {
  readonly metric: string;
  readonly platform?: string;
  readonly horizon?: number;
}): Promise<readonly ForecastPoint[]> {
  const res = await apiClient.get<readonly ForecastPoint[]>("/api/analytics/forecast", { params });
  return res.data;
}

export async function getAgentCost(params?: AnalyticsRangeParams): Promise<AgentCostResponse> {
  const res = await apiClient.get<AgentCostResponse>("/api/analytics/agent-cost", { params });
  return res.data;
}

export async function downloadAnalyticsExport(params: AnalyticsRangeParams & { readonly format: "csv" | "pdf" }): Promise<Blob> {
  const res = await apiClient.get<Blob>("/api/analytics/export", {
    params,
    responseType: "blob",
  });
  return res.data;
}
