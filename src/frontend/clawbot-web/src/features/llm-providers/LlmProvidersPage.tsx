import { useMemo, useState, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/shared/ui/Button";
import { DataTable, type Column } from "@/shared/ui/DataTable";
import { Modal } from "@/shared/ui/Modal";
import { hasPermission } from "@/shared/auth/authStore";
import { AppShell } from "@/shared/layout/AppShell";
import {
  createEmbeddingConfig,
  deleteEmbeddingConfig,
  getEmbeddingStatus,
  listEmbeddingConfigs,
  rotateEmbeddingKey,
  setEmbeddingConfigActive,
  testEmbeddingConfig,
  updateEmbeddingConfig,
  type CreateEmbeddingConfigPayload,
  type EmbeddingConfig,
  type EmbeddingProvider,
  type UpdateEmbeddingConfigPayload,
} from "@/shared/api/embeddingConfigs";
import {
  createLlmConfig,
  deleteLlmConfig,
  listLlmConfigs,
  rotateLlmKey,
  setLlmConfigActive,
  testLlmConfig,
  updateLlmConfig,
  type CreateLlmConfigPayload,
  type LlmConfig,
  type LlmProvider,
  type UpdateLlmConfigPayload,
} from "@/shared/api/llmConfigs";

const LLM_QUERY_KEY = ["llm-configs"] as const;
const EMBEDDING_QUERY_KEY = ["embedding-configs"] as const;
const EMBEDDING_STATUS_QUERY_KEY = ["embedding-configs", "status"] as const;
const MANAGE_PERMISSION = "llm-configs:manage";

interface LlmFormState {
  readonly provider: LlmProvider;
  readonly modelId: string;
  readonly displayName: string;
  readonly apiKey: string;
  readonly baseUrl: string;
  readonly inputUsdPer1M: string;
  readonly outputUsdPer1M: string;
  readonly timeoutSeconds: string;
  readonly maxOutputTokens: string;
  /** "" = auto, "true"/"false" = explicit override */
  readonly supportsVision: "" | "true" | "false";
}

interface EmbeddingFormState {
  readonly provider: EmbeddingProvider;
  readonly modelId: string;
  readonly displayName: string;
  readonly apiKey: string;
  readonly baseUrl: string;
  readonly dimension: string;
}

const EMPTY_LLM_FORM: LlmFormState = {
  provider: "anthropic",
  modelId: "",
  displayName: "",
  apiKey: "",
  baseUrl: "",
  inputUsdPer1M: "",
  outputUsdPer1M: "",
  timeoutSeconds: "",
  maxOutputTokens: "",
  supportsVision: "",
};

const EMPTY_EMBEDDING_FORM: EmbeddingFormState = {
  provider: "openai",
  modelId: "text-embedding-3-small",
  displayName: "",
  apiKey: "",
  baseUrl: "",
  dimension: "1536",
};

function toNullableNumber(value: string): number | null {
  const trimmed = value.trim();
  if (trimmed.length === 0) return null;
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}

function toRequiredInt(value: string, fallback: number): number {
  const parsed = Number(value.trim());
  return Number.isInteger(parsed) ? parsed : fallback;
}

const CONFIG_ERROR_MESSAGES: Readonly<Record<string, string>> = {
  invalid_provider: "Nhà cung cấp không được hỗ trợ.",
  invalid_model_id: "Tên mô hình không hợp lệ.",
  invalid_base_url: "Base URL không hợp lệ.",
  base_url_requires_https: "Base URL phải dùng https:// (không cho http thường ra internet).",
  base_url_private_host: "Base URL trỏ vào mạng nội bộ; cần người vận hành cấp phép.",
  base_url_mixed_dns: "Tên miền trả về cả địa chỉ công cộng lẫn nội bộ (nghi DNS rebinding).",
  invalid_rate: "Chi phí không được âm.",
  invalid_timeout: "Timeout phải từ 1 đến 600 giây.",
  invalid_max_output_tokens: "Max output tokens phải từ 1 đến 200000.",
  api_key_required: "Cần nhập khóa API.",
};

function readError(err: unknown): string {
  const apiError = (err as { response?: { data?: { error?: string } } })?.response?.data?.error;
  if (!apiError) return "Đã xảy ra lỗi, vui lòng thử lại.";
  return CONFIG_ERROR_MESSAGES[apiError] ?? apiError;
}

function toNullableBooleanTriState(value: "" | "true" | "false"): boolean | null {
  if (value === "true") return true;
  if (value === "false") return false;
  return null;
}

function toLlmUpdatePayload(form: LlmFormState): UpdateLlmConfigPayload {
  return {
    provider: form.provider,
    modelId: form.modelId.trim(),
    displayName: form.displayName.trim() || null,
    baseUrl: form.baseUrl.trim() || null,
    inputUsdPer1M: toNullableNumber(form.inputUsdPer1M),
    outputUsdPer1M: toNullableNumber(form.outputUsdPer1M),
    timeoutSeconds: toNullableNumber(form.timeoutSeconds),
    maxOutputTokens: toNullableNumber(form.maxOutputTokens),
    supportsVision: toNullableBooleanTriState(form.supportsVision),
  };
}

function toLlmCreatePayload(form: LlmFormState): CreateLlmConfigPayload {
  return { ...toLlmUpdatePayload(form), apiKey: form.apiKey };
}

function fromLlmConfig(config: LlmConfig): LlmFormState {
  return {
    provider: config.provider,
    modelId: config.modelId,
    displayName: config.displayName ?? "",
    apiKey: "",
    baseUrl: config.baseUrl ?? "",
    inputUsdPer1M: config.inputUsdPer1M?.toString() ?? "",
    outputUsdPer1M: config.outputUsdPer1M?.toString() ?? "",
    timeoutSeconds: config.timeoutSeconds?.toString() ?? "",
    maxOutputTokens: config.maxOutputTokens?.toString() ?? "",
    supportsVision:
      config.supportsVision === true ? "true" : config.supportsVision === false ? "false" : "",
  };
}

function toEmbeddingUpdatePayload(form: EmbeddingFormState): UpdateEmbeddingConfigPayload {
  const provider = form.provider;
  return {
    provider,
    modelId: provider === "hash" ? "hash-384" : form.modelId.trim(),
    displayName: form.displayName.trim() || null,
    baseUrl: provider === "hash" ? null : form.baseUrl.trim() || null,
    dimension: provider === "hash" ? 384 : toRequiredInt(form.dimension, 1536),
  };
}

function toEmbeddingCreatePayload(form: EmbeddingFormState): CreateEmbeddingConfigPayload {
  const payload = toEmbeddingUpdatePayload(form);
  return { ...payload, apiKey: payload.provider === "hash" ? null : form.apiKey };
}

function fromEmbeddingConfig(config: EmbeddingConfig): EmbeddingFormState {
  return {
    provider: config.provider,
    modelId: config.modelId,
    displayName: config.displayName ?? "",
    apiKey: "",
    baseUrl: config.baseUrl ?? "",
    dimension: config.dimension.toString(),
  };
}

const FIELD_CLASS =
  "w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary disabled:bg-surface-variant";
const LABEL_CLASS = "text-label-caps uppercase text-tertiary";

function Badge({ tone, children }: { readonly tone: "ok" | "muted" | "warn"; readonly children: string }) {
  const cls =
    tone === "ok"
      ? "bg-green-100 text-green-800"
      : tone === "warn"
        ? "bg-amber-100 text-amber-800"
        : "bg-surface-variant text-on-surface-variant";
  return <span className={`inline-block rounded-full px-2 py-0.5 text-mono-status ${cls}`}>{children}</span>;
}

function SectionHeader({ title, description, action }: { readonly title: string; readonly description: string; readonly action: ReactNode }) {
  return (
    <header className="flex items-start justify-between gap-4">
      <div>
        <h2 className="text-title-lg font-bold text-on-surface">{title}</h2>
        <p className="mt-1 text-body-md text-on-surface-variant">{description}</p>
      </div>
      {action}
    </header>
  );
}

export default function LlmProvidersPage() {
  const canManage = hasPermission(MANAGE_PERMISSION);
  const queryClient = useQueryClient();
  const { data: configs = [], isLoading } = useQuery({ queryKey: LLM_QUERY_KEY, queryFn: listLlmConfigs });
  const { data: embeddingConfigs = [], isLoading: embeddingLoading } = useQuery({
    queryKey: EMBEDDING_QUERY_KEY,
    queryFn: listEmbeddingConfigs,
  });
  const { data: embeddingStatus } = useQuery({ queryKey: EMBEDDING_STATUS_QUERY_KEY, queryFn: getEmbeddingStatus });

  const [editing, setEditing] = useState<LlmConfig | null>(null);
  const [formOpen, setFormOpen] = useState(false);
  const [form, setForm] = useState<LlmFormState>(EMPTY_LLM_FORM);
  const [rotateTarget, setRotateTarget] = useState<LlmConfig | null>(null);
  const [rotateKey, setRotateKeyValue] = useState("");

  const [embeddingEditing, setEmbeddingEditing] = useState<EmbeddingConfig | null>(null);
  const [embeddingFormOpen, setEmbeddingFormOpen] = useState(false);
  const [embeddingForm, setEmbeddingForm] = useState<EmbeddingFormState>(EMPTY_EMBEDDING_FORM);
  const [embeddingRotateTarget, setEmbeddingRotateTarget] = useState<EmbeddingConfig | null>(null);
  const [embeddingRotateKey, setEmbeddingRotateKeyValue] = useState("");

  const [error, setError] = useState<string | null>(null);
  const [testResult, setTestResult] = useState<{ id: string; text: string } | null>(null);
  const [embeddingTestResult, setEmbeddingTestResult] = useState<{ id: string; text: string } | null>(null);

  const invalidateLlm = () => queryClient.invalidateQueries({ queryKey: LLM_QUERY_KEY });
  const invalidateEmbedding = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: EMBEDDING_QUERY_KEY }),
      queryClient.invalidateQueries({ queryKey: EMBEDDING_STATUS_QUERY_KEY }),
    ]);
  };

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (editing) await updateLlmConfig(editing.id, toLlmUpdatePayload(form));
      else await createLlmConfig(toLlmCreatePayload(form));
    },
    onSuccess: () => {
      invalidateLlm();
      setFormOpen(false);
    },
    onError: (err) => setError(readError(err)),
  });

  const rotateMutation = useMutation({
    mutationFn: () => rotateLlmKey(rotateTarget!.id, rotateKey),
    onSuccess: () => {
      invalidateLlm();
      setRotateTarget(null);
      setRotateKeyValue("");
    },
    onError: (err) => setError(readError(err)),
  });

  const activeMutation = useMutation({
    mutationFn: ({ id, active }: { id: string; active: boolean }) => setLlmConfigActive(id, active),
    onSuccess: invalidateLlm,
    onError: (err) => setError(readError(err)),
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteLlmConfig(id),
    onSuccess: invalidateLlm,
    onError: (err) => setError(readError(err)),
  });

  const testMutation = useMutation({
    mutationFn: (id: string) => testLlmConfig(id),
    onSuccess: (result, id) =>
      setTestResult({ id, text: result.ok ? `OK · ${result.latencyMs}ms` : `Lỗi: ${result.error ?? "không rõ"}` }),
    onError: (err, id) => setTestResult({ id, text: `Lỗi: ${readError(err)}` }),
  });

  const embeddingSaveMutation = useMutation({
    mutationFn: async () => {
      if (embeddingEditing) await updateEmbeddingConfig(embeddingEditing.id, toEmbeddingUpdatePayload(embeddingForm));
      else await createEmbeddingConfig(toEmbeddingCreatePayload(embeddingForm));
    },
    onSuccess: () => {
      invalidateEmbedding();
      setEmbeddingFormOpen(false);
    },
    onError: (err) => setError(readError(err)),
  });

  const embeddingRotateMutation = useMutation({
    mutationFn: () => rotateEmbeddingKey(embeddingRotateTarget!.id, embeddingRotateKey),
    onSuccess: () => {
      invalidateEmbedding();
      setEmbeddingRotateTarget(null);
      setEmbeddingRotateKeyValue("");
    },
    onError: (err) => setError(readError(err)),
  });

  const embeddingActiveMutation = useMutation({
    mutationFn: ({ id, active }: { id: string; active: boolean }) => setEmbeddingConfigActive(id, active),
    onSuccess: invalidateEmbedding,
    onError: (err) => setError(readError(err)),
  });

  const embeddingDeleteMutation = useMutation({
    mutationFn: (id: string) => deleteEmbeddingConfig(id),
    onSuccess: invalidateEmbedding,
    onError: (err) => setError(readError(err)),
  });

  const embeddingTestMutation = useMutation({
    mutationFn: (id: string) => testEmbeddingConfig(id),
    onSuccess: (result, id) =>
      setEmbeddingTestResult({ id, text: result.ok ? `OK · ${result.latencyMs}ms` : `Lỗi: ${result.error ?? "không rõ"}` }),
    onError: (err, id) => setEmbeddingTestResult({ id, text: `Lỗi: ${readError(err)}` }),
  });

  const openCreate = () => {
    setEditing(null);
    setForm(EMPTY_LLM_FORM);
    setError(null);
    setFormOpen(true);
  };

  const openEdit = (config: LlmConfig) => {
    setEditing(config);
    setForm(fromLlmConfig(config));
    setError(null);
    setFormOpen(true);
  };

  const openEmbeddingCreate = () => {
    setEmbeddingEditing(null);
    setEmbeddingForm(EMPTY_EMBEDDING_FORM);
    setError(null);
    setEmbeddingFormOpen(true);
  };

  const openEmbeddingEdit = (config: EmbeddingConfig) => {
    setEmbeddingEditing(config);
    setEmbeddingForm(fromEmbeddingConfig(config));
    setError(null);
    setEmbeddingFormOpen(true);
  };

  const columns = useMemo<readonly Column<LlmConfig>[]>(
    () => [
      {
        key: "name",
        header: "Cấu hình",
        render: (row) => (
          <div className="flex flex-col">
            <span className="font-medium text-on-surface">{row.displayName || row.modelId}</span>
            <span className="text-mono-status text-on-surface-variant">{row.provider}</span>
          </div>
        ),
      },
      { key: "model", header: "Mô hình", render: (row) => <span className="font-mono text-mono-status">{row.modelId}</span> },
      {
        key: "baseUrl",
        header: "Base URL",
        render: (row) => <span className="text-mono-status text-on-surface-variant">{row.baseUrl || "mặc định"}</span>,
      },
      {
        key: "key",
        header: "Khóa API",
        render: (row) => (row.hasApiKey ? <Badge tone="ok">đã lưu</Badge> : <Badge tone="warn">thiếu</Badge>),
      },
      {
        key: "status",
        header: "Trạng thái",
        render: (row) => (row.isActive ? <Badge tone="ok">đang bật</Badge> : <Badge tone="muted">tắt</Badge>),
      },
      {
        key: "actions",
        header: "",
        className: "text-right",
        render: (row) => (
          <div className="flex flex-wrap justify-end gap-2">
            {testResult?.id === row.id ? <span className="self-center text-mono-status text-on-surface-variant">{testResult.text}</span> : null}
            <Button size="sm" variant="ghost" disabled={testMutation.isPending} onClick={() => testMutation.mutate(row.id)}>Kiểm tra</Button>
            <Button size="sm" variant="ghost" onClick={() => openEdit(row)}>Sửa</Button>
            <Button size="sm" variant="ghost" onClick={() => { setRotateTarget(row); setRotateKeyValue(""); setError(null); }}>Đổi khóa</Button>
            <Button size="sm" variant="outline" onClick={() => activeMutation.mutate({ id: row.id, active: !row.isActive })}>{row.isActive ? "Tắt" : "Bật"}</Button>
            <Button size="sm" variant="outline" onClick={() => { if (window.confirm("Xóa cấu hình này?")) deleteMutation.mutate(row.id); }}>Xóa</Button>
          </div>
        ),
      },
    ],
    [activeMutation, deleteMutation, testMutation, testResult]
  );

  const embeddingColumns = useMemo<readonly Column<EmbeddingConfig>[]>(
    () => [
      {
        key: "name",
        header: "Cấu hình",
        render: (row) => (
          <div className="flex flex-col">
            <span className="font-medium text-on-surface">{row.displayName || row.modelId}</span>
            <span className="text-mono-status text-on-surface-variant">{row.provider}</span>
          </div>
        ),
      },
      { key: "model", header: "Mô hình", render: (row) => <span className="font-mono text-mono-status">{row.modelId}</span> },
      { key: "dimension", header: "Số chiều", render: (row) => <span className="font-mono text-mono-status">{row.dimension}</span> },
      {
        key: "baseUrl",
        header: "Base URL",
        render: (row) => <span className="text-mono-status text-on-surface-variant">{row.baseUrl || "mặc định"}</span>,
      },
      {
        key: "key",
        header: "Khóa API",
        render: (row) => row.provider === "hash" ? <Badge tone="muted">không cần</Badge> : row.hasApiKey ? <Badge tone="ok">đã lưu</Badge> : <Badge tone="warn">thiếu</Badge>,
      },
      {
        key: "status",
        header: "Trạng thái",
        render: (row) => (row.isActive ? <Badge tone="ok">đang dùng</Badge> : <Badge tone="muted">tắt</Badge>),
      },
      {
        key: "actions",
        header: "",
        className: "text-right",
        render: (row) => (
          <div className="flex flex-wrap justify-end gap-2">
            {embeddingTestResult?.id === row.id ? <span className="self-center text-mono-status text-on-surface-variant">{embeddingTestResult.text}</span> : null}
            <Button size="sm" variant="ghost" disabled={embeddingTestMutation.isPending} onClick={() => embeddingTestMutation.mutate(row.id)}>Kiểm tra</Button>
            <Button size="sm" variant="ghost" onClick={() => openEmbeddingEdit(row)}>Sửa</Button>
            {row.provider !== "hash" ? (
              <Button size="sm" variant="ghost" onClick={() => { setEmbeddingRotateTarget(row); setEmbeddingRotateKeyValue(""); setError(null); }}>Đổi khóa</Button>
            ) : null}
            <Button size="sm" variant="outline" onClick={() => embeddingActiveMutation.mutate({ id: row.id, active: !row.isActive })}>{row.isActive ? "Tắt" : "Bật"}</Button>
            <Button size="sm" variant="outline" onClick={() => { if (window.confirm("Xóa cấu hình embedding này?")) embeddingDeleteMutation.mutate(row.id); }}>Xóa</Button>
          </div>
        ),
      },
    ],
    [embeddingActiveMutation, embeddingDeleteMutation, embeddingTestMutation, embeddingTestResult]
  );

  if (!canManage) {
    return (
      <AppShell title="Cấu hình mô hình AI">
        <div className="rounded-lg border border-outline bg-surface p-6 text-body-md text-on-surface-variant">
          Bạn không có quyền quản lý cấu hình mô hình AI.
        </div>
      </AppShell>
    );
  }

  return (
    <AppShell title="Cấu hình mô hình AI">
      <div className="flex flex-col gap-8">
        <header>
          <h1 className="text-headline-md font-bold text-on-surface">Cấu hình mô hình AI</h1>
          <p className="mt-1 text-body-md text-on-surface-variant">
            Quản lý mô hình hội thoại cho agent và embedding cho kho tri thức. Khóa được mã hóa khi lưu.
          </p>
        </header>

        <section className="space-y-4">
          <SectionHeader
            title="Mô hình hội thoại"
            description="Khai báo nhà cung cấp, khóa API và mô hình cho từng agent."
            action={<Button onClick={openCreate}><span aria-hidden="true" className="material-symbols-outlined">add</span>Thêm nhà cung cấp</Button>}
          />
          {isLoading ? (
            <div className="rounded-lg border border-outline bg-surface p-4 text-body-md text-on-surface-variant">Đang tải...</div>
          ) : (
            <DataTable columns={columns} rows={configs} rowKey={(row) => row.id} empty="Chưa có cấu hình nào. Thêm nhà cung cấp để bắt đầu." />
          )}
        </section>

        <section className="space-y-4">
          <SectionHeader
            title="Embedding kho tri thức (tùy chọn)"
            description="Không cấu hình: KB truy xuất bằng chính LLM của bạn (mặc định, không cần thêm gì). Cấu hình embedding để bật vector search — nhanh và rẻ hơn khi kho tri thức lớn."
            action={<Button onClick={openEmbeddingCreate}><span aria-hidden="true" className="material-symbols-outlined">add</span>Thêm embedding</Button>}
          />
          <div className={`rounded-lg border px-4 py-3 text-body-md ${embeddingStatus?.retrievalMode === "llm" ? "border-sky-200 bg-sky-50 text-sky-900" : "border-green-200 bg-green-50 text-green-900"}`}>
            {embeddingStatus?.retrievalMode === "llm" ? (
              <>Truy xuất KB: <strong>LLM trực tiếp (mặc định)</strong> — AI đọc kho tri thức bằng model chat của bạn, không cần embedding.</>
            ) : (
              <>
                Truy xuất KB: <strong>Vector search</strong> · {embeddingStatus?.displayName || embeddingStatus?.modelId || "đang tải"}
                {embeddingStatus ? ` · ${embeddingStatus.provider} · ${embeddingStatus.dimension} chiều · ${embeddingStatus.source}` : null}
              </>
            )}
          </div>
          {embeddingLoading ? (
            <div className="rounded-lg border border-outline bg-surface p-4 text-body-md text-on-surface-variant">Đang tải...</div>
          ) : (
            <DataTable columns={embeddingColumns} rows={embeddingConfigs} rowKey={(row) => row.id} empty="Chưa có cấu hình embedding — KB đang truy xuất bằng LLM (mặc định)." />
          )}
        </section>

        <Modal
          open={formOpen}
          onClose={() => setFormOpen(false)}
          title={editing ? "Sửa cấu hình" : "Thêm nhà cung cấp"}
          footer={<><Button variant="outline" onClick={() => setFormOpen(false)}>Hủy</Button><Button disabled={saveMutation.isPending} onClick={() => { setError(null); saveMutation.mutate(); }}>{editing ? "Lưu" : "Tạo"}</Button></>}
        >
          {error ? <div className="rounded border border-red-300 bg-red-50 px-3 py-2 text-mono-status text-red-700">{error}</div> : null}
          <label className="block space-y-1"><span className={LABEL_CLASS}>Tên hiển thị</span><input className={FIELD_CLASS} value={form.displayName} onChange={(e) => setForm({ ...form, displayName: e.target.value })} placeholder="Claude sản xuất" /></label>
          <label className="block space-y-1"><span className={LABEL_CLASS}>Nhà cung cấp</span><select className={FIELD_CLASS} value={form.provider} onChange={(e) => setForm({ ...form, provider: e.target.value as LlmProvider })}><option value="anthropic">Anthropic (Claude)</option><option value="openai">Chuẩn OpenAI</option><option value="openai-responses">Chuẩn OpenAI v2 (Responses API)</option></select></label>
          <label className="block space-y-1"><span className={LABEL_CLASS}>Mô hình</span><input className={`${FIELD_CLASS} font-mono`} value={form.modelId} onChange={(e) => setForm({ ...form, modelId: e.target.value })} placeholder="claude-sonnet-4-6 / gpt-4o" /></label>
          {!editing ? <label className="block space-y-1"><span className={LABEL_CLASS}>Khóa API</span><input className={`${FIELD_CLASS} font-mono`} type="password" value={form.apiKey} onChange={(e) => setForm({ ...form, apiKey: e.target.value })} placeholder="sk-..." autoComplete="off" /></label> : <p className="text-mono-status text-on-surface-variant">Khóa API được đổi qua nút “Đổi khóa”.</p>}
          <label className="block space-y-1"><span className={LABEL_CLASS}>Base URL (tùy chọn, https)</span><input className={`${FIELD_CLASS} font-mono`} value={form.baseUrl} onChange={(e) => setForm({ ...form, baseUrl: e.target.value })} placeholder="https://api.openai.com" /></label>
          <label className="block space-y-1"><span className={LABEL_CLASS}>Timeout (giây, tùy chọn)</span><input className={FIELD_CLASS} type="number" min={1} max={600} step={1} value={form.timeoutSeconds} onChange={(e) => setForm({ ...form, timeoutSeconds: e.target.value })} placeholder="mặc định 120" /></label>
          <label className="block space-y-1"><span className={LABEL_CLASS}>Max output tokens (tùy chọn)</span><input className={FIELD_CLASS} type="number" min={1} max={200000} step={1} value={form.maxOutputTokens} onChange={(e) => setForm({ ...form, maxOutputTokens: e.target.value })} placeholder="mặc định 3000" /></label>
          <label className="block space-y-1"><span className={LABEL_CLASS}>Hỗ trợ vision (review ảnh)</span><select className={FIELD_CLASS} value={form.supportsVision} onChange={(e) => setForm({ ...form, supportsVision: e.target.value as LlmFormState["supportsVision"] })}><option value="">Tự động (registry / unknown)</option><option value="true">Bật (override)</option><option value="false">Tắt (override)</option></select></label>
          <div className="grid grid-cols-2 gap-3">
            <label className="block space-y-1"><span className={LABEL_CLASS}>USD / 1M input</span><input className={FIELD_CLASS} type="number" min={0} step={0.01} value={form.inputUsdPer1M} onChange={(e) => setForm({ ...form, inputUsdPer1M: e.target.value })} /></label>
            <label className="block space-y-1"><span className={LABEL_CLASS}>USD / 1M output</span><input className={FIELD_CLASS} type="number" min={0} step={0.01} value={form.outputUsdPer1M} onChange={(e) => setForm({ ...form, outputUsdPer1M: e.target.value })} /></label>
          </div>
        </Modal>

        <Modal open={rotateTarget !== null} onClose={() => setRotateTarget(null)} title="Đổi khóa API" footer={<><Button variant="outline" onClick={() => setRotateTarget(null)}>Hủy</Button><Button disabled={rotateMutation.isPending || rotateKey.trim().length === 0} onClick={() => { setError(null); rotateMutation.mutate(); }}>Cập nhật khóa</Button></>}>
          {error ? <div className="rounded border border-red-300 bg-red-50 px-3 py-2 text-mono-status text-red-700">{error}</div> : null}
          <label className="block space-y-1"><span className={LABEL_CLASS}>Khóa API mới</span><input className={`${FIELD_CLASS} font-mono`} type="password" value={rotateKey} onChange={(e) => setRotateKeyValue(e.target.value)} placeholder="sk-..." autoComplete="off" /></label>
        </Modal>

        <Modal
          open={embeddingFormOpen}
          onClose={() => setEmbeddingFormOpen(false)}
          title={embeddingEditing ? "Sửa embedding" : "Thêm embedding"}
          footer={<><Button variant="outline" onClick={() => setEmbeddingFormOpen(false)}>Hủy</Button><Button disabled={embeddingSaveMutation.isPending} onClick={() => { setError(null); embeddingSaveMutation.mutate(); }}>{embeddingEditing ? "Lưu" : "Tạo"}</Button></>}
        >
          {error ? <div className="rounded border border-red-300 bg-red-50 px-3 py-2 text-mono-status text-red-700">{error}</div> : null}
          <label className="block space-y-1"><span className={LABEL_CLASS}>Tên hiển thị</span><input className={FIELD_CLASS} value={embeddingForm.displayName} onChange={(e) => setEmbeddingForm({ ...embeddingForm, displayName: e.target.value })} placeholder="OpenAI Embedding sản xuất" /></label>
          <label className="block space-y-1"><span className={LABEL_CLASS}>Nhà cung cấp</span><select className={FIELD_CLASS} value={embeddingForm.provider} onChange={(e) => setEmbeddingForm({ ...embeddingForm, provider: e.target.value as EmbeddingProvider })}><option value="openai">OpenAI</option><option value="openai-compatible">Chuẩn OpenAI</option><option value="hash">Hash fallback</option></select></label>
          <label className="block space-y-1"><span className={LABEL_CLASS}>Mô hình</span><input className={`${FIELD_CLASS} font-mono`} disabled={embeddingForm.provider === "hash"} value={embeddingForm.provider === "hash" ? "hash-384" : embeddingForm.modelId} onChange={(e) => setEmbeddingForm({ ...embeddingForm, modelId: e.target.value })} placeholder="text-embedding-3-small" /></label>
          {!embeddingEditing && embeddingForm.provider !== "hash" ? <label className="block space-y-1"><span className={LABEL_CLASS}>Khóa API</span><input className={`${FIELD_CLASS} font-mono`} type="password" value={embeddingForm.apiKey} onChange={(e) => setEmbeddingForm({ ...embeddingForm, apiKey: e.target.value })} placeholder="sk-..." autoComplete="off" /></label> : null}
          {embeddingEditing && embeddingForm.provider !== "hash" ? <p className="text-mono-status text-on-surface-variant">Khóa API được đổi qua nút “Đổi khóa”.</p> : null}
          <label className="block space-y-1"><span className={LABEL_CLASS}>Base URL (tùy chọn, https)</span><input className={`${FIELD_CLASS} font-mono`} disabled={embeddingForm.provider === "hash"} value={embeddingForm.provider === "hash" ? "" : embeddingForm.baseUrl} onChange={(e) => setEmbeddingForm({ ...embeddingForm, baseUrl: e.target.value })} placeholder="https://api.openai.com" /></label>
          <label className="block space-y-1"><span className={LABEL_CLASS}>Số chiều vector</span><input className={FIELD_CLASS} type="number" min={64} max={4096} step={1} disabled={embeddingForm.provider === "hash"} value={embeddingForm.provider === "hash" ? "384" : embeddingForm.dimension} onChange={(e) => setEmbeddingForm({ ...embeddingForm, dimension: e.target.value })} /><span className="text-mono-status text-on-surface-variant">Đổi số chiều tạo collection Qdrant mới. Cần phát hành lại KB sau khi đổi.</span></label>
        </Modal>

        <Modal open={embeddingRotateTarget !== null} onClose={() => setEmbeddingRotateTarget(null)} title="Đổi khóa embedding" footer={<><Button variant="outline" onClick={() => setEmbeddingRotateTarget(null)}>Hủy</Button><Button disabled={embeddingRotateMutation.isPending || embeddingRotateKey.trim().length === 0} onClick={() => { setError(null); embeddingRotateMutation.mutate(); }}>Cập nhật khóa</Button></>}>
          {error ? <div className="rounded border border-red-300 bg-red-50 px-3 py-2 text-mono-status text-red-700">{error}</div> : null}
          <label className="block space-y-1"><span className={LABEL_CLASS}>Khóa API mới</span><input className={`${FIELD_CLASS} font-mono`} type="password" value={embeddingRotateKey} onChange={(e) => setEmbeddingRotateKeyValue(e.target.value)} placeholder="sk-..." autoComplete="off" /></label>
        </Modal>
      </div>
    </AppShell>
  );
}
