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
              <MessageAvatar url={contactAvatarUrl} name={msg.senderDisplayName ?? contactDisplayName} />
            )}
            <div className="max-w-[70%] flex flex-col">
              {!isOwner && (msg.senderDisplayName ?? contactDisplayName) && (
                <span className="text-label-xs text-on-surface-variant mb-0.5 ml-1">
                  {msg.senderDisplayName ?? contactDisplayName}
                </span>
              )}
              <div className={`rounded-2xl px-4 py-2 text-body-md }>
                <p className="whitespace-pre-wrap break-words">{msg.content}</p>
                <p className={mt-1 text-label-xs }>
                  {formatTime(msg.sentAt)}
                </p>
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}
