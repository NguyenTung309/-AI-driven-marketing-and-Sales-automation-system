import { apiClient } from "./client";

export type LeadStage = "cold" | "warm" | "hot" | "customer" | "lost" | string;

export interface LeadListItem {
  readonly id: string;
  readonly contactId: string | null;
  readonly ownerUserId: string | null;
  readonly score: number;
  readonly stage: LeadStage;
  readonly sourcePlatform: string | null;
  readonly lastActivityAt: string | null;
  readonly createdAt: string;
  readonly contactName: string | null;
  readonly contactPhone: string | null;
  readonly ownerDisplayName: string | null;
}

export interface LeadContextContact {
  readonly id: string | null;
  readonly name: string | null;
  readonly phone: string | null;
  readonly email: string | null;
}

export interface LeadContextActivity {
  readonly activityType: string;
  readonly notes: string | null;
  readonly occurredAt: string;
}

export interface LeadContext {
  readonly id: string;
  readonly score: number;
  readonly stage: LeadStage;
  readonly sourcePlatform: string | null;
  readonly lastActivityAt: string | null;
  readonly createdAt: string;
  readonly contact: LeadContextContact | null;
  readonly activities: readonly LeadContextActivity[];
  readonly nextStep: string;
}

export interface LeadActivityPayload {
  readonly eventCode: string;
  readonly platform?: string | null;
  readonly notes?: string | null;
}

export interface LeadActivityResponse {
  readonly newScore: number;
  readonly stage: LeadStage;
  readonly reason: string;
  readonly matchedRules: readonly string[];
}

export interface CreateLeadPayload {
  readonly contactId: string;
  readonly sourcePlatform: string;
  readonly phone?: string | null;
  readonly email?: string | null;
}

export interface CreateLeadResponse {
  readonly leadId: string;
  readonly duplicates: readonly {
    readonly leadId: string;
    readonly contactId: string;
    readonly reason: string;
    readonly confidence: number;
  }[];
}

export interface LeadForecastPoint {
  readonly date: string;
  readonly predicted_leads: number;
  readonly lower_bound: number;
  readonly upper_bound: number;
}

export interface LeadForecastResponse {
  readonly forecast: readonly LeadForecastPoint[];
  readonly note?: string;
}

export interface ListLeadsParams {
  readonly stage?: string;
  readonly q?: string;
  readonly source?: string;
  readonly owner?: "assigned" | "unassigned" | string;
  readonly page?: number;
  readonly pageSize?: number;
}

export interface LeadListResponse {
  readonly items: readonly LeadListItem[];
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
}

export async function listLeads(params?: ListLeadsParams): Promise<LeadListResponse> {
  const res = await apiClient.get<LeadListResponse>("/api/leads", { params });
  return res.data;
}

export async function getLead(id: string): Promise<LeadListItem> {
  const res = await apiClient.get<LeadListItem>(`/api/leads/${id}`);
  return res.data;
}

export async function getLeadContext(id: string): Promise<LeadContext> {
  const res = await apiClient.get<LeadContext>(`/api/leads/${id}/context`);
  return res.data;
}

export async function createLead(payload: CreateLeadPayload): Promise<CreateLeadResponse> {
  const res = await apiClient.post<CreateLeadResponse>("/api/leads", payload);
  return res.data;
}

export async function assignLead(id: string, userId: string | null = null): Promise<void> {
  await apiClient.post(`/api/leads/${id}/assign`, { userId });
}

export async function recordLeadActivity(id: string, payload: LeadActivityPayload): Promise<LeadActivityResponse> {
  const res = await apiClient.post<LeadActivityResponse>(`/api/leads/${id}/activities`, payload);
  return res.data;
}

export async function getLeadForecast(horizonDays = 7): Promise<LeadForecastResponse> {
  const res = await apiClient.get<LeadForecastResponse>("/api/leads/forecast", { params: { horizonDays } });
  return res.data;
}
