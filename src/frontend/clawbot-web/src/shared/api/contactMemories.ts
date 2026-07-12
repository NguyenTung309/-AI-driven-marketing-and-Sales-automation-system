import { apiClient } from "./client";

// ai-self-learning-memory Lớp 2: facts AI ghi nhớ về khách.
export interface ContactMemoryItem {
  readonly id: string;
  readonly fact: string;
  readonly category: "profile" | "preference" | "commitment" | "history";
  readonly confidence: number;
  readonly updatedAt: string;
}

export async function listContactMemories(contactId: string): Promise<readonly ContactMemoryItem[]> {
  const res = await apiClient.get<readonly ContactMemoryItem[]>(`/api/contacts/${contactId}/memories`);
  return res.data;
}

export async function deleteContactMemory(contactId: string, memoryId: string): Promise<void> {
  await apiClient.delete(`/api/contacts/${contactId}/memories/${memoryId}`);
}

export async function deleteAllContactMemories(contactId: string): Promise<void> {
  await apiClient.delete(`/api/contacts/${contactId}/memories`);
}
