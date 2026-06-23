import { useNavigate } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { AppShell } from "@/shared/layout/AppShell";
import { listChannels, type InboxChannel } from "@/shared/api/inbox";
import ChannelCard from "./ChannelCard";

export default function ChannelListPage() {
  const navigate = useNavigate();

  const { data: channels, isLoading } = useQuery({
    queryKey: ["inbox", "channels"],
    queryFn: listChannels,
    refetchInterval: 30_000,
  });

  function handleSelect(channel: InboxChannel) {
    navigate(`/inbox/${channel.id}`);
  }

  return (
    <AppShell title="Chon kenh">
      <div className="mx-auto max-w-2xl">
        <h2 className="mb-6 text-heading-md font-bold">Kenh giao tiep cua ban</h2>

        {isLoading && (
          <div className="grid gap-3 sm:grid-cols-2">
            {[1, 2, 3].map((i) => (
              <div key={i} className="h-24 animate-pulse rounded-lg bg-surface-container-high" />
            ))}
          </div>
        )}

        {!isLoading && (!channels || channels.length === 0) && (
          <div className="rounded-lg border border-dashed border-outline p-12 text-center">
            <p className="text-body-md text-secondary">Ban chua duoc gan kenh nao.</p>
            <p className="text-label-sm text-tertiary mt-1">Hay lien he admin de duoc phan quyen.</p>
          </div>
        )}

        {channels && channels.length > 0 && (
          <div className="grid gap-3 sm:grid-cols-2">
            {channels.map((ch) => (
              <ChannelCard key={ch.id} channel={ch} onClick={() => handleSelect(ch)} />
            ))}
          </div>
        )}
      </div>
    </AppShell>
  );
}
