import { apiClient } from "./client";

export interface TenantBranding {
  readonly brandName: string;
  readonly logoUrl: string | null;
  readonly primaryColor: string;
  readonly accentColor: string;
  readonly supportName: string;
  readonly widgetGreeting: string;
}

export interface WidgetBootstrap {
  readonly tenantSlug: string;
  readonly tenantName: string;
  readonly supportName: string;
  readonly online: boolean;
  readonly greeting: string;
  readonly suggestedQuestions: readonly string[];
  readonly branding: TenantBranding;
}

export interface WidgetLeadPayload {
  readonly phone: string;
  readonly displayName?: string | null;
  readonly email?: string | null;
  readonly message?: string | null;
}

export interface WidgetLeadResponse {
  readonly contactId: string;
  readonly leadId: string;
  readonly conversationId: string;
  readonly reply: string;
}

export interface WidgetMessageResponse {
  readonly messageId: string;
  readonly reply: string;
  readonly sentAt: string;
}

export interface PublicFaqItem {
  readonly id: string;
  readonly moduleCode: string;
  readonly moduleName: string;
  readonly question: string;
  readonly answer: string;
}

export interface PublicFaqResponse {
  readonly tenantSlug: string;
  readonly tenantName: string;
  readonly items: readonly PublicFaqItem[];
  readonly branding: TenantBranding;
}

export async function getWidgetBootstrap(tenantSlug: string): Promise<WidgetBootstrap> {
  const res = await apiClient.get<WidgetBootstrap>(`/api/public/widget/${encodeURIComponent(tenantSlug)}/bootstrap`);
  return res.data;
}

export async function getPublicFaq(tenantSlug: string): Promise<PublicFaqResponse> {
  const res = await apiClient.get<PublicFaqResponse>(`/api/public/widget/${encodeURIComponent(tenantSlug)}/faq`);
  return res.data;
}

export async function captureWidgetLead(tenantSlug: string, payload: WidgetLeadPayload): Promise<WidgetLeadResponse> {
  const res = await apiClient.post<WidgetLeadResponse>(`/api/public/widget/${encodeURIComponent(tenantSlug)}/lead`, payload);
  return res.data;
}

export async function sendWidgetMessage(tenantSlug: string, conversationId: string, content: string): Promise<WidgetMessageResponse> {
  const res = await apiClient.post<WidgetMessageResponse>(`/api/public/widget/${encodeURIComponent(tenantSlug)}/messages`, {
    conversationId,
    content,
  });
  return res.data;
}
