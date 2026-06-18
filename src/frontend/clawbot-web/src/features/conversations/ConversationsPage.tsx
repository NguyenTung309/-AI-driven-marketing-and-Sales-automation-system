
import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AxiosError } from "axios";
import { AppShell } from "@/shared/layout/AppShell";
import { Alert, Button, Card, Input, StatusPill } from "@/shared/ui";
import { getMe } from "@/shared/api/auth";
import {
  assignConversation,
  escalateConversation,
  getConversation,
  listConversations,
  resolveConversation,
  sendConversationMessage,
  type ConversationDetail,
  type ConversationListItem,
  type ConversationStatus,
  type InboxMessage,
} from "@/shared/api/inbox";
import { SaleAssistPanel } from "./SaleAssistPanel";
import { useInboxRealtime } from "./useInboxRealtime";

type StatusFilter = "all" | "open" | "escalated" | "resolved" | "mine";
type PlatformFilter = "all" | "facebook" | "zalo" | "web";
type NoticeTone = "info" | "success" | "warning" | "error";

interface PageNotice {
  readonly tone: NoticeTone;
  readonly message: string;
}

const STATUS_FILTERS: readonly { value: StatusFilter; label: string; icon?: string }[] = [
  { value: "all", label: "Tất cả" },
  { value: "open", label: "AI đang chat", icon: "smart_toy" },
  { value: "escalated", label: "Cần hỗ trợ", icon: "warning" },
  { value: "mine", label: "Của tôi", icon: "person" },
  { value: "resolved", label: "Đã xử lý", icon: "task_alt" },
];

const PLATFORM_FILTERS: readonly { value: PlatformFilter; label: string }[] = [
  { value: "all", label: "Mọi kênh" },
  { value: "facebook", label: "Facebook" },
  { value: "zalo", label: "Zalo" },
  { value: "web", label: "Web chat" },
];

function toStatusTone(status: ConversationStatus) {
  if (status === "resolved") return "neutral";
  if (status === "escalated") return "warning";
  return "success";
}

function statusLabel(status: ConversationStatus): string {
  if (status === "resolved") return "Đã xử lý";
  if (status === "escalated") return "Cần người hỗ trợ";
  if (status === "open") return "AI đang chat";
  return status;
}

function platformLabel(platform: string): string {
  const value = platform.toLowerCase();
  if (value.includes("facebook") || value === "fb") return "Facebook";
  if (value.includes("zalo") || value === "zl") return "Zalo OA";
  if (value.includes("web")) return "Web chat";
  return platform || "Omnichannel";
}

function platformMark(platform: string): string {
  const label = platformLabel(platform);
  if (label === "Facebook") return "FB";
  if (label === "Zalo OA") return "Z";
  if (label === "Web chat") return "W";
  return label.slice(0, 2).toUpperCase();
}

function platformColor(platform: string): string {
  const label = platformLabel(platform);
  if (label === "Facebook") return "bg-blue-100 text-blue-700 border-blue-200";
  if (label === "Zalo OA") return "bg-indigo-100 text-indigo-700 border-indigo-200";
  if (label === "Web chat") return "bg-emerald-100 text-emerald-700 border-emerald-200";
  return "bg-surface-container text-secondary border-outline";
}

function formatRelative(value: string | null): string {
  if (!value) return "Chưa có";
  const at = new Date(value).getTime();
  const diff = Date.now() - at;
  if (Number.isNaN(at)) return value;
  const mins = Math.max(0, Math.round(diff / 60000));
  if (mins < 1) return "Vừa xong";
  if (mins < 60) return `${mins}p trước`;
  const hours = Math.round(mins / 60);
  if (hours < 24) return `${hours}h trước`;
  return new Intl.DateTimeFormat("vi-VN", { day: "2-digit", month: "2-digit" }).format(new Date(value));
}

function formatTime(value: string): string {
  return new Intl.DateTimeFormat("vi-VN", { hour: "2-digit", minute: "2-digit" }).format(new Date(value));
}

function customerName(conversation: ConversationListItem | ConversationDetail): string {
  return conversation.contactDisplayName?.trim() || conversation.externalThreadId || "Khách chưa định danh";
}

function isOutbound(message: InboxMessage): boolean {
  return message.direction === "out" || message.senderType === "user" || message.senderType === "agent";
}

function errorMessage(error: unknown): string {
  if (error instanceof AxiosError) {
    if (error.response?.status === 404) return "Không tìm thấy hội thoại.";
    if (error.response?.status === 401) return "Phiên đăng nhập hết hạn hoặc thiếu quyền truy cập.";
    if (error.response?.status === 400) return "Backend từ chối dữ liệu gửi lên.";
  }
  return "Không thể kết nối dữ liệu. Kiểm tra dịch vụ và thử lại.";
}

function realtimeLabel(state: ReturnType<typeof useInboxRealtime>): string {
  if (state === "connected") return "Realtime đang kết nối";
  if (state === "reconnecting") return "Realtime đang nối lại";
  if (state === "connecting") return "Đang mở realtime";
  if (state === "disabled") return "Realtime chờ token";
  return "Realtime gián đoạn";
}

function realtimeTone(state: ReturnType<typeof useInboxRealtime>) {
  if (state === "connected") return "success";
  if (state === "connecting" || state === "reconnecting") return "warning";
  return "neutral";
}

interface FilterChipProps {
  readonly active: boolean;
  readonly label: string;
  readonly icon?: string;
  readonly onClick: () => void;
}

function FilterChip({ active, label, icon, onClick }: FilterChipProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={[
        "inline-flex items-center gap-1.5 whitespace-nowrap rounded-full border px-3 py-1 text-label-sm font-semibold transition-colors",
        active
          ? "border-primary/20 bg-primary/10 text-primary"
          : "border-outline bg-surface-container-lowest text-on-surface-variant hover:bg-surface-container-low",
      ].join(" ")}
    >
      {icon ? <span className="material-symbols-outlined text-[14px]">{icon}</span> : null}
      {label}
    </button>
  );
}

interface ConversationRowProps {
  readonly conversation: ConversationListItem;
  readonly selected: boolean;
  readonly onSelect: () => void;
}

function ConversationRow({ conversation, selected, onSelect }: ConversationRowProps) {
  const name = customerName(conversation);
  return (
    <button
      type="button"
      onClick={onSelect}
      className={[
        "relative w-full border-b border-surface-variant p-4 text-left transition-colors hover:bg-surface-container-low",
        selected ? "bg-primary/5" : "bg-surface-container-lowest",
      ].join(" ")}
    >
      {selected ? <span className="absolute left-0 top-0 h-full w-1 bg-primary" /> : null}
      <div className="flex items-start gap-3">
        <div
          className={`flex size-10 shrink-0 items-center justify-center rounded-lg border text-xs font-bold ${platformColor(
            conversation.platform
          )}`}
        >
          {platformMark(conversation.platform)}
        </div>
        <div className="min-w-0 flex-1">
          <div className="mb-1 flex items-start justify-between gap-2">
            <h3 className="truncate text-body-md font-bold text-on-surface">{name}</h3>
            <span className="shrink-0 text-label-sm text-on-surface-variant">
              {formatRelative(conversation.lastMessageAt)}
            </span>
          </div>
          <p className="truncate text-body-md text-on-surface-variant">
            {conversation.lastMessagePreview || "Chưa có tin nhắn mới"}
          </p>
          <div className="mt-2 flex flex-wrap items-center gap-2">
            <StatusPill tone={toStatusTone(conversation.status)}>{statusLabel(conversation.status)}</StatusPill>
            <span className="rounded bg-surface-container px-2 py-0.5 text-label-sm font-semibold text-secondary">
              {platformLabel(conversation.platform)}
            </span>
            {conversation.unreadCount > 0 ? (
              <span className="rounded-full bg-primary px-2 py-0.5 text-label-sm font-bold text-on-primary">
                {conversation.unreadCount}
              </span>
            ) : null}
          </div>
        </div>
      </div>
    </button>
  );
}

interface MessageBubbleProps {
  readonly message: InboxMessage;
}

function MessageBubble({ message }: MessageBubbleProps) {
  const outbound = isOutbound(message);
  const byAi = message.senderType === "ai" || message.senderType === "bot";
  return (
    <div className={`flex gap-3 ${outbound ? "justify-end" : "justify-start"}`}>
      {!outbound ? (
        <div className="flex size-8 shrink-0 items-center justify-center rounded-full bg-surface-variant text-label-sm font-bold text-secondary">
          KH
        </div>
      ) : null}
      <div
        className={[
          "max-w-[78%] rounded-2xl border p-3 shadow-[0_2px_4px_rgba(15,23,42,0.04)]",
          outbound
            ? byAi
              ? "rounded-tr-sm border-tertiary/20 bg-tertiary/5"
              : "rounded-tr-sm border-primary/20 bg-primary/10"
            : "rounded-tl-sm border-surface-variant bg-white",
        ].join(" ")}
      >
        <p className="whitespace-pre-wrap text-body-md text-on-surface">{message.content}</p>
        <span className={`mt-1 block text-label-sm text-on-surface-variant ${outbound ? "text-right" : ""}`}>
          {formatTime(message.sentAt)}
          {byAi ? " - AI trả lời" : outbound ? " - Đã gửi" : ""}
        </span>
      </div>
      {outbound ? (
        <div
          className={[
            "flex size-8 shrink-0 items-center justify-center rounded-full border text-xs font-bold",
            byAi ? "border-tertiary/20 bg-tertiary/10 text-tertiary" : "border-primary/20 bg-primary text-on-primary",
          ].join(" ")}
        >
          <span className="material-symbols-outlined text-[16px]">{byAi ? "smart_toy" : "support_agent"}</span>
        </div>
      ) : null}

    </div>
  );
}

interface ChatPanelProps {
  readonly conversation: ConversationDetail | undefined;
  readonly isLoading: boolean;
  readonly error: unknown;
  readonly draft: string;
  readonly onDraftChange: (value: string) => void;
  readonly onSubmit: () => void;
  readonly sending: boolean;
}

function ChatPanel({ conversation, isLoading, error, draft, onDraftChange, onSubmit, sending }: ChatPanelProps) {
  if (isLoading) {
    return (
      <section className="flex h-full min-h-[720px] flex-col rounded-lg border border-outline bg-surface-container-lowest">
        <div className="m-auto text-body-md text-on-surface-variant">Đang tải hội thoại...</div>
      </section>
    );
  }

  if (error) {
    return (
      <section className="flex h-full min-h-[720px] flex-col rounded-lg border border-outline bg-surface-container-lowest">
        <div className="m-auto max-w-md text-center">
          <span className="material-symbols-outlined text-[40px] text-error">error</span>
          <p className="mt-3 text-body-md text-on-surface">{errorMessage(error)}</p>
        </div>
      </section>
    );
  }

  if (!conversation) {
    return (
      <section className="flex h-full min-h-[720px] flex-col rounded-lg border border-outline bg-surface-container-lowest">
        <div className="m-auto max-w-md text-center">
          <span className="material-symbols-outlined text-[44px] text-on-surface-variant">forum</span>
          <h2 className="mt-3 text-headline-sm">Chưa có hội thoại</h2>
          <p className="mt-2 text-body-md text-on-surface-variant">
            Khi có dữ liệu hội thoại mới, nội dung chat sẽ hiển thị tại đây.
          </p>
        </div>
      </section>
    );
  }

  return (
    <section className="flex h-full min-h-[720px] flex-col overflow-hidden rounded-lg border border-outline bg-surface-container-lowest">
      {conversation.status === "escalated" ? (
        <div className="flex items-center justify-between bg-warning px-gutter py-2 text-label-lg font-semibold text-white">
          <span className="flex items-center gap-2">
            <span className="material-symbols-outlined text-[18px]">warning</span>
            Hội thoại đang cần người hỗ trợ trực tiếp.
          </span>
        </div>
      ) : null}

      <header className="flex shrink-0 items-center justify-between border-b border-outline bg-white p-gutter">
        <div className="flex items-center gap-3">
          <div
            className={`flex size-10 items-center justify-center rounded-full border text-sm font-bold ${platformColor(
              conversation.platform
            )}`}
          >
            {platformMark(conversation.platform)}
          </div>
          <div>
            <h2 className="flex items-center gap-2 text-headline-sm">
              {customerName(conversation)}
              <span className="rounded bg-surface-container px-2 py-0.5 text-label-sm font-semibold text-secondary">
                {platformLabel(conversation.platform)}
              </span>
            </h2>
            <p className="text-label-sm text-on-surface-variant">
              Thread {conversation.externalThreadId} · {statusLabel(conversation.status)}
            </p>
          </div>
        </div>
        <StatusPill tone={toStatusTone(conversation.status)}>{statusLabel(conversation.status)}</StatusPill>
      </header>

      <div className="flex-1 space-y-4 overflow-y-auto bg-surface p-gutter">
        {conversation.messages.length === 0 ? (
          <div className="rounded-lg border border-dashed border-outline bg-white p-6 text-center text-body-md text-on-surface-variant">
            Chưa có message trong hội thoại này.
          </div>
        ) : (
          conversation.messages.map((message) => <MessageBubble key={message.id} message={message} />)
        )}
      </div>

      <footer className="shrink-0 border-t border-outline bg-white p-gutter">
        <div className="mb-2 flex flex-wrap gap-2">
          <button
            type="button"
            className="inline-flex items-center gap-1 rounded border border-outline px-2 py-1 text-label-sm text-secondary hover:bg-surface"
          >
            <span className="material-symbols-outlined text-[14px]">attach_file</span>
            Đính kèm
          </button>
          <button
            type="button"
            className="inline-flex items-center gap-1 rounded border border-amber-300 bg-amber-50/70 px-2 py-1 text-label-sm text-amber-800"
          >
            <span className="material-symbols-outlined text-[14px]">star</span>
            Gắn thẻ khách VIP
          </button>
        </div>
        <div className="flex items-end gap-2 rounded-xl border border-outline bg-surface-container-low p-2 focus-within:border-primary focus-within:ring-1 focus-within:ring-primary">
          <textarea
            value={draft}
            onChange={(event) => onDraftChange(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Enter" && (event.ctrlKey || event.metaKey)) onSubmit();
            }}
            rows={2}
            className="max-h-32 min-h-[44px] flex-1 resize-none bg-transparent py-2 text-body-md text-on-surface outline-none"
            placeholder="Nhập tin nhắn hỗ trợ..."
          />
          <button
            type="button"
            onClick={onSubmit}
            disabled={!draft.trim() || sending}
            className="mb-1 flex size-10 shrink-0 items-center justify-center rounded-lg bg-primary text-on-primary transition-colors hover:bg-primary-hover disabled:opacity-50"
            aria-label="Gửi tin nhắn"
          >
            <span className="material-symbols-outlined">send</span>
          </button>
        </div>
      </footer>
    </section>
  );
}

interface ContextPanelProps {
  readonly conversation: ConversationDetail | undefined;
  readonly meId: string | null;
  readonly onAssign: () => void;
  readonly onEscalate: () => void;
  readonly onResolve: () => void;
  readonly onUseSaleAssistDraft: (value: string) => void;
  readonly onNotify: (message: string, tone?: NoticeTone) => void;
  readonly busy: boolean;
}

function ContextPanel({
  conversation,
  meId,
  onAssign,
  onEscalate,
  onResolve,
  onUseSaleAssistDraft,
  onNotify,
  busy,
}: ContextPanelProps) {
  const assignedToMe = Boolean(conversation?.assignedTo && meId && conversation.assignedTo === meId);
  return (
    <aside className="flex min-h-[720px] flex-col gap-gutter overflow-y-auto">
      <Card>
        <h3 className="mb-4 text-label-caps uppercase text-secondary">Thông tin khách hàng</h3>
        <div className="text-center">
          <div className="mx-auto flex size-16 items-center justify-center rounded-full border-2 border-white bg-surface-variant text-headline-sm font-bold text-secondary shadow-sm">
            {conversation ? customerName(conversation).slice(0, 1).toUpperCase() : "?"}
          </div>
          <h4 className="mt-3 text-headline-sm">{conversation ? customerName(conversation) : "Chưa chọn"}</h4>
          <div className="mt-2 inline-flex items-center gap-1 rounded-full border border-amber-200 bg-amber-50 px-3 py-1">
            <span className="material-symbols-outlined text-[16px] text-amber-500">star</span>
            <span className="text-label-sm font-bold text-amber-800">Ưu tiên chăm sóc</span>
          </div>
        </div>
        <div className="mt-5 space-y-3 text-body-md">
          <div className="flex items-center gap-3">
            <span className="material-symbols-outlined text-[18px] text-tertiary">hub</span>
            <span>{conversation ? platformLabel(conversation.platform) : "Mọi kênh"}</span>
          </div>
          <div className="flex items-center gap-3">
            <span className="material-symbols-outlined text-[18px] text-tertiary">tag</span>
            <span className="font-mono text-mono-status">{conversation?.externalThreadId ?? "N/A"}</span>
          </div>
          <div className="flex items-center gap-3">
            <span className="material-symbols-outlined text-[18px] text-tertiary">schedule</span>
            <span>{formatRelative(conversation?.lastMessageAt ?? null)}</span>
          </div>
        </div>
      </Card>

      <Card>
        <h3 className="mb-4 text-label-caps uppercase text-secondary">Điều phối hội thoại</h3>
        <div className="space-y-2">
          <Button
            type="button"
            className="w-full"
            variant={assignedToMe ? "outline" : "primary"}
            onClick={onAssign}
            disabled={!conversation || !meId || busy || assignedToMe}
          >
            <span className="material-symbols-outlined text-[18px]">person_add</span>
            {assignedToMe ? "Đã gán cho bạn" : "Gán cho tôi"}
          </Button>
          <Button type="button" className="w-full" variant="outline" onClick={onEscalate} disabled={!conversation || busy}>
            <span className="material-symbols-outlined text-[18px]">warning</span>
            Cần người hỗ trợ
          </Button>
          <Button type="button" className="w-full" variant="ghost" onClick={onResolve} disabled={!conversation || busy}>
            <span className="material-symbols-outlined text-[18px]">task_alt</span>
            Đánh dấu đã xử lý
          </Button>
        </div>
        {!meId ? (
          <p className="mt-3 text-label-sm text-error">Không đọc được thông tin người dùng, chưa thể gán hội thoại.</p>
        ) : null}
      </Card>

      <SaleAssistPanel
        conversationId={conversation?.id ?? null}
        platform={conversation?.platform ?? null}
        onUseDraft={onUseSaleAssistDraft}
        onNotify={onNotify}
      />
    </aside>
  );
}

export default function ConversationsPage() {
  const queryClient = useQueryClient();
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");
  const [platformFilter, setPlatformFilter] = useState<PlatformFilter>("all");
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [draft, setDraft] = useState("");
  const [notice, setNotice] = useState<PageNotice | null>(null);

  useEffect(() => {
    if (!notice) return;
    const timeout = window.setTimeout(() => setNotice(null), 3_800);
    return () => window.clearTimeout(timeout);
  }, [notice]);

  function showNotice(message: string, tone: NoticeTone = "info") {
    setNotice({ message, tone });
  }

  const meQuery = useQuery({ queryKey: ["me"], queryFn: getMe });
  const meId = meQuery.data?.sub ?? null;
  const realtimeState = useInboxRealtime(Boolean(meQuery.data));

  const backendStatus = statusFilter === "mine" || statusFilter === "all" ? undefined : statusFilter;
  const conversationsQuery = useQuery({
    queryKey: ["inbox", "conversations", { status: backendStatus, platform: platformFilter }],
    queryFn: () =>
      listConversations({
        status: backendStatus,
        platform: platformFilter === "all" ? undefined : platformFilter,
        page: 1,
        pageSize: 50,
      }),
  });

  const conversationItems = useMemo(
    () => (Array.isArray(conversationsQuery.data?.items) ? conversationsQuery.data.items : []),
    [conversationsQuery.data]
  );

  const filteredItems = useMemo(() => {
    const needle = search.trim().toLowerCase();
    return conversationItems.filter((item) => {
      if (statusFilter === "mine" && (!meId || item.assignedTo !== meId)) return false;
      if (!needle) return true;
      return [customerName(item), item.externalThreadId, item.lastMessagePreview ?? "", platformLabel(item.platform)]
        .join(" ")
        .toLowerCase()
        .includes(needle);
    });
  }, [conversationItems, meId, search, statusFilter]);

  const activeConversationId = selectedId ?? filteredItems[0]?.id ?? null;
  const selectedListItem = filteredItems.find((item) => item.id === activeConversationId);

  const detailQuery = useQuery({
    queryKey: ["inbox", "conversation", activeConversationId],
    queryFn: () => getConversation(activeConversationId ?? ""),
    enabled: Boolean(activeConversationId),
  });

  const invalidateActive = async (includeDetail = true) => {
    await queryClient.invalidateQueries({ queryKey: ["inbox", "conversations"] });
    if (includeDetail) {
      await queryClient.invalidateQueries({ queryKey: ["inbox", "conversation", activeConversationId] });
    }
  };

  const sendMutation = useMutation({
    mutationFn: () => sendConversationMessage(activeConversationId ?? "", draft.trim()),
    onSuccess: async (message) => {
      setDraft("");
      showNotice("Tin nhắn đã được gửi.", "success");
      queryClient.setQueryData<ConversationDetail>(["inbox", "conversation", activeConversationId], (old) => {
        if (!old || old.messages.some((item) => item.id === message.id)) return old;
        return { ...old, lastMessageAt: message.sentAt, messages: [...old.messages, message] };
      });
      await invalidateActive(false);
    },
  });

  const assignMutation = useMutation({
    mutationFn: () => assignConversation(activeConversationId ?? "", meId ?? ""),
    onSuccess: () => {
      void invalidateActive();
    },
  });

  const escalateMutation = useMutation({
    mutationFn: () => escalateConversation(activeConversationId ?? ""),
    onSuccess: () => {
      void invalidateActive();
    },
  });

  const resolveMutation = useMutation({
    mutationFn: () => resolveConversation(activeConversationId ?? ""),
    onSuccess: () => {
      void invalidateActive();
    },
  });

  const actionBusy = assignMutation.isPending || escalateMutation.isPending || resolveMutation.isPending;
  const actionError = sendMutation.error ?? assignMutation.error ?? escalateMutation.error ?? resolveMutation.error;

  const selectedConversation = detailQuery.data;
  const openCount = conversationItems.filter((item) => item.status === "open").length;
  const escalatedCount = conversationItems.filter((item) => item.status === "escalated").length;
  const mineCount = meId ? conversationItems.filter((item) => item.assignedTo === meId).length : 0;

  return (
    <AppShell title="Hội thoại đa kênh">
      {notice ? (
        <div className="fixed right-4 top-20 z-[90] w-[min(360px,calc(100vw-32px))]">
          <Alert tone={notice.tone}>{notice.message}</Alert>
        </div>
      ) : null}

      <div className="mb-gutter grid grid-cols-1 gap-gutter lg:grid-cols-4">
        <Card className="lg:col-span-3">
          <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
            <div>
              <h1 className="text-headline-md">Hộp thư tập trung</h1>
              <p className="mt-1 text-body-md text-on-surface-variant">
                Ưu tiên hội thoại nóng, cập nhật realtime và thao tác trực tiếp với khách hàng.
              </p>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <StatusPill tone={realtimeTone(realtimeState)}>{realtimeLabel(realtimeState)}</StatusPill>
              <StatusPill tone={conversationsQuery.isError ? "error" : "success"}>
                {conversationsQuery.isError ? "Mất kết nối dữ liệu" : `${conversationsQuery.data?.total ?? 0} hội thoại`}
              </StatusPill>
            </div>
          </div>
        </Card>
        <Card>
          <div className="grid grid-cols-3 gap-3 text-center">
            <div>
              <p className="text-telemetry-data text-primary">{openCount}</p>
              <p className="text-label-sm text-on-surface-variant">Đang mở</p>
            </div>
            <div>
              <p className="text-telemetry-data text-warning">{escalatedCount}</p>
              <p className="text-label-sm text-on-surface-variant">Cần hỗ trợ</p>
            </div>
            <div>
              <p className="text-telemetry-data text-tertiary">{mineCount}</p>
              <p className="text-label-sm text-on-surface-variant">Của tôi</p>
            </div>
          </div>
        </Card>
      </div>

      {actionError ? (
        <div className="mb-gutter rounded-lg border border-error/30 bg-error/10 p-4 text-body-md text-error">
          {errorMessage(actionError)}
        </div>
      ) : null}

      <div className="grid min-h-[720px] grid-cols-1 gap-gutter xl:grid-cols-[minmax(280px,1fr)_minmax(480px,2fr)_minmax(280px,1fr)]">
        <aside className="flex min-h-[720px] flex-col overflow-hidden rounded-lg border border-outline bg-surface-container-lowest">
          <div className="shrink-0 border-b border-outline p-gutter">
            <h2 className="mb-stack-md text-headline-sm">Danh sách hội thoại</h2>
            <Input
              icon="search"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Tìm tên, SĐT, thread..."
            />
            <div className="mt-stack-md flex flex-wrap gap-2">
              {STATUS_FILTERS.map((item) => (
                <FilterChip
                  key={item.value}
                  active={statusFilter === item.value}
                  icon={item.icon}
                  label={item.label}
                  onClick={() => {
                    setStatusFilter(item.value);
                    setSelectedId(null);
                  }}
                />
              ))}
            </div>
            <div className="mt-stack-sm flex flex-wrap gap-2">
              {PLATFORM_FILTERS.map((item) => (
                <FilterChip
                  key={item.value}
                  active={platformFilter === item.value}
                  label={item.label}
                  onClick={() => {
                    setPlatformFilter(item.value);
                    setSelectedId(null);
                  }}
                />
              ))}
            </div>
          </div>

          <div className="flex-1 overflow-y-auto">
            {conversationsQuery.isLoading ? (
              <p className="p-gutter text-body-md text-on-surface-variant">Đang tải danh sách hội thoại...</p>
            ) : conversationsQuery.isError ? (
              <p className="p-gutter text-body-md text-error">{errorMessage(conversationsQuery.error)}</p>
            ) : filteredItems.length === 0 ? (
              <p className="p-gutter text-body-md text-on-surface-variant">Không có hội thoại khớp bộ lọc.</p>
            ) : (
              filteredItems.map((conversation) => (
                <ConversationRow
                  key={conversation.id}
                  conversation={conversation}
                  selected={conversation.id === activeConversationId}
                  onSelect={() => setSelectedId(conversation.id)}
                />
              ))
            )}
          </div>
        </aside>

        <ChatPanel
          conversation={selectedConversation}
          isLoading={detailQuery.isLoading || (Boolean(activeConversationId) && !selectedListItem && conversationsQuery.isFetching)}
          error={detailQuery.error}
          draft={draft}
          onDraftChange={setDraft}
          sending={sendMutation.isPending}
          onSubmit={() => {
            if (!activeConversationId || !draft.trim() || sendMutation.isPending) return;
            sendMutation.mutate();
          }}
        />

        <ContextPanel
          conversation={selectedConversation}
          meId={meId}
          busy={actionBusy}
          onAssign={() => {
            if (activeConversationId && meId) assignMutation.mutate();
          }}
          onEscalate={() => {
            if (activeConversationId) escalateMutation.mutate();
          }}
          onResolve={() => {
            if (activeConversationId) resolveMutation.mutate();
          }}
          onUseSaleAssistDraft={(value) => setDraft(value)}
          onNotify={showNotice}
        />
      </div>
    </AppShell>
  );
}

