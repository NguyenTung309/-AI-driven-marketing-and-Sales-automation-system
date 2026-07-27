import { useLayoutEffect, useRef, type PointerEvent, type ReactNode } from "react";
import {
  canRestoreFocus,
  containTabFocus,
  focusInitialElement,
  isTopmostDialog,
  registerActiveDialog,
} from "./modalFocus";

export interface ModalProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly title: string;
  readonly children: ReactNode;
  readonly footer?: ReactNode;
  readonly dismissible?: boolean;
  /** Tailwind max-width class for the dialog (default narrow). Use e.g. "max-w-3xl" for editors. */
  readonly maxWidthClass?: string;
}

// Level 2 surface: centered dialog over a dimmed, blurred backdrop. Closes on Esc / backdrop click.
export function Modal({
  open,
  onClose,
  title,
  children,
  footer,
  dismissible = true,
  maxWidthClass = "max-w-md",
}: ModalProps) {
  const didPointerStartOnBackdrop = useRef(false);
  const dialogRef = useRef<HTMLDivElement>(null);
  const dismissibleRef = useRef(dismissible);
  const onCloseRef = useRef(onClose);

  useLayoutEffect(() => {
    onCloseRef.current = onClose;
  }, [onClose]);

  useLayoutEffect(() => {
    dismissibleRef.current = dismissible;
  }, [dismissible]);

  function handleBackdropPointerDown(event: PointerEvent<HTMLDivElement>) {
    didPointerStartOnBackdrop.current = dismissibleRef.current
      && isTopmostDialog(dialogRef.current)
      && event.target === event.currentTarget;
  }

  function handleBackdropPointerUp(event: PointerEvent<HTMLDivElement>) {
    if (
      dismissibleRef.current
      && didPointerStartOnBackdrop.current
      && isTopmostDialog(dialogRef.current)
      && event.target === event.currentTarget
    ) {
      onCloseRef.current();
    }
    didPointerStartOnBackdrop.current = false;
  }

  useLayoutEffect(() => {
    if (!open) return;

    const mountedDialog = dialogRef.current;
    if (!mountedDialog) return;
    const activeDialog: HTMLElement = mountedDialog;

    const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const unregisterDialog = registerActiveDialog(activeDialog);

    function onKeyDown(event: KeyboardEvent) {
      if (!isTopmostDialog(activeDialog)) return;
      if (event.key === "Escape") {
        event.preventDefault();
        event.stopPropagation();
        if (dismissibleRef.current) onCloseRef.current();
        return;
      }
      containTabFocus(event, activeDialog);
    }

    document.addEventListener("keydown", onKeyDown);
    focusInitialElement(activeDialog);

    return () => {
      document.removeEventListener("keydown", onKeyDown);
      unregisterDialog();
      if (opener?.isConnected && canRestoreFocus(opener)) opener.focus();
    };
  }, [open]);

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 z-[100] flex items-center justify-center bg-black/50 backdrop-blur-sm p-4"
      onPointerDown={handleBackdropPointerDown}
      onPointerUp={handleBackdropPointerUp}
      onPointerCancel={() => { didPointerStartOnBackdrop.current = false; }}
      role="presentation"
    >
      <div
        ref={dialogRef}
        className={`bg-surface-container-lowest w-full ${maxWidthClass} flex max-h-[85vh] flex-col overflow-hidden rounded-xl shadow-2xl`}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        tabIndex={-1}
        onClick={(event) => event.stopPropagation()}
      >
        <div className="flex shrink-0 items-center justify-between px-6 py-4 border-b border-outline-variant">
          <h3 className="text-headline-sm font-bold text-on-surface">{title}</h3>
          <button
            type="button"
            onClick={() => { if (dismissibleRef.current) onCloseRef.current(); }}
            disabled={!dismissible}
            aria-label="Đóng"
            className="text-on-surface-variant hover:bg-surface-variant p-2 rounded-full transition-colors disabled:cursor-not-allowed disabled:opacity-40"
          >
            <span aria-hidden="true" className="material-symbols-outlined">close</span>
          </button>
        </div>
        <div className="flex-1 overflow-y-auto p-6 space-y-6">{children}</div>
        {footer ? <div className="shrink-0 px-6 py-4 bg-surface-container-low flex justify-end gap-4">{footer}</div> : null}
      </div>
    </div>
  );
}
