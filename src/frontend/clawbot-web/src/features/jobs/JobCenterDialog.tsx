import { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button, InfiniteScrollSentinel, useInfiniteList } from "@/shared/ui";
import {
  cancelJob,
  listJobs,
  retryJob,
  type BackgroundJob,
  type JobFilter,
  type JobListResponse,
} from "@/shared/api/jobs";
import { useJobsRealtime } from "./useJobsRealtime";

const TABS: readonly { readonly value: JobFilter; readonly label: string }[] = [
  { value: "active", label: "Đang chạy" },
  { value: "succeeded", label: "Đã xong" },
  { value: "failed", label: "Lỗi" },
];

const STATUS_LABELS: Record<string, string> = {
  queued: "Chờ chạy",
  running: "Đang chạy",
  succeeded: "Đã xong",
  failed: "Thất bại",
  cancelled: "Đã huỷ",
};

const STATUS_CLASSES: Record<string, string> = {
  queued: "bg-surface-container text-on-surface-variant",
  running: "bg-primary/10 text-primary",
  succeeded: "bg-green-100 text-green-800",
  failed: "bg-red-100 text-error",
  cancelled: "bg-surface-container text-on-surface-variant",
};

interface JobCenterDialogProps {
  readonly selectedId: string | null;
  readonly onClose: () => void;
  readonly onSelect: (id: string | null) => void;
}

const EMPTY_JOBS: readonly BackgroundJob[] = [];

function isActive(job: BackgroundJob): boolean {
  return job.status === "queued" || job.status === "running";
}

/**
 * Trung tâm việc chạy ngầm. Mở từ /agents (nút "Việc đang chạy") hoặc deep link /agents?job={id}
 * — thông báo job_succeeded/job_failed trỏ về đây khi job không có trang nghiệp vụ riêng.
 */
export function JobCenterDialog({ selectedId, onClose, onSelect }: JobCenterDialogProps) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [tab, setTab] = useState<JobFilter>(selectedId ? "succeeded" : "active");

  useJobsRealtime(true);

  // Key riêng "center" — KHÔNG dùng chung ["jobs", tab] với useQuery đếm việc ở AgentDashboardPage:
  // trộn useQuery + useInfiniteQuery cùng key khiến cache là InfiniteData {pages} nên data.items = undefined -> vỡ.
  const jobsList = useInfiniteList<BackgroundJob, JobListResponse>({
    queryKey: ["jobs", "center", tab],
    queryFn: (pageParam) =>
      listJobs(tab, false, {
        cursor: typeof pageParam === "string" ? pageParam : null,
        pageSize: 50,
      }),
    // Dự phòng khi hub rớt: job đang chạy thì poll 3s.
    refetchInterval: (q) => {
      const pages = q.state.data?.pages ?? [];
      const hasActive = pages.some((page) => page.items.some(isActive));
      return hasActive ? 3_000 : false;
    },
  });
  const isLoading = jobsList.isLoading;
  const { data: pinned } = useQuery({
    queryKey: ["jobs", "all"],
    queryFn: () => listJobs(),
    enabled: Boolean(selectedId),
  });

  const jobs = useMemo(() => jobsList.items, [jobsList.items]);
  const selected = useMemo(() => {
    const pool = [...jobs, ...(pinned?.items ?? EMPTY_JOBS)];
    return pool.find((j) => j.id === selectedId) ?? jobs[0] ?? null;
  }, [jobs, pinned, selectedId]);

  const cancel = useMutation({
    mutationFn: cancelJob,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["jobs"] }),
  });
  const retry = useMutation({
    mutationFn: retryJob,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["jobs"] }),
  });

  return (
    <div className="fixed inset-0 z-[95] flex items-center justify-center bg-black/40 p-4" role="dialog" aria-modal="true">
      <div className="flex h-[80vh] w-full max-w-4xl flex-col overflow-hidden rounded-xl border border-outline bg-surface-container-lowest shadow-2xl">
        <header className="flex items-center justify-between border-b border-outline px-5 py-4">
          <div>
            <h2 className="text-headline-sm">Việc đang chạy</h2>
            <p className="mt-1 text-body-md text-on-surface-variant">
              Mọi tác vụ AI chạy ngầm. Xong sẽ có thông báo — bấm vào là quay lại đúng chỗ này.
            </p>
          </div>
          <button
            aria-label="Đóng"
            className="flex size-9 items-center justify-center rounded-full text-on-surface-variant hover:bg-surface-container"
            onClick={onClose}
            type="button"
          >
            <span aria-hidden="true" className="material-symbols-outlined text-[20px]">close</span>
          </button>
        </header>

        <div className="flex min-h-0 flex-1">
          <div className="flex w-[46%] min-w-0 flex-col border-r border-outline">
            <div className="flex gap-1 border-b border-outline px-3 py-2">
              {TABS.map((item) => (
                <button
                  className={[
                    "rounded px-3 py-1.5 text-label-md transition-colors",
                    tab === item.value
                      ? "bg-primary/10 font-bold text-primary"
                      : "text-on-surface-variant hover:bg-surface-container-low",
                  ].join(" ")}
                  key={item.value}
                  onClick={() => setTab(item.value)}
                  type="button"
                >
                  {item.label}
                </button>
              ))}
            </div>

            <ul className="min-h-0 flex-1 overflow-y-auto">
              {isLoading ? (
                <li className="px-4 py-6 text-body-md text-on-surface-variant">Đang tải...</li>
              ) : jobs.length === 0 ? (
                <li className="px-4 py-6 text-body-md text-on-surface-variant">Không có việc nào.</li>
              ) : (
                <>
                  {jobs.map((job) => (
                    <li key={job.id}>
                      <button
                        className={[
                          "flex w-full flex-col gap-1 border-b border-surface-variant px-4 py-3 text-left transition-colors",
                          selected?.id === job.id ? "bg-primary/5" : "hover:bg-surface-container-low",
                        ].join(" ")}
                        onClick={() => onSelect(job.id)}
                        type="button"
                      >
                        <span className="truncate text-body-md font-semibold text-on-surface">{job.title}</span>
                        <span className="flex items-center gap-2">
                          <span className={`rounded px-2 py-0.5 text-label-sm ${STATUS_CLASSES[job.status] ?? ""}`}>
                            {STATUS_LABELS[job.status] ?? job.status}
                          </span>
                          <span className="truncate text-label-sm text-on-surface-variant">{job.type}</span>
                        </span>
                        {isActive(job) ? (
                          <span className="h-1 w-full overflow-hidden rounded bg-surface-container">
                            <span
                              className="block h-full rounded bg-primary transition-[width]"
                              style={{ width: `${job.progress}%` }}
                            />
                          </span>
                        ) : null}
                      </button>
                    </li>
                  ))}
                  <li>
                    <InfiniteScrollSentinel
                      hasNextPage={jobsList.hasNextPage}
                      isFetchingNextPage={jobsList.isFetchingNextPage}
                      onLoadMore={jobsList.fetchNextPage}
                    />
                  </li>
                </>
              )}
            </ul>
          </div>

          <div className="min-w-0 flex-1 overflow-y-auto p-5">
            {selected ? (
              <div className="flex flex-col gap-4">
                <div>
                  <span className={`rounded px-2 py-0.5 text-label-sm ${STATUS_CLASSES[selected.status] ?? ""}`}>
                    {STATUS_LABELS[selected.status] ?? selected.status}
                  </span>
                  <h3 className="mt-2 text-title-lg text-on-surface">{selected.title}</h3>
                  <p className="text-label-sm text-on-surface-variant">{selected.type}</p>
                </div>

                {isActive(selected) ? (
                  <div>
                    <div className="h-2 w-full overflow-hidden rounded bg-surface-container">
                      <div
                        className="h-full rounded bg-primary transition-[width]"
                        style={{ width: `${selected.progress}%` }}
                      />
                    </div>
                    <p className="mt-1 text-label-sm text-on-surface-variant">
                      {selected.progress}% {selected.progressNote ? `— ${selected.progressNote}` : ""}
                    </p>
                  </div>
                ) : null}

                {selected.error ? (
                  <p className="rounded border border-error/30 bg-red-50 px-3 py-2 text-body-md text-error">
                    {selected.error}
                  </p>
                ) : null}

                {selected.resultSummary ? (
                  <p className="whitespace-pre-wrap text-body-md text-on-surface">{selected.resultSummary}</p>
                ) : null}

                <dl className="grid grid-cols-2 gap-2 text-label-sm text-on-surface-variant">
                  <dt>Tạo lúc</dt>
                  <dd className="text-on-surface">{new Date(selected.createdAt).toLocaleString("vi-VN")}</dd>
                  {selected.finishedAt ? (
                    <>
                      <dt>Kết thúc</dt>
                      <dd className="text-on-surface">{new Date(selected.finishedAt).toLocaleString("vi-VN")}</dd>
                    </>
                  ) : null}
                </dl>

                <div className="flex flex-wrap gap-2">
                  {selected.resultLink ? (
                    <Button
                      onClick={() => {
                        onClose();
                        navigate(selected.resultLink as string);
                      }}
                    >
                      Mở kết quả
                    </Button>
                  ) : null}
                  {isActive(selected) ? (
                    <Button
                      disabled={cancel.isPending}
                      onClick={() => cancel.mutate(selected.id)}
                      variant="outline"
                    >
                      Huỷ
                    </Button>
                  ) : null}
                  {selected.status === "failed" || selected.status === "cancelled" ? (
                    <Button disabled={retry.isPending} onClick={() => retry.mutate(selected.id)} variant="outline">
                      Chạy lại
                    </Button>
                  ) : null}
                </div>
              </div>
            ) : (
              <p className="text-body-md text-on-surface-variant">Chọn một việc để xem chi tiết.</p>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
