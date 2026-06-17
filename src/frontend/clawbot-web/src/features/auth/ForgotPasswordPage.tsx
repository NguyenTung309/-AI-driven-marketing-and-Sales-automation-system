import { useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { AuthCardShell } from "@/shared/layout/AuthCardShell";
import { requestPasswordReset, confirmPasswordReset } from "@/shared/api/auth";

type Step = "request" | "otp" | "reset" | "success";

const OTP_LENGTH = 6;
const OTP_TTL_SECONDS = 600;

function StepHeader({ icon, title, desc }: { readonly icon: string; readonly title: string; readonly desc: React.ReactNode }) {
  return (
    <header className="mb-8 text-center">
      <div className="flex justify-center mb-4">
        <div className="bg-primary/10 p-4 rounded-full">
          <span className="material-symbols-outlined text-primary text-[40px]">{icon}</span>
        </div>
      </div>
      <h2 className="text-headline-md text-on-surface mb-2">{title}</h2>
      <p className="text-body-md text-on-surface-variant leading-relaxed">{desc}</p>
    </header>
  );
}

const submitBtn =
  "w-full bg-primary hover:bg-primary-hover text-on-primary font-bold py-3.5 px-6 rounded-xl shadow-lg shadow-primary/20 transition-all active:scale-[0.98] flex justify-center items-center gap-2 uppercase tracking-wide disabled:opacity-60 disabled:pointer-events-none";

const backLink =
  "flex items-center justify-center gap-2 text-on-surface-variant hover:text-primary transition-colors text-body-md";

const inputBox =
  "block w-full pl-10 pr-4 py-3 bg-white border border-outline rounded-xl text-body-md focus:ring-2 focus:ring-primary/20 focus:border-primary transition-all outline-none";

function OtpInputs({ value, onChange }: { readonly value: string; readonly onChange: (v: string) => void }) {
  const refs = useRef<Array<HTMLInputElement | null>>([]);

  function setDigit(index: number, digit: string) {
    const clean = digit.replace(/\D/g, "");
    const next = Array.from({ length: OTP_LENGTH }, (_, i) => refs.current[i]?.value ?? value[i] ?? "");
    if (clean.length > 1) {
      clean.slice(0, OTP_LENGTH - index).split("").forEach((char, offset) => {
        next[index + offset] = char;
      });
      onChange(next.join("").slice(0, OTP_LENGTH));
      refs.current[Math.min(index + clean.length, OTP_LENGTH - 1)]?.focus();
      return;
    }
    next[index] = clean.slice(-1);
    onChange(next.join("").slice(0, OTP_LENGTH));
    if (next[index] && index < OTP_LENGTH - 1) refs.current[index + 1]?.focus();
  }

  function onKeyDown(index: number, e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === "Backspace" && !value[index] && index > 0) refs.current[index - 1]?.focus();
  }

  function onPaste(index: number, e: React.ClipboardEvent<HTMLInputElement>) {
    e.preventDefault();
    setDigit(index, e.clipboardData.getData("text"));
  }

  return (
    <div className="flex justify-between w-full max-w-[340px] gap-2 mb-6">
      {Array.from({ length: OTP_LENGTH }).map((_, i) => (
        <input
          key={i}
          ref={(el) => {
            refs.current[i] = el;
          }}
          type="text"
          inputMode="numeric"
          maxLength={1}
          aria-label={`Mã OTP ký tự ${i + 1}`}
          value={value[i] ?? ""}
          onChange={(e) => setDigit(i, e.target.value)}
          onKeyDown={(e) => onKeyDown(i, e)}
          onPaste={(e) => onPaste(i, e)}
          className="w-12 h-12 text-center text-headline-sm font-bold border border-outline rounded-lg focus:ring-2 focus:ring-primary/20 focus:border-primary transition-all outline-none"
        />
      ))}
    </div>
  );
}

function formatTimer(total: number): string {
  const m = Math.floor(total / 60).toString().padStart(2, "0");
  const s = (total % 60).toString().padStart(2, "0");
  return `${m}:${s}`;
}

export default function ForgotPasswordPage() {
  const navigate = useNavigate();
  const [step, setStep] = useState<Step>("request");
  const [email, setEmail] = useState("");
  const [otp, setOtp] = useState("");
  const [seconds, setSeconds] = useState(OTP_TTL_SECONDS);
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [requestPending, setRequestPending] = useState(false);

  useEffect(() => {
    if (step !== "otp" || seconds <= 0) return;
    const id = window.setInterval(() => setSeconds((s) => Math.max(0, s - 1)), 1000);
    return () => window.clearInterval(id);
  }, [step, seconds]);

  useEffect(() => {
    if (step !== "success") return;
    const id = window.setTimeout(() => navigate("/login", { replace: true }), 3000);
    return () => window.clearTimeout(id);
  }, [step, navigate]);

  async function submitRequest(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setRequestPending(true);
    try {
      await requestPasswordReset(email);
      setOtp("");
      setSeconds(OTP_TTL_SECONDS);
      setStep("otp");
    } catch {
      setError("Không gửi được mã xác nhận. Vui lòng kiểm tra kết nối và thử lại.");
    } finally {
      setRequestPending(false);
    }
  }

  function submitOtp(e: React.FormEvent) {
    e.preventDefault();
    if (otp.length < OTP_LENGTH) {
      setError("Vui lòng nhập đủ 6 chữ số.");
      return;
    }
    setError(null);
    setStep("reset");
  }

  async function resendOtp() {
    if (!email || requestPending) return;
    setError(null);
    setRequestPending(true);
    try {
      await requestPasswordReset(email);
      setOtp("");
      setSeconds(OTP_TTL_SECONDS);
    } catch {
      setError("Không gửi lại được mã xác nhận. Vui lòng thử lại.");
    } finally {
      setRequestPending(false);
    }
  }

  async function submitReset(e: React.FormEvent) {
    e.preventDefault();
    if (password.length < 8) {
      setError("Mật khẩu phải dài tối thiểu 8 ký tự.");
      return;
    }
    if (password !== confirm) {
      setError("Mật khẩu xác nhận không khớp.");
      return;
    }
    setError(null);
    try {
      await confirmPasswordReset(email, otp, password);
      setStep("success");
    } catch {
      setError("Mã/token không hợp lệ hoặc đã hết hạn. Vui lòng thử lại.");
    }
  }

  return (
    <AuthCardShell>
      {step === "request" ? (
        <>
          <StepHeader
            icon="lock_reset"
            title="Quên mật khẩu?"
            desc="Đừng lo lắng! Nhập email liên kết với tài khoản Học Bá Admin — hệ thống sẽ gửi mã OTP để đặt lại mật khẩu."
          />
          <form className="space-y-6" onSubmit={submitRequest}>
            <div className="flex flex-col space-y-1">
              <label htmlFor="email" className="text-label-lg text-on-surface">Địa chỉ Email</label>
              <div className="relative flex items-center">
                <span className="material-symbols-outlined absolute left-3 text-on-surface-variant text-[20px]">mail</span>
                <input
                  id="email"
                  type="email"
                  required
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="admin@hoc-ba.edu.vn"
                  className={inputBox}
                />
              </div>
            </div>
            {error ? <p className="text-error text-body-md">{error}</p> : null}
            <div className="pt-4 space-y-4">
              <button type="submit" className={submitBtn} disabled={requestPending}>
                <span>{requestPending ? "Đang gửi mã..." : "Gửi mã xác nhận"}</span>
                <span className="material-symbols-outlined text-[20px]">arrow_forward</span>
              </button>
              <Link to="/login" className={backLink}>
                <span className="material-symbols-outlined text-[18px]">arrow_back</span>
                <span className="hover:underline">Quay lại đăng nhập</span>
              </Link>
            </div>
          </form>
        </>
      ) : null}

      {step === "otp" ? (
        <>
          <StepHeader
            icon="verified_user"
            title="Xác minh tài khoản"
            desc={
              <>
                Nhập mã gồm 6 chữ số vừa gửi đến{" "}
                <span className="font-semibold text-on-surface">{email || "email của bạn"}</span>.
              </>
            }
          />
          <form className="flex flex-col items-center" onSubmit={submitOtp}>
            <OtpInputs value={otp} onChange={setOtp} />
            <div className="flex items-center gap-2 text-warning text-label-lg mb-4">
              <span className="material-symbols-outlined text-[18px]">schedule</span>
              <span>{seconds > 0 ? <>Mã hết hạn sau {formatTimer(seconds)}</> : "Mã đã hết hạn"}</span>
            </div>
            {error ? <p className="text-error text-body-md mb-2">{error}</p> : null}
            <div className="pt-2 space-y-4 w-full">
              <button type="submit" className={submitBtn}>Xác nhận mã</button>
              <p className="text-center text-body-md text-on-surface-variant">
                Chưa nhận được mã?{" "}
                <button
                  type="button"
                  onClick={resendOtp}
                  disabled={requestPending}
                  className="text-primary font-semibold underline underline-offset-4 hover:text-primary-hover ml-1"
                >
                  {requestPending ? "Đang gửi..." : "Gửi lại mã"}
                </button>
              </p>
            </div>
          </form>
        </>
      ) : null}

      {step === "reset" ? (
        <>
          <StepHeader
            icon="lock_reset"
            title="Tạo mật khẩu mới"
            desc="Nhập mật khẩu mới, tối thiểu 8 ký tự gồm chữ và số."
          />
          <form className="space-y-6" onSubmit={submitReset}>
            <div className="flex flex-col space-y-1">
              <label htmlFor="new_password" className="text-label-lg text-on-surface">Mật khẩu mới</label>
              <div className="relative flex items-center">
                <span className="material-symbols-outlined absolute left-3 text-on-surface-variant text-[20px]">lock</span>
                <input
                  id="new_password"
                  type={showPassword ? "text" : "password"}
                  required
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="••••••••"
                  className={`${inputBox} pr-12`}
                />
                <button
                  type="button"
                  onClick={() => setShowPassword((v) => !v)}
                  aria-label={showPassword ? "Ẩn mật khẩu" : "Hiện mật khẩu"}
                  className="absolute right-3 text-on-surface-variant hover:text-primary transition-colors"
                >
                  <span className="material-symbols-outlined text-[20px]">{showPassword ? "visibility_off" : "visibility"}</span>
                </button>
              </div>
            </div>
            <div className="flex flex-col space-y-1">
              <label htmlFor="confirm_password" className="text-label-lg text-on-surface">Xác nhận mật khẩu mới</label>
              <div className="relative flex items-center">
                <span className="material-symbols-outlined absolute left-3 text-on-surface-variant text-[20px]">lock</span>
                <input
                  id="confirm_password"
                  type={showPassword ? "text" : "password"}
                  required
                  value={confirm}
                  onChange={(e) => setConfirm(e.target.value)}
                  placeholder="••••••••"
                  className={inputBox}
                />
              </div>
            </div>
            {error ? <p className="text-error text-body-md">{error}</p> : null}
            <div className="pt-4 space-y-4">
              <button type="submit" className={submitBtn}>
                <span>Cập nhật mật khẩu</span>
                <span className="material-symbols-outlined text-[20px]">check_circle</span>
              </button>
              <Link to="/login" className={backLink}>
                <span className="material-symbols-outlined text-[18px]">arrow_back</span>
                <span className="hover:underline">Quay lại đăng nhập</span>
              </Link>
            </div>
          </form>
        </>
      ) : null}

      {step === "success" ? (
        <div className="flex flex-col items-center py-4">
          <span className="material-symbols-outlined text-[64px] text-success mb-8 [font-variation-settings:'FILL'_1]">check_circle</span>
          <h2 className="text-headline-md text-on-surface text-center mb-2">Đặt lại mật khẩu thành công!</h2>
          <p className="text-body-md text-on-surface-variant text-center leading-relaxed">
            Hệ thống đang tự động quay lại trang Đăng nhập sau 3 giây...
          </p>
          <div className="mt-8">
            <span className="material-symbols-outlined animate-spin text-on-surface-variant text-[24px]">progress_activity</span>
          </div>
        </div>
      ) : null}
    </AuthCardShell>
  );
}
