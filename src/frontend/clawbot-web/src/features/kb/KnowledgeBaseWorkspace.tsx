import { useRef, useState } from "react";
import { Card } from "@/shared/ui/Card";
import { StatusPill, type StatusTone } from "@/shared/ui/StatusPill";
import { KB_UPLOAD_ACCEPT, type KbAccuracySummary, type KbModule, type KbVersion, type KbVersionDiff } from "@/shared/api/kb";

function normalize(value: string | null | undefined): string {
  return (value ?? "").trim().toLowerCase();
}

function formatDateTime(value: string | null): string {
  if (!value) return "Chưa có";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("vi-VN", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}

function statusTone(status: string): StatusTone {
  const value = normalize(status);
  if (value === "deployed" || value === "active") return "success";
  if (value === "draft") return "warning";
  return "neutral";
}

function statusLabel(status: string): string {
  const value = normalize(status);
  if (value === "deployed") return "Đã phát hành";
  if (value === "draft") return "Bản nháp";
  if (value === "archived") return "Lưu trữ";
  if (value === "active") return "Đang hoạt động";
  return status || "Không rõ";
}

function accuracyPercent(value: number | null): number | null {
  if (value === null) return null;
  return Math.max(0, Math.min(100, value <= 1 ? value * 100 : value));
}

function accuracyTone(value: number | null): string {
  const percent = accuracyPercent(value);
  if (percent === null) return "bg-surface-variant";
  if (percent >= 90) return "bg-success";
  if (percent >= 85) return "bg-warning";
  return "bg-error";
}

function accuracyLabel(value: number | null): string {
  const percent = accuracyPercent(value);
  return percent === null ? "Chưa kiểm tra" : `${percent.toFixed(percent % 1 === 0 ? 0 : 1)}%`;
}

function versionTitle(module: KbModule | null, version: KbVersion | null): string {
  if (!module) return "Chưa chọn nhóm tri thức";
  if (!version) return module.name;
  return `${module.name} · bản ${version.version}`;
}

export function ModuleRail({
  modules,
  selectedId,
  search,
  loading,
  onSearch,
  onSelect,
  onCreate,
}: {
  readonly modules: readonly KbModule[];
  readonly selectedId: string | null;
  readonly search: string;
  readonly loading: boolean;
  readonly onSearch: (value: string) => void;
  readonly onSelect: (id: string) => void;
  readonly onCreate: () => void;
}) {
  return (
    <aside className="flex flex-col border-b border-outline bg-white xl:min-h-[680px] xl:border-b-0 xl:border-r">
      <div className="border-b border-outline p-4">
        <p className="text-label-caps uppercase text-on-surface-variant">Thư mục tri thức</p>
        <div className="mt-3 flex items-center gap-2 rounded border border-outline bg-surface-container-lowest px-3 py-2 focus-within:border-primary">
          <span aria-hidden="true" className="material-symbols-outlined text-[18px] text-on-surface-variant">search</span>
          <input
            aria-label="Tìm nhóm tri thức"
            className="min-w-0 flex-1 bg-transparent text-body-md text-secondary outline-none"
            onChange={(event) => onSearch(event.target.value)}
            placeholder="Tìm nhóm tri thức..."
            value={search}
          />
        </div>
      </div>

      <div className="flex-1 space-y-1 overflow-y-auto p-2">
        {loading ? (
          <p className="p-3 text-body-md text-on-surface-variant">Đang tải nhóm tri thức...</p>
        ) : modules.length ? (
          modules.map((module) => (
            <button
              className={[
                "flex w-full items-start gap-3 rounded p-3 text-left transition-colors",
                selectedId === module.id ? "bg-red-50 text-primary" : "text-secondary hover:bg-surface-container-low",
              ].join(" ")}
              key={module.id}
              onClick={() => onSelect(module.id)}
              type="button"
            >
              <span aria-hidden="true" className="material-symbols-outlined mt-0.5 text-[19px]">
                {selectedId === module.id ? "folder_open" : "folder"}
              </span>
              <span className="min-w-0">
                <span className="block truncate text-body-md font-bold">{module.name}</span>
                <span className="mt-1 block truncate font-mono text-label-sm text-on-surface-variant">{module.code}</span>
              </span>
            </button>
          ))
        ) : (
          <p className="p-3 text-body-md text-on-surface-variant">Chưa có nhóm tri thức phù hợp.</p>
        )}
      </div>

      <div className="border-t border-outline p-3">
        <button
          className="flex w-full items-center justify-center gap-2 rounded border border-primary px-3 py-2 text-body-md font-bold text-primary hover:bg-red-50"
          onClick={onCreate}
          type="button"
        >
          <span aria-hidden="true" className="material-symbols-outlined text-[18px]">create_new_folder</span>
          Tạo nhóm tri thức
        </button>
      </div>
    </aside>
  );
}

export function VersionRail({
  module,
  versions,
  selectedId,
  loading,
  accuracy,
  onSelect,
}: {
  readonly module: KbModule | null;
  readonly versions: readonly KbVersion[];
  readonly selectedId: string | null;
  readonly loading: boolean;
  readonly accuracy: KbAccuracySummary | null;
  readonly onSelect: (id: string) => void;
}) {
  return (
    <aside className="flex flex-col border-b border-outline bg-surface-container-lowest xl:min-h-[680px] xl:border-b-0 xl:border-r">
      <div className="border-b border-outline p-4">
        <p className="truncate text-body-md font-bold text-secondary">{module?.name ?? "Phiên bản"}</p>
        <div className="mt-2 flex items-center justify-between gap-2">
          <span className="font-mono text-label-sm text-on-surface-variant">{module?.versionCount ?? 0} phiên bản</span>
          <span className="font-mono text-label-sm font-bold text-primary">{accuracyLabel(accuracy?.latestAccuracyPercent ?? null)}</span>
        </div>
      </div>

      <div className="flex-1 space-y-2 overflow-y-auto p-3">
        {!module ? (
          <p className="p-3 text-body-md text-on-surface-variant">Chọn một nhóm tri thức để xem lịch sử.</p>
        ) : loading ? (
          <p className="p-3 text-body-md text-on-surface-variant">Đang tải phiên bản...</p>
        ) : versions.length ? (
          versions.map((version) => (
            <button
              className={[
                "w-full rounded border p-3 text-left transition-colors",
                selectedId === version.id
                  ? "border-primary bg-red-50"
                  : "border-outline bg-white hover:border-primary/50 hover:bg-surface-container-low",
              ].join(" ")}
              key={version.id}
              onClick={() => onSelect(version.id)}
              type="button"
            >
              <div className="flex items-start justify-between gap-2">
                <div>
                  <p className="text-body-md font-bold text-secondary">Bản {version.version}</p>
                  <p className="mt-1 text-label-sm text-on-surface-variant">{formatDateTime(version.createdAt)}</p>
                </div>
                <StatusPill tone={statusTone(version.status)}>{statusLabel(version.status)}</StatusPill>
              </div>
              <div className="mt-3 flex items-center justify-between gap-3 font-mono text-label-sm">
                <span className="text-on-surface-variant">Độ chính xác</span>
                <span className="font-bold text-secondary">{accuracyLabel(version.accuracyScore)}</span>
              </div>
            </button>
          ))
        ) : (
          <div className="rounded border border-dashed border-outline p-4 text-body-md text-on-surface-variant">
            Nhóm tri thức chưa có bản nào. Nhập nội dung và lưu bản nháp đầu tiên.
          </div>
        )}
      </div>
    </aside>
  );
}

export function EditorWorkspace({
  module,
  version,
  initialContent,
  loading,
  saving,
  deploying,
  testPending,
  uploading,
  onSave,
  onUpload,
  onDeploy,
  onRollback,
  onCompare,
  onOpenQa,
  onEditModule,
  onArchive,
}: {
  readonly module: KbModule | null;
  readonly version: KbVersion | null;
  readonly initialContent: string;
  readonly loading: boolean;
  readonly saving: boolean;
  readonly deploying: boolean;
  readonly testPending: boolean;
  readonly uploading: boolean;
  readonly onSave: (content: string) => void;
  readonly onUpload: (file: File) => void;
  readonly onDeploy: () => void;
  readonly onRollback: () => void;
  readonly onCompare: () => void;
  readonly onOpenQa: () => void;
  readonly onEditModule: () => void;
  readonly onArchive: () => void;
}) {
  const [content, setContent] = useState(initialContent);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const hasChanges = content !== initialContent;
  const isDeployed = normalize(version?.status) === "deployed";

  const handleFileChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (file) onUpload(file);
    event.target.value = "";
  };

  return (
    <section className="flex min-w-0 flex-col bg-[#111827] text-slate-100 xl:min-h-[680px]">
      <div className="flex flex-col gap-3 border-b border-slate-700 bg-white p-4 text-secondary lg:flex-row lg:items-center lg:justify-between">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <span aria-hidden="true" className="material-symbols-outlined text-[18px] text-primary">description</span>
            <h2 className="truncate text-body-md font-bold">{versionTitle(module, version)}</h2>
            {version ? <StatusPill tone={statusTone(version.status)}>{statusLabel(version.status)}</StatusPill> : null}
          </div>
          <p className="mt-1 truncate text-label-sm text-on-surface-variant">
            {module?.description ?? "Nội dung tri thức dùng cho các agent."}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <input
            accept={KB_UPLOAD_ACCEPT}
            className="hidden"
            onChange={handleFileChange}
            ref={fileInputRef}
            type="file"
          />
          <button
            className="inline-flex items-center gap-1.5 rounded border border-outline px-3 py-2 text-label-sm font-bold text-secondary hover:border-primary hover:text-primary disabled:opacity-50"
            disabled={!module || uploading}
            onClick={() => fileInputRef.current?.click()}
            title="Tải tệp (docx, xlsx, csv, pdf, txt, md) và tự chuyển thành bản nháp"
            type="button"
          >
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">upload_file</span>
            {uploading ? "Đang xử lý tệp" : "Tải tệp"}
          </button>
          <button
            className="rounded border border-outline px-3 py-2 text-label-sm font-bold text-secondary hover:border-primary hover:text-primary disabled:opacity-50"
            disabled={!module}
            onClick={onOpenQa}
            type="button"
          >
            Kiểm thử Q&A
          </button>
          <button
            aria-label="Chỉnh sửa nhóm tri thức"
            className="rounded border border-outline p-2 text-on-surface-variant hover:border-primary hover:text-primary disabled:opacity-50"
            disabled={!module}
            onClick={onEditModule}
            title="Chỉnh sửa nhóm tri thức"
            type="button"
          >
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">edit</span>
          </button>
          <button
            aria-label="Lưu trữ nhóm tri thức"
            className="rounded border border-outline p-2 text-on-surface-variant hover:border-error hover:text-error disabled:opacity-50"
            disabled={!module}
            onClick={onArchive}
            title="Lưu trữ nhóm tri thức"
            type="button"
          >
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">archive</span>
          </button>
        </div>
      </div>

      <div className="flex items-center justify-between border-b border-slate-700 bg-[#1f2937] px-4 py-2 font-mono text-label-sm">
        <span className="text-slate-400">TRÌNH SOẠN TRI THỨC</span>
        <span className="text-slate-500">{content.length.toLocaleString("vi-VN")} ký tự</span>
      </div>

      {loading ? (
        <div className="flex flex-1 items-center justify-center text-body-md text-slate-400">Đang tải nội dung...</div>
      ) : module ? (
        <textarea
          aria-label="Nội dung tri thức"
          className="min-h-[430px] flex-1 resize-none bg-[#111827] p-5 font-mono text-[13px] leading-6 text-slate-200 outline-none placeholder:text-slate-600"
          onChange={(event) => setContent(event.target.value)}
          placeholder={"# Nội dung tri thức\n\nNhập kiến thức tại đây..."}
          spellCheck={false}
          value={content}
        />
      ) : (
        <div className="flex flex-1 flex-col items-center justify-center p-8 text-center text-slate-400">
          <span aria-hidden="true" className="material-symbols-outlined text-[42px]">menu_book</span>
          <p className="mt-3 text-body-md">Chọn hoặc tạo nhóm tri thức để bắt đầu biên tập.</p>
        </div>
      )}

      <div className="border-t border-slate-700 bg-[#1f2937] p-3">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
          <div className="flex flex-wrap items-center gap-2 font-mono text-label-sm text-slate-400">
            <span>{hasChanges ? "Có thay đổi chưa lưu" : "Nội dung đã đồng bộ"}</span>
            {version?.deployedAt ? <span>• Phát hành {formatDateTime(version.deployedAt)}</span> : null}
          </div>
          <div className="flex flex-wrap gap-2">
            <button
              className="rounded border border-slate-600 px-3 py-2 text-label-sm font-bold text-slate-200 hover:border-white disabled:opacity-40"
              disabled={!version || version.version <= 1}
              onClick={onCompare}
              type="button"
            >
              So sánh thay đổi
            </button>
            {!isDeployed && version ? (
              <button
                className="rounded border border-slate-500 px-3 py-2 text-label-sm font-bold text-white hover:border-warning disabled:opacity-40"
                disabled={deploying}
                onClick={onRollback}
                type="button"
              >
                Khôi phục về bản {version.version}
              </button>
            ) : null}
            <button
              className="rounded border border-primary bg-white px-4 py-2 text-label-sm font-bold text-primary hover:bg-red-50 disabled:opacity-40"
              disabled={!module || !content.trim() || saving || !hasChanges}
              onClick={() => onSave(content)}
              type="button"
            >
              {saving ? "Đang lưu" : "Lưu bản mới"}
            </button>
            <button
              className="rounded bg-primary px-4 py-2 text-label-sm font-bold text-white hover:bg-primary-hover disabled:opacity-40"
              disabled={!version || isDeployed || deploying || testPending}
              onClick={onDeploy}
              type="button"
            >
              {deploying ? "Đang phát hành" : isDeployed ? "Đã phát hành" : "Phát hành bản này"}
            </button>
          </div>
        </div>
      </div>
    </section>
  );
}

export function AccuracyPanel({ items, loading }: { readonly items: readonly KbAccuracySummary[]; readonly loading: boolean }) {
  const tested = items.filter((item) => item.latestAccuracyPercent !== null);
  const average = tested.length
    ? tested.reduce((sum, item) => sum + (accuracyPercent(item.latestAccuracyPercent) ?? 0), 0) / tested.length
    : null;

  return (
    <section className="mt-gutter">
      <div className="mb-stack-md flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h2 className="text-headline-sm font-bold text-secondary">Độ chính xác kho tri thức</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">Kết quả kiểm định theo từng nhóm tri thức và bản mới nhất.</p>
        </div>
        <div className="font-mono text-mono-status text-on-surface-variant">
          Trung bình: <span className="font-bold text-secondary">{accuracyLabel(average)}</span>
        </div>
      </div>

      <Card className="p-0">
        {loading ? (
          <p className="p-card-padding text-body-md text-on-surface-variant">Đang tải báo cáo độ chính xác...</p>
        ) : items.length ? (
          <div className="divide-y divide-outline">
            {items.map((item) => {
              const score = accuracyPercent(item.latestAccuracyPercent);
              return (
                <div className="grid gap-3 p-4 md:grid-cols-[220px_minmax(0,1fr)_90px_180px] md:items-center" key={item.kbModuleId}>
                  <div className="min-w-0">
                    <p className="truncate text-body-md font-bold text-secondary">{item.name}</p>
                    <p className="mt-1 font-mono text-label-sm text-on-surface-variant">
                      {item.code} · v{item.latestVersion ?? "--"}
                    </p>
                  </div>
                  <div className="h-2 overflow-hidden rounded-full bg-surface-variant">
                    <div className={`h-full rounded-full ${accuracyTone(item.latestAccuracyPercent)}`} style={{ width: `${score ?? 0}%` }} />
                  </div>
                  <p className="font-mono text-mono-status font-bold text-secondary">{accuracyLabel(item.latestAccuracyPercent)}</p>
                  <p className="text-label-sm text-on-surface-variant">{formatDateTime(item.lastTestedAt)}</p>
                </div>
              );
            })}
          </div>
        ) : (
          <p className="p-card-padding text-body-md text-on-surface-variant">Chưa có nhóm tri thức nào để tổng hợp độ chính xác.</p>
        )}
      </Card>
    </section>
  );
}

export function DiffDrawer({ diff, onClose }: { readonly diff: KbVersionDiff | null; readonly onClose: () => void }) {
  if (!diff) return null;
  return (
    <div className="fixed inset-0 z-[90] flex justify-end bg-black/40">
      <aside className="flex h-full w-full max-w-[620px] flex-col bg-white shadow-2xl">
        <div className="flex items-center justify-between border-b border-outline p-5">
          <div>
            <p className="text-label-caps uppercase text-on-surface-variant">So sánh thay đổi</p>
            <h2 className="mt-1 text-headline-sm font-bold text-secondary">
              Bản {diff.fromVersion} → bản {diff.toVersion}
            </h2>
          </div>
          <button aria-label="Đóng phần so sánh" className="rounded-full p-2 hover:bg-surface-variant" onClick={onClose} type="button">
            <span aria-hidden="true" className="material-symbols-outlined">close</span>
          </button>
        </div>
        <div className="flex gap-3 border-b border-outline p-4 font-mono text-mono-status">
          <span className="rounded bg-success/10 px-2 py-1 text-success">+{diff.linesAdded} dòng</span>
          <span className="rounded bg-error/10 px-2 py-1 text-error">-{diff.linesRemoved} dòng</span>
        </div>
        <pre className="flex-1 overflow-auto bg-[#111827] p-5 font-mono text-[12px] leading-6 text-slate-200">{diff.unifiedDiff}</pre>
      </aside>
    </div>
  );
}
