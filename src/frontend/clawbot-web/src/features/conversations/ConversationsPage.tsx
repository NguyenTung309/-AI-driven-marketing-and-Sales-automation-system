
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
  { value: "all", label: "Táº¥t cáº£" },
  { value: "open", label: "AI Ä‘ang chat", icon: "smart_toy" },
  { value: "escalated", label: "Cáº§n há»— trá»£", icon: "warning" },
  { value: "mine", label: "Cá»§a tÃ´i", icon: "person" },
  { value: "resolved", label: "ÄÃ£ xá»­ lÃ½", icon: "task_alt" },
];

const PLATFORM_FILTERS: readonly { value: PlatformFilter; label: string }[] = [
  { value: "all", label: "Má»i kÃªnh" },
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
  if (status === "resolved") return "ÄÃ£ xá»­ lÃ½";
  if (status === "escalated") return "Cáº§n ngÆ°á»i há»— trá»£";
  if (status === "open") return "AI Ä‘ang chat";
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
  if (!value) return "ChÆ°a cÃ³";
  const at = new Date(value).getTime();
  const diff = Date.now() - at;
  if (Number.isNaN(at)) return value;
  const mins = Math.max(0, Math.round(diff / 60000));
  if (mins < 1) return "Vá»«a xong";
  if (mins < 60) return `${mins}p trÆ°á»›c`;
  const hours = Math.round(mins / 60);
  if (hours < 24) return `${hours}h trÆ°á»›c`;
  return new Intl.DateTimeFormat("vi-VN", { day: "2-digit", month: "2-digit" }).format(new Date(value));
}

function formatTime(value: string): string {
  return new Intl.DateTimeFormat("vi-VN", { hour: "2-digit", minute: "2-digit" }).format(new Date(value));
}

function customerName(conversation: ConversationListItem | ConversationDetail): string {
  return conversation.contactDisplayName?.trim() || conversation.externalThreadId || "KhÃ¡ch chÆ°a Ä‘á»‹nh danh";
}

function isOutbound(message: InboxMessage): boolean {
  return message.direction === "out" || message.senderType === "user" || message.senderType === "agent";
}

function errorMessage(error: unknown): string {
  if (error instanceof AxiosError) {
    if (error.response?.status === 404) return "KhÃ´ng tÃ¬m tháº¥y há»™i thoáº¡i trÃªn backend.";
    if (error.response?.status === 401) return "PhiÃªn Ä‘Äƒng nháº­p háº¿t háº¡n hoáº·c thiáº¿u quyá»n truy cáº­p.";
    if (error.response?.status === 400) return "Backend tá»« chá»‘i dá»¯ liá»‡u gá»­i lÃªn.";
  }
  return "KhÃ´ng thá»ƒ káº¿t ná»‘i backend. Kiá»ƒm tra API vÃ  thá»­ láº¡i.";
}

function realtimeLabel(state: ReturnType<typeof useInboxRealtime>): string {
  if (state === "connected") return "Realtime Ä‘ang káº¿t ná»‘i";
  if (state === "reconnecting") return "Realtime Ä‘ang ná»‘i láº¡i";
  if (state === "connecting") return "Äang má»Ÿ realtime";
  if (state === "disabled") return "Realtime chá» token";
  return "Realtime giÃ¡n Ä‘oáº¡n";
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
            {conversation.lastMessagePreview || "ChÆ°a cÃ³ tin nháº¯n má»›i"}
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
          {byAi ? " - AI tráº£ lá»i" : outbound ? " - ÄÃ£ gá»­i" : ""}
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
        <div className="m-auto text-body-md text-on-surface-variant">Äang táº£i há»™i thoáº¡i...</div>
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
          <h2 className="mt-3 text-headline-sm">ChÆ°a cÃ³ há»™i thoáº¡i</h2>
          <p className="mt-2 text-body-md text-on-surface-variant">
            Khi backend tráº£ vá» dá»¯ liá»‡u tá»« `/api/inbox/conversations`, ná»™i dung chat sáº½ hiá»ƒn thá»‹ táº¡i Ä‘Ã¢y.
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
            Há»™i thoáº¡i Ä‘ang cáº§n ngÆ°á»i há»— trá»£ trá»±c tiáº¿p.
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
              Thread {conversation.externalThreadId} Â· {statusLabel(conversation.status)}
            </p>
          </div>
        </div>
        <StatusPill tone={toStatusTone(conversation.status)}>{statusLabel(conversation.status)}</StatusPill>
      </header>

      <div className="flex-1 space-y-4 overflow-y-auto bg-surface p-gutter">
        {conversation.messages.length === 0 ? (
          <div className="rounded-lg border border-dashed border-outline bg-white p-6 text-center text-body-md text-on-surface-variant">
            ChÆ°a cÃ³ message trong há»™i thoáº¡i nÃ y.
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
            ÄÃ­nh kÃ¨m
          </button>
          <button
            type="button"
            className="inline-flex items-center gap-1 rounded border border-amber-300 bg-amber-50/70 px-2 py-1 text-label-sm text-amber-800"
          >
            <span className="material-symbols-outlined text-[14px]">star</span>
            Gáº¯n tháº» khÃ¡ch VIP
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
            placeholder="Nháº­p tin nháº¯n há»— trá»£..."
          />
          <button
            type="button"
            onClick={onSubmit}
            disabled={!draft.trim() || sending}
            className="mb-1 flex size-10 shrink-0 items-center justify-center rounded-lg bg-primary text-on-primary transition-colors hover:bg-primary-hover disabled:opacity-50"
            aria-label="Gá»­i tin nháº¯n"
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
        <h3 className="mb-4 text-label-caps uppercase text-secondary">ThÃ´ng tin khÃ¡ch hÃ ng</h3>
        <div className="text-center">
          <div className="mx-auto flex size-16 items-center justify-center rounded-full border-2 border-white bg-surface-variant text-headline-sm font-bold text-secondary shadow-sm">
            {conversation ? customerName(conversation).slice(0, 1).toUpperCase() : "?"}
          </div>
          <h4 className="mt-3 text-headline-sm">{conversation ? customerName(conversation) : "ChÆ°a chá»n"}</h4>
          <div className="mt-2 inline-flex items-center gap-1 rounded-full border border-amber-200 bg-amber-50 px-3 py-1">
            <span className="material-symbols-outlined text-[16px] text-amber-500">star</span>
            <span className="text-label-sm font-bold text-amber-800">Æ¯u tiÃªn chÄƒm sÃ³c</span>
          </div>
        </div>
        <div className="mt-5 space-y-3 text-body-md">
          <div className="flex items-center gap-3">
            <span className="material-symbols-outlined text-[18px] text-tertiary">hub</span>
            <span>{conversation ? platformLabel(conversation.platform) : "Má»i kÃªnh"}</span>
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
        <h3 className="mb-4 text-label-caps uppercase text-secondary">Äiá»u phá»‘i há»™i thoáº¡i</h3>
        <div className="space-y-2">
          <Button
            type="button"
            className="w-full"
            variant={assignedToMe ? "outline" : "primary"}
            onClick={onAssign}
            disabled={!conversation || !meId || busy || assignedToMe}
          >
            <span className="material-symbols-outlined text-[18px]">person_add</span>
            {assignedToMe ? "ÄÃ£ gÃ¡n cho báº¡n" : "GÃ¡n cho tÃ´i"}
          </Button>
          <Button type="button" className="w-full" variant="outline" onClick={onEscalate} disabled={!conversation || busy}>
            <span className="material-symbols-outlined text-[18px]">warning</span>
            Cáº§n ngÆ°á»i há»— trá»£
          </Button>
          <Button type="button" className="w-full" variant="ghost" onClick={onResolve} disabled={!conversation || busy}>
            <span className="material-symbols-outlined text-[18px]">task_alt</span>
            ÄÃ¡nh dáº¥u Ä‘Ã£ xá»­ lÃ½
          </Button>
        </div>
        {!meId ? (
          <p className="mt-3 text-label-sm text-error">KhÃ´ng Ä‘á»c Ä‘Æ°á»£c `sub` tá»« `/auth/me`, chÆ°a thá»ƒ gÃ¡n há»™i thoáº¡i.</p>
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
      showNotice("Tin nháº¯n Ä‘Ã£ Ä‘Æ°á»£c gá»­i qua backend inbox.", "success");
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
    <AppShell title="Há»™i thoáº¡i Ä‘a kÃªnh">
      {notice ? (
        <div className="fixed right-4 top-20 z-[90] w-[min(360px,calc(100vw-32px))]">
          <Alert tone={notice.tone}>{notice.message}</Alert>
        </div>
      ) : null}

      <div className="mb-gutter grid grid-cols-1 gap-gutter lg:grid-cols-4">
        <Card className="lg:col-span-3">
          <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
            <div>
              <h1 className="text-headline-md">Há»™p thÆ° táº­p trung</h1>
              <p className="mt-1 text-body-md text-on-surface-variant">
                Æ¯u tiÃªn há»™i thoáº¡i nÃ³ng, nháº­n realtime tá»« `/hubs/inbox`, thao tÃ¡c trá»±c tiáº¿p vá»›i `/api/inbox`.
              </p>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <StatusPill tone={realtimeTone(realtimeState)}>{realtimeLabel(realtimeState)}</StatusPill>
              <StatusPill tone={conversationsQuery.isError ? "error" : "success"}>
                {conversationsQuery.isError ? "Máº¥t káº¿t ná»‘i API" : `${conversationsQuery.data?.total ?? 0} há»™i thoáº¡i`}
              </StatusPill>
            </div>
          </div>
        </Card>
        <Card>
          <div className="grid grid-cols-3 gap-3 text-center">
            <div>
              <p className="text-telemetry-data text-primary">{openCount}</p>
              <p className="text-label-sm text-on-surface-variant">Äang má»Ÿ</p>
            </div>
            <div>
              <p className="text-telemetry-data text-warning">{escalatedCount}</p>
              <p className="text-label-sm text-on-surface-variant">Cáº§n há»— trá»£</p>
            </div>
            <div>
              <p className="text-telemetry-data text-tertiary">{mineCount}</p>
              <p className="text-label-sm text-on-surface-variant">Cá»§a tÃ´i</p>
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
            <h2 className="mb-stack-md text-headline-sm">Danh sÃ¡ch há»™i thoáº¡i</h2>
            <Input
              icon="search"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="TÃ¬m tÃªn, SÄT, thread..."
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
              <p className="p-gutter text-body-md text-on-surface-variant">Äang táº£i danh sÃ¡ch há»™i thoáº¡i...</p>
            ) : conversationsQuery.isError ? (
              <p className="p-gutter text-body-md text-error">{errorMessage(conversationsQuery.error)}</p>
            ) : filteredItems.length === 0 ? (
              <p className="p-gutter text-body-md text-on-surface-variant">KhÃ´ng cÃ³ há»™i thoáº¡i khá»›p bá»™ lá»c.</p>
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

