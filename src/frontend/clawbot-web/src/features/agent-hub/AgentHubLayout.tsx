import { useCallback, useState } from "react";
import { useParams, useNavigate } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AppShell } from "@/shared/layout/AppShell";
import { getMe } from "@/shared/api/auth";
import {
  listConversations,
  listChannels,
  getConversation,
  sendConversationMessage,
  assignConversation,
  resolveConversation,
} from "@/shared/api/inbox";
import ChatMessageThread from "./ChatMessageThread";
import ComposerWithAI from "./ComposerWithAI";
import QuickActionBar from "./QuickActionBar";

export default function AgentHubLayout() {
  const { channelId } = useParams<{ channelId?: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [activeId, setActiveId] = useState<string | null>(null);

  const meQuery = useQuery({ queryKey: ["me"], queryFn: getMe });
  const meId = meQuery.data?.sub;
  const isAdmin = meQuery.data?.permissions?.includes("admin:inboxes") ?? false;

  // Load channel list if we have channelId (for header name)
  const channelsQuery = useQuery({
    queryKey: ["inbox", "channels"],
    queryFn: listChannels,
    enabled: !!channelId,
  });
  const currentChannel = channelsQuery.data?.find((c) => c.id === channelId);

  // Scope conversations by channelId if present
  const listQuery = useQuery({
    queryKey: ["inbox", "conversations", { inboxId: channelId }],
    queryFn: () =>
      listConversations({
        pageSize: 50,
        inboxId: channelId,
      }),
    refetchInterval: 15_000,
  });

  const detailQuery = useQuery({
    queryKey: ["inbox", "conversation", activeId],
    queryFn: () => getConversation(activeId!),
    enabled: activeId !== null,
    refetchInterval: 10_000,
  });

  const sendMutation = useMutation({
    mutationFn: (content: string) => sendConversationMessage(activeId!, content),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["inbox", "conversation", activeId] });
      void queryClient.invalidateQueries({ queryKey: ["inbox", "conversations"] });
    },
  });

  const assignMutation = useMutation({
    mutationFn: () => assignConversation(activeId!, meId!, detailQuery.data?.rowVersion),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["inbox", "conversation", activeId] });
      void queryClient.invalidateQueries({ queryKey: ["inbox", "conversations"] });
    },
  });

  const resolveMutation = useMutation({
    mutationFn: () => resolveConversation(activeId!, detailQuery.data?.rowVersion),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["inbox", "conversation", activeId] });
      void queryClient.invalidateQueries({ queryKey: ["inbox", "conversations"] });
    },
  });

  const conversations = listQuery.data?.items ?? [];
  const activeConv = detailQuery.data;
  const activeItem = conversations.find((c) => c.id === activeId);

  const handleSend = useCallback(
    (content: string) => sendMutation.mutate(content),
    [sendMutation],
  );

  function selectConversation(id: string) {
    setActiveId(id);
  }

  function statusColor(status: string): string {
    switch (status) {
      case "open": return "bg-success text-success";
      case "pending": return "bg-warning-container text-warning";
      case "resolved": return "bg-surface-variant text-on-surface-variant";
      case "snoozed": return "bg-surface-variant text-on-surface-variant";
      default: return "bg-surface-variant text-on-surface-variant";
    }
  }

  const title = currentChannel ? currentChannel.name : "Agent Hub";

  return (
    <AppShell title={title}>
      {isAdmin && (
        <div className="mx-4 mt-2 rounded-lg bg-warning-container px-3 py-1.5 text-label-sm text-on-warning-container">
          Xem chi doc ? Ban co quyen admin, khong the gui tin nhan.
        </div>
      )}
      <div className="flex h-[calc(100vh-4rem)] overflow-hidden">
        {/* Left panel: conversation list */}
        <aside className="w-[320px] shrink-0 border-r border-outline bg-surface-container-lowest flex flex-col">
          <div className="border-b border-outline px-4 py-3">
            {channelId && (
              <button
                type="button"
                onClick={() => navigate("/inbox")}
                className="mb-2 flex items-center gap-1 text-label-sm text-primary hover:underline"
              >
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="size-4">
                  <path d="M19 12H5m7-7-7 7 7 7" />
                </svg>
                Tat ca kenh
              </button>
            )}
            <h2 className="text-label-md font-bold text-secondary">Hoi thoai</h2>
            <p className="text-label-sm text-on-surface-variant">
              {listQuery.data?.total ?? 0} cuoc hoi thoai
            </p>
          </div>
          <div className="flex-1 overflow-y-auto">
            {listQuery.isLoading ? (
              <div className="p-4 text-body-md text-on-surface-variant">Dang tai...</div>
            ) : conversations.length === 0 ? (
              <div className="p-4 text-body-md text-on-surface-variant">Khong co hoi thoai</div>
            ) : (
              conversations.map((item) => (
                <button
                  key={item.id}
                  type="button"
                  onClick={() => selectConversation(item.id)}
                  className={w-full text-left px-4 py-3 border-b border-outline/50 hover:bg-surface-container-high transition-colors }
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-semibold text-body-md text-secondary truncate">
                      {item.contactDisplayName ?? item.externalThreadId}
                    </span>
                    <span className={inline-flex items-center rounded-full px-2 py-0.5 text-label-xs }>
                      {item.status}
                    </span>
                  </div>
                  <p className="mt-1 text-body-sm text-on-surface-variant truncate">
                    {item.lastMessagePreview ?? "Chua co tin nhan"}
                  </p>
                  <p className="mt-1 text-label-xs text-on-surface-variant">
                    {item.lastMessageAt
                      ? new Date(item.lastMessageAt).toLocaleString("vi-VN")
                      : "Chua co tin nhan"}
                  </p>
                </button>
              ))
            )}
          </div>
        </aside>

        {/* Right panel: chat area */}
        <main className="flex-1 flex flex-col">
          {!activeId ? (
            <div className="flex-1 flex items-center justify-center text-body-md text-on-surface-variant">
              Chon mot cuoc hoi thoai de bat dau
            </div>
          ) : (
            <>
              {/* Tab bar */}
              <div className="flex items-center gap-2 border-b border-outline bg-surface-container-lowest px-4 py-2">
                {activeItem ? (
                  <div className="flex items-center gap-2 rounded-lg bg-primary/10 px-3 py-1.5">
                    <span className="font-semibold text-label-sm text-primary">
                      {activeItem.contactDisplayName ?? activeItem.externalThreadId}
                    </span>
                    <button type="button" onClick={() => setActiveId(null)} className="ml-1 text-on-surface-variant hover:text-secondary">
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="size-4">
                        <path d="M18 6 6 18M6 6l12 12" />
                      </svg>
                    </button>
                  </div>
                ) : null}
              </div>

              {/* Messages */}
              <div className="flex-1 overflow-y-auto">
                <ChatMessageThread
                  messages={activeConv?.messages ?? []}
                  loading={detailQuery.isLoading}
                />
              </div>

              {!isAdmin && (
                <>
                  {/* Quick actions */}
                  <QuickActionBar
                    conversationId={activeId}
                    status={activeConv?.status ?? "open"}
                    onResolve={() => resolveMutation.mutate()}
                    onAssign={() => assignMutation.mutate()}
                  />

                  {/* Composer */}
                  <ComposerWithAI
                    conversationId={activeId}
                    onSend={handleSend}
                    disabled={activeConv?.status === "resolved" || sendMutation.isPending}
                  />
                </>
              )}
            </>
          )}
        </main>
      </div>
    </AppShell>
  );
}
