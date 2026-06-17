import { apiClient } from "./client";

export interface SaleAssistDraftResponse {
  readonly draftText: string;
  readonly suggestedAction: string;
  readonly leadScoreHint: number;
  readonly latencyMs: number;
}

export interface SaleAssistDraftFeedbackPayload {
  readonly conversationId: string;
  readonly draftText: string;
  readonly finalText?: string | null;
  readonly outcome: "sent" | "edited" | "discarded";
}

export interface SaleAssistDraftFeedbackResponse {
  readonly sessionId: string;
  readonly edited: boolean;
  readonly recordedAt: string;
}

export interface SaleAssistSummaryResponse {
  readonly summary: string;
  readonly latencyMs: number;
}

export interface SaleAssistUpsellResponse {
  readonly eligible: boolean;
  readonly suggestion: string;
  readonly reason: string;
  readonly leadScore: number;
}

export interface SaleAssistDailySummary {
  readonly date: string;
  readonly new_leads: number;
  readonly conversations: number;
  readonly messages_sent: number;
  readonly hot_leads: number;
}

export interface SaleAssistHotLead {
  readonly id: string;
  readonly conversationId: string | null;
  readonly score: number;
  readonly lastActivityAt: string | null;
  readonly contact: {
    readonly name: string | null;
    readonly phone: string | null;
  } | null;
  readonly eligible: boolean;
  readonly suggestion: string;
  readonly reason: string;
}

export interface SaleAssistUpsellSuggestionsResponse {
  readonly hot_leads: readonly SaleAssistHotLead[];
  readonly count: number;
}

export interface QuickReply {
  readonly id: string;
  readonly code: string;
  readonly category: string | null;
  readonly body: string;
  readonly platforms: string | null;
}

export interface CreateQuickReplyPayload {
  readonly code: string;
  readonly body: string;
  readonly category?: string | null;
  readonly platforms?: string | null;
}

export interface UpdateQuickReplyPayload {
  readonly body: string;
  readonly category?: string | null;
  readonly platforms?: string | null;
}

export async function generateSaleAssistDraft(conversationId: string): Promise<SaleAssistDraftResponse> {
  const res = await apiClient.post<SaleAssistDraftResponse>("/api/sale-assist/draft", { conversationId });
  return res.data;
}

export async function recordSaleAssistDraftFeedback(payload: SaleAssistDraftFeedbackPayload): Promise<SaleAssistDraftFeedbackResponse> {
  const res = await apiClient.post<SaleAssistDraftFeedbackResponse>("/api/sale-assist/draft-feedback", payload);
  return res.data;
}

export async function summarizeSaleAssistConversation(conversationId: string): Promise<SaleAssistSummaryResponse> {
  const res = await apiClient.post<SaleAssistSummaryResponse>("/api/sale-assist/summary", { conversationId });
  return res.data;
}

export async function listQuickReplies(): Promise<readonly QuickReply[]> {
  const res = await apiClient.get<readonly QuickReply[]>("/api/sale-assist/quick-replies");
  return res.data;
}

export async function createQuickReply(payload: CreateQuickReplyPayload): Promise<QuickReply> {
  const res = await apiClient.post<QuickReply>("/api/sale-assist/quick-replies", payload);
  const shouldPatchMetadata = Boolean(payload.category?.trim() || payload.platforms?.trim());
  if (!shouldPatchMetadata) return res.data;

  await updateQuickReply(res.data.id, {
    body: payload.body,
    category: payload.category ?? null,
    platforms: payload.platforms ?? null,
  });

  return {
    ...res.data,
    category: payload.category ?? null,
    platforms: payload.platforms ?? null,
  };
}

export async function updateQuickReply(id: string, payload: UpdateQuickReplyPayload): Promise<void> {
  await apiClient.put(`/api/sale-assist/quick-replies/${id}`, payload);
}

export async function deleteQuickReply(id: string): Promise<void> {
  await apiClient.delete(`/api/sale-assist/quick-replies/${id}`);
}

export async function getSaleAssistDailySummary(): Promise<SaleAssistDailySummary> {
  const res = await apiClient.get<SaleAssistDailySummary>("/api/sale-assist/daily-summary");
  return res.data;
}

export async function getSaleAssistUpsell(conversationId: string): Promise<SaleAssistUpsellResponse> {
  const res = await apiClient.get<SaleAssistUpsellResponse>("/api/sale-assist/upsell", { params: { conversationId } });
  return res.data;
}

export async function getSaleAssistUpsellSuggestions(): Promise<SaleAssistUpsellSuggestionsResponse> {
  const res = await apiClient.get<SaleAssistUpsellSuggestionsResponse>("/api/sale-assist/upsell-suggestions");
  return res.data;
}
