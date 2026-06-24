import type { MessageDto } from './types';
interface Props {
  messages: MessageDto[];
  contactName: string | null;
  contactAvatarUrl?: string | null;
}
function formatTime(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleTimeString('vi', { hour: '2-digit', minute: '2-digit' });
}
export default function ChatPane({ messages, contactName, contactAvatarUrl }: Props) {
  return (
    <div className="flex-1 flex flex-col gap-2 p-4 overflow-y-auto">
      {messages.length === 0 && (
        <div className="flex items-center justify-center h-full text-sm text-slate-400">Chưa có tin nhắn</div>
      )}
      {messages.map(msg => {
        const isIn = msg.direction === 'in';
        return (
          <div key={msg.id} className={'flex ' + (isIn ? 'justify-start' : 'justify-end')}>
            <div className="max-w-[75%] flex flex-col">
              {isIn && msg.senderType !== 'contact' && (
                <span className="text-[10px] text-slate-400 mb-0.5 ml-1">{contactName ?? "Hệ thống"}</span>
              )}
              <div className={'rounded-2xl px-3.5 py-2 text-sm ' + (isIn ? 'bg-slate-100 text-slate-900 rounded-bl-sm' : 'bg-blue-500 text-white rounded-br-sm')}>
                <p className="whitespace-pre-wrap break-words">{msg.content}</p>
              </div>
              <span className={'text-[10px] text-slate-400 mt-0.5 ' + (isIn ? 'ml-1' : 'mr-1 text-right')}>
                {formatTime(msg.sentAt)}
              </span>
            </div>
          </div>
        );
      })}
    </div>
  );
}
