import type { InboxMessage } from "@/shared/api/inbox";

interface Props {
  readonly messages: readonly InboxMessage[];
  readonly loading: boolean;
  readonly contactAvatarUrl?: string | null;
  readonly contactDisplayName?: string | null;
}

function formatTime(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit" });
}

function deliveryLabel(status: InboxMessage["status"]): string {
  if (status === "pending_approval") return " - Chờ duyệt";
  if (status === "pending_send") return " - Đang gửi";
  if (status === "send_failed") return " - Gửi thất bại";
  if (status === "blocked") return " - Đã chặn";
  return "";
}

function MessageAvatar({ url, name }: { url?: string | null; name?: string | null }) {
  if (url) {
    return (
      <img
        src={url}
        alt=""
        className="size-8 rounded-full object-cover shrink-0"
        onError={(e) => {
          (e.target as HTMLImageElement).style.display = "none";
          ((e.target as HTMLImageElement).nextElementSibling as HTMLElement)?.classList.remove("hidden");
        }}
      />
    );
  }
  return (
    <div className="size-8 rounded-full bg-surface-variant flex items-center justify-center text-label-sm font-bold text-on-surface-variant shrink-0">
      {(name?.charAt(0) ?? "?").toUpperCase()}
    </div>
  );
}

export default function ChatMessageThread({ messages, loading, contactAvatarUrl, contactDisplayName }: Props) {
  if (loading) {
    return (
      <div className="flex items-center justify-center h-full text-body-md text-on-surface-variant">
        Dang tai tin nhan...
      </div>
    );
  }
  if (messages.length === 0) {
    return (
      <div className="flex items-center justify-center h-full text-body-md text-on-surface-variant">
        Chua co tin nhan
      </div>
    );
  }
  return (
    <div className="flex flex-col gap-3 p-4 overflow-y-auto h-full">
      {messages.map((msg) => {
        const isOwner = msg.direction === "out";
        return (
          <div key={msg.id} className={`flex gap-2 ${isOwner ? "justify-end" : "justify-start"}`}>
            {!isOwner && (
              <MessageAvatar url={msg.senderAvatarUrl || contactAvatarUrl} name={msg.senderDisplayName ?? contactDisplayName} />
            )}
            <div className="max-w-[70%] flex flex-col">
              {!isOwner && (msg.senderDisplayName ?? contactDisplayName) && (
                <span className="text-label-xs text-on-surface-variant mb-0.5 ml-1">
                  {msg.senderDisplayName ?? contactDisplayName}
                </span>
              )}
              <div
                className={`rounded-2xl px-4 py-2 text-body-md ${isOwner
                    ? msg.status === "send_failed" || msg.status === "blocked"
                      ? "border border-error/40 bg-error/10 text-error"
                      : msg.status === "pending_send" || msg.status === "pending_approval"
                        ? "border border-warning/40 bg-warning/10 text-on-surface"
                        : "bg-primary text-on-primary"
                    : "bg-surface-variant text-on-surface-variant"
                  }`}
              >
                {msg.contentType === "photo" && (msg.attachmentUrl || msg.content) ? (
                  <img src={msg.attachmentUrl || msg.content} alt="Anh dinh kem" className="max-h-48 rounded-lg object-cover" onError={(e) => { (e.target as HTMLImageElement).style.display = "none"; }} />
                ) : msg.contentType === "sticker" && msg.content ? (
                  <img src={msg.content} alt="Sticker" className="max-h-24 object-contain" onError={(e) => { (e.target as HTMLImageElement).style.display = "none"; }} />
                ) : msg.contentType === "document" ? (
                  <div className="flex items-center gap-2 rounded-lg border border-outline bg-white/10 p-2">
                    <span aria-hidden="true" className="material-symbols-outlined text-[20px]">description</span>
                    <span className="text-body-md">{msg.content}</span>
                    {msg.attachmentUrl && (
                      <a href={msg.attachmentUrl} target="_blank" rel="noopener noreferrer" className="text-label-sm underline">Tai ve</a>
                    )}
                  </div>
                ) : msg.contentType === "video" && msg.attachmentUrl ? (
                  <video controls src={msg.attachmentUrl} className="max-h-48 rounded-lg" />
                ) : msg.contentType === "audio" ? (
                  <div className="flex items-center gap-2">
                    <span aria-hidden="true" className="material-symbols-outlined text-[20px]">headphones</span>
                    {msg.attachmentUrl ? (
                      <audio controls src={msg.attachmentUrl} className="max-w-[200px]" />
                    ) : (
                      <span className="text-body-md">Am thanh</span>
                    )}
                  </div>
                ) : msg.contentType === "call_missed" ? (
                  <div className="flex items-center gap-2 text-body-md">
                    <span aria-hidden="true" className="material-symbols-outlined text-[18px]">call_missed</span>
                    {msg.content}
                  </div>
                ) : (
                  <p className="whitespace-pre-wrap break-words">{msg.content}</p>
                )}
                <p
                  className={`mt-1 text-label-xs ${isOwner && msg.status !== "send_failed" && msg.status !== "blocked"
                    ? "text-on-primary/70"
                    : "text-on-surface-variant/70"
                    }`}
                >
                  {formatTime(msg.sentAt)}{deliveryLabel(msg.status)}
                </p>
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}