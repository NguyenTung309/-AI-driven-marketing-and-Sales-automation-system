import { apiClient } from "./client";

export type ConversationStatus = "open" | "resolved" | "escalated" | string;

export interface ConversationListItem {
  readonly id: string;
  readonly platform: string;
  readonly externalThreadId: string;
  readonly status: ConversationStatus;
  readonly contactId: string | null;
  readonly contactDisplayName: string | null;
  readonly assignedTo: string | null;
  readonly lastMessageAt: string | null;
  readonly lastMessagePreview: string | null;
  readonly unreadCount: number;
}

export interface ConversationListResponse {
  readonly items: readonly ConversationListItem[];
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
}

export interface InboxMessage {
  readonly id: string;
  readonly direction: string;
  readonly senderType: string;
  readonly senderUserId: string | null;
  readonly content: string;
  readonly contentType: string;
  readonly sentAt: string;
}

export interface ConversationDetail {
  readonly id: string;
  readonly platform: string;
  readonly externalThreadId: string;
  readonly status: ConversationStatus;
  readonly contactId: string | null;
  readonly contactDisplayName: string | null;
  readonly assignedTo: string | null;
  readonly lastMessageAt: string | null;
  readonly createdAt: string;
  readonly messages: readonly InboxMessage[];
}

export interface InboxConversationEvent {
  readonly conversationId: string;
  readonly status: ConversationStatus;
  readonly assignedTo: string | null;
  readonly lastMessageAt: string | null;
}

export interface InboxMessageEvent {
  readonly conversationId: string;
  readonly messageId: string;
  readonly direction: string;
  readonly senderType: string;
  readonly content: string;
  readonly contentType: string;
  readonly sentAt: string;
}

export interface ListConversationsParams {
  readonly status?: string;
  readonly platform?: string;
  readonly page?: number;
  readonly pageSize?: number;
}

export async function listConversations(params?: ListConversationsParams): Promise<ConversationListResponse> {
  const res = await apiClient.get<ConversationListResponse>("/api/inbox/conversations", { params });
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

export async function assignConversation(id: string, userId: string): Promise<void> {
  await apiClient.post(`/api/inbox/conversations/${id}/assign`, { userId });
}

export async function resolveConversation(id: string): Promise<void> {
  await apiClient.post(`/api/inbox/conversations/${id}/resolve`);
}

export async function escalateConversation(id: string): Promise<void> {
  await apiClient.post(`/api/inbox/conversations/${id}/escalate`);
}

export async function sendConversationMessage(id: string, content: string): Promise<InboxMessage> {
  const res = await apiClient.post<InboxMessage>(`/api/inbox/conversations/${id}/messages`, {
    content,
    contentType: "text",
  });
  return res.data;
}
