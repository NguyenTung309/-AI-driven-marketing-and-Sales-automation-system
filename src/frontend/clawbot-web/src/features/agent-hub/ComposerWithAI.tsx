import { useEffect, useRef, useState } from "react";
import { apiClient } from "@/shared/api/client";
import { checkTone } from "./toneWarning";

interface Props {
  readonly conversationId: string | null;
  readonly onSend: (content: string) => void;
  readonly disabled: boolean;
}

export default function ComposerWithAI({ conversationId, onSend, disabled }: Props) {
  const [text, setText] = useState("");
  const [ghost, setGhost] = useState("");
  const [toneWarning, setToneWarning] = useState<string | null>(null);
  const draftVersionRef = useRef(0);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

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
        // ignore
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

    const toneResult = checkTone(text.trim());
    if (toneResult.hasIssue && toneWarning === null) {
      setToneWarning(toneResult.message);
      return;
    }
    setToneWarning(null);

    onSend(text.trim());
    setText("");
    setGhost("");
  }

  return (
    <form onSubmit={handleSubmit} className="flex items-end gap-2 border-t border-outline bg-surface-container-lowest p-3">
      <div className="relative flex-1">
        {toneWarning && (
          <div className="flex items-center gap-1 mb-1 px-1">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="shrink-0 text-warning">
              <path d="M12 9v4M12 17h.01" />
            </svg>
            <p className="text-label-sm text-warning flex-1">{toneWarning}</p>
            <button type="button" onClick={() => setToneWarning(null)} className="text-label-xs text-primary hover:underline shrink-0">
              Bo qua
            </button>
          </div>
        )}
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
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
          <path d="M22 2 11 13M22 2l-7 20-4-9-9-4 20-7z" />
        </svg>
      </button>
    </form>
  );
}
