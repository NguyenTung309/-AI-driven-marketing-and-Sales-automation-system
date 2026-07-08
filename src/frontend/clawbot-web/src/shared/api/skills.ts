import { apiClient } from "./client";

export interface SkillFile {
  readonly id: string;
  readonly name: string;
  readonly description: string | null;
  readonly sizeBytes: number;
  readonly updatedAt: string;
}

export interface SkillFileDetail extends SkillFile {
  readonly contentMd: string;
}

export interface CreateSkillFilePayload {
  readonly name: string;
  readonly description?: string | null;
  readonly contentMd: string;
}

export async function listSkillFiles(): Promise<readonly SkillFile[]> {
  const res = await apiClient.get<readonly SkillFile[]>("/api/skills");
  return res.data;
}

export async function getSkillFile(id: string): Promise<SkillFileDetail> {
  const res = await apiClient.get<SkillFileDetail>(`/api/skills/${id}`);
  return res.data;
}

export async function createSkillFile(payload: CreateSkillFilePayload): Promise<SkillFile> {
  const res = await apiClient.post<SkillFile>("/api/skills", payload);
  return res.data;
}

export async function deleteSkillFile(id: string): Promise<void> {
  await apiClient.delete(`/api/skills/${id}`);
}
