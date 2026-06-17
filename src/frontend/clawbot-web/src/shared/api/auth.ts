import { apiClient } from "./client";

// /auth/me — JWT claims only (no name/email/phone).
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

// POST /auth/reset/request → 200 (always; token currently logged server-side, email delivery is backend TODO).
export async function requestPasswordReset(email: string): Promise<void> {
  await apiClient.post("/auth/reset/request", { email });
}

// POST /auth/reset/confirm → 200 | 400. `token` = Identity reset token (NOT a 6-digit OTP).
export async function confirmPasswordReset(email: string, token: string, newPassword: string): Promise<void> {
  await apiClient.post("/auth/reset/confirm", { email, token, newPassword });
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

// POST /auth/logout ? 204 (revokes refresh cookie server-side).
export async function logout(): Promise<void> {
  await apiClient.post("/auth/logout");
}
