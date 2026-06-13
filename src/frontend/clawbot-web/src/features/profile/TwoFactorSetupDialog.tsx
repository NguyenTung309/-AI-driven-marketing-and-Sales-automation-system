import { useEffect, useState } from "react";
import { Modal, Alert, Button } from "@/shared/ui";
import { enableTwoFactor, verifyTwoFactor } from "@/shared/api/auth";

export interface TwoFactorSetupDialogProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly onVerified: () => void;
}

// POST /auth/2fa/enable (get key) → user adds to authenticator → POST /auth/2fa/verify (activate).
export function TwoFactorSetupDialog({ open, onClose, onVerified }: TwoFactorSetupDialogProps) {
  const [sharedKey, setSharedKey] = useState("");
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!open) return;
    let active = true;
    // Async setState only (component is remounted via `key` on open, so initial state is already clean).
    enableTwoFactor()
      .then((r) => {
        if (active) setSharedKey(r.sharedKey);
      })
      .catch(() => {
        if (active) setError("Không thể khởi tạo 2FA. Vui lòng thử lại.");
      });
    return () => {
      active = false;
    };
  }, [open]);

  async function verify() {
    setError(null);
    setLoading(true);
    try {
      await verifyTwoFactor(code);
      onVerified();
      onClose();
    } catch {
      setError("Mã không đúng. Vui lòng thử lại.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Bật xác thực 2 yếu tố"
      footer={
        <>
          <Button variant="outline" onClick={onClose}>HỦY</Button>
          <Button onClick={verify} disabled={loading || code.length < 6}>XÁC NHẬN</Button>
        </>
      }
    >
      <p className="text-body-md text-on-surface-variant">
        Thêm khóa bên dưới vào ứng dụng Authenticator (Google Authenticator, Authy…), rồi nhập mã 6 số để kích hoạt.
      </p>
      <div className="flex flex-col gap-1">
        <label className="text-label-lg text-on-surface">Khóa thủ công</label>
        <code className="block px-4 py-3 bg-surface-container-low border border-outline rounded-lg font-mono text-body-md break-all">
          {sharedKey || "Đang tạo khóa…"}
        </code>
      </div>
      <div className="flex flex-col gap-1">
        <label className="text-label-lg text-on-surface">Mã xác thực</label>
        <input
          value={code}
          onChange={(e) => setCode(e.target.value.replace(/\D/g, "").slice(0, 6))}
          inputMode="numeric"
          maxLength={6}
          placeholder="000000"
          className="w-full px-4 py-3 border border-outline-variant rounded-lg text-body-lg text-center tracking-[0.4em] focus:outline-none focus:border-primary focus:ring-1 focus:ring-primary/20"
        />
      </div>
      {error ? <Alert tone="error">{error}</Alert> : null}
    </Modal>
  );
}
