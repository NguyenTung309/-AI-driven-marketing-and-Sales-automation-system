import { apiClient } from "./client";

export type JobStatus = "queued" | "running" | "succeeded" | "failed" | "cancelled";

export interface BackgroundJob {
  readonly id: string;
  readonly type: string;
  readonly title: string;
  readonly status: JobStatus;
  readonly progress: number;
  readonly progressNote: string | null;
  readonly resultLink: string | null;
  readonly resultSummary: string | null;
  readonly error: string | null;
  readonly userId: string | null;
  readonly createdAt: string;
  readonly startedAt: string | null;
  readonly finishedAt: string | null;
}

/** 202 Accepted từ mọi endpoint AI chạy ngầm. */
export interface JobAccepted {
  readonly jobId: string;
  readonly statusUrl: string;
}

export interface JobListResponse {
  readonly items: readonly BackgroundJob[];
}

/** Sự kiện realtime từ hub "job" — chỉ mang phần thay đổi, không phải job đầy đủ. */
export interface JobEvent {
  readonly jobId: string;
  readonly status?: JobStatus;
  readonly progress?: number;
  readonly progressNote?: string | null;
}

export type JobFilter = "active" | JobStatus;

export async function listJobs(filter?: JobFilter, mine = false): Promise<JobListResponse> {
  const res = await apiClient.get<JobListResponse>("/api/jobs", {
    params: { status: filter, mine: mine ? "true" : undefined },
  });
  return res.data;
}

export async function getJob(id: string): Promise<BackgroundJob> {
  const res = await apiClient.get<BackgroundJob>(`/api/jobs/${id}`);
  return res.data;
}

export async function cancelJob(id: string): Promise<BackgroundJob> {
  const res = await apiClient.post<BackgroundJob>(`/api/jobs/${id}/cancel`);
  return res.data;
}

export async function retryJob(id: string): Promise<BackgroundJob> {
  const res = await apiClient.post<BackgroundJob>(`/api/jobs/${id}/retry`);
  return res.data;
}
