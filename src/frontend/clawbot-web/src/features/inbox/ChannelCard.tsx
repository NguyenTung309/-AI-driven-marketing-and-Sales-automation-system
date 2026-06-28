import type { ReactNode } from "react";
import { Card } from "@/shared/ui";
import type { InboxChannel } from "@/shared/api/inbox";

interface ChannelCardProps {
  readonly channel: InboxChannel;
  readonly onClick?: () => void;
}

function PlatformIcon({ platform }: { platform: string }) {
  const icon: Record<string, ReactNode> = {
    facebook: (
      <svg viewBox="0 0 24 24" className="size-5 fill-current" aria-hidden="true">
        <path d="M24 12.073c0-6.627-5.373-12-12-12s-12 5.373-12 12c0 5.99 4.388 10.954 10.125 11.854v-8.385H7.078v-3.47h3.047V9.43c0-3.007 1.792-4.669 4.533-4.669 1.312 0 2.686.235 2.686.235v2.953H15.83c-1.491 0-1.956.925-1.956 1.874v2.25h3.328l-.532 3.47h-2.796v8.385C19.612 23.027 24 18.062 24 12.073z" />
      </svg>
    ),
    zalo: (
      <svg viewBox="0 0 24 24" className="size-5 fill-current" aria-hidden="true">
        <rect x="2" y="2" width="20" height="20" rx="4" />
        <text x="12" y="16" textAnchor="middle" fontSize="11" fontWeight="bold" fill="white">Z</text>
      </svg>
    ),
    web: (
      <svg viewBox="0 0 24 24" className="size-5 fill-current" aria-hidden="true">
        <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z" />
      </svg>
    ),
  };
  return <span className="size-5 shrink-0">{icon[platform] ?? icon.web}</span>;
}

export default function ChannelCard({ channel, onClick }: ChannelCardProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="w-full text-left"
    >
      <Card className="flex items-center gap-3 p-4 hover:border-primary transition-colors cursor-pointer">
        <div className="flex size-10 items-center justify-center rounded-full bg-surface-container-high text-on-surface-variant shrink-0">
          <PlatformIcon platform={channel.platform} />
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className="text-body-md font-semibold truncate">{channel.name}</span>
            {!channel.hasToken && (
              <span className="rounded bg-warning-container px-1.5 py-0.5 text-label-xs text-on-warning-container">
                No token
              </span>
            )}
          </div>
          <span className="text-label-sm text-secondary block truncate">
            {channel.memberDisplayName ?? "Chua gan sale"}
          </span>
          
        </div>
        {channel.unreadCount > 0 && (
          <span className="flex size-6 items-center justify-center rounded-full bg-error text-label-xs font-bold text-on-error">
            {channel.unreadCount > 99 ? "99+" : channel.unreadCount}
          </span>
        )}
      </Card>
    </button>
  );
}
