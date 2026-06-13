import { useMemo, useState } from "react";
import { Modal, Alert, Button } from "@/shared/ui";

export interface ChangePasswordDialogProps {
  readonly open: boolean;
  readonly onClose: () => void;
}

interface Strength {
  readonly score: number; // 0..3
  readonly label: string;
  readonly color: string;
}

function scorePassword(pw: string): Strength {
  let s = 0;
  if (pw.length >= 8) s += 1;
  if (/[A-Z]/.test(pw) && /[a-z]/.test(pw)) s += 1;
  if (/\d/.test(pw) && /[^A-Za-z0-9]/.test(pw)) s += 1;
  const labels = ["Yếu", "Trung bình", "Khá", "Mạnh"] as const;
  const colors = ["bg-error", "bg-warning", "bg-warning", "bg-success"] as const;
  return { score: s, label: labels[s], color: colors[s] };
}

function PasswordField({
  label,
  value,
  onChange,
  placeholder,
}: {
  readonly label: string;
  readonly value: string;
  readonly onChange: (v: string) => void;
  readonly placeholder: string;
}) {
  const [show, setShow] = useState(false);
  return (
    <div className="flex flex-col gap-1">
      <label className="text-label-lg text-on-surface">{label}</label>
      <div className="relative">
        <input
          type={show ? "text" : "password"}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder}
          className="w-full px-4 py-3 border border-outline-variant rounded-lg text-body-lg focus:outline-none focus:border-primary focus:ring-1 focus:ring-primary/20 transition-all"
        />
        <button
          type="button"
          onClick={() => setShow((v) => !v)}
          aria-label={show ? "Ẩn mật khẩu" : "Hiện mật khẩu"}
          className="absolute right-3 top-1/2 -translate-y-1/2 material-symbols-outlined text-on-surface-variant cursor-pointer hover:text-primary"
        >
          {show ? "visibility_off" : "visibility"}
        </button>
      </div>
    </div>
  );
}

export function ChangePasswordDialog({ open, onClose }: ChangePasswordDialogProps) {
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [confirm, setConfirm] = useState("");
  const strength = useMemo(() => scorePassword(next), [next]);

  function reset() {
    setCurrent("");
    setNext("");
    setConfirm("");
  }

  function close() {
    reset();
    onClose();
  }

  return (
    <Modal
      open={open}
      onClose={close}
      title="Thay đổi mật khẩu"
      footer={
        <>
          <Button variant="outline" onClick={close}>HỦY BỎ</Button>
          <Button onClick={close}>CẬP NHẬT MẬT KHẨU</Button>
        </>
      }
    >
      <div className="space-y-4">
        <PasswordField label="Mật khẩu hiện tại" value={current} onChange={setCurrent} placeholder="Nhập mật khẩu hiện tại" />
        <div className="flex flex-col gap-1">
          <PasswordField label="Mật khẩu mới" value={next} onChange={setNext} placeholder="Nhập mật khẩu mới" />
          {next ? (
            <div className="flex items-center gap-2 mt-1">
              <div className="flex gap-1">
                {[0, 1, 2].map((i) => (
                  <div key={i} className={`h-1 w-8 rounded-full ${i < strength.score ? strength.color : "bg-surface-variant"}`} />
                ))}
              </div>
              <span className="text-label-sm font-bold text-on-surface-variant">{strength.label}</span>
            </div>
          ) : null}
        </div>
        <PasswordField label="Xác nhận mật khẩu mới" value={confirm} onChange={setConfirm} placeholder="Nhập lại mật khẩu mới" />
      </div>

      <Alert tone="error" icon="info">
        Mật khẩu mới phải chứa ít nhất 8 ký tự, bao gồm cả chữ và số. Bạn sẽ bị đăng xuất khỏi tất cả các thiết bị khác
        sau khi đổi mật khẩu thành công.
      </Alert>
    </Modal>
  );
}
