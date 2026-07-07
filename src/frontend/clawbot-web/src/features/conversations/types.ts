export interface ConversationItem {
  id: string;
  platform: string;
  externalThreadId: string;
  status: string;
  contactId: string | null;
  contactDisplayName: string | null;
  assignedTo: string | null;
  lastMessageAt: string | null;
  lastMessagePreview: string | null;
  unreadCount: number;
}

export interface ConversationListResponse {
  items: ConversationItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface MessageDto {
  id: string;
  direction: 'in' | 'out';
  senderType: string;
  senderUserId: string | null;
  content: string;
  contentType: string;
  sentAt: string;
  senderDisplayName?: string | null;
  senderAvatarUrl?: string | null;
}

export interface ConversationDetail {
  id: string;
  platform: string;
  externalThreadId: string;
  status: string;
  contactId: string | null;
  contactDisplayName: string | null;
  assignedTo: string | null;
  lastMessageAt: string | null;
  createdAt: string;
  messages: MessageDto[];
}
