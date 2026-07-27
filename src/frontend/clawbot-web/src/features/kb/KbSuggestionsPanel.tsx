import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getTenantOrchestration, setTenantOrchestration } from "@/shared/api/admin";
import { Alert } from "@/shared/ui/Alert";
import { Modal } from "@/shared/ui/Modal";
import { toUserFriendlyError } from "@/shared/utils/userText";
import {
  approveKbSuggestion,
  listKbSuggestions,
  rejectKbSuggestion,
  type KbSuggestion,
  type KbSuggestionEvidence,
} from "@/shared/api/kb";

const EMPTY: readonly KbSuggestion[] = [];

const SIGNAL_LABELS: Record<string, string> = {
  ai_failed: "AI trả lời kém",
  sale_answered: "Sale trả lời tay",
  repeated_question: "Câu hỏi lặp nhiều",
};

const OP_LABELS: Record<KbSuggestion["op"], string> = {
  add: "Nhóm tri thức mới",
  update: "Cập nhật nhóm",
  merge: "Gộp vào nhóm",
};

function parseEvidence(json: string): readonly KbSuggestionEvidence[] {
  try {
    const parsed: unknown = JSON.parse(json);
    return Array.isArray(parsed) ? (parsed as KbSuggestionEvidence[]) : [];
  } catch {
    return [];
  }
}

function accuracyText(value: number | null): string {
  return value === null ? "—" : `${value.toFixed(0)}%`;
}

interface SuggestionCardProps {
  readonly suggestion: KbSuggestion;
  readonly busy: boolean;
  readonly onApprove: (id: string, contentMd: string) => void;
  readonly onReject: (id: string) => void;
}

function SuggestionCard({ suggestion, busy, onApprove, onReject }: SuggestionCardProps) {
  const [expanded, setExpanded] = useState(false);
  const [content, setContent] = useState(suggestion.contentMd);
  const evidence = useMemo(() => parseEvidence(suggestion.evidenceJson), [suggestion.evidenceJson]);
  const pending = suggestion.status === "pending";
  const hasAccuracy = suggestion.accuracyBefore !== null && suggestion.accuracyAfter !== null;

  return (
    <article className="rounded-lg border border-outline bg-surface-container-lowest p-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <h3 className="text-body-lg font-bold text-secondary">{suggestion.title}</h3>
          <p className="mt-0.5 text-body-sm text-on-surface-variant">
            {OP_LABELS[suggestion.op]}
            {suggestion.targetModuleName ? ` — ${suggestion.targetModuleName}` : ""}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2 text-body-sm">
          {suggestion.approvalMode === "auto" ? (
            <span className="rounded-full bg-primary/10 px-2 py-0.5 font-semibold text-primary">AI tự duyệt</span>
          ) : null}
          {suggestion.status === "rejected" ? (
            <span className="rounded-full bg-error/10 px-2 py-0.5 font-semibold text-error">Đã loại</span>
          ) : null}
          {suggestion.reviewerVerdict ? (
            <span
              className={[
                "rounded-full px-2 py-0.5 font-semibold",
                suggestion.reviewerVerdict === "approve" ? "bg-success/10 text-success" : "bg-warning/10 text-warning",
              ].join(" ")}
            >
              Reviewer: {suggestion.reviewerVerdict === "approve" ? "đạt" : suggestion.reviewerVerdict === "reject" ? "loại" : "cần người"}
            </span>
          ) : null}
          <span className="rounded-full bg-surface-variant px-2 py-0.5 text-on-surface-variant" title="Accuracy bộ test của nhóm: trước / sau khi thêm đề xuất">
            {hasAccuracy
              ? `Accuracy ${accuracyText(suggestion.accuracyBefore)} → ${accuracyText(suggestion.accuracyAfter)}`
              : "Chưa có bộ test"}
          </span>
        </div>
      </div>

      {suggestion.rationale ? (
        <p className="mt-2 text-body-sm text-on-surface-variant">{suggestion.rationale}</p>
      ) : null}

      {evidence.length > 0 ? (
        <div className="mt-2 rounded border border-outline-variant bg-surface p-2">
          <p className="text-body-sm font-semibold text-secondary">Nguồn gốc</p>
          <ul className="mt-1 space-y-1">
            {evidence.slice(0, 3).map((item, index) => (
              <li className="text-body-sm text-on-surface-variant" key={`${item.conversationId}-${index}`}>
                [{SIGNAL_LABELS[item.signal] ?? item.signal}] {item.snippetRedacted}
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      <button
        className="mt-2 text-body-sm font-semibold text-primary hover:underline"
        onClick={() => setExpanded((v) => !v)}
        type="button"
      >
        {expanded ? "Thu gọn nội dung" : "Xem / sửa nội dung"}
      </button>

      {expanded ? (
        pending ? (
          <textarea
            className="mt-2 w-full rounded border border-outline bg-surface p-2 font-mono text-body-sm"
            onChange={(e) => setContent(e.target.value)}
            rows={8}
            value={content}
          />
        ) : (
          <pre className="mt-2 max-h-64 overflow-auto whitespace-pre-wrap rounded border border-outline-variant bg-surface p-2 text-body-sm">
            {suggestion.contentMd}
          </pre>
        )
      ) : null}

      {pending ? (
        <div className="mt-3 flex gap-2">
          <button
            className="rounded bg-primary px-3 py-1.5 text-body-sm font-bold text-white hover:bg-primary-hover disabled:opacity-50"
            disabled={busy}
            onClick={() => onApprove(suggestion.id, content)}
            type="button"
          >
            Duyệt và đưa vào kho
          </button>
          <button
            className="rounded border border-outline px-3 py-1.5 text-body-sm font-bold text-secondary hover:bg-surface-variant disabled:opacity-50"
            disabled={busy}
            onClick={() => onReject(suggestion.id)}
            type="button"
          >
            Loại
          </button>
        </div>
      ) : null}
    </article>
  );
}

interface KbSuggestionsPanelProps {
  // alwaysShow: dùng khi panel là 1 tab — hiện cả khi rỗng (kèm empty-state) thay vì tự ẩn.
  readonly alwaysShow?: boolean;
}

// Panel "Đề xuất tri thức" (ai-self-learning-memory): đề xuất do job chưng cất đêm sinh —
// pending chờ người duyệt; mục "Đã tự duyệt" để soi lại các bản AI tự đưa vào kho.
export function KbSuggestionsPanel({ alwaysShow = false }: KbSuggestionsPanelProps = {}) {
  const queryClient = useQueryClient();
  const [showDecided, setShowDecided] = useState(false);
  const [rejectTarget, setRejectTarget] = useState<KbSuggestion | null>(null);
  const [rejectReason, setRejectReason] = useState("");

  const suggestionsQuery = useQuery({
    queryKey: ["kb", "suggestions"],
    queryFn: () => listKbSuggestions(undefined, { page: 1, pageSize: 100 }),
  });
  const suggestions = Array.isArray(suggestionsQuery.data?.items) ? suggestionsQuery.data.items : EMPTY;
  const pending = suggestions.filter((s) => s.status === "pending");
  const decided = suggestions.filter((s) => s.status !== "pending");

  const approveMutation = useMutation({
    mutationFn: ({ id, contentMd }: { readonly id: string; readonly contentMd: string }) =>
      approveKbSuggestion(id, contentMd),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["kb"] });
    },
  });

  const rejectMutation = useMutation({
    mutationFn: ({ id, reason }: { readonly id: string; readonly reason: string }) => rejectKbSuggestion(id, reason),
    onSuccess: async () => {
      setRejectTarget(null);
      setRejectReason("");
      await queryClient.invalidateQueries({ queryKey: ["kb", "suggestions"] });
    },
  });

  // Toggle "AI tự duyệt tri thức" (cùng flag requireKbHumanReview với trang /agents). Bật = AI tự đưa
  // vào kho khi đạt chuẩn kép; tắt = mọi đề xuất chờ người. Đặt ngay đây cho đúng ngữ cảnh quản đề xuất.
  const orchestrationQuery = useQuery({ queryKey: ["tenant", "orchestration"], queryFn: getTenantOrchestration });
  const requireKbHumanReview = orchestrationQuery.data?.requireKbHumanReview ?? false;
  const autoApproveMutation = useMutation({
    mutationFn: (nextRequireHuman: boolean) =>
      setTenantOrchestration({ requireKbHumanReview: nextRequireHuman }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["tenant", "orchestration"] });
    },
  });

  if (!alwaysShow && (suggestionsQuery.isLoading || (pending.length === 0 && decided.length === 0))) return null;

  const busy = approveMutation.isPending || rejectMutation.isPending;
  const error = suggestionsQuery.error ?? approveMutation.error ?? rejectMutation.error;

  return (
    <section className="mb-stack-lg rounded-lg border border-outline p-4 shadow-sm">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="text-title-lg font-bold text-secondary">Đề xuất tri thức từ hội thoại</h2>
          <p className="text-body-sm text-on-surface-variant">
            AI chưng cất mỗi đêm từ câu AI trả lời kém, câu sale trả lời tay và câu hỏi lặp nhiều.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <button
            aria-pressed={!requireKbHumanReview}
            className={[
              "flex items-center gap-2 rounded-lg border px-3 py-1.5 text-body-sm font-semibold transition-colors disabled:opacity-60",
              !requireKbHumanReview ? "border-primary bg-primary/10 text-primary" : "border-outline bg-surface-container-lowest text-secondary",
            ].join(" ")}
            disabled={autoApproveMutation.isPending || orchestrationQuery.isLoading}
            onClick={() => autoApproveMutation.mutate(!requireKbHumanReview)}
            title="Bật: tri thức AI chưng cất được tự đưa vào kho khi đạt chuẩn kép (reviewer duyệt + accuracy không giảm); không đạt vẫn chờ người. Tắt: mọi tri thức mới chờ người duyệt."
            type="button"
          >
            <span aria-hidden="true" className="material-symbols-outlined text-[16px]">school</span>
            {!requireKbHumanReview ? "AI tự duyệt & lưu kho: BẬT" : "AI tự duyệt & lưu kho: tắt"}
          </button>
          {decided.length > 0 ? (
            <button
              className="text-body-sm font-semibold text-primary hover:underline"
              onClick={() => setShowDecided((v) => !v)}
              type="button"
            >
              {showDecided ? "Ẩn lịch sử đã quyết" : `Lịch sử đã quyết (${decided.length})`}
            </button>
          ) : null}
        </div>
      </div>

      {error ? (
        <div className="mt-2">
          <Alert tone="error">{toUserFriendlyError(error, "Không xử lý được đề xuất tri thức.")}</Alert>
        </div>
      ) : null}

      {suggestionsQuery.isLoading ? (
        <p className="mt-3 text-body-sm text-on-surface-variant">Đang tải...</p>
      ) : pending.length === 0 ? (
        <p className="mt-3 text-body-sm text-on-surface-variant">Không có đề xuất nào chờ duyệt.</p>
      ) : (
        <div className="mt-3 space-y-3">
          {pending.map((s) => (
            <SuggestionCard
              busy={busy}
              key={s.id}
              onApprove={(id, contentMd) => approveMutation.mutate({ id, contentMd })}
              onReject={() => setRejectTarget(s)}
              suggestion={s}
            />
          ))}
        </div>
      )}

      {showDecided && decided.length > 0 ? (
        <div className="mt-3 space-y-3 border-t border-outline-variant pt-3">
          {decided.map((s) => (
            <SuggestionCard busy key={s.id} onApprove={() => undefined} onReject={() => undefined} suggestion={s} />
          ))}
        </div>
      ) : null}

      <Modal
        footer={
          <>
            <button
              className="rounded px-4 py-2 text-body-md font-bold text-on-surface-variant hover:bg-surface-variant"
              onClick={() => setRejectTarget(null)}
              type="button"
            >
              Hủy
            </button>
            <button
              className="rounded bg-error px-4 py-2 text-body-md font-bold text-white hover:bg-red-700 disabled:opacity-50"
              disabled={rejectMutation.isPending || !rejectReason.trim()}
              onClick={() => rejectTarget && rejectMutation.mutate({ id: rejectTarget.id, reason: rejectReason.trim() })}
              type="button"
            >
              {rejectMutation.isPending ? "Đang loại" : "Loại đề xuất"}
            </button>
          </>
        }
        onClose={() => setRejectTarget(null)}
        open={rejectTarget !== null}
        title="Loại đề xuất tri thức"
      >
        <p className="text-body-md text-on-surface-variant">
          Đề xuất <strong>{rejectTarget?.title}</strong> sẽ bị loại và không được đề xuất lại (cùng câu hỏi).
        </p>
        <label className="mt-3 block text-body-sm font-semibold text-secondary" htmlFor="kb-suggestion-reject-reason">
          Lý do loại
        </label>
        <textarea
          className="mt-1 w-full rounded border border-outline bg-surface p-2 text-body-sm"
          id="kb-suggestion-reject-reason"
          onChange={(e) => setRejectReason(e.target.value)}
          rows={3}
          value={rejectReason}
        />
      </Modal>
    </section>
  );
}
