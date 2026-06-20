import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/shared/ui/Button";
import { DataTable, type Column } from "@/shared/ui/DataTable";
import { Modal } from "@/shared/ui/Modal";
import { hasPermission } from "@/shared/auth/authStore";
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

const QUERY_KEY = ["llm-configs"] as const;
const MANAGE_PERMISSION = "llm-configs:manage";

interface FormState {
  readonly provider: LlmProvider;
  readonly modelId: string;
  readonly displayName: string;
  readonly apiKey: string;
  readonly baseUrl: string;
  readonly maxTokens: string;
  readonly temperature: string;
  readonly inputUsdPer1M: string;
  readonly outputUsdPer1M: string;
}

const EMPTY_FORM: FormState = {
  provider: "anthropic",
  modelId: "",
  displayName: "",
  apiKey: "",
  baseUrl: "",
  maxTokens: "",
  temperature: "",
  inputUsdPer1M: "",
  outputUsdPer1M: "",
};

function toNullableNumber(value: string): number | null {
  const trimmed = value.trim();
  if (trimmed.length === 0) return null;
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}

function toUpdatePayload(form: FormState): UpdateLlmConfigPayload {
  return {
    provider: form.provider,
    modelId: form.modelId.trim(),
    displayName: form.displayName.trim() || null,
    baseUrl: form.baseUrl.trim() || null,
    maxTokens: toNullableNumber(form.maxTokens),
    temperature: toNullableNumber(form.temperature),
    inputUsdPer1M: toNullableNumber(form.inputUsdPer1M),
    outputUsdPer1M: toNullableNumber(form.outputUsdPer1M),
  };
}

function toCreatePayload(form: FormState): CreateLlmConfigPayload {
  return { ...toUpdatePayload(form), apiKey: form.apiKey };
}

function fromConfig(config: LlmConfig): FormState {
  return {
    provider: config.provider,
    modelId: config.modelId,
    displayName: config.displayName ?? "",
    apiKey: "",
    baseUrl: config.baseUrl ?? "",
    maxTokens: config.maxTokens?.toString() ?? "",
    temperature: config.temperature?.toString() ?? "",
    inputUsdPer1M: config.inputUsdPer1M?.toString() ?? "",
    outputUsdPer1M: config.outputUsdPer1M?.toString() ?? "",
  };
}

const FIELD_CLASS =
  "w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary";
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

export default function LlmProvidersPage() {
  const canManage = hasPermission(MANAGE_PERMISSION);
  const queryClient = useQueryClient();
  const { data: configs = [], isLoading } = useQuery({ queryKey: QUERY_KEY, queryFn: listLlmConfigs });

  const [editing, setEditing] = useState<LlmConfig | null>(null);
  const [formOpen, setFormOpen] = useState(false);
  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const [rotateTarget, setRotateTarget] = useState<LlmConfig | null>(null);
  const [rotateKey, setRotateKeyValue] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [testResult, setTestResult] = useState<{ id: string; text: string } | null>(null);

  const invalidate = () => queryClient.invalidateQueries({ queryKey: QUERY_KEY });
  const readError = (err: unknown): string => {
    const apiError = (err as { response?: { data?: { error?: string } } })?.response?.data?.error;
    return apiError ?? "Đã xảy ra lỗi, vui lòng thử lại.";
  };

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (editing) {
        await updateLlmConfig(editing.id, toUpdatePayload(form));
      } else {
        await createLlmConfig(toCreatePayload(form));
      }
    },
    onSuccess: () => {
      invalidate();
      setFormOpen(false);
    },
    onError: (err) => setError(readError(err)),
  });

  const rotateMutation = useMutation({
    mutationFn: () => rotateLlmKey(rotateTarget!.id, rotateKey),
    onSuccess: () => {
      invalidate();
      setRotateTarget(null);
      setRotateKeyValue("");
    },
    onError: (err) => setError(readError(err)),
  });

  const activeMutation = useMutation({
    mutationFn: ({ id, active }: { id: string; active: boolean }) => setLlmConfigActive(id, active),
    onSuccess: invalidate,
    onError: (err) => setError(readError(err)),
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteLlmConfig(id),
    onSuccess: invalidate,
    onError: (err) => setError(readError(err)),
  });

  const testMutation = useMutation({
    mutationFn: (id: string) => testLlmConfig(id),
    onSuccess: (result, id) =>
      setTestResult({ id, text: result.ok ? `OK · ${result.latencyMs}ms` : `Lỗi: ${result.error ?? "không rõ"}` }),
    onError: (err, id) => setTestResult({ id, text: `Lỗi: ${readError(err)}` }),
  });

  const openCreate = () => {
    setEditing(null);
    setForm(EMPTY_FORM);
    setError(null);
    setFormOpen(true);
  };

  const openEdit = (config: LlmConfig) => {
    setEditing(config);
    setForm(fromConfig(config));
    setError(null);
    setFormOpen(true);
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
            {testResult?.id === row.id ? (
              <span className="self-center text-mono-status text-on-surface-variant">{testResult.text}</span>
            ) : null}
            <Button size="sm" variant="ghost" disabled={testMutation.isPending} onClick={() => testMutation.mutate(row.id)}>
              Kiểm tra
            </Button>
            <Button size="sm" variant="ghost" onClick={() => openEdit(row)}>
              Sửa
            </Button>
            <Button size="sm" variant="ghost" onClick={() => { setRotateTarget(row); setRotateKeyValue(""); setError(null); }}>
              Đổi khóa
            </Button>
            <Button
              size="sm"
              variant="outline"
              onClick={() => activeMutation.mutate({ id: row.id, active: !row.isActive })}
            >
              {row.isActive ? "Tắt" : "Bật"}
            </Button>
            <Button
              size="sm"
              variant="outline"
              onClick={() => { if (window.confirm("Xóa cấu hình này?")) deleteMutation.mutate(row.id); }}
            >
              Xóa
            </Button>
          </div>
        ),
      },
    ],
    [activeMutation, deleteMutation, testMutation, testResult]
  );

  if (!canManage) {
    return (
      <div className="p-8">
        <div className="rounded-lg border border-outline bg-surface p-6 text-body-md text-on-surface-variant">
          Bạn không có quyền quản lý cấu hình mô hình AI.
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-6 p-8">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-headline-md font-bold text-on-surface">Cấu hình mô hình AI</h1>
          <p className="mt-1 text-body-md text-on-surface-variant">
            Khai báo nhà cung cấp (Anthropic / chuẩn OpenAI), khóa API và mô hình cho từng agent. Khóa được mã hóa khi lưu.
          </p>
        </div>
        <Button onClick={openCreate}>
          <span aria-hidden="true" className="material-symbols-outlined">add</span>
          Thêm nhà cung cấp
        </Button>
      </header>

      {isLoading ? (
        <div className="rounded-lg border border-outline bg-surface p-4 text-body-md text-on-surface-variant">Đang tải...</div>
      ) : (
        <DataTable
          columns={columns}
          rows={configs}
          rowKey={(row) => row.id}
          empty="Chưa có cấu hình nào. Thêm nhà cung cấp để bắt đầu."
        />
      )}

      <Modal
        open={formOpen}
        onClose={() => setFormOpen(false)}
        title={editing ? "Sửa cấu hình" : "Thêm nhà cung cấp"}
        footer={
          <>
            <Button variant="outline" onClick={() => setFormOpen(false)}>
              Hủy
            </Button>
            <Button disabled={saveMutation.isPending} onClick={() => { setError(null); saveMutation.mutate(); }}>
              {editing ? "Lưu" : "Tạo"}
            </Button>
          </>
        }
      >
        {error ? <div className="rounded border border-red-300 bg-red-50 px-3 py-2 text-mono-status text-red-700">{error}</div> : null}
        <label className="block space-y-1">
          <span className={LABEL_CLASS}>Tên hiển thị</span>
          <input className={FIELD_CLASS} value={form.displayName} onChange={(e) => setForm({ ...form, displayName: e.target.value })} placeholder="Claude sản xuất" />
        </label>
        <label className="block space-y-1">
          <span className={LABEL_CLASS}>Nhà cung cấp</span>
          <select className={FIELD_CLASS} value={form.provider} onChange={(e) => setForm({ ...form, provider: e.target.value as LlmProvider })}>
            <option value="anthropic">Anthropic (Claude)</option>
            <option value="openai">Chuẩn OpenAI</option>
          </select>
        </label>
        <label className="block space-y-1">
          <span className={LABEL_CLASS}>Mô hình</span>
          <input className={`${FIELD_CLASS} font-mono`} value={form.modelId} onChange={(e) => setForm({ ...form, modelId: e.target.value })} placeholder="claude-sonnet-4-6 / gpt-4o" />
        </label>
        {!editing ? (
          <label className="block space-y-1">
            <span className={LABEL_CLASS}>Khóa API</span>
            <input className={`${FIELD_CLASS} font-mono`} type="password" value={form.apiKey} onChange={(e) => setForm({ ...form, apiKey: e.target.value })} placeholder="sk-..." autoComplete="off" />
          </label>
        ) : (
          <p className="text-mono-status text-on-surface-variant">Khóa API được đổi qua nút “Đổi khóa”.</p>
        )}
        <label className="block space-y-1">
          <span className={LABEL_CLASS}>Base URL (tùy chọn, https)</span>
          <input className={`${FIELD_CLASS} font-mono`} value={form.baseUrl} onChange={(e) => setForm({ ...form, baseUrl: e.target.value })} placeholder="https://api.openai.com" />
        </label>
        <div className="grid grid-cols-2 gap-3">
          <label className="block space-y-1">
            <span className={LABEL_CLASS}>Max tokens</span>
            <input className={FIELD_CLASS} type="number" min={128} step={128} value={form.maxTokens} onChange={(e) => setForm({ ...form, maxTokens: e.target.value })} />
          </label>
          <label className="block space-y-1">
            <span className={LABEL_CLASS}>Temperature</span>
            <input className={FIELD_CLASS} type="number" min={0} max={2} step={0.1} value={form.temperature} onChange={(e) => setForm({ ...form, temperature: e.target.value })} />
          </label>
          <label className="block space-y-1">
            <span className={LABEL_CLASS}>USD / 1M input</span>
            <input className={FIELD_CLASS} type="number" min={0} step={0.01} value={form.inputUsdPer1M} onChange={(e) => setForm({ ...form, inputUsdPer1M: e.target.value })} />
          </label>
          <label className="block space-y-1">
            <span className={LABEL_CLASS}>USD / 1M output</span>
            <input className={FIELD_CLASS} type="number" min={0} step={0.01} value={form.outputUsdPer1M} onChange={(e) => setForm({ ...form, outputUsdPer1M: e.target.value })} />
          </label>
        </div>
      </Modal>

      <Modal
        open={rotateTarget !== null}
        onClose={() => setRotateTarget(null)}
        title="Đổi khóa API"
        footer={
          <>
            <Button variant="outline" onClick={() => setRotateTarget(null)}>
              Hủy
            </Button>
            <Button disabled={rotateMutation.isPending || rotateKey.trim().length === 0} onClick={() => { setError(null); rotateMutation.mutate(); }}>
              Cập nhật khóa
            </Button>
          </>
        }
      >
        {error ? <div className="rounded border border-red-300 bg-red-50 px-3 py-2 text-mono-status text-red-700">{error}</div> : null}
        <label className="block space-y-1">
          <span className={LABEL_CLASS}>Khóa API mới</span>
          <input className={`${FIELD_CLASS} font-mono`} type="password" value={rotateKey} onChange={(e) => setRotateKeyValue(e.target.value)} placeholder="sk-..." autoComplete="off" />
        </label>
      </Modal>
    </div>
  );
}
