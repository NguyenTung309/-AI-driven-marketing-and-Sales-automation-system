import { useId, useMemo, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { changePassword } from "@/shared/api/auth";
import { Alert, Button, Modal } from "@/shared/ui";
import { toUserFriendlyError } from "@/shared/utils/userText";

export interface ChangePasswordDialogProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly onChanged?: () => void;
}

interface Strength {
  readonly score: number;
  readonly label: string;
  readonly color: string;
}

function scorePassword(password: string): Strength {
  let score = 0;
  if (password.length >= 8) score += 1;
  if (/[A-Z]/.test(password) && /[a-z]/.test(password)) score += 1;
  if (/\d/.test(password) && /[^A-Za-z0-9]/.test(password)) score += 1;
  const labels = ["Yếu", "Trung bình", "Khá", "Mạnh"] as const;
  const colors = ["bg-error", "bg-warning", "bg-warning", "bg-success"] as const;
  return { score, label: labels[score], color: colors[score] };
}

function errorMessage(error: unknown): string {
  return toUserFriendlyError(error, "Không đổi được mật khẩu. Vui lòng thử lại.");
}

function PasswordField({
  label,
  value,
  onChange,
  placeholder,
  autoComplete,
}: {
  readonly label: string;
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly placeholder: string;
  readonly autoComplete: string;
}) {
  const [show, setShow] = useState(false);
  const id = useId();
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} className="text-label-lg text-on-surface">{label}</label>
      <div className="relative">
        <input
          id={id}
          type={show ? "text" : "password"}
          value={value}
          onChange={(event) => onChange(event.target.value)}
          placeholder={placeholder}
          autoComplete={autoComplete}
          className="w-full rounded-lg border border-outline-variant px-4 py-3 text-body-lg transition-all focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary/20"
        />
        <button
          type="button"
          onClick={() => setShow((current) => !current)}
          aria-label={show ? "Ẩn mật khẩu" : "Hiện mật khẩu"}
          className="absolute right-3 top-1/2 -translate-y-1/2 cursor-pointer text-on-surface-variant hover:text-primary"
        >
          <span aria-hidden="true" className="material-symbols-outlined">{show ? "visibility_off" : "visibility"}</span>
        </button>
      </div>
    </div>
  );
}

export function ChangePasswordDialog({ open, onClose, onChanged }: ChangePasswordDialogProps) {
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [confirm, setConfirm] = useState("");
  const [localError, setLocalError] = useState<string | null>(null);
  const strength = useMemo(() => scorePassword(next), [next]);

  const mutation = useMutation({
    mutationFn: () => changePassword(current, next),
    onSuccess: () => {
      reset();
      onChanged?.();
      onClose();
    },
  });

  function reset() {
    setCurrent("");
    setNext("");
    setConfirm("");
    setLocalError(null);
  }

  function close() {
    reset();
    onClose();
  }

  function submit() {
    setLocalError(null);
    if (next.length < 8) {
      setLocalError("Mật khẩu mới phải có ít nhất 8 ký tự.");
      return;
    }
    if (next !== confirm) {
      setLocalError("Xác nhận mật khẩu mới không khớp.");
      return;
    }
    mutation.mutate();
  }

  return (
    <Modal
      open={open}
      onClose={close}
      title="Thay đổi mật khẩu"
      footer={
        <>
          <Button type="button" variant="outline" onClick={close} disabled={mutation.isPending}>
            HỦY BỎ
          </Button>
          <Button type="submit" form="change-password-form" disabled={mutation.isPending || !current || !next || !confirm}>
            {mutation.isPending ? "ĐANG CẬP NHẬT..." : "CẬP NHẬT MẬT KHẨU"}
          </Button>
        </>
      }
    >
      <form
        id="change-password-form"
        className="space-y-4"
        onSubmit={(event) => {
          event.preventDefault();
          submit();
        }}
      >
        {(localError || mutation.error) ? <Alert tone="error">{localError ?? errorMessage(mutation.error)}</Alert> : null}
        <PasswordField
          label="Mật khẩu hiện tại"
          value={current}
          onChange={setCurrent}
          placeholder="Nhập mật khẩu hiện tại"
          autoComplete="current-password"
        />
        <div className="flex flex-col gap-1">
          <PasswordField
            label="Mật khẩu mới"
            value={next}
            onChange={setNext}
            placeholder="Nhập mật khẩu mới"
            autoComplete="new-password"
          />
          {next ? (
            <div className="mt-1 flex items-center gap-2">
              <div className="flex gap-1">
                {[0, 1, 2].map((item) => (
                  <div key={item} className={`h-1 w-8 rounded-full ${item < strength.score ? strength.color : "bg-surface-variant"}`} />
                ))}
              </div>
              <span className="text-label-sm font-bold text-on-surface-variant">{strength.label}</span>
            </div>
          ) : null}
        </div>
        <PasswordField
          label="Xác nhận mật khẩu mới"
          value={confirm}
          onChange={setConfirm}
          placeholder="Nhập lại mật khẩu mới"
          autoComplete="new-password"
        />
        <Alert tone="warning" icon="info">
          Mật khẩu mới phải chứa ít nhất 8 ký tự, bao gồm cả chữ và số. Bạn sẽ cần đăng nhập lại trên các thiết bị khác sau khi đổi mật khẩu.
        </Alert>
      </form>
    </Modal>
  );
}
