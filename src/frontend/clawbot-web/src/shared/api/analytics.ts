import { apiClient } from "./client";

export interface OmniChannelRow {
  readonly platform: string;
  readonly leads: number;
  readonly dms: number;
  readonly replies: number;
  readonly conversions: number;
  readonly avgResponseTimeSec: number | null;
  readonly adSpend: number | null;
  readonly cpl: number | null;
}

export interface OmniChannelResponse {
  readonly from: string;
  readonly to: string;
  readonly rows: readonly OmniChannelRow[];
  readonly stale: boolean;
}

// GET /api/analytics/omnichannel?from=&to=
export async function getOmnichannel(params?: { from?: string; to?: string }): Promise<OmniChannelResponse> {
  const res = await apiClient.get<OmniChannelResponse>("/analytics/omnichannel", { params });
  return res.data;
}
