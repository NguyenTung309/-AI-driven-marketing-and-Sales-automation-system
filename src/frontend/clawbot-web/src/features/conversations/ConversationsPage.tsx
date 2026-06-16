import { useState, useEffect, useRef } from 'react';
import { useInbox, useConversation, useSendMessage, useSuggestedReply } from './useInbox';
import ConversationList from './ConversationList';
import ChatPane from './ChatPane';
import MessageInput from './MessageInput';
import SuggestedReply from './SuggestedReply';
export default function ConversationsPage() {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [draftText, setDraftText] = useState<string>('');
  const { data: inbox, isLoading } = useInbox();
  const { data: detail } = useConversation(selectedId);
  const sendMutation = useSendMessage(selectedId ?? '');
  const chatEndRef = useRef<HTMLDivElement>(null);
  const conversations = inbox?.items ?? [];
  useEffect(() => {
    if (!selectedId && conversations.length > 0) setSelectedId(conversations[0].id);
  }, [selectedId, conversations]);
  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [detail?.messages.length]);

  const lastCustomerMsg = detail?.messages?.reduceRight<string | null>((acc, m) => {
    if (acc !== null) return acc;
    if (m.direction === 'in' && m.senderType === 'contact') return m.content;
    return null;
  }, null) ?? null;

  const { data: suggestedDraft, refetch: refetchDraft } = useSuggestedReply(
    selectedId,
    lastCustomerMsg ?? '',
  );

  useEffect(() => {
    if (typeof suggestedDraft === 'string') setDraftText(suggestedDraft);
  }, [suggestedDraft]);

  const handleApplyDraft = (text: string) => {
    handleSend(text);
    setDraftText('');
  };

  const handleRefreshDraft = () => {
    refetchDraft();
  };

  const handleSend = (content: string) => {
    if (!selectedId) return;
    sendMutation.mutate(content);
    setDraftText('');
  };

  const hasDraft = draftText.length > 0 && selectedId !== null;

  const totalText = inbox?.total ?? 0;

  return (
    <div className="flex h-[calc(100vh-3.5rem)] bg-white">
      <div className="w-80 shrink-0 flex flex-col border-r border-slate-200">
        <div className="px-4 py-3 border-b border-slate-200">
          <h1 className="text-lg font-semibold text-slate-800">Inbox</h1>
          {inbox && <p className="text-xs text-slate-400 mt-0.5">{totalText} h\\u1ed9i tho\\u1ea1i</p>}
        </div>
        {isLoading ? (
          <div className="flex-1 flex items-center justify-center text-sm text-slate-400">\\u0110ang t\\u1ea3i...</div>
        ) : (
          <ConversationList conversations={conversations} selectedId={selectedId ?? undefined} onSelect={setSelectedId} />
        )}
      </div>
      <div className="flex-1 flex flex-col">
        {selectedId && detail ? (
          <>
            <div className="px-5 py-3 border-b border-slate-200 flex items-center gap-3">
              <div className="w-8 h-8 rounded-full bg-blue-100 flex items-center justify-center text-xs font-medium text-blue-600">
                {detail.contactDisplayName?.charAt(0).toUpperCase() ?? '?'}
              </div>
              <div>
                <span className="font-medium text-sm text-slate-800">{detail.contactDisplayName ?? 'Unknown'}</span>
                <span className="text-xs text-slate-400 ml-2">{detail.platform}</span>
              </div>
              <span className={'ml-auto text-xs px-2 py-0.5 rounded-full ' + (detail.status === 'open' ? 'bg-green-100 text-green-700' : 'bg-slate-100 text-slate-500')}>
                {detail.status === 'open' ? '\\u0110ang m\\u1edf' : '\\u0110\\u00f3ng'}
              </span>
            </div>
            <ChatPane messages={detail.messages} contactName={detail.contactDisplayName} />
            <div ref={chatEndRef} />
            {hasDraft && (
              <SuggestedReply draft={draftText} onApply={handleApplyDraft} onRefresh={handleRefreshDraft} />
            )}
            <MessageInput onSend={handleSend} disabled={sendMutation.isPending} />
          </>
        ) : (
          <div className="flex items-center justify-center h-full text-sm text-slate-400">{selectedId ? '\\u0110ang t\\u1ea3i...' : 'Ch\\u1ecdn h\\u1ed9i tho\\u1ea1i'}</div>
        )}
      </div>
    </div>
  );
}
