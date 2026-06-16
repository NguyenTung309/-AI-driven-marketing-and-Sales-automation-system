import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/shared/api/client';
import type { ConversationListResponse, ConversationDetail } from './types';

const BASE = '/inbox/conversations';

export function useInbox() {
  return useQuery<ConversationListResponse>({
    queryKey: ['inbox'],
    queryFn: () => apiClient.get(BASE).then(r => r.data),
    refetchInterval: 30_000,
  });
}

export function useConversation(id: string | null) {
  return useQuery<ConversationDetail>({
    queryKey: ['inbox', id],
    queryFn: () => apiClient.get(BASE + '/' + id).then(r => r.data),
    enabled: id !== null,
    refetchInterval: 10_000,
  });
}

export function useSendMessage(convId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (content: string) =>
      apiClient.post(BASE + '/' + convId + '/messages', { content, contentType: 'text' }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['inbox', convId] });
    },
  });
}

export function useSuggestedReply(convId: string | null, lastMessage: string) {
  return useQuery<string>({
    queryKey: ['suggest', convId, lastMessage],
    queryFn: async () => {
      const res = await apiClient.post('/sale-assist/draft', { conversationId: convId });
      const data = res.data;
      return data.draftText ?? data;
    },
    enabled: convId !== null && lastMessage.length > 0,
    staleTime: 60_000,
  });
}
