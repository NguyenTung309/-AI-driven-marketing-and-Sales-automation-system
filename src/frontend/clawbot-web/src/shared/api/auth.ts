import { apiClient } from "./client";

export interface MeResponse {
  readonly sub: string | null;
  readonly tenantId: string | null;
  readonly tenantSlug: string | null;
  readonly roles: readonly string[];
  readonly permissions: readonly string[];
}

export type LoginOutcome =
  | { readonly kind: "ok"; readonly accessToken: string; readonly expiresAt: string }
  | { readonly kind: "twoFactor" };

export interface TwoFactorEnableResponse {
  readonly sharedKey: string;
  readonly authenticatorUri: string;
}

// POST /auth/login → 200 {accessToken,expiresAt} | 202 {requiresTwoFactor} | 401 | 423 locked (caller catches).
export async function login(email: string, password: string): Promise<LoginOutcome> {
  const res = await apiClient.post("/auth/login", { email, password });
  if (res.status === 202 || res.data?.requiresTwoFactor === true) return { kind: "twoFactor" };
  return { kind: "ok", accessToken: res.data.accessToken as string, expiresAt: res.data.expiresAt as string };
}

// POST /auth/login/2fa → {accessToken,expiresAt}
export async function loginTwoFactor(email: string, password: string, code: string): Promise<string> {
  const res = await apiClient.post("/auth/login/2fa", { email, password, code });
  return res.data.accessToken as string;
}

// POST /auth/reset/request → 200 (always; sends/logs a 6-digit OTP, anti-enumeration).
export async function requestPasswordReset(email: string): Promise<void> {
  await apiClient.post("/auth/reset/request", { email });
}

// POST /auth/reset/confirm → 200 | 400. API field remains `token`; value is the 6-digit OTP from email.
export async function confirmPasswordReset(email: string, otp: string, newPassword: string): Promise<void> {
  await apiClient.post("/auth/reset/confirm", { email, token: otp, newPassword });
}

export async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  await apiClient.post("/auth/change-password", { currentPassword, newPassword });
}

export async function logout(): Promise<void> {
  await apiClient.post("/auth/logout");
}

// GET /auth/me (auth)
export async function getMe(): Promise<MeResponse> {
  const res = await apiClient.get<MeResponse>("/auth/me");
  return res.data;
}

// POST /auth/2fa/enable (auth) → authenticator key + otpauth URI (then verify a code to activate).
export async function enableTwoFactor(): Promise<TwoFactorEnableResponse> {
  const res = await apiClient.post<TwoFactorEnableResponse>("/auth/2fa/enable");
  return res.data;
}

// POST /auth/2fa/verify (auth) → 200 | 400
export async function verifyTwoFactor(code: string): Promise<void> {
  await apiClient.post("/auth/2fa/verify", { code });
}

// POST /auth/2fa/disable (auth)
export async function disableTwoFactor(): Promise<void> {
  await apiClient.post("/auth/2fa/disable");
}
