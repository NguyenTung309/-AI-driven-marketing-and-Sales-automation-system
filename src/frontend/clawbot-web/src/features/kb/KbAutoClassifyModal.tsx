import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Alert } from "@/shared/ui/Alert";
import { Modal } from "@/shared/ui/Modal";
import { toUserFriendlyError } from "@/shared/utils/userText";
import { classifyUploadKb, KB_UPLOAD_ACCEPT } from "@/shared/api/kb";
import { useJobWatcher } from "@/features/jobs/useJobWatcher";

interface KbAutoClassifyModalProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly onDone: () => void;
}

export function KbAutoClassifyModal({ open, onClose, onDone }: KbAutoClassifyModalProps) {
  const [files, setFiles] = useState<readonly File[]>([]);
  const [autoDeploy, setAutoDeploy] = useState(true);

  // Nạp tri thức chạy ngầm: file đã lên object storage, job đọc lại và phân loại từng tệp.
  const [jobId, setJobId] = useState<string | null>(null);
  const mutation = useMutation({
    mutationFn: () => classifyUploadKb(files, autoDeploy),
    onSuccess: (job) => setJobId(job.jobId),
  });
  const job = useJobWatcher(jobId, () => {
    setJobId(null);
    onDone();
  });
  const running = mutation.isPending || Boolean(jobId);

  const close = () => {
    setFiles([]);
    setJobId(null);
    mutation.reset();
    onClose();
  };

  return (
    <Modal
      footer={
        <>
          <button
            className="rounded px-4 py-2 text-body-md font-bold text-on-surface-variant hover:bg-surface-variant"
            onClick={close}
            type="button"
          >
            Đóng
          </button>
          <button
            className="rounded bg-primary px-4 py-2 text-body-md font-bold text-white hover:bg-primary-hover disabled:opacity-50"
            disabled={files.length === 0 || running}
            onClick={() => mutation.mutate()}
            type="button"
          >
            {running ? "Agent đang phân loại…" : "Tải lên & phân loại"}
          </button>
        </>
      }
      onClose={close}
      open={open}
      title="Tải tài liệu — agent tự phân loại"
    >
      <div className="flex flex-col gap-3">
        <p className="text-body-md text-on-surface-variant">
          Chọn nhiều tài liệu (docx, xlsx, csv, pdf, txt, md). Agent research sẽ đọc nội dung và tự xếp vào nhóm tri
          thức phù hợp, hoặc tạo nhóm mới nếu chưa có.
        </p>
        <input
          accept={KB_UPLOAD_ACCEPT}
          className="text-body-md"
          multiple
          onChange={(event) => setFiles(Array.from(event.target.files ?? []))}
          type="file"
        />
        <label className="flex items-center gap-2 text-body-md text-secondary">
          <input checked={autoDeploy} onChange={(event) => setAutoDeploy(event.target.checked)} type="checkbox" />
          Tự động triển khai sau khi phân loại (tắt để duyệt tay từng bản nháp)
        </label>
        {mutation.error ? (
          <Alert tone="error">{toUserFriendlyError(mutation.error, "Không phân loại được tài liệu. Vui lòng thử lại.")}</Alert>
        ) : null}
        {job && (job.status === "queued" || job.status === "running") ? (
          <Alert tone="info">
            {job.progressNote ?? "Đang xử lý ở chế độ nền…"} Có thể đóng cửa sổ này — xong sẽ có thông báo.
          </Alert>
        ) : null}
        {job?.status === "succeeded" && job.resultSummary ? (
          <Alert tone="success">
            <span className="whitespace-pre-wrap">{job.resultSummary}</span>
          </Alert>
        ) : null}
        {job?.status === "failed" ? <Alert tone="error">{job.error ?? "Nạp tri thức thất bại."}</Alert> : null}
      </div>
    </Modal>
  );
}
