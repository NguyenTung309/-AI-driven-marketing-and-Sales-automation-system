import { apiClient } from "./client";

export interface KbModule {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly description: string | null;
  readonly ownerRole: string | null;
  readonly status: string;
  readonly versionCount: number;
  readonly latestVersion: number | null;
  readonly createdAt: string;
}

export interface CreateKbModulePayload {
  readonly code: string;
  readonly name: string;
  readonly description?: string | null;
  readonly ownerRole?: string | null;
}

export interface UpdateKbModulePayload {
  readonly name: string;
  readonly description?: string | null;
  readonly ownerRole?: string | null;
}

export interface KbVersion {
  readonly id: string;
  readonly kbModuleId: string;
  readonly version: number;
  readonly status: string;
  readonly accuracyScore: number | null;
  readonly deployedAt: string | null;
  readonly createdAt: string;
}

export interface KbVersionDetail extends KbVersion {
  readonly contentMd: string;
}

export interface KbVersionDiff {
  readonly fromVersion: number;
  readonly toVersion: number;
  readonly linesAdded: number;
  readonly linesRemoved: number;
  readonly unifiedDiff: string;
}

export interface KbTestCase {
  readonly id: string;
  readonly question: string;
  readonly expectedAnswer: string;
  readonly isActive: boolean;
}

export interface KbTestCaseResult {
  readonly testCaseId: string;
  readonly question: string;
  readonly passed: boolean;
  readonly answer: string | null;
}

export interface KbTestRunResult {
  readonly versionId: string;
  readonly version: number;
  readonly totalCases: number;
  readonly passedCases: number;
  readonly accuracyPercent: number;
  readonly cases: readonly KbTestCaseResult[];
}

export interface KbAccuracySummary {
  readonly kbModuleId: string;
  readonly code: string;
  readonly name: string;
  readonly latestVersion: number | null;
  readonly latestAccuracyPercent: number | null;
  readonly rollingAccuracyPercent: number | null;
  readonly lastTestedAt: string | null;
}

export async function listKbModules(): Promise<readonly KbModule[]> {
  const response = await apiClient.get<readonly KbModule[]>("/api/kb/modules");
  return response.data;
}

export async function getKbModule(id: string): Promise<KbModule> {
  const response = await apiClient.get<KbModule>(`/api/kb/modules/${id}`);
  return response.data;
}

export async function createKbModule(payload: CreateKbModulePayload): Promise<KbModule> {
  const response = await apiClient.post<KbModule>("/api/kb/modules", payload);
  return response.data;
}

export async function updateKbModule(id: string, payload: UpdateKbModulePayload): Promise<void> {
  await apiClient.put(`/api/kb/modules/${id}`, payload);
}

export async function archiveKbModule(id: string): Promise<void> {
  await apiClient.post(`/api/kb/modules/${id}/archive`);
}

export async function listKbVersions(moduleId: string): Promise<readonly KbVersion[]> {
  const response = await apiClient.get<readonly KbVersion[]>(`/api/kb/modules/${moduleId}/versions`);
  return response.data;
}

export async function getKbVersion(moduleId: string, versionId: string): Promise<KbVersionDetail> {
  const response = await apiClient.get<KbVersionDetail>(`/api/kb/modules/${moduleId}/versions/${versionId}`);
  return response.data;
}

export async function createKbVersion(moduleId: string, contentMd: string): Promise<KbVersion> {
  const response = await apiClient.post<KbVersion>(`/api/kb/modules/${moduleId}/versions`, { contentMd });
  return response.data;
}

export interface KbUploadResult {
  readonly version: KbVersion;
  readonly sourceFormat: string;
  readonly charCount: number;
  readonly contentMd: string;
}

export const KB_UPLOAD_ACCEPT = ".docx,.xlsx,.csv,.pdf,.txt,.md";

export async function uploadKbVersion(moduleId: string, file: File): Promise<KbUploadResult> {
  const form = new FormData();
  form.append("file", file, file.name);
  const response = await apiClient.post<KbUploadResult>(`/api/kb/modules/${moduleId}/upload`, form);
  return response.data;
}

export interface KbClassifiedFile {
  readonly fileName: string;
  readonly success: boolean;
  readonly error: string | null;
  readonly moduleId: string | null;
  readonly moduleCode: string | null;
  readonly moduleName: string | null;
  readonly isNewModule: boolean;
  readonly confidence: number;
  readonly reason: string | null;
  readonly version: KbVersion | null;
  readonly deployed: boolean;
}

export interface KbClassifyUploadResponse {
  readonly results: readonly KbClassifiedFile[];
}

export async function classifyUploadKb(files: readonly File[], autoDeploy: boolean): Promise<KbClassifyUploadResponse> {
  const form = new FormData();
  for (const file of files) form.append("files", file, file.name);
  const response = await apiClient.post<KbClassifyUploadResponse>("/api/kb/classify-upload", form, {
    params: { autoDeploy },
  });
  return response.data;
}

export async function deployKbVersion(moduleId: string, versionId: string): Promise<void> {
  await apiClient.post(`/api/kb/modules/${moduleId}/versions/${versionId}/deploy`);
}

export async function rollbackKbVersion(moduleId: string, versionId: string): Promise<void> {
  await apiClient.post(`/api/kb/modules/${moduleId}/versions/${versionId}/rollback`);
}

export async function getKbVersionDiff(moduleId: string, fromVersion: number, toVersion: number): Promise<KbVersionDiff> {
  const response = await apiClient.get<KbVersionDiff>(`/api/kb/modules/${moduleId}/diff`, {
    params: { fromVersion, toVersion },
  });
  return response.data;
}

export async function listKbTestCases(moduleId: string): Promise<readonly KbTestCase[]> {
  const response = await apiClient.get<readonly KbTestCase[]>(`/api/kb/modules/${moduleId}/test-cases`);
  return response.data;
}

export async function addKbTestCase(moduleId: string, question: string, expectedAnswer: string): Promise<KbTestCase> {
  const response = await apiClient.post<KbTestCase>(`/api/kb/modules/${moduleId}/test-cases`, {
    question,
    expectedAnswer,
  });
  return response.data;
}

export async function generateKbTestCases(moduleId: string, count = 5): Promise<readonly KbTestCase[]> {
  const response = await apiClient.post<readonly KbTestCase[]>(`/api/kb/modules/${moduleId}/test-cases/generate`, {
    count,
  });
  return response.data;
}

export async function runKbTest(moduleId: string): Promise<KbTestRunResult> {
  const response = await apiClient.post<KbTestRunResult>(`/api/kb/modules/${moduleId}/test`);
  return response.data;
}

export async function getKbAccuracy(): Promise<readonly KbAccuracySummary[]> {
  const response = await apiClient.get<readonly KbAccuracySummary[]>("/api/kb/accuracy");
  return response.data;
}

// ai-self-learning-memory: đề xuất tri thức do job chưng cất đêm sinh ra, chờ duyệt hoặc đã tự duyệt.
export interface KbSuggestion {
  readonly id: string;
  readonly op: "add" | "update" | "merge";
  readonly targetKbModuleId: string | null;
  readonly targetModuleName: string | null;
  readonly title: string;
  readonly contentMd: string;
  readonly rationale: string;
  readonly evidenceJson: string;
  readonly reviewerVerdict: "approve" | "reject" | "needs_human" | null;
  readonly reviewerNotes: string | null;
  readonly accuracyBefore: number | null;
  readonly accuracyAfter: number | null;
  readonly status: "pending" | "approved" | "rejected";
  readonly approvalMode: "auto" | "human" | null;
  readonly rejectedReason: string | null;
  readonly createdAt: string;
  readonly decidedAt: string | null;
}

export interface KbSuggestionEvidence {
  readonly conversationId: string;
  readonly snippetRedacted: string;
  readonly signal: string;
}

export async function listKbSuggestions(status?: string): Promise<readonly KbSuggestion[]> {
  const response = await apiClient.get<readonly KbSuggestion[]>("/api/kb/suggestions", {
    params: status ? { status } : undefined,
  });
  return response.data;
}

export async function approveKbSuggestion(id: string, contentMd?: string): Promise<void> {
  await apiClient.post(`/api/kb/suggestions/${id}/approve`, { contentMd: contentMd ?? null });
}

export async function rejectKbSuggestion(id: string, reason: string): Promise<void> {
  await apiClient.post(`/api/kb/suggestions/${id}/reject`, { reason });
}
