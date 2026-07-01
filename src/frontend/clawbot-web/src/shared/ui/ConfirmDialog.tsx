import { Modal } from "./Modal";
import { Button } from "./Button";

export interface ConfirmDialogProps {
  readonly open: boolean;
  readonly title: string;
  readonly message: string;
  readonly confirmLabel?: string;
  readonly cancelLabel?: string;
  readonly danger?: boolean;
  readonly pending?: boolean;
  readonly onConfirm: () => void;
  readonly onCancel: () => void;
}

// Reusable Yes/No gate for destructive actions (delete, disable, reset). Wraps Modal.
export function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel = "Xác nhận",
  cancelLabel = "Hủy",
  danger = true,
  pending = false,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  return (
    <Modal
      open={open}
      onClose={onCancel}
      title={title}
      footer={
        <>
          <Button variant="outline" onClick={onCancel} disabled={pending}>
            {cancelLabel}
          </Button>
          <Button
            onClick={onConfirm}
            disabled={pending}
            className={danger ? "bg-error text-on-primary hover:bg-error/90" : undefined}
          >
            {pending ? "Đang xử lý..." : confirmLabel}
          </Button>
        </>
      }
    >
      <p className="text-body-md text-on-surface-variant">{message}</p>
    </Modal>
  );
}
