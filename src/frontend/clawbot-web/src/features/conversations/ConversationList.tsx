import type { ConversationItem } from './types';

interface Props {
  conversations: ConversationItem[];
  selectedId?: string;
  onSelect: (id: string) => void;
}

function formatTime(iso: string | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  const now = new Date();
  const diff = now.getTime() - d.getTime();
  if (diff < 60_000) return 'V\u1eeba xong';
  if (diff < 3_600_000) return Math.floor(diff / 60_000) + 'p';
  if (d.toDateString() === now.toDateString()) return d.toLocaleTimeString('vi', { hour: '2-digit', minute: '2-digit' });
  return d.toLocaleDateString('vi', { day: '2-digit', month: '2-digit' });
}

export default function ConversationList({ conversations, selectedId, onSelect }: Props) {
  return (
    <div className="flex flex-col overflow-y-auto border-r border-slate-200 h-full">
      {conversations.map(conv => (
        <button
          key={conv.id}
          onClick={() => onSelect(conv.id)}
          className={
            'flex items-start gap-3 px-4 py-3 text-left border-b border-slate-100 hover:bg-slate-50 transition-colors' +
            (selectedId === conv.id ? ' bg-blue-50 border-l-2 border-l-blue-500' : '')
          }
        >
          <div className="w-9 h-9 rounded-full bg-slate-200 flex items-center justify-center text-xs font-medium text-slate-600 shrink-0">
            {(conv.contactDisplayName?.charAt(0).toUpperCase() ?? '?')}
          </div>
          <div className="flex-1 min-w-0">
            <div className="flex items-center justify-between gap-2">
              <span className="font-medium text-sm truncate">{conv.contactDisplayName ?? 'Unknown'}</span>
              <span className="text-xs text-slate-400 shrink-0">{formatTime(conv.lastMessageAt)}</span>
            </div>
            <p className="text-xs text-slate-500 truncate mt-0.5">{conv.lastMessagePreview ?? conv.status}</p>
          </div>
          {conv.unreadCount > 0 && (
            <span className="bg-blue-500 text-white text-[10px] font-medium px-1.5 py-0.5 rounded-full shrink-0">
              {conv.unreadCount}
            </span>
          )}
        </button>
      ))}
    </div>
  );
}
