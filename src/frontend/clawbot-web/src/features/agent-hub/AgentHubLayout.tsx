import { useCallback, useEffect, useState } from "react";
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
  type ConversationListItem,
} from "@/shared/api/inbox";
import ChatMessageThread from "./ChatMessageThread";
import ComposerWithAI from "./ComposerWithAI";
import QuickActionBar from "./QuickActionBar";
import ConversationTabs from "./ConversationTabs";
import CommandPalette from "./CommandPalette";
import SideDrawer from "./SideDrawer";
import DailySummaryPopup from "./DailySummaryPopup";

function readSummaryFromUrl(): boolean {
  return new URLSearchParams(window.location.search).has("summary");
}

export default function AgentHubLayout() {
  const { channelId } = useParams<{ channelId?: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [sessionChannelId, setSessionChannelId] = useState(channelId);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [commandOpen, setCommandOpen] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [showSummary, setShowSummary] = useState(readSummaryFromUrl);
  const [tabs, setTabs] = useState<string[]>([]);

  // Reset activeId + tabs when switching channels (adjust state during render).
  if (sessionChannelId !== channelId) {
    setSessionChannelId(channelId);
    setActiveId(null);
    setTabs([]);
  }

  const meQuery = useQuery({ queryKey: ["me"], queryFn: getMe });
  const meId = meQuery.data?.sub;
  const isAdmin = meQuery.data?.permissions?.includes("admin:inboxes") ?? false;

  // Ctrl+K for command palette
  useEffect(() => {
    function handleKey(e: KeyboardEvent) {
      if (e.key === "k" && (e.metaKey || e.ctrlKey)) {
        e.preventDefault();
        setCommandOpen(true);
      }
    }
    window.addEventListener("keydown", handleKey);
    return () => window.removeEventListener("keydown", handleKey);
  }, []);

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

  const handleSend = useCallback(
    (content: string) => sendMutation.mutate(content),
    [sendMutation],
  );

  function selectConversation(id: string) {
    setActiveId(id);
    setTabs((prev) => {
      if (prev.includes(id)) return prev;
      return [...prev, id];
    });
  }

  function closeTab(id: string) {
    setTabs((prev) => prev.filter((t) => t !== id));
    if (activeId === id) setActiveId(tabs.filter((t) => t !== id)[0] ?? null);
  }

  const title = currentChannel ? currentChannel.name : "Agent Hub";

  return (
    <AppShell title={title} noPadding={true}>
      {isAdmin && (
        <div className="mx-4 mt-2 shrink-0 rounded-lg bg-warning-container px-3 py-1.5 text-label-sm text-on-warning-container">
          Xem chỉ đọc? Bạn có quyền admin, không thể gửi tin nhắn.
        </div>
      )}
      <div className="flex-1 flex min-h-0 overflow-hidden">
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
            <h2 className="text-label-md font-bold text-secondary">Hội thoại</h2>
            <p className="text-label-sm text-on-surface-variant">
              {listQuery.data?.total ?? 0} cuộc hội thoại
            </p>
          </div>
          <div className="flex-1 overflow-y-auto">
            {listQuery.isLoading ? (
              <div className="p-4 text-body-md text-on-surface-variant">Đang tải...</div>
            ) : conversations.length === 0 ? (
              <div className="p-4 text-body-md text-on-surface-variant">Không có hội thoại</div>
            ) : (
              conversations.map((item) => (
                <button
                  key={item.id}
                  type="button"
                  onClick={() => selectConversation(item.id)}
                  className="w-full text-left px-4 py-3 border-b border-outline/50 hover:bg-surface-container-high transition-colors"
                >
                  <div className="flex items-start gap-3">
                    {item.contactAvatarUrl ? (
                      <img src={item.contactAvatarUrl} alt="" className="size-9 rounded-full object-cover shrink-0" onError={(e) => { (e.target as HTMLImageElement).style.display = "none"; }} />
                    ) : (
                      <div className="size-9 rounded-full bg-surface-variant flex items-center justify-center text-label-sm font-bold text-on-surface-variant shrink-0">
                        {(item.contactDisplayName?.charAt(0) ?? "?").toUpperCase()}
                      </div>
                    )}
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center justify-between gap-2">
                        <span className="font-semibold text-body-md text-secondary truncate">
                          {item.contactDisplayName ?? item.externalThreadId}
                        </span>
                        <span className="inline-flex items-center rounded-full px-2 py-0.5 text-label-xs">
                          {item.status}
                        </span>
                      </div>
                      <p className="mt-1 text-body-sm text-on-surface-variant truncate">
                        {item.lastMessagePreview ?? "Chưa có tin nhắn"}
                      </p>
                      <p className="mt-1 text-label-xs text-on-surface-variant">
                        {item.lastMessageAt
                          ? new Date(item.lastMessageAt).toLocaleString("vi-VN")
                          : "Chưa có tin nhắn"}
                      </p>
                    </div>
                  </div>
                </button>
              ))
            )}
          </div>
        </aside>

        {/* Right panel: chat area */}
        <main className="flex-1 flex flex-col">
          {!activeId ? (
            <div className="flex-1 flex items-center justify-center text-body-md text-on-surface-variant">
              Chọn một cuộc hội thoại để bắt đầu
            </div>
          ) : (
            <>
              <ConversationTabs
                conversations={tabs.map((tid) => conversations.find((c) => c.id === tid)).filter(Boolean) as ConversationListItem[]}
                activeId={activeId}
                onSelect={selectConversation}
                onClose={closeTab}
              />

              {/* Messages */}
              <div className="flex-1 overflow-y-auto">
                <ChatMessageThread
                  messages={activeConv?.messages ?? []}
                  loading={detailQuery.isLoading}
                  contactAvatarUrl={activeConv?.contactAvatarUrl}
                  contactDisplayName={activeConv?.contactDisplayName}
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

        {/* Side drawer */}
        {activeConv && (
          <SideDrawer
            open={drawerOpen}
            onClose={() => setDrawerOpen(false)}
            contactDisplayName={activeConv.contactDisplayName}
            contactId={activeConv.contactId}
            platform={activeConv.platform}
          />
        )}

        {/* Drawer toggle button */}
        {activeId && (
          <button
            type="button"
            onClick={() => setDrawerOpen(!drawerOpen)}
            className="fixed bottom-4 right-4 z-20 flex size-10 items-center justify-center rounded-full bg-primary text-on-primary shadow-lg md:hidden"
          >
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z" />
            </svg>
          </button>
        )}
      </div>

      {/* Command palette */}
      <CommandPalette open={commandOpen} onClose={() => setCommandOpen(false)} />

      {/* Daily summary popup */}
      {showSummary && <DailySummaryPopup onClose={() => setShowSummary(false)} />}
    </AppShell>
  );
}
