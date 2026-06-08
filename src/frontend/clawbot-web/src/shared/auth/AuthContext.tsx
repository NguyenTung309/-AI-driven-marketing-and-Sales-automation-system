import { useEffect, type ReactNode } from "react";
import { loadPermissions, refreshAccessToken } from "@/shared/api/client";
import { useAuthStore } from "@/shared/auth/authStore";

/**
 * SPEC-11: on app start / F5, hydrate the access token from the refresh cookie ONCE before
 * rendering protected routes. A 401 here (first-time visitor / no cookie) is a normal "anon"
 * state — not an error — so we fall through to /login silently.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const status = useAuthStore((s) => s.status);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const token = await refreshAccessToken();
      if (cancelled) return;
      if (token) await loadPermissions();
      else useAuthStore.getState().setStatus("anon");
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  if (status === "loading") {
    return <div className="flex min-h-screen items-center justify-center text-gray-500">Loading…</div>;
  }

  return <>{children}</>;
}
