import { apiClient } from "./client";

export interface UserProfile {
  readonly id: string;
  readonly email: string | null;
  readonly displayName: string;
  readonly phone: string | null;
  readonly dateOfBirth: string | null;
  readonly avatarUrl: string | null;
  readonly isActive: boolean;
  readonly roles: readonly string[];
  readonly tenantSlug: string | null;
}

export interface UpdateProfileRequest {
  readonly displayName?: string | null;
  readonly phone?: string | null;
  readonly dateOfBirth?: string | null;
}

export interface AvatarUploadResponse {
  readonly avatarUrl: string;
}

export interface LoginHistoryItem {
  readonly id: string;
  readonly occurredAt: string;
  readonly ipAddress: string | null;
  readonly userAgent: string | null;
  readonly status: "success" | string;
}

export interface LoginHistoryResponse {
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
  readonly items: readonly LoginHistoryItem[];
}

export async function getProfile(): Promise<UserProfile> {
  const res = await apiClient.get<UserProfile>("/api/profile");
  return res.data;
}

export async function updateProfile(body: UpdateProfileRequest): Promise<Pick<UserProfile, "id" | "displayName" | "phone" | "dateOfBirth">> {
  const res = await apiClient.put<Pick<UserProfile, "id" | "displayName" | "phone" | "dateOfBirth">>("/api/profile", body);
  return res.data;
}

export async function uploadProfileAvatar(file: File): Promise<AvatarUploadResponse> {
  const body = new FormData();
  body.append("file", file);
  const res = await apiClient.post<AvatarUploadResponse>("/api/profile/avatar", body);
  return res.data;
}

export async function listLoginHistory(params?: { readonly page?: number; readonly pageSize?: number }): Promise<LoginHistoryResponse> {
  const res = await apiClient.get<LoginHistoryResponse>("/api/profile/login-history", { params });
  return res.data;
}
