import { useState, useEffect, useRef } from 'react';
import { useInbox, useConversation, useSendMessage } from './useInbox';
import ConversationList from './ConversationList';
import ChatPane from './ChatPane';
import MessageInput from './MessageInput';
export default function ConversationsPage() {
  const [selectedId, setSelectedId] = useState<string | null>(null);
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
  const handleSend = (content: string) => {
    if (!selectedId) return;
    sendMutation.mutate(content);
  };
  return (
    <div className="flex h-[calc(100vh-3.5rem)] bg-white">
      <div className="w-80 shrink-0 flex flex-col border-r border-slate-200">
        <div className="px-4 py-3 border-b border-slate-200">
          <h1 className="text-lg font-semibold text-slate-800">Inbox</h1>
          {inbox && <p className="text-xs text-slate-400 mt-0.5">{inbox.total} hội thoại</p>}
        </div>
        {isLoading ? (
          <div className="flex-1 flex items-center justify-center text-sm text-slate-400">Đang tải…</div>
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
                <span className="font-medium text-sm text-slate-800">{detail.contactDisplayName ?? "Unknown"}</span>
                <span className="text-xs text-slate-400 ml-2">{detail.platform}</span>
              </div>
              <span className={'ml-auto text-xs px-2 py-0.5 rounded-full ' + (detail.status === 'open' ? 'bg-green-100 text-green-700' : 'bg-slate-100 text-slate-500')}>
                {detail.status === 'open' ? 'Đang mở' : 'Đóng'}
              </span>
            </div>
            <ChatPane messages={detail.messages} contactName={detail.contactDisplayName} />
            <div ref={chatEndRef} />
            <MessageInput onSend={handleSend} disabled={sendMutation.isPending} />
          </>
        ) : (
          <div className="flex items-center justify-center h-full text-sm text-slate-400">{selectedId ? "Đang tải…" : "Chọn hội thoại"}</div>
        )}
      </div>
    </div>
  );
}
