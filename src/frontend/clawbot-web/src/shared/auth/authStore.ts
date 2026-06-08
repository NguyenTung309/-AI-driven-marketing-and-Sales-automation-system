import { create } from "zustand";

// SPEC-11 (Frontend): access token lives in-memory only — never localStorage/sessionStorage.
// On F5 the token is gone from RAM and is re-hydrated via POST /auth/refresh (httpOnly cookie).
export type AuthStatus = "loading" | "authed" | "anon";

interface AuthState {
  accessToken: string | null;
  permissions: string[];
  status: AuthStatus;
  /** Set after a successful login / refresh. */
  setAuth: (token: string, permissions?: string[]) => void;
  setPermissions: (permissions: string[]) => void;
  setStatus: (status: AuthStatus) => void;
  /** Clear on logout / failed refresh. */
  clear: () => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  accessToken: null,
  permissions: [],
  status: "loading",
  setAuth: (accessToken, permissions) =>
    set((s) => ({
      accessToken,
      status: "authed",
      permissions: permissions ?? s.permissions,
    })),
  setPermissions: (permissions) => set({ permissions }),
  setStatus: (status) => set({ status }),
  clear: () => set({ accessToken: null, permissions: [], status: "anon" }),
}));

/** Permission check for gating UI (backend remains source of truth). */
export const hasPermission = (code: string) =>
  useAuthStore.getState().permissions.includes(code);

// Backward-compatible hook for token/status/permission consumers (e.g. route guards).
export const useAuth = () => {
  const token = useAuthStore((s) => s.accessToken);
  const status = useAuthStore((s) => s.status);
  const permissions = useAuthStore((s) => s.permissions);
  return { token, status, permissions };
};
