import { useState } from "react";
import { apiClient, loadPermissions } from "@/shared/api/client";
import { useAuthStore } from "@/shared/auth/authStore";

type Step = "credentials" | "twoFactor";

export default function LoginPage() {
  const setAuth = useAuthStore((s) => s.setAuth);
  const [step, setStep] = useState<Step>("credentials");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);

  // SPEC-11: token in-memory + permissions from /auth/me, then enter the app.
  async function finishLogin(accessToken: string) {
    setAuth(accessToken);
    await loadPermissions();
    window.location.href = "/";
  }

  async function onSubmitCredentials(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      const res = await apiClient.post("/auth/login", { email, password });
      // 202 → the account has 2FA enabled; collect the authenticator code next.
      if (res.status === 202 && res.data?.requiresTwoFactor) {
        setStep("twoFactor");
        return;
      }
      await finishLogin(res.data.accessToken as string);
    } catch (err) {
      setError(loginError(err));
    }
  }

  async function onSubmitTwoFactor(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      const res = await apiClient.post("/auth/login/2fa", { email, password, code });
      await finishLogin(res.data.accessToken as string);
    } catch {
      setError("Mã xác thực không đúng.");
    }
  }

  if (step === "twoFactor") {
    return (
      <form onSubmit={onSubmitTwoFactor} className="mx-auto max-w-sm p-6 space-y-4">
        <h1 className="text-2xl font-semibold">Xác thực 2 lớp</h1>
        <p className="text-sm text-gray-600">Nhập mã 6 số từ ứng dụng authenticator.</p>
        <input
          className="w-full border p-2 rounded tracking-widest text-center"
          value={code}
          onChange={(e) => setCode(e.target.value)}
          placeholder="000000"
          inputMode="numeric"
          autoFocus
          required
        />
        {error && <p className="text-red-600 text-sm">{error}</p>}
        <button className="w-full bg-black text-white p-2 rounded">Verify</button>
      </form>
    );
  }

  return (
    <form onSubmit={onSubmitCredentials} className="mx-auto max-w-sm p-6 space-y-4">
      <h1 className="text-2xl font-semibold">ClawBot</h1>
      <input
        className="w-full border p-2 rounded"
        value={email}
        onChange={(e) => setEmail(e.target.value)}
        placeholder="Email"
        type="email"
        required
      />
      <input
        className="w-full border p-2 rounded"
        type="password"
        value={password}
        onChange={(e) => setPassword(e.target.value)}
        placeholder="Password"
        required
      />
      {error && <p className="text-red-600 text-sm">{error}</p>}
      <button className="w-full bg-black text-white p-2 rounded">Sign in</button>
    </form>
  );
}

function loginError(err: unknown): string {
  const status =
    typeof err === "object" && err !== null && "response" in err
      ? (err as { response?: { status?: number } }).response?.status
      : undefined;
  if (status === 423) return "Tài khoản đã bị khóa. Vui lòng thử lại sau 30 phút.";
  return "Login failed. Check credentials.";
}
