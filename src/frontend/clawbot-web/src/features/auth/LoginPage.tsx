import { useState } from "react";
import { apiClient } from "@/shared/api/client";
import { useAuth } from "@/shared/auth/AuthContext";

export default function LoginPage() {
  const { setToken } = useAuth();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    try {
      const res = await apiClient.post("/auth/login", { email, password });
      setToken(res.data.accessToken as string);
      window.location.href = "/";
    } catch {
      setError("Login failed. Check credentials.");
    }
  }

  return (
    <form onSubmit={onSubmit} className="mx-auto max-w-sm p-6 space-y-4">
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
