import { useCallback, useEffect, useRef, useState } from "react";
import type { JobAccepted } from "@/shared/api/jobs";
import { useJobWatcher } from "./useJobWatcher";

export interface JobRunOptions<T> {
  readonly onResult?: (data: T) => void;
  readonly onError?: (message: string) => void;
}

export interface JobRun<T> {
  readonly data: T | null;
  readonly error: string | null;
  readonly running: boolean;
  readonly progress: number;
  readonly start: (launch: () => Promise<JobAccepted>) => Promise<void>;
  readonly reset: () => void;
}

/**
 * Chạy 1 việc LLM ngầm rồi lấy kết quả ngay tại màn hình đang mở.
 * Dùng cho việc tương tác (soạn nháp, tóm tắt, chạy thử agent): job vẫn hiện trong "Việc đang chạy"
 * và huỷ được, nhưng kết quả đổ thẳng vào panel thay vì bắt user đi tìm trong thông báo.
 * Kết quả nằm trong resultSummary của job dưới dạng JSON (việc này không có trang riêng để mở).
 *
 * Kết quả trả về qua callback onResult — KHÔNG bắt call site tự viết effect rồi setState trong đó.
 */
export function useJobRun<T>(options?: JobRunOptions<T>): JobRun<T> {
  const [jobId, setJobId] = useState<string | null>(null);
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Giữ callback mới nhất mà không phải nối lại watcher mỗi render (ghi ref trong effect, không ghi khi render).
  const optionsRef = useRef(options);
  useEffect(() => {
    optionsRef.current = options;
  }, [options]);

  const job = useJobWatcher(jobId, (finished) => {
    setJobId(null);

    if (finished.status === "succeeded") {
      if (!finished.resultSummary) return;
      try {
        const parsed = JSON.parse(finished.resultSummary) as T;
        setData(parsed);
        optionsRef.current?.onResult?.(parsed);
      } catch {
        setError("Không đọc được kết quả từ agent.");
        optionsRef.current?.onError?.("Không đọc được kết quả từ agent.");
      }
      return;
    }

    if (finished.status === "failed") {
      const message = finished.error ?? "Agent thất bại.";
      setError(message);
      optionsRef.current?.onError?.(message);
    }
  });

  const start = useCallback(async (launch: () => Promise<JobAccepted>) => {
    setError(null);
    setData(null);
    try {
      const accepted = await launch();
      setJobId(accepted.jobId);
    } catch (cause) {
      const message = cause instanceof Error ? cause.message : "Không gửi được yêu cầu tới agent.";
      setError(message);
      optionsRef.current?.onError?.(message);
    }
  }, []);

  const reset = useCallback(() => {
    setJobId(null);
    setData(null);
    setError(null);
  }, []);

  return {
    data,
    error,
    running: Boolean(jobId),
    progress: job?.progress ?? 0,
    start,
    reset,
  };
}
