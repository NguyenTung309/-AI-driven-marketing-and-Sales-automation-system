import { useRef, useState, type DragEvent, type KeyboardEvent } from "react";

export interface FileDropzoneProps {
  readonly files: readonly File[];
  readonly onFilesChange: (files: File[]) => void;
  /** Danh sách đuôi file cho phép, ví dụ ".pdf,.docx". Cũng dùng để chặn file kéo-thả sai định dạng. */
  readonly accept?: string;
  readonly multiple?: boolean;
  readonly disabled?: boolean;
  /** Giới hạn dung lượng mỗi tệp (MB); vượt sẽ bị bỏ và báo lỗi. */
  readonly maxSizeMb?: number;
  /** Dòng gợi ý định dạng hiển thị trong khung thả, ví dụ "docx, xlsx, csv, pdf, txt, md". */
  readonly hintFormats?: string;
  /** 0..100 khi đang bận (tải lên / xử lý). null = rảnh, cho phép chỉnh sửa. */
  readonly progress?: number | null;
  readonly progressLabel?: string;
  readonly progressHint?: string;
  /** Bận nhưng chưa có % cụ thể — hiện thanh chạy qua lại thay vì đứng yên ở 0%. */
  readonly indeterminate?: boolean;
}

interface FileKind {
  readonly icon: string;
  readonly wrap: string;
  readonly fg: string;
}

// Icon + màu theo định dạng để hàng file dễ nhận diện, không đơn điệu một icon xám.
const FILE_KINDS: readonly { readonly exts: readonly string[]; readonly kind: FileKind }[] = [
  { exts: ["pdf"], kind: { icon: "picture_as_pdf", wrap: "bg-rose-50", fg: "text-rose-600" } },
  { exts: ["doc", "docx"], kind: { icon: "description", wrap: "bg-blue-50", fg: "text-blue-600" } },
  { exts: ["xls", "xlsx", "csv"], kind: { icon: "table_chart", wrap: "bg-emerald-50", fg: "text-emerald-600" } },
  { exts: ["md", "markdown"], kind: { icon: "article", wrap: "bg-violet-50", fg: "text-violet-600" } },
  { exts: ["txt"], kind: { icon: "text_snippet", wrap: "bg-slate-100", fg: "text-slate-600" } },
];

const DEFAULT_KIND: FileKind = { icon: "draft", wrap: "bg-slate-100", fg: "text-slate-500" };

function extOf(name: string): string {
  const dot = name.lastIndexOf(".");
  return dot >= 0 ? name.slice(dot + 1).trim().toLowerCase() : "";
}

// MIME theo từng đuôi — khớp với FileNameFromContentType phía backend để nhận diện cả khi
// trình duyệt gửi tên "blob"/không có đuôi (docx/xlsx tải từ cloud, kéo-thả từ vài nguồn).
const EXT_MIME: Record<string, readonly string[]> = {
  docx: ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
  xlsx: ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
  csv: ["text/csv", "application/csv", "application/vnd.ms-excel"],
  pdf: ["application/pdf"],
  txt: ["text/plain"],
  md: ["text/markdown", "text/x-markdown"],
};

function kindOf(name: string): FileKind {
  const ext = extOf(name);
  return FILE_KINDS.find((entry) => entry.exts.includes(ext))?.kind ?? DEFAULT_KIND;
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  const kb = bytes / 1024;
  if (kb < 1024) return `${kb < 10 ? kb.toFixed(1) : Math.round(kb)} KB`;
  const mb = kb / 1024;
  return `${mb < 10 ? mb.toFixed(1) : Math.round(mb)} MB`;
}

function acceptExtList(accept?: string): readonly string[] {
  if (!accept) return [];
  return accept
    .split(",")
    .map((token) => token.trim().replace(/^\./, "").toLowerCase())
    .filter(Boolean);
}

// Chấp nhận file theo thứ tự khoan dung (không chặt hơn backend, tránh false-reject docx thật):
// 1) đuôi tên hợp lệ, hoặc 2) MIME khớp đuôi hợp lệ, hoặc 3) blob không tên/không type → để backend dò nội dung.
function isTypeAllowed(file: File, exts: readonly string[]): boolean {
  if (exts.length === 0) return true;
  const ext = extOf(file.name);
  if (ext && exts.includes(ext)) return true;
  const mime = (file.type || "").toLowerCase();
  if (mime && exts.some((e) => EXT_MIME[e]?.includes(mime))) return true;
  if (!ext && (!mime || mime === "application/octet-stream")) return true;
  return false;
}

// Nhãn định dạng để báo lỗi rõ ràng (vd ".doc" hay "image/png"), giúp người dùng hiểu vì sao bị chặn.
function typeLabel(file: File): string {
  const ext = extOf(file.name);
  if (ext) return `.${ext}`;
  return file.type || "không rõ";
}

function fileKey(file: File): string {
  return `${file.name}:${file.size}`;
}

export function FileDropzone({
  files,
  onFilesChange,
  accept,
  multiple = true,
  disabled = false,
  maxSizeMb,
  hintFormats,
  progress = null,
  progressLabel,
  progressHint,
  indeterminate = false,
}: FileDropzoneProps) {
  const inputRef = useRef<HTMLInputElement | null>(null);
  const [dragging, setDragging] = useState(false);
  const [rejections, setRejections] = useState<readonly string[]>([]);

  const busy = progress !== null;
  const editable = !disabled && !busy;
  const pct = Math.max(0, Math.min(100, Math.round(progress ?? 0)));

  // Cho hộp thoại chọn file của OS khớp cả theo MIME, không chỉ đuôi — để docx/xlsx tên "blob" vẫn chọn được.
  const inputAccept = accept
    ? [accept, ...acceptExtList(accept).flatMap((e) => EXT_MIME[e] ?? [])].join(",")
    : undefined;

  function ingest(incoming: readonly File[]) {
    const allowedExts = acceptExtList(accept);
    const seen = new Set(files.map(fileKey));
    const next: File[] = [];
    const rejected: string[] = [];

    for (const file of incoming) {
      if (!isTypeAllowed(file, allowedExts)) {
        rejected.push(`${file.name}: định dạng ${typeLabel(file)} không được hỗ trợ`);
        continue;
      }
      if (maxSizeMb && file.size > maxSizeMb * 1024 * 1024) {
        rejected.push(`${file.name}: vượt quá ${maxSizeMb}MB`);
        continue;
      }
      const key = fileKey(file);
      if (seen.has(key)) continue;
      seen.add(key);
      next.push(file);
    }

    setRejections(rejected);
    if (next.length === 0) return;
    onFilesChange(multiple ? [...files, ...next] : next.slice(0, 1));
  }

  function handleDrop(event: DragEvent<HTMLDivElement>) {
    event.preventDefault();
    setDragging(false);
    if (!editable) return;
    ingest(Array.from(event.dataTransfer.files ?? []));
  }

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      inputRef.current?.click();
    }
  }

  function removeAt(index: number) {
    onFilesChange(files.filter((_, i) => i !== index));
  }

  return (
    <div className="flex flex-col gap-3">
      {editable ? (
        <div
          role="button"
          tabIndex={0}
          aria-label="Chọn hoặc kéo thả tài liệu để tải lên"
          onClick={() => inputRef.current?.click()}
          onKeyDown={handleKeyDown}
          onDragOver={(event) => {
            event.preventDefault();
            if (!dragging) setDragging(true);
          }}
          onDragLeave={(event) => {
            event.preventDefault();
            setDragging(false);
          }}
          onDrop={handleDrop}
          className={[
            "group flex cursor-pointer flex-col items-center justify-center gap-2 rounded-xl border-2 border-dashed px-6 py-8 text-center transition-all",
            "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2",
            dragging
              ? "scale-[1.01] border-primary bg-primary/5"
              : "border-surface-variant bg-surface-container-low hover:border-primary/50 hover:bg-primary/[0.03]",
          ].join(" ")}
        >
          <span
            className={[
              "flex size-12 items-center justify-center rounded-full transition-colors",
              dragging
                ? "bg-primary/15 text-primary"
                : "bg-surface-variant text-on-surface-variant group-hover:bg-primary/10 group-hover:text-primary",
            ].join(" ")}
          >
            <span aria-hidden="true" className="material-symbols-outlined text-[26px]">
              {dragging ? "file_download" : "cloud_upload"}
            </span>
          </span>
          <div>
            <p className="text-body-md font-semibold text-on-surface">
              <span className="text-primary">Bấm để chọn</span> hoặc kéo thả tài liệu vào đây
            </p>
            {hintFormats ? (
              <p className="mt-0.5 text-label-sm text-on-surface-variant">
                {hintFormats}
                {maxSizeMb ? ` · tối đa ${maxSizeMb}MB mỗi tệp` : ""}
              </p>
            ) : null}
          </div>
          <input
            ref={inputRef}
            type="file"
            accept={inputAccept}
            multiple={multiple}
            className="hidden"
            onChange={(event) => {
              const picked = Array.from(event.target.files ?? []);
              event.target.value = "";
              ingest(picked);
            }}
          />
        </div>
      ) : null}

      {rejections.length > 0 ? (
        <ul className="space-y-1 rounded-lg border border-error/30 bg-error/10 p-3 text-label-sm text-error">
          {rejections.map((message) => (
            <li key={message} className="flex items-start gap-2">
              <span aria-hidden="true" className="material-symbols-outlined mt-px text-[16px] shrink-0">
                block
              </span>
              <span className="min-w-0 break-words">{message}</span>
            </li>
          ))}
        </ul>
      ) : null}

      {files.length > 0 ? (
        <ul className="flex flex-col gap-2">
          {files.map((file, index) => {
            const kind = kindOf(file.name);
            return (
              <li
                key={fileKey(file)}
                className={[
                  "flex items-center gap-3 rounded-lg border border-outline bg-surface-container-lowest p-3 transition-opacity",
                  busy ? "opacity-70" : "",
                ].join(" ")}
              >
                <span className={`flex size-9 shrink-0 items-center justify-center rounded-lg ${kind.wrap}`}>
                  <span aria-hidden="true" className={`material-symbols-outlined text-[20px] ${kind.fg}`}>
                    {kind.icon}
                  </span>
                </span>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-body-md font-semibold text-on-surface">{file.name}</p>
                  <p className="text-label-sm text-on-surface-variant">
                    {formatBytes(file.size)}
                    {extOf(file.name) ? ` · ${extOf(file.name).toUpperCase()}` : ""}
                  </p>
                </div>
                {editable ? (
                  <button
                    type="button"
                    aria-label={`Bỏ tệp ${file.name}`}
                    onClick={() => removeAt(index)}
                    className="shrink-0 rounded-full p-1.5 text-on-surface-variant transition-colors hover:bg-error/10 hover:text-error"
                  >
                    <span aria-hidden="true" className="material-symbols-outlined text-[18px]">
                      close
                    </span>
                  </button>
                ) : busy ? (
                  <span
                    aria-hidden="true"
                    className="material-symbols-outlined shrink-0 animate-spin text-[18px] text-primary"
                  >
                    progress_activity
                  </span>
                ) : null}
              </li>
            );
          })}
        </ul>
      ) : null}

      {busy ? (
        <div className="rounded-lg border border-primary/20 bg-primary/5 p-4">
          <div className="mb-2 flex items-center justify-between gap-3">
            <span className="flex min-w-0 items-center gap-2 text-body-md font-semibold text-on-surface">
              <span
                aria-hidden="true"
                className="material-symbols-outlined shrink-0 animate-spin text-[18px] text-primary"
              >
                progress_activity
              </span>
              <span className="truncate">{progressLabel ?? "Đang xử lý…"}</span>
            </span>
            {!indeterminate ? (
              <span className="shrink-0 text-label-sm font-bold tabular-nums text-primary">{pct}%</span>
            ) : null}
          </div>
          <div
            className="relative h-2 w-full overflow-hidden rounded-full bg-surface-variant"
            role="progressbar"
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={indeterminate ? undefined : pct}
          >
            {indeterminate ? (
              <span className="progress-indeterminate-bar w-1/3 rounded-full bg-primary" />
            ) : (
              <div
                className="h-full rounded-full bg-primary transition-[width] duration-300 ease-out"
                style={{ width: `${pct}%` }}
              />
            )}
          </div>
          {progressHint ? <p className="mt-2 text-label-sm text-on-surface-variant">{progressHint}</p> : null}
        </div>
      ) : null}
    </div>
  );
}
