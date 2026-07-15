import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Card, Modal, StatusPill, ToggleSwitch } from "@/shared/ui";
import { toUserFriendlyError } from "@/shared/utils/userText";
import { useJobRun } from "@/features/jobs/useJobRun";
import { getTenantOrchestration, setTenantOrchestration } from "@/shared/api/admin";
import {
  createQuickReply,
  deleteQuickReply,
  generateSaleAssistDraft,
  getSaleAssistDailySummary,
  getSaleAssistUpsell,
  getSaleAssistUpsellSuggestions,
  listQuickReplies,
  summarizeSaleAssistConversation,
  updateQuickReply,
  type CreateQuickReplyPayload,
  type QuickReply,
  type SaleAssistDailySummary,
  type SaleAssistDraftResponse,
  type SaleAssistSummaryResponse,
  type SaleAssistUpsellResponse,
  type SaleAssistUpsellSuggestionsResponse,
  type UpdateQuickReplyPayload,
} from "@/shared/api/saleAssist";

type NoticeTone = "info" | "success" | "warning" | "error";
type QuickReplyDialogMode = "create" | "edit";

interface SaleAssistPanelProps {
  readonly conversationId: string | null | undefined;
  readonly platform: string | null | undefined;
  readonly onUseDraft: (value: string) => void;
  readonly onNotify?: (message: string, tone?: NoticeTone) => void;
}

interface DraftState {
  readonly conversationId: string;
  readonly response: SaleAssistDraftResponse;
  readonly text: string;
}

interface SummaryState {
  readonly conversationId: string;
  readonly response: SaleAssistSummaryResponse;
}

interface QuickReplyFormState {
  readonly code: string;
  readonly category: string;
  readonly platforms: string;
  readonly body: string;
}

interface QuickReplyDialogState {
  readonly mode: QuickReplyDialogMode;
  readonly reply: QuickReply | null;
}

interface QuickReplyDialogProps {
  readonly state: QuickReplyDialogState;
  readonly saving: boolean;
  readonly error: unknown;
  readonly onClose: () => void;
  readonly onSubmit: (payload: QuickReplyFormState) => void;
}

const EMPTY_QUICK_REPLY: QuickReplyFormState = {
  code: "",
  category: "",
  platforms: "",
  body: "",
};

function errorMessage(error: unknown): string {
  return toUserFriendlyError(error, "Không xử lý được thao tác hỗ trợ bán hàng. Vui lòng thử lại.");
}

function actionLabel(action: string | null | undefined): string {
  const normalized = (action ?? "").toLowerCase();
  if (normalized.includes("book_trial")) return "Đặt lịch học thử";
  if (normalized.includes("send_quote")) return "Gửi báo giá";
  if (normalized.includes("ask_goal")) return "Hỏi mục tiêu";
  if (normalized.includes("follow_up")) return "Theo dõi lại";
  return action || "Đề xuất tiếp theo";
}

function scoreTone(score: number): "success" | "warning" | "neutral" {
  if (score >= 70) return "success";
  if (score >= 40) return "warning";
  return "neutral";
}

function responseSpeedLabel(ms: number): string {
  return ms < 5000 ? "Phản hồi nhanh" : "Đã xử lý";
}

function formatRelative(value: string | null): string {
  if (!value) return "Chưa có hoạt động";
  const at = new Date(value).getTime();
  const diff = Date.now() - at;
  if (Number.isNaN(at)) return value;
  const minutes = Math.round(diff / 60_000);
  if (minutes < 1) return "Vừa xong";
  if (minutes < 60) return `${minutes} phút trước`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} giờ trước`;
  const days = Math.round(hours / 24);
  return `${days} ngày trước`;
}

function matchesPlatform(reply: QuickReply, platform: string | null | undefined): boolean {
  if (!platform || !reply.platforms) return true;
  const channel = platform.toLowerCase();
  return reply.platforms
    .toLowerCase()
    .split(/[,;\s]+/)
    .filter(Boolean)
    .some((item) => channel.includes(item) || item.includes(channel));
}

function quickReplyInitialState(reply: QuickReply | null): QuickReplyFormState {
  if (!reply) return EMPTY_QUICK_REPLY;
  return {
    code: reply.code,
    category: reply.category ?? "",
    platforms: reply.platforms ?? "",
    body: reply.body,
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value && typeof value === "object");
}

function isDailySummary(value: unknown): value is SaleAssistDailySummary {
  return (
    isRecord(value) &&
    typeof value.new_leads === "number" &&
    typeof value.conversations === "number" &&
    typeof value.messages_sent === "number" &&
    typeof value.hot_leads === "number"
  );
}

function isUpsell(value: unknown): value is SaleAssistUpsellResponse {
  return (
    isRecord(value) &&
    typeof value.eligible === "boolean" &&
    typeof value.suggestion === "string" &&
    typeof value.reason === "string" &&
    typeof value.leadScore === "number"
  );
}

function isUpsellSuggestions(value: unknown): value is SaleAssistUpsellSuggestionsResponse {
  return isRecord(value) && Array.isArray(value.hot_leads) && typeof value.count === "number";
}

function QuickReplyDialog({ state, saving, error, onClose, onSubmit }: QuickReplyDialogProps) {
  const [form, setForm] = useState<QuickReplyFormState>(() => quickReplyInitialState(state.reply));
  const isEdit = state.mode === "edit";

  return (
    <Modal
      open
      onClose={onClose}
      title={isEdit ? "Chỉnh sửa phản hồi tự động" : "Tạo phản hồi nhanh"}
      footer={
        <>
          <Button type="button" variant="ghost" onClick={onClose} disabled={saving}>
            Hủy bỏ
          </Button>
          <Button
            type="button"
            onClick={() => onSubmit(form)}
            disabled={saving || !form.body.trim() || (!isEdit && !form.code.trim())}
          >
            {saving ? "Đang lưu..." : isEdit ? "Cập nhật" : "Lưu phản hồi"}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {error ? <Alert tone="error">{errorMessage(error)}</Alert> : null}
        <label className="block">
          <span className="mb-1 block text-label-caps uppercase text-secondary">Mã gợi nhớ</span>
          <input
            value={form.code}
            onChange={(event) => setForm((old) => ({ ...old, code: event.target.value.toUpperCase() }))}
            disabled={isEdit}
            className="w-full rounded border border-outline bg-surface-container-lowest px-3 py-2 text-body-md outline-none focus:border-primary focus:ring-1 focus:ring-primary disabled:bg-surface"
            placeholder="VD: HSK4_GOI"
          />
        </label>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <label className="block">
            <span className="mb-1 block text-label-caps uppercase text-secondary">Nhóm</span>
            <input
              value={form.category}
              onChange={(event) => setForm((old) => ({ ...old, category: event.target.value }))}
              className="w-full rounded border border-outline bg-surface-container-lowest px-3 py-2 text-body-md outline-none focus:border-primary focus:ring-1 focus:ring-primary"
              placeholder="Tư vấn"
            />
          </label>
          <label className="block">
            <span className="mb-1 block text-label-caps uppercase text-secondary">Kênh</span>
            <input
              value={form.platforms}
              onChange={(event) => setForm((old) => ({ ...old, platforms: event.target.value }))}
              className="w-full rounded border border-outline bg-surface-container-lowest px-3 py-2 text-body-md outline-none focus:border-primary focus:ring-1 focus:ring-primary"
              placeholder="facebook, zalo, chat web"
            />
          </label>
        </div>
        <label className="block">
          <span className="mb-1 block text-label-caps uppercase text-secondary">Nội dung phản hồi</span>
          <textarea
            value={form.body}
            onChange={(event) => setForm((old) => ({ ...old, body: event.target.value }))}
            rows={6}
            className="max-h-64 min-h-32 w-full resize-y rounded border border-outline bg-surface-container-lowest px-3 py-2 text-body-md outline-none focus:border-primary focus:ring-1 focus:ring-primary"
            placeholder="Nhập nội dung sale có thể gửi nhanh..."
          />
        </label>
      </div>
    </Modal>
  );
}

export function SaleAssistPanel({ conversationId, platform, onUseDraft, onNotify }: SaleAssistPanelProps) {
  const queryClient = useQueryClient();
  // "Duyệt tay AI reply" là flag tenant-global (requireChatReplyApproval) — cùng nguồn với dialog
  // "Cấu hình duyệt" ở /agents. Share query key ["tenant","orchestration"] để 2 chỗ luôn đồng bộ.
  const orchestrationQuery = useQuery({
    queryKey: ["tenant", "orchestration"],
    queryFn: getTenantOrchestration,
    staleTime: 60_000,
  });
  const manualApproval = orchestrationQuery.data?.requireChatReplyApproval ?? false;
  const manualApprovalMutation = useMutation({
    // PUT ghi cả requireApproval + cap, nên phải gửi kèm giá trị hiện tại (tránh xoá nhầm field khác).
    mutationFn: (next: boolean) =>
      setTenantOrchestration(
        orchestrationQuery.data?.requireApproval ?? false,
        orchestrationQuery.data?.monthlyCostCapUsd ?? null,
        { requireChatReplyApproval: next },
      ),
    onSuccess: async (res) => {
      await queryClient.invalidateQueries({ queryKey: ["tenant", "orchestration"] });
      onNotify?.(res.requireChatReplyApproval ? "Đã bật duyệt tay AI reply." : "Đã tắt duyệt tay AI reply.", "success");
    },
    onError: (error) => onNotify?.(toUserFriendlyError(error), "error"),
  });
  const [draftState, setDraftState] = useState<DraftState | null>(null);
  const [summaryState, setSummaryState] = useState<SummaryState | null>(null);
  const [dialogState, setDialogState] = useState<QuickReplyDialogState | null>(null);

  const quickRepliesQuery = useQuery({
    queryKey: ["sale-assist", "quick-replies"],
    queryFn: listQuickReplies,
    staleTime: 60_000,
  });
  const dailySummaryQuery = useQuery({
    queryKey: ["sale-assist", "daily-summary"],
    queryFn: getSaleAssistDailySummary,
    staleTime: 60_000,
  });
  // 3 việc LLM (upsell / nháp / tóm tắt) chạy ngầm qua job — thấy được ở "Việc đang chạy", huỷ được.
  // Kết quả đổ thẳng vào panel (job không bắn thông báo: sale đang ngồi nhìn màn hình chờ).
  const upsellRun = useJobRun<SaleAssistUpsellResponse>();
  const upsellStart = upsellRun.start;
  useEffect(() => {
    if (!conversationId) return;
    void upsellStart(() => getSaleAssistUpsell(conversationId));
  }, [conversationId, upsellStart]);
  const upsellSuggestionsQuery = useQuery({
    queryKey: ["sale-assist", "upsell-suggestions"],
    queryFn: getSaleAssistUpsellSuggestions,
    staleTime: 60_000,
  });

  const draftRun = useJobRun<SaleAssistDraftResponse>({
    onResult: (response) => {
      if (!conversationId) return;
      setDraftState({ conversationId, response, text: response.draftText ?? "" });
      onNotify?.("AI đã tạo bản nháp, đang chờ sale duyệt.", "success");
    },
  });

  const summaryRun = useJobRun<SaleAssistSummaryResponse>({
    onResult: (response) => {
      if (!conversationId) return;
      setSummaryState({ conversationId, response });
      onNotify?.("Đã cập nhật tóm tắt hội thoại.", "success");
    },
  });

  const createMutation = useMutation({
    mutationFn: createQuickReply,
    onSuccess: async () => {
      setDialogState(null);
      await queryClient.invalidateQueries({ queryKey: ["sale-assist", "quick-replies"] });
      onNotify?.("Đã lưu phản hồi nhanh.", "success");
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, payload }: { readonly id: string; readonly payload: UpdateQuickReplyPayload }) => updateQuickReply(id, payload),
    onSuccess: async () => {
      setDialogState(null);
      await queryClient.invalidateQueries({ queryKey: ["sale-assist", "quick-replies"] });
      onNotify?.("Đã cập nhật phản hồi nhanh.", "success");
    },
  });

  const deleteMutation = useMutation({
    mutationFn: deleteQuickReply,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["sale-assist", "quick-replies"] });
      onNotify?.("Đã xóa phản hồi nhanh.", "success");
    },
  });

  const activeDraft = draftState?.conversationId === conversationId ? draftState : null;
  const activeSummary = summaryState?.conversationId === conversationId ? summaryState : null;
  const quickReplies = useMemo(
    () => (Array.isArray(quickRepliesQuery.data) ? quickRepliesQuery.data : []),
    [quickRepliesQuery.data]
  );
  const filteredReplies = useMemo(
    () => quickReplies.filter((reply) => matchesPlatform(reply, platform)).slice(0, 6),
    [platform, quickReplies]
  );
  const busySavingQuickReply = createMutation.isPending || updateMutation.isPending;
  const dialogError = createMutation.error ?? updateMutation.error;
  const activeError = draftRun.error ?? summaryRun.error ?? upsellRun.error ?? quickRepliesQuery.error;
  const summary = isDailySummary(dailySummaryQuery.data) ? dailySummaryQuery.data : null;
  const upsell = isUpsell(upsellRun.data) ? upsellRun.data : null;
  const upsellSuggestions = isUpsellSuggestions(upsellSuggestionsQuery.data) ? upsellSuggestionsQuery.data : null;

  function submitQuickReply(form: QuickReplyFormState) {
    const body = form.body.trim();
    const category = form.category.trim() || null;
    const platforms = form.platforms.trim() || null;
    if (!body || !dialogState) return;

    if (dialogState.mode === "edit" && dialogState.reply) {
      updateMutation.mutate({ id: dialogState.reply.id, payload: { body, category, platforms } });
      return;
    }

    const payload: CreateQuickReplyPayload = {
      code: form.code.trim().toUpperCase(),
      body,
      category,
      platforms,
    };
    if (!payload.code) return;
    createMutation.mutate(payload);
  }

  function applyDraft(text: string) {
    onUseDraft(text);
    onNotify?.(manualApproval ? "Đã đưa bản nháp đã duyệt vào ô soạn." : "Đã đưa bản nháp AI vào ô soạn.", "success");
  }

  return (
    <>
      <Card>
        <div className="mb-4 flex items-start justify-between gap-3">
          <div>
            <h3 className="flex items-center gap-2 text-label-caps uppercase text-secondary">
              <span aria-hidden="true" className="material-symbols-outlined text-primary">psychology_alt</span>
              Trợ lý tư vấn
            </h3>
            <p className="mt-1 text-label-sm text-on-surface-variant">Bản nháp AI, trả lời nhanh và gợi ý bán thêm.</p>
          </div>
          <StatusPill tone={manualApproval ? "warning" : "success"}>{manualApproval ? "Duyệt tay" : "Tự động"}</StatusPill>
        </div>

        <div className="mb-4 flex items-center justify-between rounded-lg border border-outline bg-surface p-3">
          <ToggleSwitch
            checked={manualApproval}
            onChange={(next) => manualApprovalMutation.mutate(next)}
            disabled={manualApprovalMutation.isPending || orchestrationQuery.isLoading}
            label="Duyệt trước khi gửi"
          />
          <span className="text-label-sm text-on-surface-variant">{platform ?? "Mọi kênh"}</span>
        </div>

        {summary ? (
          <div className="mb-4 grid grid-cols-2 gap-2">
            <div className="rounded border border-outline bg-surface p-2">
              <p className="text-mono-status text-on-surface-variant">Lead mới</p>
              <p className="text-telemetry-data text-primary">{summary.new_leads}</p>
            </div>
            <div className="rounded border border-outline bg-surface p-2">
              <p className="text-mono-status text-on-surface-variant">Hot lead</p>
              <p className="text-telemetry-data text-warning">{summary.hot_leads}</p>
            </div>
            <div className="rounded border border-outline bg-surface p-2">
              <p className="text-mono-status text-on-surface-variant">Hội thoại</p>
              <p className="text-telemetry-data text-secondary">{summary.conversations}</p>
            </div>
            <div className="rounded border border-outline bg-surface p-2">
              <p className="text-mono-status text-on-surface-variant">Đã gửi</p>
              <p className="text-telemetry-data text-success">{summary.messages_sent}</p>
            </div>
          </div>
        ) : null}

        {activeError ? <Alert tone="error">{errorMessage(activeError)}</Alert> : null}

        <div className="mt-4 grid grid-cols-1 gap-2 sm:grid-cols-2">
          <Button
            type="button"
            onClick={() => {
              if (conversationId) void draftRun.start(() => generateSaleAssistDraft(conversationId));
            }}
            disabled={!conversationId || draftRun.running}
          >
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">auto_awesome</span>
            {draftRun.running ? "Đang tạo..." : "Tạo nháp AI"}
          </Button>
          <Button
            type="button"
            variant="outline"
            onClick={() => {
              if (conversationId) void summaryRun.start(() => summarizeSaleAssistConversation(conversationId));
            }}
            disabled={!conversationId || summaryRun.running}
          >
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">summarize</span>
            {summaryRun.running ? "Đang tóm tắt..." : "Tóm tắt"}
          </Button>
        </div>

        {!conversationId ? (
          <div className="mt-4 rounded-lg border border-dashed border-outline bg-surface p-4 text-body-md text-on-surface-variant">
            Chọn một hội thoại để tạo nháp và kiểm tra upsell.
          </div>
        ) : null}

        {activeDraft ? (
          <div className="mt-4 rounded-lg border border-amber-200 bg-amber-50/60 p-3">
            <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
              <div className="flex flex-wrap gap-2">
                <StatusPill tone={scoreTone(activeDraft.response.leadScoreHint)}>Điểm {activeDraft.response.leadScoreHint}</StatusPill>
                <StatusPill tone="neutral">{actionLabel(activeDraft.response.suggestedAction)}</StatusPill>
              </div>
              <span className="text-label-sm text-on-surface-variant">{responseSpeedLabel(activeDraft.response.latencyMs)}</span>
            </div>
            <textarea
              value={activeDraft.text ?? ""}
              onChange={(event) =>
                setDraftState((old) => (old ? { ...old, text: event.target.value } : old))
              }
              rows={5}
              className="max-h-48 min-h-28 w-full resize-y rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary focus:ring-1 focus:ring-primary"
            />
            <div className="mt-3 flex flex-wrap gap-2">
              <Button
                type="button"
                size="sm"
                onClick={() => {
                  applyDraft(activeDraft.text ?? "");
                  setDraftState(null);
                }}
                disabled={!(activeDraft.text ?? "").trim()}
              >
                <span aria-hidden="true" className="material-symbols-outlined text-[16px]">approval</span>
                Dùng bản nháp
              </Button>
              <Button type="button" size="sm" variant="ghost" onClick={() => setDraftState(null)}>
                <span aria-hidden="true" className="material-symbols-outlined text-[16px]">close</span>
                Từ chối
              </Button>
            </div>
          </div>
        ) : null}

        {activeSummary ? (
          <div className="mt-4 rounded-lg border border-outline bg-surface p-3">
            <div className="mb-2 flex items-center justify-between">
              <p className="text-label-caps uppercase text-secondary">Tóm tắt hội thoại</p>
              <span className="text-label-sm text-on-surface-variant">{responseSpeedLabel(activeSummary.response.latencyMs)}</span>
            </div>
            <p className="text-body-md text-on-surface">{activeSummary.response.summary}</p>
          </div>
        ) : null}
      </Card>

      <Card>
        <div className="mb-4 flex items-center justify-between gap-3">
          <h3 className="text-label-caps uppercase text-secondary">Phản hồi nhanh</h3>
          <Button type="button" size="sm" variant="outline" onClick={() => setDialogState({ mode: "create", reply: null })}>
            <span aria-hidden="true" className="material-symbols-outlined text-[16px]">add</span>
            Thêm
          </Button>
        </div>
        {quickRepliesQuery.isLoading ? (
          <p className="text-body-md text-on-surface-variant">Đang tải phản hồi nhanh...</p>
        ) : filteredReplies.length === 0 ? (
          <div className="rounded-lg border border-dashed border-outline bg-surface p-4 text-body-md text-on-surface-variant">
            Chưa có phản hồi nhanh phù hợp kênh này.
          </div>
        ) : (
          <div className="space-y-2">
            {filteredReplies.map((reply) => (
              <div key={reply.id} className="rounded-lg border border-outline bg-surface p-3">
                <div className="mb-2 flex items-start justify-between gap-3">
                  <div>
                    <p className="font-mono text-mono-status font-semibold text-secondary">{reply.code}</p>
                    <p className="text-label-sm text-on-surface-variant">
                      {[reply.category, reply.platforms].filter(Boolean).join(" · ") || "Dùng chung"}
                    </p>
                  </div>
                  <div className="flex gap-1">
                    <button
                      type="button"
                      className="flex size-8 items-center justify-center rounded text-on-surface-variant hover:bg-surface-variant"
                      onClick={() => setDialogState({ mode: "edit", reply })}
                      aria-label="Chỉnh sửa phản hồi nhanh"
                    >
                      <span aria-hidden="true" className="material-symbols-outlined text-[18px]">edit</span>
                    </button>
                    <button
                      type="button"
                      className="flex size-8 items-center justify-center rounded text-error hover:bg-error/10"
                      onClick={() => deleteMutation.mutate(reply.id)}
                      disabled={deleteMutation.isPending}
                      aria-label="Xóa phản hồi nhanh"
                    >
                      <span aria-hidden="true" className="material-symbols-outlined text-[18px]">delete</span>
                    </button>
                  </div>
                </div>
                <p className="line-clamp-3 text-body-md text-on-surface">{reply.body}</p>
                <Button type="button" size="sm" variant="ghost" className="mt-2" onClick={() => applyDraft(reply.body)}>
                  <span aria-hidden="true" className="material-symbols-outlined text-[16px]">reply</span>
                  Dùng trong ô soạn
                </Button>
              </div>
            ))}
          </div>
        )}
      </Card>

      <Card>
        <h3 className="mb-4 text-label-caps uppercase text-secondary">Gợi ý bán thêm</h3>
        {upsell ? (
          <div className="rounded-lg border border-outline bg-surface p-3">
            <div className="mb-2 flex items-center justify-between">
              <StatusPill tone={upsell.eligible ? "success" : "neutral"}>
                Điểm {upsell.leadScore}
              </StatusPill>
              <span className="text-label-sm text-on-surface-variant">{upsell.eligible ? "Đủ điều kiện" : "Chưa đủ"}</span>
            </div>
            <p className="text-body-md font-semibold text-on-surface">{upsell.suggestion}</p>
            <p className="mt-2 text-label-sm text-on-surface-variant">{upsell.reason}</p>
          </div>
        ) : (
          <p className="text-body-md text-on-surface-variant">Chọn hội thoại để xem gợi ý upsell.</p>
        )}

        {upsellSuggestions?.hot_leads.length ? (
          <div className="mt-4 space-y-2">
            <p className="text-label-caps uppercase text-secondary">Lead tiềm năng khác</p>
            {upsellSuggestions.hot_leads.slice(0, 3).map((lead) => (
              <div key={lead.id} className="rounded border border-outline bg-surface p-2">
                <div className="flex items-center justify-between gap-2">
                  <p className="truncate text-body-md font-semibold text-on-surface">{lead.contact?.name ?? lead.contact?.phone ?? lead.id}</p>
                  <StatusPill tone={scoreTone(lead.score)}>{lead.score}</StatusPill>
                </div>
                <p className="mt-1 text-label-sm text-on-surface-variant">{formatRelative(lead.lastActivityAt)}</p>
                {lead.eligible ? (
                  <p className="mt-2 text-label-sm font-semibold text-on-surface">{lead.suggestion}</p>
                ) : (
                  <p className="mt-2 text-label-sm text-on-surface-variant">{lead.reason}</p>
                )}
              </div>
            ))}
          </div>
        ) : null}
      </Card>

      {dialogState ? (
        <QuickReplyDialog
          key={dialogState.reply?.id ?? "create"}
          state={dialogState}
          saving={busySavingQuickReply}
          error={dialogError}
          onClose={() => setDialogState(null)}
          onSubmit={submitQuickReply}
        />
      ) : null}
    </>
  );
}
