import type { InboxMessage } from "@/shared/api/inbox";

interface Props {
  readonly messages: readonly InboxMessage[];
  readonly loading: boolean;
}

export default function ChatMessageThread({ messages, loading }: Props) {
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
        const isUser = msg.direction === "out";
        return (
          <div key={msg.id} className={`flex ${isUser ? "justify-end" : "justify-start"}`}>
            <div
              className={`max-w-[75%] rounded-xl px-4 py-2 text-body-md ${
                isUser
                  ? "bg-primary text-on-primary rounded-br-md"
                  : "bg-surface-container-high text-secondary rounded-bl-md"
              }`}
            >
              <p className="whitespace-pre-wrap break-words">{msg.content}</p>
              <p className={`mt-1 text-label-xs ${isUser ? "text-on-primary/70" : "text-on-surface-variant"}`}>
                {new Date(msg.sentAt).toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit" })}
              </p>
            </div>
          </div>
        );
      })}
    </div>
  );
}