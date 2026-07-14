
import { useEffect, useMemo, useRef, useState } from "react";
import { useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AxiosError } from "axios";
import { AppShell } from "@/shared/layout/AppShell";
import { Alert, Button, Card, Input, StatusPill } from "@/shared/ui";
import { platformClasses } from "@/shared/theme/colors";
import { getMe } from "@/shared/api/auth";
import {
  approveConversationDraft,
  escalateConversation,
  getConversation,
  listConversations,
  listChannels,
  rejectConversationDraft,
  resolveConversation,
  sendConversationMessage,
  setConversationAi,
  type ConversationDetail,
  type ConversationListItem,
  type ConversationStatus,
  type InboxMessage,
} from "@/shared/api/inbox";
import { ContactMemoryPanel } from "./ContactMemoryPanel";
import { SaleAssistPanel } from "./SaleAssistPanel";
import { useInboxRealtime } from "./useInboxRealtime";
import { toUserFriendlyError } from "@/shared/utils/userText";

type StatusFilter = "all" | "open" | "escalated" | "resolved" | "mine";
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

// Badge theo cờ AI thật của hội thoại: open + AI bật -> "AI đang chat"; open + AI tắt -> sale đang cầm
function conversationBadge(conversation: { status: ConversationStatus; aiAutoReplyEnabled: boolean }): {
  tone: ReturnType<typeof toStatusTone>;
  label: string;
} {
  if (conversation.status === "open" && !conversation.aiAutoReplyEnabled) {
    return { tone: "neutral", label: "Sale phụ trách" };
  }
  return { tone: toStatusTone(conversation.status), label: statusLabel(conversation.status) };
}

function platformLabel(platform: string): string {
  const value = platform.toLowerCase();
  if (value.includes("facebook") || value === "fb") return "Facebook";
  if (value.includes("zalo") || value === "zl") return "Zalo OA";
  if (value.includes("web")) return "Khung chat web";
  return platform || "Omnichannel";
}

function platformMark(platform: string): string {
  const label = platformLabel(platform);
  if (label === "Facebook") return "FB";
  if (label === "Zalo OA") return "Z";
  if (label === "Khung chat web") return "W";
  return label.slice(0, 2).toUpperCase();
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
  return conversation.contactDisplayName?.trim() || "Khách chưa định danh";
}

function isOutbound(message: InboxMessage): boolean {
  return message.direction === "out" || message.senderType === "user" || message.senderType === "agent";
}

function errorMessage(error: unknown): string {
  if (error instanceof AxiosError) {
    if (error.response?.status === 404) return "Không tìm thấy hội thoại.";
    if (error.response?.status === 401) return "Phiên đăng nhập hết hạn hoặc thiếu quyền truy cập.";
    if (error.response?.status === 400) return "Thông tin gửi lên chưa hợp lệ. Vui lòng kiểm tra lại.";
    if (error.response?.status === 409) return "Dữ liệu đã thay đổi bởi người khác. Vui lòng tải lại hội thoại.";
  }
  return toUserFriendlyError(error, "Không thể kết nối dữ liệu. Vui lòng thử lại.");
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
      title={label}
      className={[
        "inline-flex max-w-[12rem] shrink-0 items-center gap-1.5 rounded-full border px-3 py-1 text-label-sm font-semibold transition-colors",
        active
          ? "border-primary/20 bg-primary/10 text-primary"
          : "border-outline bg-surface-container-lowest text-on-surface-variant hover:bg-surface-container-low",
      ].join(" ")}
    >
      {icon ? <span aria-hidden="true" className="material-symbols-outlined text-[14px] shrink-0">{icon}</span> : null}
      <span className="truncate">{label}</span>
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
        {conversation.contactAvatarUrl ? (
          <img
            src={conversation.contactAvatarUrl}
            alt=""
            className="size-10 rounded-full object-cover shrink-0"
            onError={(e) => { (e.target as HTMLImageElement).style.display = "none"; }}
          />
        ) : (
          <div
            className={`flex size-10 shrink-0 items-center justify-center rounded-lg border text-xs font-bold ${platformClasses(
              conversation.platform
            )}`}
          >
            {platformMark(conversation.platform)}
          </div>
        )}
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
            <StatusPill tone={conversationBadge(conversation).tone}>{conversationBadge(conversation).label}</StatusPill>
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
  readonly contactAvatarUrl: string | null;
  readonly contactDisplayName: string | null;
  readonly onApproveDraft?: (messageId: string) => void;
  readonly onRejectDraft?: (messageId: string) => void;
  readonly draftActionBusy?: boolean;
}

function MessageBubble({ message, contactAvatarUrl, contactDisplayName, onApproveDraft, onRejectDraft, draftActionBusy }: MessageBubbleProps) {
  const outbound = isOutbound(message);
  const byAi = message.senderType === "ai" || message.senderType === "bot";
  const avatarUrl = message.senderAvatarUrl || contactAvatarUrl;
  const displayName = message.senderDisplayName || contactDisplayName;
  return (
    <div className={`flex gap-3 ${outbound ? "justify-end" : "justify-start"}`}>
      {!outbound ? (
        avatarUrl ? (
          <img
            src={avatarUrl}
            alt=""
            className="size-8 rounded-full object-cover shrink-0"
            onError={(e) => { (e.target as HTMLImageElement).style.display = "none"; }}
          />
        ) : (
          <div className="flex size-8 shrink-0 items-center justify-center rounded-full bg-surface-variant text-label-sm font-bold text-secondary">
            {(displayName?.charAt(0) || "K").toUpperCase()}
          </div>
        )
      ) : null}
      <div
        className={[
          "max-w-[78%] rounded-2xl border p-3 shadow-[0_2px_4px_rgba(15,23,42,0.04)]",
          outbound
            ? byAi
              ? "rounded-tr-sm border-tertiary/20 bg-tertiary/5"
              : "rounded-tr-sm border-primary/20 bg-primary/10"
            : "rounded-tl-sm border-surface-variant bg-white dark:bg-[#242526]",
        ].join(" ")}
      >
        <div className={`text-label-sm font-semibold mb-1 ${outbound ? "text-primary dark:text-primary-light" : "text-on-surface-variant"}`}>
          {outbound
            ? (message.senderDisplayName ?? (byAi ? "AI Agent" : "Hệ thống"))
            : (message.senderDisplayName ?? contactDisplayName ?? "Khách hàng")}
        </div>
        {message.contentType === "photo" && (message.attachmentUrl || message.content) ? (
          <img src={message.attachmentUrl || message.content} alt="Anh dinh kem" className="max-h-48 rounded-lg object-cover" onError={(e) => { (e.target as HTMLImageElement).style.display = "none"; }} />
        ) : message.contentType === "sticker" && message.content ? (
          <img src={message.content} alt="Sticker" className="max-h-24 object-contain" onError={(e) => { (e.target as HTMLImageElement).style.display = "none"; }} />
        ) : message.contentType === "document" ? (
          <div className="flex items-center gap-2 rounded-lg border border-outline bg-surface p-2">
            <span aria-hidden="true" className="material-symbols-outlined text-[20px] text-secondary">description</span>
            <span className="text-body-md text-on-surface">{message.content}</span>
            {message.attachmentUrl && (
              <a href={message.attachmentUrl} target="_blank" rel="noopener noreferrer" className="text-label-sm text-primary underline ml-1">Tai ve</a>
            )}
          </div>
        ) : message.contentType === "video" && message.attachmentUrl ? (
          <video controls src={message.attachmentUrl} className="max-h-48 rounded-lg" />
        ) : message.contentType === "audio" ? (
          <div className="flex items-center gap-2">
            <span aria-hidden="true" className="material-symbols-outlined text-[20px] text-secondary">headphones</span>
            {message.attachmentUrl ? (
              <audio controls src={message.attachmentUrl} className="max-w-[200px]" />
            ) : (
              <span className="text-body-md text-on-surface">Am thanh</span>
            )}
          </div>
        ) : message.contentType === "call_missed" ? (
          <div className="flex items-center gap-2 text-body-md text-on-surface-variant">
            <span aria-hidden="true" className="material-symbols-outlined text-[18px] text-error">call_missed</span>
            {message.content}
          </div>
        ) : (
          <p className="whitespace-pre-wrap break-words text-body-md text-on-surface">{message.content}</p>
        )}
        <span className={`mt-1 block text-label-sm ${message.status === "pending_approval" ? "font-semibold text-warning" : message.status === "blocked" ? "font-semibold text-error" : "text-on-surface-variant"} ${outbound ? "text-right" : ""}`}>
          {formatTime(message.sentAt)}
          {message.status === "pending_approval"
            ? " - Chờ duyệt (chưa gửi)"
            : message.status === "blocked"
              ? " - Đã chặn (không gửi)"
              : byAi ? " - AI trả lời" : outbound ? " - Đã gửi" : ""}
        </span>
        {message.status === "pending_approval" && onApproveDraft && onRejectDraft ? (
          <div className="mt-2 flex justify-end gap-2">
            <button
              type="button"
              disabled={draftActionBusy}
              onClick={() => onRejectDraft(message.id)}
              className="rounded border border-error/40 px-2.5 py-1 text-label-sm font-semibold text-error hover:bg-error/10 disabled:opacity-50"
            >
              Bỏ tin này
            </button>
            <button
              type="button"
              disabled={draftActionBusy}
              onClick={() => onApproveDraft(message.id)}
              className="rounded bg-primary px-2.5 py-1 text-label-sm font-semibold text-on-primary hover:bg-primary-hover disabled:opacity-50"
            >
              Duyệt & gửi
            </button>
          </div>
        ) : null}
      </div>
      {outbound ? (
        <div
          className={[
            "flex size-8 shrink-0 items-center justify-center rounded-full border text-xs font-bold",
            byAi ? "border-tertiary/20 bg-tertiary/10 text-tertiary" : "border-primary/20 bg-primary text-on-primary",
          ].join(" ")}
        >
          <span aria-hidden="true" className="material-symbols-outlined text-[16px]">{byAi ? "smart_toy" : "support_agent"}</span>
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
  readonly onToggleAi: (enabled: boolean) => void;
  readonly aiToggling: boolean;
  readonly onApproveDraft: (messageId: string) => void;
  readonly onRejectDraft: (messageId: string) => void;
  readonly draftActionBusy: boolean;
}

function ChatPanel({ conversation, isLoading, error, draft, onDraftChange, onSubmit, sending, onToggleAi, aiToggling, onApproveDraft, onRejectDraft, draftActionBusy }: ChatPanelProps) {
  const messagesRef = useRef<HTMLDivElement | null>(null);
  const conversationId = conversation?.id ?? null;
  const messageCount = conversation?.messages.length ?? 0;

  // Giong Zalo: mo hoi thoai la neo o tin moi nhat (cuoi danh sach)
  useEffect(() => {
    const el = messagesRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [conversationId]);

  // Tin moi den: chi keo xuong khi dang o gan day, khong giat khi dang doc lich su
  useEffect(() => {
    const el = messagesRef.current;
    if (!el) return;
    const nearBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 240;
    if (nearBottom) el.scrollTop = el.scrollHeight;
  }, [messageCount]);

  if (isLoading) {
    return (
      <section className="flex h-full min-h-[480px] min-w-0 flex-col rounded-lg border border-outline bg-surface-container-lowest xl:min-h-0">
        <div className="m-auto text-body-md text-on-surface-variant">Đang tải hội thoại...</div>
      </section>
    );
  }

  if (error) {
    return (
      <section className="flex h-full min-h-[480px] min-w-0 flex-col rounded-lg border border-outline bg-surface-container-lowest xl:min-h-0">
        <div className="m-auto max-w-md text-center">
          <span aria-hidden="true" className="material-symbols-outlined text-[40px] text-error">error</span>
          <p className="mt-3 text-body-md text-on-surface">{errorMessage(error)}</p>
        </div>
      </section>
    );
  }

  if (!conversation) {
    return (
      <section className="flex h-full min-h-[480px] min-w-0 flex-col rounded-lg border border-outline bg-surface-container-lowest xl:min-h-0">
        <div className="m-auto max-w-md text-center">
          <span aria-hidden="true" className="material-symbols-outlined text-[44px] text-on-surface-variant">forum</span>
          <h2 className="mt-3 text-headline-sm">Chưa có hội thoại</h2>
          <p className="mt-2 text-body-md text-on-surface-variant">
            Khi có dữ liệu hội thoại mới, nội dung chat sẽ hiển thị tại đây.
          </p>
        </div>
      </section>
    );
  }

  return (
    <section className="flex h-full min-h-[480px] min-w-0 flex-col overflow-hidden rounded-lg border border-outline bg-surface-container-lowest xl:min-h-0">
      {conversation.status === "escalated" ? (
        <div className="flex items-center justify-between bg-warning px-gutter py-2 text-label-lg font-semibold text-white">
          <span className="flex items-center gap-2">
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">warning</span>
            Hội thoại đang cần người hỗ trợ trực tiếp.
          </span>
        </div>
      ) : null}

      <header className="flex shrink-0 items-center justify-between gap-3 border-b border-outline bg-white p-4">
        <div className="flex min-w-0 items-center gap-3">
          <div
            className={`flex size-10 shrink-0 items-center justify-center rounded-full border text-sm font-bold ${platformClasses(
              conversation.platform
            )}`}
          >
            {platformMark(conversation.platform)}
          </div>
          <div className="min-w-0">
            <h2 className="flex items-center gap-2 text-headline-sm">
              <span className="truncate">{customerName(conversation)}</span>
              <span className="shrink-0 rounded bg-surface-container px-2 py-0.5 text-label-sm font-semibold text-secondary">
                {platformLabel(conversation.platform)}
              </span>
            </h2>
            <p className="truncate text-label-sm text-on-surface-variant" title={conversation.externalThreadId || undefined}>
              Mã hội thoại: {conversation.externalThreadId || "chưa có"} · {conversationBadge(conversation).label}
            </p>
          </div>
        </div>
        <div className="flex shrink-0 items-center gap-3">
          {/* Cong tac "AI dang chat": bat/tat auto-reply cho rieng hoi thoai nay */}
          <label className="flex cursor-pointer select-none items-center gap-2 text-label-sm font-semibold text-secondary">
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">smart_toy</span>
            <span className="hidden sm:inline">AI đang chat</span>
            <button
              type="button"
              role="switch"
              aria-checked={conversation.aiAutoReplyEnabled}
              disabled={aiToggling}
              onClick={() => onToggleAi(!conversation.aiAutoReplyEnabled)}
              className={[
                "relative h-6 w-11 rounded-full transition-colors",
                conversation.aiAutoReplyEnabled ? "bg-primary" : "bg-surface-variant",
                aiToggling ? "opacity-60" : "",
              ].join(" ")}
            >
              <span
                className={[
                  "absolute top-0.5 size-5 rounded-full bg-white shadow transition-all",
                  conversation.aiAutoReplyEnabled ? "left-[22px]" : "left-0.5",
                ].join(" ")}
              />
            </button>
          </label>
          <StatusPill tone={conversationBadge(conversation).tone}>{conversationBadge(conversation).label}</StatusPill>
        </div>
      </header>

      <div ref={messagesRef} className="flex-1 space-y-3 overflow-y-auto bg-surface p-4">
        {conversation.messages.length === 0 ? (
          <div className="rounded-lg border border-dashed border-outline bg-white p-6 text-center text-body-md text-on-surface-variant">
            Chưa có tin nhắn trong hội thoại này.
          </div>
        ) : (
          conversation.messages.map((message) => (
            <MessageBubble
              key={message.id}
              message={message}
              contactAvatarUrl={conversation.contactAvatarUrl}
              contactDisplayName={conversation.contactDisplayName}
              onApproveDraft={onApproveDraft}
              onRejectDraft={onRejectDraft}
              draftActionBusy={draftActionBusy}
            />
          ))
        )}
      </div>

      <footer className="shrink-0 border-t border-outline bg-white p-3">
        <div className="mb-2 flex flex-wrap gap-2">
          <button
            type="button"
            className="inline-flex items-center gap-1 rounded border border-outline px-2 py-1 text-label-sm text-secondary hover:bg-surface"
          >
            <span aria-hidden="true" className="material-symbols-outlined text-[14px]">attach_file</span>
            Đính kèm
          </button>
          <button
            type="button"
            className="inline-flex items-center gap-1 rounded border border-amber-300 bg-amber-50/70 px-2 py-1 text-label-sm text-amber-800"
          >
            <span aria-hidden="true" className="material-symbols-outlined text-[14px]">star</span>
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
            rows={1}
            className="max-h-32 min-h-[40px] flex-1 resize-none bg-transparent py-2 text-body-md text-on-surface outline-none"
            placeholder="Nhập tin nhắn hỗ trợ..."
          />
          <button
            type="button"
            onClick={onSubmit}
            disabled={!draft.trim() || sending}
            className="mb-1 flex size-10 shrink-0 items-center justify-center rounded-lg bg-primary text-on-primary transition-colors hover:bg-primary-hover disabled:opacity-50"
            aria-label="Gửi tin nhắn"
          >
            <span aria-hidden="true" className="material-symbols-outlined">send</span>
          </button>
        </div>
      </footer>
    </section>
  );
}

interface ContextPanelProps {
  readonly conversation: ConversationDetail | undefined;
  readonly onEscalate: () => void;
  readonly onResolve: () => void;
  readonly onUseSaleAssistDraft: (value: string) => void;
  readonly onNotify: (message: string, tone?: NoticeTone) => void;
  readonly busy: boolean;
}

function ContextPanel({
  conversation,
  onEscalate,
  onResolve,
  onUseSaleAssistDraft,
  onNotify,
  busy,
}: ContextPanelProps) {
  const [failedAvatarUrl, setFailedAvatarUrl] = useState<string | null>(null);
  const avatarUrl = conversation?.contactAvatarUrl ?? null;
  const showAvatar = Boolean(avatarUrl && failedAvatarUrl !== avatarUrl);
  return (
    <aside className="flex min-h-0 min-w-0 flex-col gap-3 overflow-y-auto overflow-x-hidden xl:h-full">
      <Card>
        <h3 className="mb-4 text-label-caps uppercase text-secondary">Thông tin khách hàng</h3>
        <div className="text-center">
          <div className="mx-auto flex size-16 items-center justify-center rounded-full border-2 border-white bg-surface-variant text-headline-sm font-bold text-secondary shadow-sm overflow-hidden">
            {showAvatar ? (
              <img
                src={avatarUrl!}
                alt=""
                className="size-full object-cover"
                onError={() => setFailedAvatarUrl(avatarUrl)}
              />
            ) : conversation ? (
              customerName(conversation).slice(0, 1).toUpperCase()
            ) : (
              "?"
            )}
          </div>
          <h4 className="mt-3 text-headline-sm">{conversation ? customerName(conversation) : "Chưa chọn"}</h4>
          <div className="mt-2 inline-flex items-center gap-1 rounded-full border border-amber-200 bg-amber-50 px-3 py-1">
            <span aria-hidden="true" className="material-symbols-outlined text-[16px] text-amber-500">star</span>
            <span className="text-label-sm font-bold text-amber-800">Ưu tiên chăm sóc</span>
          </div>
        </div>
        <div className="mt-5 space-y-3 text-body-md">
          <div className="flex items-center gap-3">
            <span aria-hidden="true" className="material-symbols-outlined text-[18px] text-tertiary">hub</span>
            <span>{conversation ? platformLabel(conversation.platform) : "Mọi kênh"}</span>
          </div>
          <div className="flex items-start gap-3">
            <span aria-hidden="true" className="material-symbols-outlined text-[18px] text-tertiary">tag</span>
            <span className="min-w-0 break-all font-mono text-mono-status">{conversation?.externalThreadId ?? "Chưa có mã"}</span>
          </div>
          <div className="flex items-center gap-3">
            <span aria-hidden="true" className="material-symbols-outlined text-[18px] text-tertiary">schedule</span>
            <span>{formatRelative(conversation?.lastMessageAt ?? null)}</span>
          </div>
        </div>
      </Card>

      <Card>
        <h3 className="mb-4 text-label-caps uppercase text-secondary">Điều phối hội thoại</h3>
        <div className="space-y-2">
          <Button type="button" className="w-full" variant="outline" onClick={onEscalate} disabled={!conversation || busy}>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">warning</span>
            Cần người hỗ trợ
          </Button>
          <Button type="button" className="w-full" variant="ghost" onClick={onResolve} disabled={!conversation || busy}>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">task_alt</span>
            Đánh dấu đã xử lý
          </Button>
        </div>
      </Card>

      <ContactMemoryPanel
        contactId={conversation?.contactId ?? null}
        onNotify={(message) => onNotify(message)}
      />

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
  // Deep-link từ notification: /conversations/{id} preselect hội thoại đó.
  const { conversationId: routeConversationId } = useParams<{ conversationId: string }>();
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");
  const [inboxIdFilter, setInboxIdFilter] = useState<string>("all");
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(routeConversationId ?? null);

  useEffect(() => {
    if (routeConversationId) setSelectedId(routeConversationId);
  }, [routeConversationId]);
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
  // Vẫn mở kết nối realtime để tin nhắn/hội thoại tự cập nhật; chỉ bỏ pill hiển thị trạng thái kết nối.
  useInboxRealtime(Boolean(meQuery.data));

  const channelsQuery = useQuery({
    queryKey: ["inbox", "channels"],
    queryFn: listChannels,
  });

  const backendStatus = statusFilter === "mine" || statusFilter === "all" ? undefined : statusFilter;
  const conversationsQuery = useQuery({
    queryKey: ["inbox", "conversations", { status: backendStatus, inboxId: inboxIdFilter }],
    queryFn: () =>
      listConversations({
        status: backendStatus,
        inboxId: inboxIdFilter === "all" ? undefined : inboxIdFilter,
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
        // BE tu tat AI khi sale gui tay (handover) - dong bo cache ngay
        return { ...old, lastMessageAt: message.sentAt, messages: [...old.messages, message], aiAutoReplyEnabled: false };
      });
      await invalidateActive(false);
    },
  });

  const escalateMutation = useMutation({
    mutationFn: () => escalateConversation(activeConversationId ?? "", selectedConversation?.rowVersion),
    onSuccess: () => {
      void invalidateActive();
    },
  });

  const resolveMutation = useMutation({
    mutationFn: () => resolveConversation(activeConversationId ?? "", selectedConversation?.rowVersion),
    onSuccess: () => {
      void invalidateActive();
    },
  });

  const aiToggleMutation = useMutation({
    mutationFn: (enabled: boolean) => setConversationAi(activeConversationId ?? "", enabled),
    onSuccess: (_, enabled) => {
      showNotice(enabled ? "Đã bật AI trả lời tự động cho hội thoại này." : "Đã tắt AI — sale phụ trách hội thoại.", "success");
      void invalidateActive();
    },
  });

  // Review-gate P3: duyệt/từ chối AI draft đang hold (status pending_approval)
  const approveDraftMutation = useMutation({
    mutationFn: (messageId: string) => approveConversationDraft(activeConversationId ?? "", messageId),
    onSuccess: () => {
      showNotice("Đã duyệt — tin nhắn được gửi tới khách.", "success");
      void invalidateActive();
    },
    onError: (error) => showNotice(errorMessage(error), "error"),
  });
  const rejectDraftMutation = useMutation({
    mutationFn: (messageId: string) => rejectConversationDraft(activeConversationId ?? "", messageId),
    onSuccess: () => {
      showNotice("Đã bỏ bản nháp AI — tin không được gửi.", "info");
      void invalidateActive();
    },
    onError: (error) => showNotice(errorMessage(error), "error"),
  });

  const actionBusy = escalateMutation.isPending || resolveMutation.isPending;
  const actionError = sendMutation.error ?? escalateMutation.error ?? resolveMutation.error;

  const selectedConversation = detailQuery.data;
  const openCount = conversationItems.filter((item) => item.status === "open").length;
  const escalatedCount = conversationItems.filter((item) => item.status === "escalated").length;
  const mineCount = meId ? conversationItems.filter((item) => item.assignedTo === meId).length : 0;

  return (
    <AppShell title="Hội thoại đa kênh" noPadding>
      {/* Flex-fill khít viewport (không calc cứng): header shrink-0, khối 3 cột chiếm phần còn lại.
          Dưới xl: cuộn cả trang như cũ; từ xl: khóa trong màn hình, từng cột tự cuộn. */}
      <div className="flex h-full min-h-0 flex-col overflow-y-auto p-3 sm:p-4 xl:overflow-hidden">
      {notice ? (
        <div className="fixed right-4 top-20 z-[90] w-[min(360px,calc(100vw-32px))]">
          <Alert tone={notice.tone}>{notice.message}</Alert>
        </div>
      ) : null}

      {/* Header gọn 1 hàng: tiêu đề trái, cụm chỉ số + tổng số phải — nhường tối đa chiều cao cho 3 cột. */}
      <Card className="mb-3 shrink-0 !p-4">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
          <div className="min-w-0">
            <h1 className="text-headline-sm">Hộp thư tập trung</h1>
            <p className="mt-0.5 hidden truncate text-label-sm text-on-surface-variant sm:block">
              Ưu tiên hội thoại nóng, cập nhật tức thì và thao tác trực tiếp với khách hàng.
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-x-5 gap-y-2">
            <div className="flex items-center gap-5 text-center">
              <div>
                <p className="text-headline-md font-bold leading-none text-primary">{openCount}</p>
                <p className="mt-1 text-label-sm text-on-surface-variant">Đang mở</p>
              </div>
              <div>
                <p className="text-headline-md font-bold leading-none text-warning">{escalatedCount}</p>
                <p className="mt-1 text-label-sm text-on-surface-variant">Cần hỗ trợ</p>
              </div>
              <div>
                <p className="text-headline-md font-bold leading-none text-tertiary">{mineCount}</p>
                <p className="mt-1 text-label-sm text-on-surface-variant">Của tôi</p>
              </div>
            </div>
            <StatusPill tone={conversationsQuery.isError ? "error" : "success"}>
              {conversationsQuery.isError ? "Mất kết nối dữ liệu" : `${conversationsQuery.data?.total ?? 0} hội thoại`}
            </StatusPill>
          </div>
        </div>
      </Card>

      {actionError ? (
        <div className="mb-gutter shrink-0 rounded-lg border border-error/30 bg-error/10 p-4 text-body-md text-error">
          {errorMessage(actionError)}
        </div>
      ) : null}

      {/* Cột co giãn theo viewport: minmax(0,fr) + min-w-0 để không bao giờ tràn ngang;
          minimum nhỏ vừa đủ (220/300/240) để 3 cột vẫn lọt trên laptop 1280px sau khi trừ sidebar. */}
      <div className="grid grid-cols-1 gap-3 xl:min-h-0 xl:flex-1 xl:grid-cols-[minmax(220px,0.9fr)_minmax(300px,1.7fr)_minmax(240px,1fr)]">
        <aside className="flex min-h-[480px] min-w-0 flex-col overflow-hidden rounded-lg border border-outline bg-surface-container-lowest xl:h-full xl:min-h-0">
          <div className="shrink-0 border-b border-outline p-4">
            <h2 className="mb-3 text-headline-sm">Danh sách hội thoại</h2>
            <Input
              icon="search"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Tìm tên, SĐT, mã hội thoại..."
            />
            {/* Bộ lọc trạng thái cuộn ngang 1 hàng thay vì xuống nhiều dòng, đỡ ăn chiều cao danh sách. */}
            <div className="mt-3 flex gap-2 overflow-x-auto pb-1 [scrollbar-width:thin]">
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
            <div className="mt-2 flex flex-wrap gap-2">
              <FilterChip
                active={inboxIdFilter === "all"}
                label="Tất cả kênh"
                onClick={() => {
                  setInboxIdFilter("all");
                  setSelectedId(null);
                }}
              />
              {channelsQuery.data?.map((channel) => (
                <FilterChip
                  key={channel.id}
                  active={inboxIdFilter === channel.id}
                  label={channel.name}
                  onClick={() => {
                    setInboxIdFilter(channel.id);
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
          aiToggling={aiToggleMutation.isPending}
          onToggleAi={(enabled) => {
            if (!activeConversationId || aiToggleMutation.isPending) return;
            aiToggleMutation.mutate(enabled);
          }}
          draftActionBusy={approveDraftMutation.isPending || rejectDraftMutation.isPending}
          onApproveDraft={(messageId) => {
            if (!activeConversationId || approveDraftMutation.isPending) return;
            approveDraftMutation.mutate(messageId);
          }}
          onRejectDraft={(messageId) => {
            if (!activeConversationId || rejectDraftMutation.isPending) return;
            rejectDraftMutation.mutate(messageId);
          }}
        />

        <ContextPanel
          conversation={selectedConversation}
          busy={actionBusy}
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
      </div>
    </AppShell>
  );
}

