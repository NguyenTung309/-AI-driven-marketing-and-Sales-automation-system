import { useState } from "react";
import { useNavigate, Link } from "react-router-dom";
import { isAxiosError } from "axios";
import { apiClient } from "@/shared/api/client";
import { useAuth } from "@/shared/auth/AuthContext";
import { Alert } from "@/shared/ui";

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

// Left brand panel — red gradient + abstract Zalo OA → AI Agent → CRM flow.
function BrandPanel() {
  return (
    <section className="hidden md:flex w-1/2 relative bg-primary overflow-hidden flex-col">
      <div className="absolute inset-0 bg-gradient-to-br from-primary via-primary-container to-[#800000] opacity-90 z-0" />
      <div className="absolute top-0 left-0 w-full flex items-center px-8 py-6 z-10">
        <span className="text-headline-md font-bold text-on-primary tracking-tight">Học Bá Education</span>
      </div>

      <div className="relative z-10 flex flex-col justify-center items-start h-full px-20 max-w-3xl">
        <h1 className="text-on-primary text-headline-lg mb-4">
          Hệ Thống Trí Tuệ Nhân Tạo Sale &amp; Marketing
        </h1>
        <p className="text-on-primary/80 text-body-lg mb-12">
          Quản trị tiến trình tự động hóa, tối ưu chuyển đổi và nâng tầm trải nghiệm học viên thông qua
          các tác vụ thông minh.
        </p>

        <div className="w-full relative mt-8 flex items-center justify-between [filter:drop-shadow(0_0_8px_rgba(255,255,255,0.5))]">
          {FLOW.map((node, i) => (
            <div key={node.label} className="flex items-center flex-1 last:flex-none">
              <div
                className={`flex flex-col items-center gap-2 rounded-xl border backdrop-blur-sm ${
                  node.emphasis
                    ? "bg-white/20 border-white/40 p-6 scale-110"
                    : "bg-white/10 border-white/20 p-4"
                }`}
              >
                <span
                  className={`material-symbols-outlined text-on-primary ${node.emphasis ? "text-4xl" : "text-3xl"}`}
                  style={node.emphasis ? { fontVariationSettings: "'FILL' 1" } : undefined}
                >
                  {node.icon}
                </span>
                <span className="text-on-primary text-label-lg">{node.label}</span>
              </div>
              {i < FLOW.length - 1 ? (
                <div className="flex-grow mx-4 border-t border-dashed border-white/40" />
              ) : null}
            </div>
          ))}
        </div>
      </div>

      <div className="absolute bottom-0 left-0 w-full flex items-center px-8 py-4 z-10 text-on-primary/60">
        <span className="text-label-sm">© 2024 Học Bá Education. All rights reserved.</span>
      </div>
    </section>
  );
}

export default function LoginPage() {
  const { setToken } = useAuth();
  const navigate = useNavigate();
  const [identity, setIdentity] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      const res = await apiClient.post("/auth/login", { email: identity, password });
      setToken(res.data.accessToken as string);
      navigate("/", { replace: true });
    } catch (err: unknown) {
      if (isAxiosError(err) && err.response?.status === 423) {
        setError("Tài khoản của bạn đã bị khóa. Vui lòng liên hệ Quản trị viên cấp cao.");
      } else {
        setError("Đăng nhập thất bại. Kiểm tra lại tài khoản hoặc mật khẩu.");
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className="flex h-screen w-full overflow-hidden">
      <BrandPanel />

      <section className="w-full md:w-1/2 bg-surface-container-low flex flex-col items-center justify-center p-6 md:p-12 relative">
        <div className="md:hidden absolute top-8 left-8 text-headline-md font-bold text-primary">
          Học Bá Education
        </div>

        <div className="w-full max-w-[460px] bg-surface-container-lowest p-10 rounded-[12px] shadow-[0px_1px_3px_rgba(0,0,0,0.05),0px_1px_2px_rgba(0,0,0,0.03)] border border-outline-variant/30">
          {error ? (
            <div className="mb-6">
              <Alert tone="error" icon="warning">{error}</Alert>
            </div>
          ) : null}
          <header className="mb-8">
            <h2 className="text-secondary text-headline-md mb-2">Đăng nhập hệ thống</h2>
            <p className="text-on-surface-variant text-body-md">
              Vui lòng nhập thông tin tài khoản Học Bá Admin để tiếp tục.
            </p>
          </header>

          <form className="space-y-6" onSubmit={onSubmit}>
            <div className="space-y-2">
              <label className="block text-label-lg text-on-surface" htmlFor="identity">
                Tên đăng nhập hoặc Email
              </label>
              <div className="relative flex items-center">
                <span className="material-symbols-outlined absolute left-4 text-on-surface-variant text-[20px]">
                  mail
                </span>
                <input
                  id="identity"
                  name="identity"
                  type="text"
                  required
                  value={identity}
                  onChange={(e) => setIdentity(e.target.value)}
                  placeholder="admin@hoc-ba.edu.vn"
                  className="w-full pl-12 pr-4 py-3 bg-white border border-outline rounded-lg text-body-md outline-none transition-all focus:ring-2 focus:ring-primary/10 focus:border-primary"
                />
              </div>
            </div>

            <div className="space-y-2">
              <label className="block text-label-lg text-on-surface" htmlFor="password">
                Mật khẩu
              </label>
              <div className="relative flex items-center">
                <span className="material-symbols-outlined absolute left-4 text-on-surface-variant text-[20px]">
                  lock
                </span>
                <input
                  id="password"
                  name="password"
                  type={showPassword ? "text" : "password"}
                  required
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="••••••••"
                  className="w-full pl-12 pr-12 py-3 bg-white border border-outline rounded-lg text-body-md outline-none transition-all focus:ring-2 focus:ring-primary/10 focus:border-primary"
                />
                <button
                  type="button"
                  onClick={() => setShowPassword((v) => !v)}
                  aria-label={showPassword ? "Ẩn mật khẩu" : "Hiện mật khẩu"}
                  className="absolute right-4 text-on-surface-variant hover:text-primary transition-colors"
                >
                  <span className="material-symbols-outlined text-[20px]">
                    {showPassword ? "visibility_off" : "visibility"}
                  </span>
                </button>
              </div>
            </div>

            <div className="flex items-center justify-between text-label-lg">
              <label className="flex items-center gap-2 cursor-pointer group">
                <input type="checkbox" className="w-4 h-4 rounded border-outline text-primary focus:ring-primary" />
                <span className="text-on-surface-variant group-hover:text-on-surface transition-colors">
                  Ghi nhớ đăng nhập
                </span>
              </label>
              <Link to="/forgot-password" className="text-primary hover:underline transition-all">
                Quên mật khẩu?
              </Link>
            </div>

            <button
              type="submit"
              disabled={loading}
              className="w-full py-4 bg-primary hover:bg-primary-hover text-on-primary font-bold text-label-lg rounded-lg shadow-lg shadow-primary/20 transition-all active:scale-[0.98] tracking-wider disabled:opacity-60 disabled:pointer-events-none"
            >
              {loading ? "ĐANG XỬ LÝ..." : "ĐĂNG NHẬP"}
            </button>
          </form>

          <div className="mt-8 pt-6 border-t border-outline-variant/30 text-center">
            <p className="text-on-surface-variant text-label-sm">
              Gặp sự cố truy cập?{" "}
              <a href="#" className="text-primary hover:underline font-semibold">
                Liên hệ IT Support
              </a>
            </p>
          </div>
        </div>

        <div className="absolute bottom-8 right-8 flex items-center gap-2 text-on-surface-variant/40 select-none">
          <span className="material-symbols-outlined">verified_user</span>
          <span className="text-label-sm font-medium tracking-widest">SECURE ADMIN GATEWAY</span>
        </div>
      </section>
    </main>
  );
}
