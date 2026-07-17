import { apiClient } from "./client";
export interface InboxChannel {
  readonly id: string;
  readonly name: string;
  readonly platform: string;
  readonly externalPageId: string;
  readonly isActive: boolean;
  readonly avatarUrl: string | null;
  readonly hasToken: boolean;
  readonly unreadCount: number;
  readonly memberDisplayName: string | null;
}


export interface DailySummary {
  readonly conversationsHandled: number;
  readonly messagesSent: number;
  readonly openConversations: number;
  readonly closeRate: number;
  readonly date: string;
}

export async function getDailySummary(): Promise<DailySummary> {
  const res = await apiClient.get<DailySummary>("/api/inbox/daily-summary");
  return res.data;
}

export async function listChannels(): Promise<readonly InboxChannel[]> {
  const res = await apiClient.get<readonly InboxChannel[]>("/api/inbox/channels");
  return res.data;
}

export type ConversationStatus = "open" | "resolved" | "escalated" | string;

export interface ConversationListItem {
  readonly id: string;
  readonly platform: string;
  readonly externalThreadId: string;
  readonly status: ConversationStatus;
  readonly contactId: string | null;
  readonly contactDisplayName: string | null;
  readonly contactAvatarUrl: string | null;
  readonly inboxId: string | null;
  readonly inboxName: string | null;
  readonly inboxAvatarUrl: string | null;
  readonly assignedTo: string | null;
  readonly lastMessageAt: string | null;
  readonly lastMessagePreview: string | null;
  readonly rowVersion: string | null;
  readonly unreadCount: number;
  readonly aiAutoReplyEnabled: boolean;
}

export interface ConversationListResponse {
  readonly items: readonly ConversationListItem[];
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
}

/** Cursor feed (default list sort last_message_at). */
export interface ConversationCursorPage {
  readonly items: readonly ConversationListItem[];
  readonly nextCursor: string | null;
  readonly total: number | null;
}

export type InboxMessageStatus =
  | "sent"
  | "pending_send"
  | "send_failed"
  | "pending_approval"
  | "blocked"
  | string;

export interface InboxMessage {
  readonly id: string;
  readonly direction: string;
  readonly senderType: string;
  readonly senderUserId: string | null;
  readonly content: string;
  readonly contentType: string;
  readonly sentAt: string;
  readonly senderDisplayName: string | null;
  readonly senderAvatarUrl: string | null;
  readonly attachmentUrl: string | null;
  readonly status?: InboxMessageStatus;
}

export interface ConversationDetail {
  readonly id: string;
  readonly platform: string;
  readonly externalThreadId: string;
  readonly status: ConversationStatus;
  readonly contactId: string | null;
  readonly contactDisplayName: string | null;
  readonly contactAvatarUrl: string | null;
  readonly inboxId: string | null;
  readonly inboxName: string | null;
  readonly inboxAvatarUrl: string | null;
  readonly assignedTo: string | null;
  readonly lastMessageAt: string | null;
  readonly createdAt: string;
  readonly rowVersion: string | null;
  readonly messages: readonly InboxMessage[];
  readonly aiAutoReplyEnabled: boolean;
}

export interface InboxConversationEvent {
  readonly conversationId: string;
  readonly status: ConversationStatus;
  readonly assignedTo: string | null;
  readonly lastMessageAt: string | null;
  readonly inboxId?: string | null;
}

export interface InboxMessageStatusEvent {
  readonly conversationId: string;
  readonly messageId: string;
  readonly status: InboxMessageStatus;
  readonly assignedTo?: string | null;
  readonly inboxId?: string | null;
}

export interface InboxMessageEvent {
  readonly conversationId: string;
  readonly messageId: string;
  readonly direction: string;
  readonly senderType: string;
  readonly content: string;
  readonly contentType: string;
  readonly sentAt: string;
  readonly senderDisplayName?: string | null;
  readonly senderAvatarUrl?: string | null;
  readonly attachmentUrl?: string | null;
  readonly inboxId?: string | null;
  readonly isSynthetic?: boolean;
}

export interface ListConversationsParams {
  readonly inboxId?: string;
  readonly status?: string;
  readonly platform?: string;
  readonly q?: string;
  readonly assignedTo?: string;
  /** Default keyset; pass "lead_score" for offset score sort. */
  readonly sort?: "lead_score" | string;
  readonly cursor?: string | null;
  readonly page?: number;
  readonly pageSize?: number;
}

export async function listConversations(
  params?: ListConversationsParams,
): Promise<ConversationCursorPage | ConversationListResponse> {
  const res = await apiClient.get<ConversationCursorPage | ConversationListResponse>(
    "/api/inbox/conversations",
    { params },
  );
  return res.data;
}

export async function searchConversations(
  q: string,
  params?: Omit<ListConversationsParams, "q">,
): Promise<ConversationListResponse> {
  const res = await apiClient.get<ConversationListResponse>("/api/inbox/search", {
    params: { ...params, q },
  });
  return res.data;
}

export async function getConversation(id: string): Promise<ConversationDetail> {
  const res = await apiClient.get<ConversationDetail>(`/api/inbox/conversations/${id}`);
  return res.data;
}

export async function assignConversation(id: string, userId: string, rowVersion?: string | null): Promise<void> {
  const headers = rowVersion ? { "If-Match": rowVersion } : undefined;
  await apiClient.post(`/api/inbox/conversations/${id}/assign`, { userId }, { headers });
}

export async function resolveConversation(id: string, rowVersion?: string | null): Promise<void> {
  const headers = rowVersion ? { "If-Match": rowVersion } : undefined;
  await apiClient.post(`/api/inbox/conversations/${id}/resolve`, null, { headers });
}

export async function escalateConversation(id: string, rowVersion?: string | null): Promise<void> {
  const headers = rowVersion ? { "If-Match": rowVersion } : undefined;
  await apiClient.post(`/api/inbox/conversations/${id}/escalate`, null, { headers });
}

export async function setConversationAi(id: string, enabled: boolean): Promise<void> {
  await apiClient.post(`/api/inbox/conversations/${id}/ai`, { enabled });
}

export async function sendConversationMessage(id: string, content: string): Promise<InboxMessage> {
  const res = await apiClient.post<InboxMessage>(`/api/inbox/conversations/${id}/messages`, {
    content,
    contentType: "text",
  });
  return res.data;
}

export async function retryConversationMessage(
  conversationId: string,
  messageId: string,
): Promise<InboxMessage> {
  const res = await apiClient.post<InboxMessage>(
    `/api/inbox/conversations/${conversationId}/messages/${messageId}/retry`,
  );
  return res.data;
}

// Kích AI soạn lại phản hồi cho tin khách đang chờ (vd sau khi từ chối draft bị chặn).
// 202 = đang soạn; 409 = không có tin khách treo / AI đang tắt / có draft chờ duyệt.
export async function regenerateAiReply(conversationId: string): Promise<void> {
  await apiClient.post(`/api/inbox/conversations/${conversationId}/ai/regenerate`);
}

// Review-gate P3: duyệt AI draft đang hold — gửi thật qua kênh + chuyển status sent.
export async function approveConversationDraft(conversationId: string, messageId: string): Promise<InboxMessage> {
  const res = await apiClient.post<InboxMessage>(
    `/api/inbox/conversations/${conversationId}/drafts/${messageId}/approve`,
  );
  return res.data;
}

// Từ chối draft — chuyển status blocked, không bao giờ gửi.
export async function rejectConversationDraft(conversationId: string, messageId: string): Promise<InboxMessage> {
  const res = await apiClient.post<InboxMessage>(
    `/api/inbox/conversations/${conversationId}/drafts/${messageId}/reject`,
  );
  return res.data;
}




