import { useCallback, useMemo, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { AppShell } from "@/shared/layout/AppShell";
import {
  Alert,
  Button,
  Card,
  InfiniteScrollSentinel,
  StatusPill,
  useInfiniteList,
  type StatusTone,
} from "@/shared/ui";
import { useJobWatcher } from "@/features/jobs/useJobWatcher";
import { toUserFriendlyError } from "@/shared/utils/userText";
import {
  createDocumentTemplate,
  deleteDocumentTemplate,
  generateDocument,
  generateDocumentKit,
  listDocumentTemplates,
  listGeneratedDocuments,
  updateDocumentTemplate,
  type DocumentListResponse,
  type DocumentTemplate,
  type GeneratedDocument,
  type TemplateField,
} from "@/shared/api/documents";
import { useGeneratedDocumentUrl, useOpenGeneratedDocument } from "./useGeneratedDocumentFile";
import { DocumentFieldsForm } from "./DocumentFieldsForm";
import { DocumentPreview } from "./DocumentPreview";
import { TemplateFieldsEditor } from "./TemplateFieldsEditor";
import {
  applyVars,
  cleanVars,
  formFieldsFor,
  missingRequired,
  sampleVars,
  syncFieldsWithBody,
  TEMPLATE_PRESETS,
  type TemplatePreset,
} from "./templateModel";

type NoticeTone = "info" | "success" | "warning" | "error";
type PreviewMode = "fill" | "document";

interface NoticeState {
  readonly tone: NoticeTone;
  readonly message: string;
}

interface TemplateDraft {
  readonly id: string | null;
  readonly code: string;
  readonly docType: string;
  readonly body: string;
  readonly fields: readonly TemplateField[];
}

const EMPTY_TEMPLATES: readonly DocumentTemplate[] = [];
const EMPTY_DOCUMENTS: readonly GeneratedDocument[] = [];

function draftFromPreset(preset: TemplatePreset): TemplateDraft {
  return { id: null, code: preset.code, docType: preset.docType, body: preset.body, fields: preset.fields };
}

function draftFromTemplate(template: DocumentTemplate): TemplateDraft {
  return {
    id: template.id,
    code: template.code,
    docType: template.docType,
    body: template.templateHtml,
    fields: formFieldsFor(template),
  };
}

function normalize(value: string | null | undefined): string {
  return (value ?? "").trim().toLowerCase();
}

function errorMessage(error: unknown): string {
  return toUserFriendlyError(error, "Không xử lý được thao tác tài liệu. Vui lòng thử lại.");
}

function formatDateTime(value: string | null | undefined): string {
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

function docTypeLabel(value: string): string {
  const normalized = normalize(value);
  if (normalized === "quote") return "Báo giá";
  if (normalized === "brochure") return "Tờ giới thiệu";
  if (normalized === "onboarding") return "Hồ sơ nhập học";
  if (normalized === "slide") return "Bài trình chiếu";
  return value || "Tài liệu";
}

function docTone(doc: GeneratedDocument): StatusTone {
  if (doc.openedAt) return "success";
  if (doc.sentAt) return "warning";
  const expiresAt = doc.expiresAt ? new Date(doc.expiresAt).getTime() : null;
  if (expiresAt && expiresAt < Date.now()) return "error";
  return "neutral";
}

function docStatusLabel(doc: GeneratedDocument): string {
  const expiresAt = doc.expiresAt ? new Date(doc.expiresAt).getTime() : null;
  if (doc.openedAt) return "Đã mở";
  if (doc.sentAt) return `Đã gửi ${doc.sentVia ?? ""}`.trim();
  if (expiresAt && expiresAt < Date.now()) return "Hết hạn";
  return "Sẵn sàng";
}

function firstLine(body: string, max = 90): string {
  const line = body.replace(/\r\n/g, "\n").split("\n").find((item) => item.trim().length > 0) ?? "";
  const clean = line.trim();
  if (clean.length <= max) return clean;
  return `${clean.slice(0, max - 1)}…`;
}

function metricCards(templates: readonly DocumentTemplate[], documents: readonly GeneratedDocument[]) {
  const sent = documents.filter((doc) => doc.sentAt).length;
  const opened = documents.filter((doc) => doc.openedAt).length;
  return [
    { icon: "article", label: "Mẫu tài liệu", value: templates.length, meta: "Đang sử dụng" },
    { icon: "picture_as_pdf", label: "Đã tạo", value: documents.length, meta: "100 tài liệu mới nhất" },
    { icon: "send", label: "Đã gửi", value: sent, meta: "Theo kênh đã chọn" },
    { icon: "visibility", label: "Đã mở", value: opened, meta: "Theo lượt mở/tải" },
  ];
}

function MetricCard({ icon, label, value, meta }: { readonly icon: string; readonly label: string; readonly value: number; readonly meta: string }) {
  return (
    <Card>
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-label-caps uppercase text-on-surface-variant">{label}</p>
          <p className="mt-2 text-telemetry-data text-secondary">{value.toLocaleString("vi-VN")}</p>
          <p className="mt-1 text-label-sm text-on-surface-variant">{meta}</p>
        </div>
        <span aria-hidden="true" className="material-symbols-outlined rounded bg-primary/10 p-2 text-primary">{icon}</span>
      </div>
    </Card>
  );
}

function TemplateList({
  templates,
  selectedId,
  onSelect,
}: {
  readonly templates: readonly DocumentTemplate[];
  readonly selectedId: string | null;
  readonly onSelect: (template: DocumentTemplate) => void;
}) {
  if (!templates.length) {
    return (
      <div className="rounded-lg border border-dashed border-outline bg-surface p-4 text-body-md text-on-surface-variant">
        Chưa có mẫu tài liệu nào. Chọn một mẫu dựng sẵn bên dưới để bắt đầu.
      </div>
    );
  }
  return (
    <div className="space-y-2">
      {templates.map((template) => {
        const fieldCount = formFieldsFor(template).length;
        return (
          <button
            key={template.id}
            type="button"
            onClick={() => onSelect(template)}
            className={`w-full rounded-lg border p-3 text-left transition-colors ${
              selectedId === template.id ? "border-primary bg-red-50" : "border-outline bg-white hover:border-primary/40"
            }`}
          >
            <div className="mb-2 flex items-center justify-between gap-2">
              <span className="font-mono text-mono-status font-bold text-primary">{template.code}</span>
              <StatusPill tone="neutral">{docTypeLabel(template.docType)}</StatusPill>
            </div>
            <p className="text-body-md text-secondary">{firstLine(template.templateHtml)}</p>
            <p className="mt-2 text-label-sm text-on-surface-variant">
              {fieldCount} trường cần nhập · cập nhật {formatDateTime(template.updatedAt)}
            </p>
          </button>
        );
      })}
    </div>
  );
}

function PresetPicker({ onPick }: { readonly onPick: (preset: TemplatePreset) => void }) {
  return (
    <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
      {TEMPLATE_PRESETS.map((preset) => (
        <button
          key={preset.id}
          type="button"
          onClick={() => onPick(preset)}
          className="rounded-lg border border-outline bg-white p-3 text-left transition-colors hover:border-primary/60"
        >
          <span className="block text-body-md font-bold text-secondary">{preset.name}</span>
          <span className="mt-1 block text-label-sm text-on-surface-variant">{preset.description}</span>
        </button>
      ))}
    </div>
  );
}

function TemplateEditor({
  draft,
  saving,
  deleting,
  error,
  onDraft,
  onPickPreset,
  onSave,
  onDelete,
}: {
  readonly draft: TemplateDraft;
  readonly saving: boolean;
  readonly deleting: boolean;
  readonly error: unknown;
  readonly onDraft: (draft: TemplateDraft) => void;
  readonly onPickPreset: (preset: TemplatePreset) => void;
  readonly onSave: () => void;
  readonly onDelete: () => void;
}) {
  const [open, setOpen] = useState(false);

  return (
    <Card>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-headline-sm text-secondary">Thiết lập mẫu</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">
            Dành cho người quản lý: chọn mẫu dựng sẵn hoặc chỉnh nội dung và các trường cần nhập.
          </p>
        </div>
        <Button type="button" variant="outline" size="sm" onClick={() => setOpen((value) => !value)}>
          <span aria-hidden="true" className="material-symbols-outlined text-[16px]">{open ? "expand_less" : "expand_more"}</span>
          {open ? "Thu gọn" : "Mở thiết lập"}
        </Button>
      </div>

      {!open ? null : (
        <div className="mt-4">
          {error ? <Alert tone="error">{errorMessage(error)}</Alert> : null}

          <p className="mb-2 mt-4 text-label-caps uppercase text-secondary">Mẫu dựng sẵn</p>
          <PresetPicker onPick={onPickPreset} />

          <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-[minmax(0,1fr)_150px]">
            <label className="block">
              <span className="mb-1 block text-label-caps uppercase text-secondary">Mã mẫu</span>
              <input
                className="w-full rounded border border-outline bg-white px-3 py-2 font-mono text-mono-status outline-none focus:border-primary disabled:bg-surface"
                value={draft.code}
                disabled={Boolean(draft.id)}
                onChange={(event) => onDraft({ ...draft, code: event.target.value.toUpperCase() })}
                placeholder="BAO-GIA-KHOA-HOC"
              />
            </label>
            <label className="block">
              <span className="mb-1 block text-label-caps uppercase text-secondary">Loại</span>
              <select
                className="w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
                value={draft.docType}
                onChange={(event) => onDraft({ ...draft, docType: event.target.value })}
              >
                <option value="quote">Báo giá</option>
                <option value="brochure">Tờ giới thiệu</option>
                <option value="onboarding">Hồ sơ nhập học</option>
                <option value="slide">Bài trình chiếu</option>
              </select>
            </label>
          </div>

          <label className="mt-3 block">
            <span className="mb-1 block text-label-caps uppercase text-secondary">Nội dung mẫu</span>
            <textarea
              className="min-h-[220px] w-full resize-y rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
              value={draft.body}
              onChange={(event) => onDraft({ ...draft, body: event.target.value })}
              placeholder={"BÁO GIÁ KHÓA HỌC\n\nKính gửi {{ ten_khach }},"}
            />
            <span className="mt-1 block text-label-sm text-on-surface-variant">
              Viết như văn bản thường. Dòng đầu tiên thành tiêu đề. Chỗ nào cần điền thì đặt {"{{ ten_truong }}"}.
            </span>
          </label>

          <div className="mt-4">
            <TemplateFieldsEditor
              fields={draft.fields}
              onChange={(fields) => onDraft({ ...draft, fields })}
              onSyncFromBody={() => onDraft({ ...draft, fields: syncFieldsWithBody(draft.body, draft.fields) })}
            />
          </div>

          <div className="mt-4 flex flex-wrap gap-2">
            <Button type="button" onClick={onSave} disabled={saving || !draft.code.trim() || !draft.body.trim()}>
              <span aria-hidden="true" className="material-symbols-outlined text-[18px]">save</span>
              {saving ? "Đang lưu..." : draft.id ? "Cập nhật mẫu" : "Lưu mẫu mới"}
            </Button>
            {draft.id ? (
              <Button type="button" variant="ghost" onClick={onDelete} disabled={deleting}>
                <span aria-hidden="true" className="material-symbols-outlined text-[18px]">delete</span>
                Xóa mẫu
              </Button>
            ) : null}
          </div>
        </div>
      )}
    </Card>
  );
}

function GeneratePanel({
  templates,
  templateCode,
  fields,
  values,
  missingKeys,
  contactId,
  sentVia,
  generating,
  generatingKit,
  error,
  onTemplateCode,
  onValue,
  onFillSample,
  onContactId,
  onSentVia,
  onGenerate,
  onGenerateKit,
}: {
  readonly templates: readonly DocumentTemplate[];
  readonly templateCode: string;
  readonly fields: readonly TemplateField[];
  readonly values: Readonly<Record<string, string>>;
  readonly missingKeys: readonly string[];
  readonly contactId: string;
  readonly sentVia: string;
  readonly generating: boolean;
  readonly generatingKit: boolean;
  readonly error: unknown;
  readonly onTemplateCode: (value: string) => void;
  readonly onValue: (key: string, value: string) => void;
  readonly onFillSample: () => void;
  readonly onContactId: (value: string) => void;
  readonly onSentVia: (value: string) => void;
  readonly onGenerate: () => void;
  readonly onGenerateKit: () => void;
}) {
  const busy = generating || generatingKit;
  return (
    <Card>
      <div className="mb-4 flex flex-wrap items-start justify-between gap-2">
        <div>
          <h2 className="text-headline-sm text-secondary">Điền thông tin và tạo</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">
            Chọn mẫu, điền các ô bên dưới. Chọn gửi email để hệ thống gửi luôn cho khách.
          </p>
        </div>
        {fields.length ? (
          <Button type="button" variant="ghost" size="sm" onClick={onFillSample}>
            <span aria-hidden="true" className="material-symbols-outlined text-[16px]">edit_note</span>
            Điền dữ liệu mẫu
          </Button>
        ) : null}
      </div>
      {error ? <Alert tone="error">{errorMessage(error)}</Alert> : null}

      <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2">
        <label className="block">
          <span className="mb-1 block text-label-caps uppercase text-secondary">Mẫu tài liệu</span>
          <select
            className="w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
            value={templateCode}
            onChange={(event) => onTemplateCode(event.target.value)}
          >
            <option value="">Chọn mẫu tài liệu</option>
            {templates.map((template) => (
              <option key={template.id} value={template.code}>
                {template.code} — {docTypeLabel(template.docType)}
              </option>
            ))}
          </select>
        </label>
        <label className="block">
          <span className="mb-1 block text-label-caps uppercase text-secondary">Gửi qua</span>
          <select
            className="w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
            value={sentVia}
            onChange={(event) => onSentVia(event.target.value)}
          >
            <option value="">Chỉ tạo tài liệu</option>
            <option value="email">Email</option>
          </select>
        </label>
      </div>

      <div className="mt-4">
        {!templateCode ? (
          <div className="rounded-lg border border-dashed border-outline bg-surface p-4 text-body-md text-on-surface-variant">
            Chọn một mẫu để hiện các ô cần điền.
          </div>
        ) : (
          <DocumentFieldsForm fields={fields} values={values} missingKeys={missingKeys} onChange={onValue} />
        )}
      </div>

      <label className="mt-3 block">
        <span className="mb-1 block text-label-caps uppercase text-secondary">Gắn khách hàng (không bắt buộc)</span>
        <input
          className="w-full rounded border border-outline bg-white px-3 py-2 font-mono text-mono-status outline-none focus:border-primary"
          value={contactId}
          onChange={(event) => onContactId(event.target.value)}
          placeholder="Dán mã khách hàng để tự điền tên, điện thoại, email"
        />
      </label>

      <div className="mt-4 flex flex-wrap gap-2">
        <Button type="button" onClick={onGenerate} disabled={busy || !templateCode}>
          <span aria-hidden="true" className="material-symbols-outlined text-[18px]">{sentVia === "email" ? "outgoing_mail" : "picture_as_pdf"}</span>
          {generating ? "Đang xử lý..." : sentVia === "email" ? "Tạo và gửi email" : "Tạo tài liệu"}
        </Button>
        <Button type="button" variant="outline" onClick={onGenerateKit} disabled={busy} data-testid="Generate kit">
          <span aria-hidden="true" className="material-symbols-outlined text-[18px]">inventory_2</span>
          {generatingKit ? "Đang tạo bộ tài liệu..." : "Tạo bộ tài liệu"}
        </Button>
      </div>
    </Card>
  );
}

// Tên file lúc lưu về máy: lấy từ fileUrl (đã là "<template>-<timestamp>-<guid>.pdf"), fallback theo id.
function downloadFileName(doc: GeneratedDocument): string {
  const fromUrl = (doc.fileUrl ?? "").split("?")[0].split("/").filter(Boolean).pop();
  return fromUrl && fromUrl.toLowerCase().endsWith(".pdf") ? fromUrl : `tai-lieu-${doc.id}.pdf`;
}

function GeneratedList({
  documents,
  templatesById,
  selectedId,
  onSelect,
  onOpenFile,
  openPendingId,
}: {
  readonly documents: readonly GeneratedDocument[];
  readonly templatesById: ReadonlyMap<string, DocumentTemplate>;
  readonly selectedId: string | null;
  readonly onSelect: (document: GeneratedDocument) => void;
  readonly onOpenFile: (document: GeneratedDocument) => Promise<void>;
  readonly openPendingId: string | null;
}) {
  if (!documents.length) {
    return (
      <div className="rounded-lg border border-dashed border-outline bg-surface p-4 text-body-md text-on-surface-variant">
        Chưa có tài liệu nào được tạo.
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="min-w-[760px] w-full border-collapse text-left">
        <thead className="bg-surface-variant text-label-sm uppercase text-secondary">
          <tr>
            <th className="px-4 py-3 font-bold">Tài liệu</th>
            <th className="px-4 py-3 font-bold">Khách hàng</th>
            <th className="px-4 py-3 font-bold">Trạng thái</th>
            <th className="px-4 py-3 font-bold">Hiệu lực</th>
            <th className="px-4 py-3 text-right font-bold">Thao tác</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-outline bg-white">
          {documents.map((doc) => {
            const template = templatesById.get(doc.templateId);
            return (
              <tr key={doc.id} className={selectedId === doc.id ? "bg-red-50/70" : "hover:bg-surface-container-low"}>
                <td className="px-4 py-4 align-top">
                  <button type="button" className="block text-left" onClick={() => onSelect(doc)}>
                    <span className="block font-mono text-mono-status font-bold text-primary">
                      {template?.code ?? doc.templateId.slice(0, 8)}
                    </span>
                    <span className="mt-1 block text-label-sm text-on-surface-variant">{formatDateTime(doc.createdAt)}</span>
                  </button>
                </td>
                <td className="px-4 py-4 align-top text-body-md text-secondary">{doc.contactId?.slice(0, 8) ?? "Chưa gắn khách hàng"}</td>
                <td className="px-4 py-4 align-top">
                  <StatusPill tone={docTone(doc)}>{docStatusLabel(doc)}</StatusPill>
                </td>
                <td className="px-4 py-4 align-top text-body-md text-secondary">{formatDateTime(doc.expiresAt)}</td>
                <td className="px-4 py-4 align-top">
                  <div className="flex justify-end gap-2">
                    <Button type="button" size="sm" variant="outline" onClick={() => onSelect(doc)}>
                      Xem trước
                    </Button>
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      disabled={openPendingId === doc.id}
                      onClick={() => void onOpenFile(doc)}
                    >
                      {openPendingId === doc.id ? "Đang tải…" : "Mở file"}
                    </Button>
                  </div>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function PreviewPanel({
  mode,
  body,
  document,
  onMode,
  onOpenFile,
  openPendingId,
}: {
  readonly mode: PreviewMode;
  readonly body: string;
  readonly document: GeneratedDocument | null;
  readonly onMode: (mode: PreviewMode) => void;
  readonly onOpenFile: (document: GeneratedDocument) => Promise<void>;
  readonly openPendingId: string | null;
}) {
  // Chỉ tải PDF khi thực sự đang xem tab "File đã tạo".
  const fileState = useGeneratedDocumentUrl(mode === "document" ? (document?.id ?? null) : null);

  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-outline p-card-padding">
        <div>
          <h2 className="text-headline-sm text-secondary">Xem trước</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">Bản xem trước dựng đúng cách file PDF sẽ in ra.</p>
        </div>
        <div className="flex rounded border border-outline bg-white p-1">
          {(["fill", "document"] as const).map((item) => (
            <button
              key={item}
              type="button"
              onClick={() => onMode(item)}
              className={`rounded px-3 py-1.5 font-mono text-mono-status ${
                mode === item ? "bg-primary text-on-primary" : "text-on-surface-variant hover:bg-surface-container-low"
              }`}
            >
              {item === "fill" ? "Bản nháp" : "File đã tạo"}
            </button>
          ))}
        </div>
      </div>
      {mode === "document" && document ? (
        <div className="p-card-padding">
          <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
            <StatusPill tone={docTone(document)}>{docStatusLabel(document)}</StatusPill>
            <button
              type="button"
              className="text-label-sm font-bold text-primary hover:underline disabled:opacity-60"
              disabled={openPendingId === document.id}
              onClick={() => void onOpenFile(document)}
            >
              {openPendingId === document.id ? "Đang tải…" : "Mở tài liệu"}
            </button>
          </div>
          {fileState.error ? (
            <Alert tone="error">{fileState.error}</Alert>
          ) : fileState.url ? (
            <iframe
              className="h-[560px] w-full rounded-lg border border-outline bg-white"
              src={fileState.url}
              title="Xem trước tài liệu đã tạo"
            />
          ) : (
            <div className="flex h-[560px] w-full items-center justify-center rounded-lg border border-outline bg-white text-body-md text-on-surface-variant">
              {fileState.isLoading ? "Đang tải file PDF…" : "Chưa có file để hiển thị."}
            </div>
          )}
        </div>
      ) : (
        <div className="p-card-padding">
          {body.trim() ? (
            <DocumentPreview body={body} />
          ) : (
            <div className="rounded-lg border border-dashed border-outline bg-surface p-4 text-body-md text-on-surface-variant">
              Chọn mẫu tài liệu để xem trước.
            </div>
          )}
        </div>
      )}
    </Card>
  );
}

export default function DocumentsPage() {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<TemplateDraft>(() => draftFromPreset(TEMPLATE_PRESETS[0]!));
  const [selectedTemplateId, setSelectedTemplateId] = useState<string | null>(null);
  const [selectedDocId, setSelectedDocId] = useState<string | null>(null);
  const [generateTemplateCode, setGenerateTemplateCode] = useState("");
  const [contactId, setContactId] = useState("");
  const [values, setValues] = useState<Record<string, string>>({});
  const [missingKeys, setMissingKeys] = useState<readonly string[]>([]);
  const [sentVia, setSentVia] = useState("");
  const [previewMode, setPreviewMode] = useState<PreviewMode>("fill");
  const [notice, setNotice] = useState<NoticeState | null>(null);

  const templatesList = useInfiniteList<DocumentTemplate, DocumentListResponse<DocumentTemplate>>({
    queryKey: ["documents", "templates"],
    initialPageParam: 1,
    queryFn: (pageParam) =>
      listDocumentTemplates({
        page: typeof pageParam === "number" ? pageParam : 1,
        pageSize: 50,
      }),
  });
  const generatedList = useInfiniteList<GeneratedDocument, DocumentListResponse<GeneratedDocument>>({
    queryKey: ["documents", "generated"],
    initialPageParam: 1,
    queryFn: (pageParam) =>
      listGeneratedDocuments({
        page: typeof pageParam === "number" ? pageParam : 1,
        pageSize: 50,
      }),
  });
  const templatesQuery = templatesList.query;
  const generatedQuery = generatedList.query;
  const templates = templatesList.items.length ? templatesList.items : EMPTY_TEMPLATES;
  const documents = generatedList.items.length ? generatedList.items : EMPTY_DOCUMENTS;
  const templatesById = useMemo(
    () => new Map<string, DocumentTemplate>(templates.map((template) => [template.id, template] as [string, DocumentTemplate])),
    [templates],
  );
  const selectedDocument = documents.find((doc) => doc.id === selectedDocId) ?? documents[0] ?? null;
  const documentFile = useOpenGeneratedDocument();
  const openFile = useCallback(
    (doc: GeneratedDocument) => documentFile.open(doc.id, downloadFileName(doc)),
    [documentFile],
  );
  const generateTemplate = templates.find((template) => template.code === generateTemplateCode) ?? null;
  const formFields = useMemo(() => formFieldsFor(generateTemplate), [generateTemplate]);
  const metrics = metricCards(templates, documents);
  const apiError = templatesQuery.error ?? generatedQuery.error;

  // Đang chỉnh mẫu nào thì xem trước theo bản nháp đó, để thấy ngay thay đổi chưa lưu.
  const previewSource = generateTemplate
    ? draft.id === generateTemplate.id
      ? draft.body
      : generateTemplate.templateHtml
    : draft.body;
  const previewBody = applyVars(previewSource, values);

  // Đổi mẫu thì nạp sẵn giá trị mẫu để người dùng thấy kết quả ngay, không phải nhập từ số 0.
  // Làm ngay trong handler chọn mẫu (không dùng effect) để tránh ghi đè khi người dùng đang nhập.
  function changeTemplateCode(code: string) {
    setGenerateTemplateCode(code);
    setMissingKeys([]);
    const next = templates.find((template) => template.code === code) ?? null;
    setValues(next ? sampleVars(formFieldsFor(next)) : {});
  }

  const saveTemplateMutation = useMutation<DocumentTemplate | null>({
    mutationFn: async () => {
      const fields = syncFieldsWithBody(draft.body, draft.fields);
      if (draft.id) {
        await updateDocumentTemplate(draft.id, { docType: draft.docType, templateHtml: draft.body.trim(), fields });
        return null;
      }
      return createDocumentTemplate({
        code: draft.code.trim().toUpperCase(),
        docType: draft.docType,
        templateHtml: draft.body.trim(),
        fields,
      });
    },
    onSuccess: async (template) => {
      if (template) {
        setDraft(draftFromTemplate(template));
        setSelectedTemplateId(template.id);
        setGenerateTemplateCode(template.code);
      }
      setNotice({ tone: "success", message: "Đã lưu mẫu tài liệu." });
      await queryClient.invalidateQueries({ queryKey: ["documents", "templates"] });
    },
  });

  const deleteTemplateMutation = useMutation({
    mutationFn: (id: string) => deleteDocumentTemplate(id),
    onSuccess: async () => {
      setDraft(draftFromPreset(TEMPLATE_PRESETS[0]!));
      setSelectedTemplateId(null);
      setGenerateTemplateCode("");
      setNotice({ tone: "success", message: "Đã xóa mẫu tài liệu." });
      await queryClient.invalidateQueries({ queryKey: ["documents", "templates"] });
    },
  });

  // Sinh tài liệu chạy ngầm: theo dõi job để tự làm mới danh sách khi xong.
  const [generateJobId, setGenerateJobId] = useState<string | null>(null);
  const generateMutation = useMutation({
    mutationFn: () =>
      generateDocument({
        templateCode: generateTemplateCode,
        contactId: contactId.trim() || null,
        vars: cleanVars(values),
        sentVia: sentVia || null,
      }),
    onSuccess: (job) => {
      setGenerateJobId(job.jobId);
      setNotice({ tone: "info", message: "Đang tạo tài liệu ở chế độ nền. Xong sẽ có thông báo." });
    },
  });

  const generateKitMutation = useMutation({
    mutationFn: () =>
      generateDocumentKit({
        contactId: contactId.trim() || null,
        vars: cleanVars(values),
        sentVia: sentVia || null,
      }),
    onSuccess: (job) => {
      setGenerateJobId(job.jobId);
      setNotice({ tone: "info", message: "Đang tạo bộ tài liệu ở chế độ nền. Xong sẽ có thông báo." });
    },
  });

  // Job nào cũng chỉ cần 1 watcher: sinh 1 doc hay cả bộ đều đổ về danh sách tài liệu.
  useJobWatcher(generateJobId, (job) => {
    setGenerateJobId(null);
    if (job.status === "succeeded") {
      setNotice({ tone: "success", message: job.resultSummary ?? "Đã tạo xong tài liệu." });
      void queryClient.invalidateQueries({ queryKey: ["documents", "generated"] });
    } else if (job.status === "failed") {
      setNotice({ tone: "error", message: job.error ?? "Tạo tài liệu thất bại." });
    }
  });

  // Chặn ngay ở form: thiếu trường bắt buộc thì không gọi API để khỏi chờ job rồi mới báo lỗi.
  function startGenerate() {
    if (!generateTemplateCode) {
      setNotice({ tone: "warning", message: "Chọn mẫu tài liệu trước khi tạo." });
      return;
    }
    const missing = missingRequired(formFields, values);
    if (missing.length) {
      setMissingKeys(missing.map((field) => field.key));
      setNotice({
        tone: "warning",
        message: `Còn thiếu: ${missing.map((field) => field.label).join(", ")}.`,
      });
      return;
    }
    setMissingKeys([]);
    setPreviewMode("fill");
    generateMutation.mutate();
  }

  function selectTemplate(template: DocumentTemplate) {
    setSelectedTemplateId(template.id);
    setDraft(draftFromTemplate(template));
    setGenerateTemplateCode(template.code);
    setValues(sampleVars(formFieldsFor(template)));
    setMissingKeys([]);
    setPreviewMode("fill");
  }

  function pickPreset(preset: TemplatePreset) {
    setSelectedTemplateId(null);
    setDraft(draftFromPreset(preset));
    setGenerateTemplateCode("");
    setValues(sampleVars(preset.fields));
    setPreviewMode("fill");
  }

  return (
    <AppShell title="Thư viện tài liệu">
      <section className="mb-gutter rounded-lg border border-primary/20 bg-primary/5 p-4">
        <div className="flex flex-col gap-3 xl:flex-row xl:items-center xl:justify-between">
          <div>
            <h1 className="text-headline-md text-secondary">Thư viện tài liệu & gửi báo giá</h1>
            <p className="mt-1 text-body-md text-on-surface-variant">
              Chọn mẫu, điền thông tin, hệ thống tạo file PDF và gửi cho khách.
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            <StatusPill tone={apiError ? "error" : "success"}>{apiError ? "Mất kết nối" : "Đã kết nối"}</StatusPill>
            <Button
              type="button"
              variant="outline"
              onClick={() => {
                void queryClient.invalidateQueries({ queryKey: ["documents"] });
              }}
            >
              <span aria-hidden="true" className="material-symbols-outlined text-[18px]">refresh</span>
              Làm mới
            </Button>
          </div>
        </div>
      </section>

      {notice ? (
        <div className="mb-gutter">
          <Alert tone={notice.tone}>{notice.message}</Alert>
        </div>
      ) : null}

      <section className="mb-gutter grid grid-cols-1 gap-gutter sm:grid-cols-2 xl:grid-cols-4">
        {metrics.map((metric) => (
          <MetricCard key={metric.label} {...metric} />
        ))}
      </section>

      <section className="grid grid-cols-1 gap-gutter xl:grid-cols-[390px_minmax(0,1fr)]">
        <div className="space-y-gutter">
          <Card>
            <div className="mb-4 flex items-center justify-between gap-3">
              <div>
                <h2 className="text-headline-sm text-secondary">Kho mẫu</h2>
                <p className="mt-1 text-body-md text-on-surface-variant">Báo giá, hồ sơ nhập học, tờ giới thiệu...</p>
              </div>
              <StatusPill tone="neutral">{templates.length}</StatusPill>
            </div>
            <TemplateList templates={templates} selectedId={selectedTemplateId} onSelect={selectTemplate} />
            <InfiniteScrollSentinel
              hasNextPage={templatesList.hasNextPage}
              isFetchingNextPage={templatesList.isFetchingNextPage}
              onLoadMore={templatesList.fetchNextPage}
            />
          </Card>

          <TemplateEditor
            draft={draft}
            saving={saveTemplateMutation.isPending}
            deleting={deleteTemplateMutation.isPending}
            error={saveTemplateMutation.error ?? deleteTemplateMutation.error}
            onDraft={setDraft}
            onPickPreset={pickPreset}
            onSave={() => saveTemplateMutation.mutate()}
            onDelete={() => {
              if (draft.id) deleteTemplateMutation.mutate(draft.id);
            }}
          />
        </div>

        <div className="space-y-gutter">
          <div className="grid grid-cols-1 gap-gutter 2xl:grid-cols-[minmax(0,1fr)_380px]">
            <PreviewPanel
              mode={previewMode}
              body={previewBody}
              document={selectedDocument}
              onMode={setPreviewMode}
              onOpenFile={openFile}
              openPendingId={documentFile.pendingId}
            />
            <GeneratePanel
              templates={templates}
              templateCode={generateTemplateCode}
              fields={formFields}
              values={values}
              missingKeys={missingKeys}
              contactId={contactId}
              sentVia={sentVia}
              generating={generateMutation.isPending}
              generatingKit={generateKitMutation.isPending}
              error={generateMutation.error ?? generateKitMutation.error}
              onTemplateCode={changeTemplateCode}
              onValue={(key, value) => {
                setValues((previous) => ({ ...previous, [key]: value }));
                setMissingKeys((previous) => previous.filter((item) => item !== key));
              }}
              onFillSample={() => setValues((previous) => ({ ...previous, ...sampleVars(formFields) }))}
              onContactId={setContactId}
              onSentVia={setSentVia}
              onGenerate={startGenerate}
              onGenerateKit={() => generateKitMutation.mutate()}
            />
          </div>

          <Card className="p-0">
            <div className="flex flex-wrap items-center justify-between gap-3 border-b border-outline p-card-padding">
              <div>
                <h2 className="text-headline-sm text-secondary">Tài liệu đã tạo</h2>
                <p className="mt-1 text-body-md text-on-surface-variant">Tệp tài liệu, trạng thái gửi/mở và hạn liên kết 7 ngày.</p>
              </div>
              {generatedQuery.isLoading ? <StatusPill tone="warning">Đang tải</StatusPill> : <StatusPill tone="neutral">{documents.length}</StatusPill>}
            </div>
            {generatedQuery.error ? (
              <div className="p-card-padding">
                <Alert tone="error">{errorMessage(generatedQuery.error)}</Alert>
              </div>
            ) : (
              <>
                {documentFile.error ? (
                  <div className="px-card-padding pt-card-padding">
                    <Alert tone="error">{documentFile.error}</Alert>
                  </div>
                ) : null}
                <GeneratedList
                  documents={documents}
                  templatesById={templatesById}
                  selectedId={selectedDocument?.id ?? null}
                  onSelect={(doc) => {
                    setSelectedDocId(doc.id);
                    setPreviewMode("document");
                  }}
                  onOpenFile={openFile}
                  openPendingId={documentFile.pendingId}
                />
                <InfiniteScrollSentinel
                  hasNextPage={generatedList.hasNextPage}
                  isFetchingNextPage={generatedList.isFetchingNextPage}
                  onLoadMore={generatedList.fetchNextPage}
                />
              </>
            )}
          </Card>
        </div>
      </section>
    </AppShell>
  );
}
