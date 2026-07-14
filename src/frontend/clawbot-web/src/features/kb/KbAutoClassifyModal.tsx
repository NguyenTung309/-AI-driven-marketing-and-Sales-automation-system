import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Alert } from "@/shared/ui/Alert";
import { FileDropzone } from "@/shared/ui/FileDropzone";
import { Modal } from "@/shared/ui/Modal";
import { toUserFriendlyError } from "@/shared/utils/userText";
import { classifyUploadKb, KB_UPLOAD_ACCEPT } from "@/shared/api/kb";
import { useJobWatcher } from "@/features/jobs/useJobWatcher";

interface KbAutoClassifyModalProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly onDone: () => void;
}

const MAX_FILE_MB = 25;

export function KbAutoClassifyModal({ open, onClose, onDone }: KbAutoClassifyModalProps) {
  const [files, setFiles] = useState<readonly File[]>([]);
  const [autoDeploy, setAutoDeploy] = useState(true);
  const [autoTest, setAutoTest] = useState(true);
  const [uploadPct, setUploadPct] = useState(0);

  // Nạp tri thức chạy ngầm: file đã lên object storage, job đọc lại và phân loại từng tệp.
  const [jobId, setJobId] = useState<string | null>(null);
  const mutation = useMutation({
    mutationFn: () => classifyUploadKb(files, autoDeploy, autoTest, setUploadPct),
    onMutate: () => setUploadPct(0),
    onSuccess: (job) => setJobId(job.jobId),
  });
  const job = useJobWatcher(jobId, () => {
    setJobId(null);
    onDone();
  });

  const uploading = mutation.isPending;
  const jobActive = Boolean(jobId) && (!job || job.status === "queued" || job.status === "running");
  const running = uploading || jobActive;

  // Tiến trình 2 pha cho FileDropzone: (1) đang tải file lên, (2) agent xử lý ngầm.
  let progress: number | null = null;
  let progressLabel: string | undefined;
  let progressHint: string | undefined;
  let indeterminate = false;
  if (uploading) {
    progress = uploadPct;
    progressLabel = uploadPct >= 100 ? "Đã tải lên — đang khởi tạo phân loại…" : "Đang tải tài liệu lên…";
  } else if (jobActive) {
    progress = job?.progress ?? 0;
    progressLabel = job?.progressNote ?? "Agent đang đọc và phân loại tài liệu…";
    progressHint = "Có thể đóng cửa sổ này — xong sẽ có thông báo.";
    indeterminate = (job?.progress ?? 0) <= 0;
  }

  const close = () => {
    setFiles([]);
    setJobId(null);
    setUploadPct(0);
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
            {uploading ? "Đang tải lên…" : jobActive ? "Agent đang phân loại…" : "Tải lên & phân loại"}
          </button>
        </>
      }
      onClose={close}
      open={open}
      title="Tải tài liệu — agent tự phân loại"
    >
      <div className="flex flex-col gap-4">
        <p className="text-body-md text-on-surface-variant">
          Chọn nhiều tài liệu (docx, xlsx, csv, pdf, txt, md). Agent research sẽ đọc nội dung và tự xếp vào nhóm tri
          thức phù hợp, hoặc tạo nhóm mới nếu chưa có.
        </p>

        <FileDropzone
          accept={KB_UPLOAD_ACCEPT}
          files={files}
          hintFormats="docx, xlsx, csv, pdf, txt, md"
          indeterminate={indeterminate}
          maxSizeMb={MAX_FILE_MB}
          multiple
          onFilesChange={setFiles}
          progress={progress}
          progressHint={progressHint}
          progressLabel={progressLabel}
        />

        <label className="flex cursor-pointer items-start gap-2 text-body-md text-secondary">
          <input
            checked={autoDeploy}
            className="mt-0.5"
            disabled={running}
            onChange={(event) => setAutoDeploy(event.target.checked)}
            type="checkbox"
          />
          <span>Tự động triển khai sau khi phân loại (tắt để duyệt tay từng bản nháp)</span>
        </label>

        <label className="flex cursor-pointer items-start gap-2 text-body-md text-secondary">
          <input
            checked={autoTest}
            className="mt-0.5"
            disabled={running}
            onChange={(event) => setAutoTest(event.target.checked)}
            type="checkbox"
          />
          <span>
            Tự kiểm thử &amp; chấm độ chính xác sau khi phân loại
            <span className="mt-0.5 block text-label-sm text-on-surface-variant">
              Agent tự sinh câu hỏi phủ khắp tài liệu rồi chấm điểm; tốn thêm ít thời gian xử lý.
            </span>
          </span>
        </label>

        {mutation.error ? (
          <Alert tone="error">{toUserFriendlyError(mutation.error, "Không phân loại được tài liệu. Vui lòng thử lại.")}</Alert>
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
