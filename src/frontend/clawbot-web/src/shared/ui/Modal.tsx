import { useEffect, useRef, type PointerEvent, type ReactNode } from "react";

export interface ModalProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly title: string;
  readonly children: ReactNode;
  readonly footer?: ReactNode;
  /** Tailwind max-width class for the dialog (default narrow). Use e.g. "max-w-3xl" for editors. */
  readonly maxWidthClass?: string;
}

// Level 2 surface: centered dialog over a dimmed, blurred backdrop. Closes on Esc / backdrop click.
export function Modal({ open, onClose, title, children, footer, maxWidthClass = "max-w-md" }: ModalProps) {
  const didPointerStartOnBackdrop = useRef(false);

  function handleBackdropPointerDown(e: PointerEvent<HTMLDivElement>) {
    didPointerStartOnBackdrop.current = e.target === e.currentTarget;
  }

  function handleBackdropPointerUp(e: PointerEvent<HTMLDivElement>) {
    if (didPointerStartOnBackdrop.current && e.target === e.currentTarget) onClose();
    didPointerStartOnBackdrop.current = false;
  }

  useEffect(() => {
    if (!open) return;
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 z-[100] flex items-center justify-center bg-black/50 backdrop-blur-sm p-4"
      onPointerDown={handleBackdropPointerDown}
      onPointerUp={handleBackdropPointerUp}
      role="presentation"
    >
      <div
        className={`bg-surface-container-lowest w-full ${maxWidthClass} rounded-xl shadow-2xl overflow-hidden`}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between px-6 py-4 border-b border-outline-variant">
          <h3 className="text-headline-sm font-bold text-on-surface">{title}</h3>
          <button
            type="button"
            onClick={onClose}
            aria-label="Đóng"
            className="text-on-surface-variant hover:bg-surface-variant p-2 rounded-full transition-colors"
          >
            <span aria-hidden="true" className="material-symbols-outlined">close</span>
          </button>
        </div>
        <div className="p-6 space-y-6">{children}</div>
        {footer ? <div className="px-6 py-4 bg-surface-container-low flex justify-end gap-4">{footer}</div> : null}
      </div>
    </div>
  );
}
