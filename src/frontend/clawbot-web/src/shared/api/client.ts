import axios, { type AxiosRequestConfig, type InternalAxiosRequestConfig } from "axios";
import { useAuthStore } from "@/shared/auth/authStore";

export const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? "",
  withCredentials: true,
});

// Bare client (no response interceptor) for the refresh call itself, so a 401 on /auth/refresh
// cannot recurse back into the refresh logic. Same baseURL => identical URL resolution as login.
const refreshClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? "",
  withCredentials: true,
});

// SPEC-11: attach the in-memory access token to every request.
apiClient.interceptors.request.use((config) => {
  const token = useAuthStore.getState().accessToken;
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

// SPEC-11: single-flight refresh -- concurrent 401s in THIS tab share one /auth/refresh promise
// (cross-tab races are absorbed by the server grace-window, D10).
let refreshPromise: Promise<string | null> | null = null;

export function refreshAccessToken(): Promise<string | null> {
  if (!refreshPromise) {
    refreshPromise = refreshClient
      .post("/auth/refresh")
      .then((res) => {
        const token = res.data.accessToken as string;
        useAuthStore.getState().setAuth(token);
        return token;
      })
      .catch(() => {
        useAuthStore.getState().clear();
        return null;
      })
      .finally(() => {
        refreshPromise = null;
      });
  }
  return refreshPromise;
}

export async function getRealtimeAccessToken(): Promise<string> {
  return useAuthStore.getState().accessToken ?? (await refreshAccessToken()) ?? "";
}

// SPEC-11: fetch the user's permissions (for UI gating) after a token is in RAM.
export async function loadPermissions(): Promise<void> {
  try {
    const res = await apiClient.get("/auth/me");
    useAuthStore.getState().setPermissions((res.data.permissions as string[]) ?? []);
  } catch {
    // Non-fatal: UI gating degrades to hidden; backend still enforces.
  }
}

// SPEC-11: on 401 from an expired access token, refresh once (single-flight) and retry the
// original request; if refresh also fails, clear store + bounce to /login.
apiClient.interceptors.response.use(
  (res) => res,
  async (error) => {
    const original = error.config as (AxiosRequestConfig & { _retried?: boolean }) | undefined;
    const status = error.response?.status;

    if (status === 401 && original && !original._retried) {
      original._retried = true;
      const token = await refreshAccessToken();
      if (token) {
        original.headers = { ...original.headers, Authorization: `Bearer ${token}` };
        return apiClient(original as InternalAxiosRequestConfig);
      }
      if (window.location.pathname !== "/login") window.location.href = "/login";
    }

    return Promise.reject(error);
  }
);
