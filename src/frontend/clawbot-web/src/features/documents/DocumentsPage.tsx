import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AppShell } from "@/shared/layout/AppShell";
import { Alert, Button, Card, StatusPill, type StatusTone } from "@/shared/ui";
import { toUserFriendlyError } from "@/shared/utils/userText";
import {
  createDocumentTemplate,
  deleteDocumentTemplate,
  documentDownloadUrl,
  generateDocument,
  generateDocumentKit,
  listDocumentTemplates,
  listGeneratedDocuments,
  updateDocumentTemplate,
  type DocumentTemplate,
  type GeneratedDocument,
} from "@/shared/api/documents";

type NoticeTone = "info" | "success" | "warning" | "error";
type PreviewMode = "template" | "document";

interface NoticeState {
  readonly tone: NoticeTone;
  readonly message: string;
}

interface TemplateDraft {
  readonly id: string | null;
  readonly code: string;
  readonly docType: string;
  readonly templateHtml: string;
}

const EMPTY_TEMPLATES: readonly DocumentTemplate[] = [];
const EMPTY_DOCUMENTS: readonly GeneratedDocument[] = [];

const NEW_TEMPLATE: TemplateDraft = {
  id: null,
  code: "",
  docType: "quote",
  templateHtml:
    "<h1>Báo giá khóa học</h1>\n<p>Xin chào {{ ten_khach }},</p>\n<p>Gói học phù hợp: {{ khoa_hoc }}.</p>\n<p>Học phí ưu đãi: {{ hoc_phi }}.</p>",
};

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

function compact(value: string, max = 96): string {
  const clean = value.replace(/\s+/g, " ").trim();
  if (clean.length <= max) return clean;
  return `${clean.slice(0, max - 1)}…`;
}

function templateToDraft(template: DocumentTemplate): TemplateDraft {
  return {
    id: template.id,
    code: template.code,
    docType: template.docType,
    templateHtml: template.templateHtml,
  };
}

function parseVars(value: string): Record<string, string> | null {
  const trimmed = value.trim();
  if (!trimmed) return null;
  if (trimmed.startsWith("{")) {
    const parsed = JSON.parse(trimmed) as unknown;
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
      throw new Error("Dữ liệu điền mẫu phải là danh sách tên và giá trị.");
    }
    return Object.fromEntries(
      Object.entries(parsed).map(([key, rawValue]) => [key, typeof rawValue === "string" ? rawValue : String(rawValue)])
    );
  }

  const entries = trimmed
    .split("\n")
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => {
      const index = line.indexOf("=");
      if (index <= 0) throw new Error("Mỗi dòng cần có tên và giá trị tương ứng.");
      return [line.slice(0, index).trim(), line.slice(index + 1).trim()];
    });
  return Object.fromEntries(entries);
}

function applyVars(templateHtml: string, varsText: string): string {
  try {
    const vars = parseVars(varsText) ?? {};
    return templateHtml.replace(/\{\{\s*([\w.-]+)\s*\}\}/g, (_match, key: string) => vars[key] ?? `{{ ${key} }}`);
  } catch {
    return templateHtml;
  }
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
        Chưa có mẫu tài liệu nào.
      </div>
    );
  }
  return (
    <div className="space-y-2">
      {templates.map((template) => (
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
          <p className="text-label-sm text-on-surface-variant">{compact(template.templateHtml, 120)}</p>
          <p className="mt-2 text-label-sm text-on-surface-variant">Cập nhật {formatDateTime(template.updatedAt)}</p>
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
  onNew,
  onSave,
  onDelete,
}: {
  readonly draft: TemplateDraft;
  readonly saving: boolean;
  readonly deleting: boolean;
  readonly error: unknown;
  readonly onDraft: (draft: TemplateDraft) => void;
  readonly onNew: () => void;
  readonly onSave: () => void;
  readonly onDelete: () => void;
}) {
  return (
    <Card>
      <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-headline-sm text-secondary">Mẫu tài liệu</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">Quản lý mẫu nội dung cho agent tài liệu.</p>
        </div>
        <Button type="button" variant="outline" size="sm" onClick={onNew}>
          <span aria-hidden="true" className="material-symbols-outlined text-[16px]">add</span>
          Mẫu mới
        </Button>
      </div>

      {error ? <Alert tone="error">{errorMessage(error)}</Alert> : null}

      <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-[minmax(0,1fr)_150px]">
        <label className="block">
          <span className="mb-1 block text-label-caps uppercase text-secondary">Mã mẫu</span>
          <input
            className="w-full rounded border border-outline bg-white px-3 py-2 font-mono text-mono-status outline-none focus:border-primary disabled:bg-surface"
            value={draft.code}
            disabled={Boolean(draft.id)}
            onChange={(event) => onDraft({ ...draft, code: event.target.value.toUpperCase() })}
            placeholder="BAO-GIA-HSK"
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
          className="min-h-[240px] w-full resize-y rounded border border-outline bg-white px-3 py-2 font-mono text-mono-status outline-none focus:border-primary"
          value={draft.templateHtml}
          onChange={(event) => onDraft({ ...draft, templateHtml: event.target.value })}
          placeholder="<h1>{{ ten_khach }}</h1>"
        />
      </label>

      <div className="mt-4 flex flex-wrap gap-2">
        <Button type="button" onClick={onSave} disabled={saving || !draft.code.trim() || !draft.templateHtml.trim()}>
          <span aria-hidden="true" className="material-symbols-outlined text-[18px]">save</span>
          {saving ? "Đang lưu..." : draft.id ? "Cập nhật mẫu" : "Tạo mẫu"}
        </Button>
        {draft.id ? (
          <Button type="button" variant="ghost" onClick={onDelete} disabled={deleting}>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">delete</span>
            Xóa mẫu
          </Button>
        ) : null}
      </div>
    </Card>
  );
}

function GeneratePanel({
  templates,
  templateCode,
  contactId,
  varsText,
  sentVia,
  generating,
  generatingKit,
  error,
  onTemplateCode,
  onContactId,
  onVarsText,
  onSentVia,
  onGenerate,
  onGenerateKit,
}: {
  readonly templates: readonly DocumentTemplate[];
  readonly templateCode: string;
  readonly contactId: string;
  readonly varsText: string;
  readonly sentVia: string;
  readonly generating: boolean;
  readonly generatingKit: boolean;
  readonly error: unknown;
  readonly onTemplateCode: (value: string) => void;
  readonly onContactId: (value: string) => void;
  readonly onVarsText: (value: string) => void;
  readonly onSentVia: (value: string) => void;
  readonly onGenerate: () => void;
  readonly onGenerateKit: () => void;
}) {
  const busy = generating || generatingKit;
  return (
    <Card>
      <div className="mb-4">
        <h2 className="text-headline-sm text-secondary">Tạo và gửi</h2>
        <p className="mt-1 text-body-md text-on-surface-variant">Chọn gửi email để hệ thống gửi tài liệu và ghi nhận trạng thái đã gửi.</p>
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
                {template.code}
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
      <label className="mt-3 block">
        <span className="mb-1 block text-label-caps uppercase text-secondary">Mã khách hàng</span>
        <input
          className="w-full rounded border border-outline bg-white px-3 py-2 font-mono text-mono-status outline-none focus:border-primary"
          value={contactId}
          onChange={(event) => onContactId(event.target.value)}
          placeholder="Nhập mã khách hàng nếu có"
        />
      </label>
      <label className="mt-3 block">
        <span className="mb-1 block text-label-caps uppercase text-secondary">Dữ liệu điền mẫu</span>
        <textarea
          className="min-h-[132px] w-full resize-y rounded border border-outline bg-white px-3 py-2 font-mono text-mono-status outline-none focus:border-primary"
          value={varsText}
          onChange={(event) => onVarsText(event.target.value)}
          placeholder={"ten_khach=Nguyễn Minh Anh\nkhoa_hoc=HSK 4 cấp tốc\nhoc_phi=4.500.000đ"}
        />
      </label>
      <div className="mt-4 flex flex-wrap gap-2">
        <Button type="button" onClick={onGenerate} disabled={busy || !templateCode}>
          <span aria-hidden="true" className="material-symbols-outlined text-[18px]">{sentVia === "email" ? "outgoing_mail" : "picture_as_pdf"}</span>
          {generating ? "Đang xử lý..." : sentVia === "email" ? "Tạo và gửi email" : "Tạo tài liệu"}
        </Button>
        <Button type="button" variant="outline" onClick={onGenerateKit} disabled={busy}>
          <span aria-hidden="true" className="material-symbols-outlined text-[18px]">inventory_2</span>
          {generatingKit ? "Đang tạo bộ tài liệu..." : "Tạo bộ tài liệu"}
        </Button>
      </div>
    </Card>
  );
}

function GeneratedList({
  documents,
  templatesById,
  selectedId,
  onSelect,
}: {
  readonly documents: readonly GeneratedDocument[];
  readonly templatesById: ReadonlyMap<string, DocumentTemplate>;
  readonly selectedId: string | null;
  readonly onSelect: (document: GeneratedDocument) => void;
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
                    <a
                      className="inline-flex items-center justify-center rounded border border-outline px-3 py-1.5 text-mono-status font-medium text-on-surface hover:bg-surface-variant"
                      href={doc.fileUrl || documentDownloadUrl(doc.id)}
                      target="_blank"
                      rel="noreferrer"
                    >
                      Mở file
                    </a>
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
  templateHtml,
  varsText,
  document,
  onMode,
}: {
  readonly mode: PreviewMode;
  readonly templateHtml: string;
  readonly varsText: string;
  readonly document: GeneratedDocument | null;
  readonly onMode: (mode: PreviewMode) => void;
}) {
  const previewHtml = applyVars(templateHtml, varsText);
  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-outline p-card-padding">
        <div>
          <h2 className="text-headline-sm text-secondary">Xem trước</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">Mẫu nội dung hoặc tài liệu đã tạo từ agent tài liệu.</p>
        </div>
        <div className="flex rounded border border-outline bg-white p-1">
          {(["template", "document"] as const).map((item) => (
            <button
              key={item}
              type="button"
              onClick={() => onMode(item)}
              className={`rounded px-3 py-1.5 font-mono text-mono-status ${
                mode === item ? "bg-primary text-on-primary" : "text-on-surface-variant hover:bg-surface-container-low"
              }`}
            >
              {item === "template" ? "Mẫu" : "Tài liệu"}
            </button>
          ))}
        </div>
      </div>
      {mode === "document" && document ? (
        <div className="p-card-padding">
          <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
            <StatusPill tone={docTone(document)}>{docStatusLabel(document)}</StatusPill>
            <a className="text-label-sm font-bold text-primary hover:underline" href={document.fileUrl} target="_blank" rel="noreferrer">
              Mở tài liệu
            </a>
          </div>
          <iframe
            className="h-[560px] w-full rounded-lg border border-outline bg-white"
            src={document.fileUrl}
            title="Xem trước tài liệu đã tạo"
          />
        </div>
      ) : (
        <div className="p-card-padding">
          <iframe
            className="h-[560px] w-full rounded-lg border border-outline bg-white"
            sandbox=""
            srcDoc={`<!doctype html><html><head><meta charset="utf-8"><style>body{font-family:Inter,Arial,sans-serif;line-height:1.5;padding:32px;color:#1e293b}h1{color:#d32f2f}</style></head><body>${previewHtml}</body></html>`}
            title="Xem trước mẫu tài liệu"
          />
        </div>
      )}
    </Card>
  );
}

export default function DocumentsPage() {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<TemplateDraft>(NEW_TEMPLATE);
  const [selectedTemplateId, setSelectedTemplateId] = useState<string | null>(null);
  const [selectedDocId, setSelectedDocId] = useState<string | null>(null);
  const [generateTemplateCode, setGenerateTemplateCode] = useState("");
  const [contactId, setContactId] = useState("");
  const [varsText, setVarsText] = useState("ten_khach=Nguyễn Minh Anh\nkhoa_hoc=HSK 4 cấp tốc\nhoc_phi=4.500.000đ");
  const [sentVia, setSentVia] = useState("");
  const [previewMode, setPreviewMode] = useState<PreviewMode>("template");
  const [notice, setNotice] = useState<NoticeState | null>(null);

  const templatesQuery = useQuery({ queryKey: ["documents", "templates"], queryFn: listDocumentTemplates });
  const generatedQuery = useQuery({ queryKey: ["documents", "generated"], queryFn: listGeneratedDocuments });
  const templates = Array.isArray(templatesQuery.data) ? templatesQuery.data : EMPTY_TEMPLATES;
  const documents = Array.isArray(generatedQuery.data) ? generatedQuery.data : EMPTY_DOCUMENTS;
  const templatesById = useMemo(() => new Map(templates.map((template) => [template.id, template])), [templates]);
  const selectedDocument = documents.find((doc) => doc.id === selectedDocId) ?? documents[0] ?? null;
  const selectedTemplate = templates.find((template) => template.id === selectedTemplateId) ?? null;
  const metrics = metricCards(templates, documents);
  const apiError = templatesQuery.error ?? generatedQuery.error;

  const saveTemplateMutation = useMutation<DocumentTemplate | null>({
    mutationFn: async () => {
      if (draft.id) {
        await updateDocumentTemplate(draft.id, { docType: draft.docType, templateHtml: draft.templateHtml.trim() });
        return null;
      }
      return createDocumentTemplate({ code: draft.code.trim().toUpperCase(), docType: draft.docType, templateHtml: draft.templateHtml.trim() });
    },
    onSuccess: async (template) => {
      if (template) {
        setDraft(templateToDraft(template));
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
      setDraft(NEW_TEMPLATE);
      setSelectedTemplateId(null);
      setNotice({ tone: "success", message: "Đã xóa mẫu tài liệu." });
      await queryClient.invalidateQueries({ queryKey: ["documents", "templates"] });
    },
  });

  const generateMutation = useMutation({
    mutationFn: () => {
      const vars = parseVars(varsText);
      return generateDocument({
        templateCode: generateTemplateCode || draft.code,
        contactId: contactId.trim() || null,
        vars,
        sentVia: sentVia || null,
      });
    },
    onSuccess: async (response) => {
      setSelectedDocId(response.documentId);
      setPreviewMode("document");
      setNotice({
        tone: "success",
        message: sentVia === "email" ? "Đã tạo tài liệu và gửi email." : "Đã tạo tài liệu.",
      });
      await queryClient.invalidateQueries({ queryKey: ["documents", "generated"] });
    },
  });

  const generateKitMutation = useMutation({
    mutationFn: () => {
      const vars = parseVars(varsText);
      return generateDocumentKit({
        contactId: contactId.trim() || null,
        vars,
        sentVia: sentVia || null,
      });
    },
    onSuccess: async (response) => {
      const first = response.documents[0];
      if (first) {
        setSelectedDocId(first.documentId);
        setPreviewMode("document");
      }
      setNotice({
        tone: "success",
        message: `Đã tạo bộ ${response.documents.length} tài liệu.`,
      });
      await queryClient.invalidateQueries({ queryKey: ["documents", "generated"] });
    },
  });

  function selectTemplate(template: DocumentTemplate) {
    setSelectedTemplateId(template.id);
    setDraft(templateToDraft(template));
    setGenerateTemplateCode(template.code);
    setPreviewMode("template");
  }

  function newTemplate() {
    setSelectedTemplateId(null);
    setDraft(NEW_TEMPLATE);
    setPreviewMode("template");
  }

  return (
    <AppShell title="Thư viện tài liệu">
      <section className="mb-gutter rounded-lg border border-primary/20 bg-primary/5 p-4">
        <div className="flex flex-col gap-3 xl:flex-row xl:items-center xl:justify-between">
          <div>
            <h1 className="text-headline-md text-secondary">Thư viện tài liệu & gửi báo giá</h1>
            <p className="mt-1 text-body-md text-on-surface-variant">
              Quản lý mẫu, xem trước và tạo tài liệu tự động.
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
            <Button
              type="button"
              onClick={() => generateMutation.mutate()}
              disabled={generateMutation.isPending || generateKitMutation.isPending || !(generateTemplateCode || draft.code)}
            >
              <span aria-hidden="true" className="material-symbols-outlined text-[18px]">picture_as_pdf</span>
              Tạo PDF
            </Button>
            <Button type="button" variant="outline" onClick={() => generateKitMutation.mutate()} disabled={generateMutation.isPending || generateKitMutation.isPending}>
              <span aria-hidden="true" className="material-symbols-outlined text-[18px]">inventory_2</span>
              Tạo bộ tài liệu
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
          </Card>

          <TemplateEditor
            draft={draft}
            saving={saveTemplateMutation.isPending}
            deleting={deleteTemplateMutation.isPending}
            error={saveTemplateMutation.error ?? deleteTemplateMutation.error}
            onDraft={setDraft}
            onNew={newTemplate}
            onSave={() => saveTemplateMutation.mutate()}
            onDelete={() => {
              if (draft.id) deleteTemplateMutation.mutate(draft.id);
            }}
          />
        </div>

        <div className="space-y-gutter">
          <div className="grid grid-cols-1 gap-gutter 2xl:grid-cols-[minmax(0,1fr)_360px]">
            <PreviewPanel
              mode={previewMode}
              templateHtml={draft.templateHtml || selectedTemplate?.templateHtml || ""}
              varsText={varsText}
              document={selectedDocument}
              onMode={setPreviewMode}
            />
            <GeneratePanel
              templates={templates}
              templateCode={generateTemplateCode}
              contactId={contactId}
              varsText={varsText}
              sentVia={sentVia}
              generating={generateMutation.isPending}
              generatingKit={generateKitMutation.isPending}
              error={generateMutation.error ?? generateKitMutation.error}
              onTemplateCode={setGenerateTemplateCode}
              onContactId={setContactId}
              onVarsText={setVarsText}
              onSentVia={setSentVia}
              onGenerate={() => generateMutation.mutate()}
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
              <GeneratedList
                documents={documents}
                templatesById={templatesById}
                selectedId={selectedDocument?.id ?? null}
                onSelect={(doc) => {
                  setSelectedDocId(doc.id);
                  setPreviewMode("document");
                }}
              />
            )}
          </Card>
        </div>
      </section>
    </AppShell>
  );
}
