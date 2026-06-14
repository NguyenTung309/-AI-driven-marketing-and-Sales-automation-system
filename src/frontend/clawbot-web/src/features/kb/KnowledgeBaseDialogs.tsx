import { useState } from "react";
import { Alert } from "@/shared/ui/Alert";
import { Modal } from "@/shared/ui/Modal";
import type { CreateKbModulePayload, KbModule, KbTestCase, KbTestRunResult } from "@/shared/api/kb";

export type ModuleDialogMode = "create" | "edit" | null;

export function ModuleFormModal({
  mode,
  module,
  pending,
  onClose,
  onSubmit,
}: {
  readonly mode: ModuleDialogMode;
  readonly module: KbModule | null;
  readonly pending: boolean;
  readonly onClose: () => void;
  readonly onSubmit: (payload: CreateKbModulePayload) => void;
}) {
  const editing = mode === "edit";
  const [code, setCode] = useState(editing ? module?.code ?? "" : "");
  const [name, setName] = useState(editing ? module?.name ?? "" : "");
  const [description, setDescription] = useState(editing ? module?.description ?? "" : "");
  const [ownerRole, setOwnerRole] = useState(editing ? module?.ownerRole ?? "" : "");

  return (
    <Modal
      footer={
        <>
          <button className="rounded px-4 py-2 text-body-md font-bold text-on-surface-variant hover:bg-surface-variant" onClick={onClose} type="button">
            Hủy
          </button>
          <button
            className="rounded bg-primary px-4 py-2 text-body-md font-bold text-white hover:bg-primary-hover disabled:opacity-50"
            disabled={pending || !code.trim() || !name.trim()}
            onClick={() => onSubmit({ code: code.trim(), name: name.trim(), description: description.trim() || null, ownerRole: ownerRole || null })}
            type="button"
          >
            {pending ? "Đang lưu" : editing ? "Cập nhật" : "Tạo module"}
          </button>
        </>
      }
      onClose={onClose}
      open={Boolean(mode)}
      title={editing ? "Chỉnh sửa module" : "Tạo module tri thức"}
    >
      <div className="space-y-4">
        <label className="block">
          <span className="text-label-sm font-bold text-secondary">Mã module</span>
          <input
            className="mt-2 w-full rounded border border-outline px-3 py-2 text-body-md outline-none focus:border-primary"
            disabled={editing}
            onChange={(event) => setCode(event.target.value)}
            placeholder="hoc-phi-hsk4"
            value={code}
          />
        </label>
        <label className="block">
          <span className="text-label-sm font-bold text-secondary">Tên module</span>
          <input
            className="mt-2 w-full rounded border border-outline px-3 py-2 text-body-md outline-none focus:border-primary"
            onChange={(event) => setName(event.target.value)}
            placeholder="Học phí HSK 4"
            value={name}
          />
        </label>
        <label className="block">
          <span className="text-label-sm font-bold text-secondary">Mô tả</span>
          <textarea
            className="mt-2 min-h-[84px] w-full resize-none rounded border border-outline px-3 py-2 text-body-md outline-none focus:border-primary"
            onChange={(event) => setDescription(event.target.value)}
            value={description}
          />
        </label>
        <label className="block">
          <span className="text-label-sm font-bold text-secondary">Vai trò phụ trách</span>
          <select
            className="mt-2 w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
            onChange={(event) => setOwnerRole(event.target.value)}
            value={ownerRole}
          >
            <option value="">Chưa phân công</option>
            <option value="Admin">Admin</option>
            <option value="Sale">Sale</option>
            <option value="Marketer">Marketer</option>
            <option value="QA">QA</option>
          </select>
        </label>
      </div>
    </Modal>
  );
}

export function QaModal({
  open,
  module,
  cases,
  loading,
  adding,
  testing,
  testResult,
  onClose,
  onAdd,
  onRun,
}: {
  readonly open: boolean;
  readonly module: KbModule | null;
  readonly cases: readonly KbTestCase[];
  readonly loading: boolean;
  readonly adding: boolean;
  readonly testing: boolean;
  readonly testResult: KbTestRunResult | null;
  readonly onClose: () => void;
  readonly onAdd: (question: string, answer: string) => void;
  readonly onRun: () => void;
}) {
  const [question, setQuestion] = useState("");
  const [answer, setAnswer] = useState("");

  return (
    <Modal
      footer={
        <>
          <button className="rounded px-4 py-2 text-body-md font-bold text-on-surface-variant hover:bg-surface-variant" onClick={onClose} type="button">
            Đóng
          </button>
          <button
            className="rounded bg-primary px-4 py-2 text-body-md font-bold text-white hover:bg-primary-hover disabled:opacity-50"
            disabled={!cases.length || testing}
            onClick={onRun}
            type="button"
          >
            {testing ? "Đang chạy test" : "Chạy accuracy test"}
          </button>
        </>
      }
      onClose={onClose}
      open={open}
      title={`Q&A test · ${module?.code ?? ""}`}
    >
      <div className="max-h-[58vh] space-y-4 overflow-y-auto pr-1">
        {testResult ? (
          <Alert tone={testResult.accuracyPercent >= 85 ? "success" : "warning"}>
            Version {testResult.version}: đạt {testResult.passedCases}/{testResult.totalCases} câu, accuracy{" "}
            <strong>{testResult.accuracyPercent}%</strong>.
          </Alert>
        ) : null}

        <div className="space-y-3 rounded-lg border border-outline bg-surface-container-low p-4">
          <label className="block">
            <span className="text-label-sm font-bold text-secondary">Câu hỏi người dùng</span>
            <input
              className="mt-2 w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
              onChange={(event) => setQuestion(event.target.value)}
              placeholder="Ví dụ: Học phí khóa HSK 4 là bao nhiêu?"
              value={question}
            />
          </label>
          <label className="block">
            <span className="text-label-sm font-bold text-secondary">Câu trả lời chuẩn</span>
            <textarea
              className="mt-2 min-h-[80px] w-full resize-none rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
              onChange={(event) => setAnswer(event.target.value)}
              value={answer}
            />
          </label>
          <button
            className="w-full rounded border border-primary px-4 py-2 text-body-md font-bold text-primary hover:bg-red-50 disabled:opacity-50"
            disabled={adding || !question.trim() || !answer.trim()}
            onClick={() => {
              onAdd(question.trim(), answer.trim());
              setQuestion("");
              setAnswer("");
            }}
            type="button"
          >
            {adding ? "Đang thêm" : "Thêm câu hỏi"}
          </button>
        </div>

        <div>
          <p className="mb-2 text-label-caps uppercase text-on-surface-variant">Bộ kiểm thử ({cases.length})</p>
          {loading ? (
            <p className="text-body-md text-on-surface-variant">Đang tải test case...</p>
          ) : cases.length ? (
            <div className="space-y-2">
              {cases.map((testCase) => (
                <article className="rounded border border-outline bg-white p-3" key={testCase.id}>
                  <p className="text-body-md font-bold text-secondary">{testCase.question}</p>
                  <p className="mt-2 text-label-sm text-on-surface-variant">{testCase.expectedAnswer}</p>
                </article>
              ))}
            </div>
          ) : (
            <p className="text-body-md text-on-surface-variant">Chưa có test case. Thêm ít nhất một câu trước khi chạy test.</p>
          )}
        </div>
      </div>
    </Modal>
  );
}
