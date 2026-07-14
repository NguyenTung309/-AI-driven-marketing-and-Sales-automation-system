import { useEffect } from "react";
import { useQuery } from "@tanstack/react-query";
import { getJob, type BackgroundJob } from "@/shared/api/jobs";

/**
 * Theo dõi 1 job vừa kích từ màn nghiệp vụ: poll tới khi kết thúc rồi gọi onSettled.
 * Thông báo + link đã do JobRunner lo; hook này chỉ để màn hiện tại tự làm mới dữ liệu.
 */
export function useJobWatcher(
  jobId: string | null,
  onSettled: (job: BackgroundJob) => void,
): BackgroundJob | null {
  const { data } = useQuery({
    queryKey: ["job", jobId],
    queryFn: () => getJob(jobId as string),
    enabled: Boolean(jobId),
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status === "queued" || status === "running" ? 3_000 : false;
    },
  });

  useEffect(() => {
    if (!data) return;
    if (data.status === "queued" || data.status === "running") return;
    onSettled(data);
    // onSettled đổi mỗi render ở call site cũng không sao: chỉ chạy lại khi job đổi trạng thái.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data?.id, data?.status]);

  return data ?? null;
}
