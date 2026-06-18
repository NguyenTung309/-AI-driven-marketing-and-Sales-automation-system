import { useState } from "react";
import { useNavigate, Link } from "react-router-dom";
import { isAxiosError } from "axios";
import { apiClient, loadPermissions } from "@/shared/api/client";
import { useAuthStore } from "@/shared/auth/authStore";
import { Alert } from "@/shared/ui";

type Step = "credentials" | "twoFactor";

interface FlowNode {
  readonly icon: string;
  readonly label: string;
  readonly emphasis?: boolean;
}

const FLOW: readonly FlowNode[] = [
  { icon: "forum", label: "Zalo OA" },
  { icon: "smart_toy", label: "AI Agent", emphasis: true },
  { icon: "hub", label: "CRM" },
];

function BrandPanel() {
  return (
    <section className="hidden md:flex w-1/2 relative bg-primary overflow-hidden flex-col">
      <div className="absolute inset-0 bg-gradient-to-br from-primary via-primary-container to-[#800000] opacity-90 z-0" />
      <div className="absolute top-0 left-0 w-full flex items-center px-8 py-6 z-10">
        <span className="text-headline-md font-bold text-on-primary tracking-tight">Học Bá AI</span>
      </div>

      <div className="relative z-10 flex flex-col justify-center items-start h-full px-20 max-w-3xl">
        <h1 className="text-on-primary text-headline-lg mb-4">
          Hệ thống AI tư vấn &amp; marketing
        </h1>
        <p className="text-on-primary/80 text-body-lg mb-12">
          Quản trị tiến trình tự động hóa, tối ưu chuyển đổi và nâng tầm trải nghiệm học viên thông qua
          các tác vụ thông minh.
        </p>

        <div className="w-full relative mt-8 flex items-center justify-between [filter:drop-shadow(0_0_8px_rgba(255,255,255,0.5))]">
          {FLOW.map((node, i) => (
            <div key={node.label} className="flex items-center flex-1 last:flex-none">
              <div
                className="flex flex-col items-center gap-2 rounded-xl backdrop-blur-sm"
              >
                <span aria-hidden="true"
                  className="material-symbols-outlined text-on-primary"
                  style={node.emphasis ? { fontVariationSettings: "'FILL' 1" } : undefined}
                >
                  {node.icon}
                </span>
                <span className="text-on-primary text-label-lg">{node.label}</span>
              </div>
              {i < FLOW.length - 1 ? <div className="flex-grow mx-4 border-t border-dashed border-white/40" /> : null}
            </div>
          ))}
        </div>
      </div>

      <div className="absolute bottom-0 left-0 w-full flex items-center px-8 py-4 z-10 text-on-primary/60">
        <span className="text-label-sm">&copy; 2024 Học Bá AI. Bảo lưu mọi quyền.</span>
      </div>
    </section>
  );
}

const fieldInput =
  "w-full pl-12 pr-4 py-3 bg-white border border-outline rounded-lg text-body-md outline-none transition-all focus:ring-2 focus:ring-primary/10 focus:border-primary";

const submitBtn =
  "w-full py-4 bg-primary hover:bg-primary-hover text-on-primary font-bold text-label-lg rounded-lg shadow-lg shadow-primary/20 transition-all active:scale-[0.98] tracking-wider disabled:opacity-60 disabled:pointer-events-none";

export default function LoginPage() {
  const setAuth = useAuthStore((s) => s.setAuth);
  const navigate = useNavigate();
  const [step, setStep] = useState<Step>("credentials");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  function onError(err: unknown) {
    if (isAxiosError(err) && err.response?.status === 423) {
      setError("Tài khoản của bạn đã bị khóa. Vui lòng liên hệ Quản trị viên cấp cao.");
    } else {
      setError("Đăng nhập thất bại. Kiểm tra lại tài khoản hoặc mật khẩu.");
    }
  }

  // SPEC-11: token in-memory + permissions from /auth/me, then enter the app.
  async function finishLogin(accessToken: string) {
    setAuth(accessToken);
    await loadPermissions();
    navigate("/", { replace: true });
  }

  async function onSubmitCredentials(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      const res = await apiClient.post("/auth/login", { email, password });
      // 202 => the account has 2FA enabled; collect the authenticator code next.
      if (res.status === 202 && res.data?.requiresTwoFactor) {
        setStep("twoFactor");
        return;
      }
      await finishLogin(res.data.accessToken as string);
    } catch (err) {
      onError(err);
    } finally {
      setLoading(false);
    }
  }

  async function onSubmitTwoFactor(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      const res = await apiClient.post("/auth/login/2fa", { email, password, code });
      await finishLogin(res.data.accessToken as string);
    } catch {
      setError("Mã xác thực không đúng.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className="flex h-screen w-full overflow-hidden">
      <BrandPanel />

      <section className="w-full md:w-1/2 bg-surface-container-low flex flex-col items-center justify-center p-6 md:p-12 relative">
        <div className="md:hidden absolute top-8 left-8 text-headline-md font-bold text-primary">Học Bá AI</div>

        <div className="w-full max-w-[460px] bg-surface-container-lowest p-10 rounded-[12px] shadow-[0px_1px_3px_rgba(0,0,0,0.05),0px_1px_2px_rgba(0,0,0,0.03)] border border-outline-variant/30">
          {error ? (
            <div className="mb-6">
              <Alert tone="error" icon="warning">{error}</Alert>
            </div>
          ) : null}

          {step === "twoFactor" ? (
            <form className="space-y-6" onSubmit={onSubmitTwoFactor}>
              <header className="mb-2">
                <h2 className="text-secondary text-headline-md mb-2">Xác thực 2 bước</h2>
                <p className="text-on-surface-variant text-body-md">
                  Nhập mã 6 chữ số từ ứng dụng xác thực của bạn.
                </p>
              </header>
              <div className="relative flex items-center">
                <span aria-hidden="true" className="material-symbols-outlined absolute left-4 text-on-surface-variant text-[20px]">shield</span>
                <input
                  type="text"
                  inputMode="numeric"
                  maxLength={6}
                  required
                  value={code}
                  onChange={(e) => setCode(e.target.value.replace(/\D/g, ""))}
                  placeholder="000000"
                  className={`${fieldInput} tracking-[0.5em] text-center`}
                />
              </div>
              <button type="submit" disabled={loading} className={submitBtn}>
                {loading ? "ĐANG XÁC THỰC..." : "XÁC NHẬN"}
              </button>
              <button
                type="button"
                onClick={() => {
                  setStep("credentials");
                  setCode("");
                  setError(null);
                }}
                className="w-full text-center text-on-surface-variant hover:text-primary text-body-md transition-colors"
              >
                &larr; Quay lại
              </button>
            </form>
          ) : (
            <>
              <header className="mb-8">
                <h2 className="text-secondary text-headline-md mb-2">Đăng nhập hệ thống</h2>
                <p className="text-on-surface-variant text-body-md">
                  Vui lòng nhập thông tin tài khoản quản trị Học Bá để tiếp tục.
                </p>
              </header>

              <form className="space-y-6" onSubmit={onSubmitCredentials}>
                <div className="space-y-2">
                  <label className="block text-label-lg text-on-surface" htmlFor="email">Email</label>
                  <div className="relative flex items-center">
                    <span aria-hidden="true" className="material-symbols-outlined absolute left-4 text-on-surface-variant text-[20px]">mail</span>
                    <input
                      id="email"
                      name="email"
                      type="email"
                      required
                      value={email}
                      onChange={(e) => setEmail(e.target.value)}
                      placeholder="admin@hoc-ba.edu.vn"
                      className={fieldInput}
                    />
                  </div>
                </div>

                <div className="space-y-2">
                  <label className="block text-label-lg text-on-surface" htmlFor="password">Mật khẩu</label>
                  <div className="relative flex items-center">
                    <span aria-hidden="true" className="material-symbols-outlined absolute left-4 text-on-surface-variant text-[20px]">lock</span>
                    <input
                      id="password"
                      name="password"
                      type={showPassword ? "text" : "password"}
                      required
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
                      placeholder="&bull;&bull;&bull;&bull;&bull;&bull;&bull;&bull;"
                      className={`${fieldInput} pr-12`}
                    />
                    <button
                      type="button"
                      onClick={() => setShowPassword((v) => !v)}
                      aria-label={showPassword ? "Ẩn mật khẩu" : "Hiện mật khẩu"}
                      className="absolute right-4 text-on-surface-variant hover:text-primary transition-colors"
                    >
                      <span aria-hidden="true" className="material-symbols-outlined text-[20px]">{showPassword ? "visibility_off" : "visibility"}</span>
                    </button>
                  </div>
                </div>

                <div className="flex items-center justify-between text-label-lg">
                  <label className="flex items-center gap-2 cursor-pointer group">
                    <input type="checkbox" className="w-4 h-4 rounded border-outline text-primary focus:ring-primary" />
                    <span className="text-on-surface-variant group-hover:text-on-surface transition-colors">Ghi nhớ đăng nhập</span>
                  </label>
                  <Link to="/forgot-password" className="text-primary hover:underline transition-all">Quên mật khẩu?</Link>
                </div>

                <button type="submit" disabled={loading} className={submitBtn}>
                  {loading ? "ĐANG XỬ LÝ..." : "ĐĂNG NHẬP"}
                </button>
              </form>
            </>
          )}

          <div className="mt-8 pt-6 border-t border-outline-variant/30 text-center">
            <p className="text-on-surface-variant text-label-sm">
              Gặp sự cố truy cập?{" "}
              <a href="#" className="text-primary hover:underline font-semibold">Liên hệ hỗ trợ kỹ thuật</a>
            </p>
          </div>
        </div>

        <div className="absolute bottom-8 right-8 flex items-center gap-2 text-on-surface-variant/40 select-none">
          <span aria-hidden="true" className="material-symbols-outlined">verified_user</span>
          <span className="text-label-sm font-medium tracking-widest">CỔNG QUẢN TRỊ AN TOÀN</span>
        </div>
      </section>
    </main>
  );
}
