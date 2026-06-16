import { useState, useRef, useEffect } from 'react';
interface Props {
  onSend: (content: string) => void;
  disabled?: boolean;
}
export default function MessageInput({ onSend, disabled }: Props) {
  const [text, setText] = useState('');
  const inputRef = useRef<HTMLTextAreaElement>(null);
  useEffect(() => {
    if (!disabled) inputRef.current?.focus();
  }, [disabled]);
  const handleSend = () => {
    const trimmed = text.trim();
    if (!trimmed) return;
    onSend(trimmed);
    setText('');
    inputRef.current?.focus();
  };
  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };
  return (
    <div className="flex items-end gap-2 border-t border-slate-200 bg-white p-4">
      <textarea ref={inputRef} value={text}
        onChange={e => setText(e.target.value)} onKeyDown={handleKeyDown}
        placeholder="Nhập tin nhắn…" disabled={disabled} rows={1}
        className="flex-1 resize-none rounded-xl border border-slate-300 px-3.5 py-2.5 text-sm outline-none focus:border-blue-400 disabled:bg-slate-50"
        style={{ minHeight: 40, maxHeight: 120 }} />
      <button onClick={handleSend}
        disabled={disabled || !text.trim()}
        className="rounded-xl bg-blue-500 px-5 py-2.5 text-sm font-medium text-white hover:bg-blue-600 disabled:opacity-40 shrink-0">Gửi</button>
    </div>
  );
}
