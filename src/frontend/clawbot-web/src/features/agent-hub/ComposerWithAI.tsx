import { useEffect, useRef, useState } from "react";
import { apiClient } from "@/shared/api/client";

interface Props {
  readonly conversationId: string | null;
  readonly onSend: (content: string) => void;
  readonly disabled: boolean;
}

export default function ComposerWithAI({ conversationId, onSend, disabled }: Props) {
  const [text, setText] = useState("");
  const [ghost, setGhost] = useState("");
  const draftVersionRef = useRef(0);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  // AI suggest when user types >=3 chars
  useEffect(() => {
    if (!conversationId || text.length < 3 || text.length > 200) {
      setGhost("");
      return;
    }
    const version = ++draftVersionRef.current;
    const timer = setTimeout(async () => {
      try {
        const res = await apiClient.post<{ suggestion: string | null; draftVersion: number }>(
          `/api/inbox/conversations/${conversationId}/copilot/suggest`,
          { currentDraft: text, draftVersion: version }
        );
        if (res.data.draftVersion === version) {
          setGhost(res.data.suggestion || "");
        }
      } catch {
        // ignore network errors for suggestions
      }
    }, 400);
    return () => clearTimeout(timer);
  }, [text, conversationId]);

  function acceptGhost() {
    if (ghost) {
      setText((prev) => prev + ghost);
      setGhost("");
      textareaRef.current?.focus();
    }
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!text.trim() || !conversationId) return;
    onSend(text.trim());
    setText("");
    setGhost("");
  }

  return (
    <form onSubmit={handleSubmit} className="flex items-end gap-2 border-t border-outline bg-surface-container-lowest p-3">
      <div className="relative flex-1">
        <textarea
          ref={textareaRef}
          className="w-full rounded-lg border border-outline bg-white px-3 py-2 text-body-md outline-none resize-none focus:border-primary min-h-[44px] max-h-[120px]"
          rows={1}
          placeholder={conversationId ? "Nhap tin nhan... (Tab de chap nhan goi y)" : "Chon mot cuoc hoi thoai..."}
          value={text}
          disabled={!conversationId || disabled}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Tab" && ghost) {
              e.preventDefault();
              acceptGhost();
            } else if (e.key === "Escape") {
              setGhost("");
            } else if (e.key === "Enter" && !e.shiftKey) {
              e.preventDefault();
              handleSubmit(e);
            }
          }}
        />
        {/* Ghost text overlay */}
        {ghost ? (
          <div className="absolute inset-x-3 top-0 bottom-0 pointer-events-none flex items-start pt-2">
            <span className="text-body-md text-on-surface-variant opacity-60">
              <span className="opacity-0">{text}</span>
              <span className="italic">{ghost}</span>
            </span>
          </div>
        ) : null}
      </div>
      <button
        type="submit"
        disabled={!text.trim() || !conversationId || disabled}
        className="flex size-10 items-center justify-center rounded-full bg-primary text-on-primary disabled:opacity-40"
      >
        <span aria-hidden="true" className="material-symbols-outlined text-[20px]">send</span>
      </button>
    </form>
  );
}