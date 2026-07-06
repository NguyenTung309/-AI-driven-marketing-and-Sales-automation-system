import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Alert } from "@/shared/ui/Alert";
import { Modal } from "@/shared/ui/Modal";
import { StatusPill } from "@/shared/ui/StatusPill";
import { toUserFriendlyError } from "@/shared/utils/userText";
import { classifyUploadKb, KB_UPLOAD_ACCEPT, type KbClassifiedFile } from "@/shared/api/kb";

const ERROR_LABELS: Readonly<Record<string, string>> = {
  file_required: "Tệp rỗng",
  file_too_large: "Tệp vượt quá 10MB",
  unsupported_format: "Định dạng không hỗ trợ",
  extraction_failed: "Không đọc được nội dung tệp",
  llm_not_configured: "Chưa cấu hình LLM cho tenant",
  classification_failed: "Agent không phân loại được",
  deploy_failed: "Đã lưu bản nháp nhưng triển khai thất bại",
};

function errorLabel(code: string | null): string {
  if (!code) return "Lỗi không xác định";
  return ERROR_LABELS[code] ?? code;
}

interface ResultRowProps {
  readonly item: KbClassifiedFile;
}

function ResultRow({ item }: ResultRowProps) {
  return (
    <li className="flex flex-col gap-1 rounded border border-outline px-3 py-2">
      <div className="flex flex-wrap items-center gap-2">
        <span aria-hidden="true" className={`material-symbols-outlined text-[18px] ${item.success ? "text-primary" : "text-error"}`}>
          {item.success ? "check_circle" : "error"}
        </span>
        <span className="text-body-md font-bold text-secondary">{item.fileName}</span>
        {item.success && item.moduleName ? (
          <span className="text-body-md text-on-surface-variant">
            → {item.moduleName} ({item.moduleCode})
          </span>
        ) : null}
        {item.isNewModule ? <StatusPill tone="warning">Nhóm mới</StatusPill> : null}
        {item.success ? (
          <StatusPill tone={item.deployed ? "success" : "neutral"}>{item.deployed ? "Đã triển khai" : "Bản nháp"}</StatusPill>
        ) : null}
      </div>
      {item.error ? <p className="text-body-sm text-error">{errorLabel(item.error)}</p> : null}
      {item.reason ? <p className="text-body-sm text-on-surface-variant">{item.reason}</p> : null}
    </li>
  );
}

interface KbAutoClassifyModalProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly onDone: () => void;
}

export function KbAutoClassifyModal({ open, onClose, onDone }: KbAutoClassifyModalProps) {
  const [files, setFiles] = useState<readonly File[]>([]);
  const [autoDeploy, setAutoDeploy] = useState(true);

  const mutation = useMutation({
    mutationFn: () => classifyUploadKb(files, autoDeploy),
    onSuccess: onDone,
  });
  const results = mutation.data?.results ?? null;

  const close = () => {
    setFiles([]);
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
            disabled={files.length === 0 || mutation.isPending}
            onClick={() => mutation.mutate()}
            type="button"
          >
            {mutation.isPending ? "Agent đang phân loại…" : "Tải lên & phân loại"}
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
        {results ? (
          <ul className="flex max-h-80 list-none flex-col gap-2 overflow-y-auto p-0">
            {results.map((item, index) => (
              <ResultRow item={item} key={`${item.fileName}-${index}`} />
            ))}
          </ul>
        ) : null}
      </div>
    </Modal>
  );
}
