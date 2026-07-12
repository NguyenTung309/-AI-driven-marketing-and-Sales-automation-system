import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AppShell } from "@/shared/layout/AppShell";
import { Alert, Button, Card, Modal } from "@/shared/ui";
import { toUserFriendlyError } from "@/shared/utils/userText";
import {
  connectPancake,
  getSimpleUserList,
  listConnectedPancakePages,
  listInboxes,
  mintPancakePages,
  getInboxMembers,
  updateInboxMember,
  updateInbox,
  createInbox,
  type InboxItem,
  type PancakePageSummary,
} from "@/shared/api/admin";

function errorMessage(error: unknown): string {
  return toUserFriendlyError(error, "Không xử lý được yêu cầu. Vui lòng thử lại.");
}

const emptyForm = {

  platform: "facebook",
  externalPageId: "",
  pageAccessToken: "",
  agentId: "",
};

export default function ChannelManagementPage() {
  const queryClient = useQueryClient();
  const [editInboxId, setEditInboxId] = useState<string | null>(null);
  const [showCreate, setShowCreate] = useState(false);
  const [selectedAgentId, setSelectedAgentId] = useState<string | null>(null);
  const [tokenInput, setTokenInput] = useState("");
  const [notice, setNotice] = useState<string | null>(null);
  const [createForm, setCreateForm] = useState(emptyForm);
  // SPEC-16 Module M-5: Pancake connect flow state (user token → list pages → select → mint+store).
  const [pancakeToken, setPancakeToken] = useState("");
  const [discoveredPages, setDiscoveredPages] = useState<readonly PancakePageSummary[]>([]);
  const [selectedPageIds, setSelectedPageIds] = useState<Set<string>>(new Set());

  const inboxesQuery = useQuery({
    queryKey: ["admin", "inboxes"],
    queryFn: listInboxes,
  });
  const usersQuery = useQuery({
    queryKey: ["admin", "users-simple"],
    queryFn: getSimpleUserList,
  });
  const membersQuery = useQuery({
    queryKey: ["admin", "inbox-members", editInboxId],
    queryFn: () => getInboxMembers(editInboxId!),
    enabled: editInboxId !== null,
  });

  const inboxes = inboxesQuery.data ?? [];
  const users = usersQuery.data ?? [];

  useEffect(() => {
    if (membersQuery.data) {
      setSelectedAgentId(membersQuery.data.length > 0 ? membersQuery.data[0] : null);
    }
  }, [membersQuery.data]);

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!editInboxId) throw new Error("No inbox selected");
      if (tokenInput.trim()) {
        await updateInbox(editInboxId, tokenInput.trim());
      }
      return updateInboxMember(editInboxId, selectedAgentId);
    },
    onSuccess: () => {
      setEditInboxId(null);
      setTokenInput("");
      setNotice("Đã cập nhật kênh.");
      void queryClient.invalidateQueries({ queryKey: ["admin", "inboxes"] });
    },
  });

  const createMutation = useMutation({
    mutationFn: () =>
      createInbox({

        name: createForm.externalPageId.trim(),
        platform: createForm.platform,
        externalPageId: createForm.externalPageId,
        pageAccessToken: createForm.pageAccessToken || null,
        agentId: createForm.agentId || null,
      }),
    onSuccess: () => {
      setShowCreate(false);
      setCreateForm(emptyForm);
      setNotice("Đã tạo kênh mới.");
      void queryClient.invalidateQueries({ queryKey: ["admin", "inboxes"] });
    },
  });

  // SPEC-16 Module M-5: Pancake page-token lifecycle.
  const connectedPagesQuery = useQuery({
    queryKey: ["admin", "pancake-pages"],
    queryFn: listConnectedPancakePages,
  });
  const connectMutation = useMutation({
    mutationFn: () => connectPancake(pancakeToken.trim()),
    onSuccess: (pages) => {
      setDiscoveredPages(pages);
      setSelectedPageIds(new Set(pages.map((p) => p.pageId)));
    },
  });
  const mintMutation = useMutation({
    mutationFn: () =>
      mintPancakePages(
        pancakeToken.trim(),
        discoveredPages.filter((p) => selectedPageIds.has(p.pageId)),
      ),
    onSuccess: (results) => {
      const ok = results.filter((r) => r.status === "connected").length;
      const fail = results.length - ok;
      setNotice(`Đã kết nối ${ok} trang Pancake${fail > 0 ? ` (${fail} thất bại)` : ""}.`);
      setDiscoveredPages([]);
      setSelectedPageIds(new Set());
      setPancakeToken("");
      void queryClient.invalidateQueries({ queryKey: ["admin", "pancake-pages"] });
    },
  });

  function memberInfo(inbox: InboxItem): string {
    return inbox.memberCount > 0 ? inbox.memberCount + " sale" : "Chưa gán";
  }

  function resetEdit(inbox: InboxItem) {
    setEditInboxId(inbox.id);
    setTokenInput("");
  }

  return (
    <AppShell title="Kênh giao tiếp">
      <section className="mb-gutter rounded-lg border border-primary/20 bg-primary/5 p-4">
        <div className="flex flex-col gap-3 xl:flex-row xl:items-center xl:justify-between">
          <div>
            <h1 className="text-headline-md text-secondary">Kênh giao tiếp</h1>
            <p className="mt-1 text-body-md text-on-surface-variant">
              Quản lý inbox và gán sale vào từng kênh.
            </p>
          </div>
          <Button type="button" onClick={() => setShowCreate(true)}>
            + Tạo kênh
          </Button>
        </div>
      </section>

      {/* SPEC-16 Module M-5: Pancake connect — paste user token, list pages, mint+store page tokens. */}
      <Card className="mb-gutter flex flex-col gap-3">
        <div>
          <h2 className="text-headline-sm text-secondary">Kết nối Pancake</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">
            Dán Pancake user access token để liệt kê trang, rồi chọn trang cần kết nối. Hệ thống sẽ mint + lưu
            page access token cho từng trang.
          </p>
        </div>
        <div className="flex flex-col gap-2 sm:flex-row">
          <input
            type="password"
            className="flex-1 rounded border border-outline bg-surface-container-lowest px-3 py-2 text-body-md"
            placeholder="Pancake user access token"
            value={pancakeToken}
            onChange={(e) => setPancakeToken(e.target.value)}
            disabled={connectMutation.isPending || mintMutation.isPending}
          />
          <Button
            type="button"
            variant="outline"
            onClick={() => connectMutation.mutate()}
            disabled={!pancakeToken.trim() || connectMutation.isPending || mintMutation.isPending}
          >
            Liệt kê trang
          </Button>
        </div>
        {connectMutation.error ? (
          <Alert tone="error">{errorMessage(connectMutation.error)}</Alert>
        ) : null}

        {discoveredPages.length > 0 ? (
          <div className="flex flex-col gap-2">
            <p className="text-label-sm text-on-surface-variant">Chọn trang cần kết nối:</p>
            <ul className="flex flex-col gap-1">
              {discoveredPages.map((page) => (
                <li key={page.pageId} className="flex items-center gap-2 rounded border border-outline px-3 py-2">
                  <input
                    type="checkbox"
                    checked={selectedPageIds.has(page.pageId)}
                    onChange={(e) =>
                      setSelectedPageIds((prev) => {
                        const next = new Set(prev);
                        if (e.target.checked) next.add(page.pageId);
                        else next.delete(page.pageId);
                        return next;
                      })
                    }
                    disabled={mintMutation.isPending}
                  />
                  <span className="text-body-md text-secondary">{page.name}</span>
                  <span className="font-mono text-mono-status text-on-surface-variant">{page.platform}</span>
                </li>
              ))}
            </ul>
            <div className="flex gap-2">
              <Button
                type="button"
                onClick={() => mintMutation.mutate()}
                disabled={selectedPageIds.size === 0 || mintMutation.isPending}
              >
                Kết nối {selectedPageIds.size} trang
              </Button>
              <Button
                type="button"
                variant="ghost"
                onClick={() => {
                  setDiscoveredPages([]);
                  setSelectedPageIds(new Set());
                }}
                disabled={mintMutation.isPending}
              >
                Hủy
              </Button>
            </div>
            {mintMutation.error ? <Alert tone="error">{errorMessage(mintMutation.error)}</Alert> : null}
          </div>
        ) : null}

        {/* Connected pages status (never exposes the token). */}
        {connectedPagesQuery.data && connectedPagesQuery.data.length > 0 ? (
          <div className="border-t border-outline pt-3">
            <h3 className="text-label-md text-secondary">Trang đã kết nối</h3>
            <ul className="mt-2 flex flex-col gap-1">
              {connectedPagesQuery.data.map((page) => (
                <li key={page.pageId} className="flex items-center gap-2 text-body-sm">
                  <span
                    className={
                      "inline-flex items-center rounded-full px-2 py-0.5 text-label-xs " +
                      (page.status === "connected"
                        ? "bg-success-container text-success"
                        : "bg-warning-container text-warning")
                    }
                  >
                    {page.status === "connected" ? "Đã kết nối" : "Chưa cấu hình"}
                  </span>
                  <span className="text-secondary">{page.name}</span>
                  <span className="font-mono text-mono-status text-on-surface-variant">{page.platform}</span>
                </li>
              ))}
            </ul>
          </div>
        ) : null}
      </Card>

      {notice ? (
        <div className="mb-gutter">
          <Alert tone="success">{notice}</Alert>
        </div>
      ) : null}

      {inboxesQuery.error ? (
        <div className="mb-gutter">
          <Alert tone="error">{errorMessage(inboxesQuery.error)}</Alert>
        </div>
      ) : null}

      <Card className="overflow-hidden p-0">
        <div className="border-b border-outline p-card-padding">
          <h2 className="text-headline-sm text-secondary">Danh sách kênh</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">
            Nhấn vào một kênh để gán sale phụ trách.
          </p>
        </div>

        {inboxes.length === 0 ? (
          <div className="p-card-padding">
            <div className="rounded-lg border border-dashed border-outline bg-surface p-6 text-center text-body-md text-on-surface-variant">
              Chưa có kênh giao tiếp nào.
            </div>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-[700px] w-full border-collapse text-left">
              <thead className="bg-surface-variant text-label-sm uppercase text-secondary">
                <tr>
                  <th className="px-4 py-3 font-bold">Tên kênh</th>
                  <th className="px-4 py-3 font-bold">Nền tảng</th>
                  <th className="px-4 py-3 font-bold">Trạng thái</th>
                  <th className="px-4 py-3 font-bold">Token</th>
                  <th className="px-4 py-3 font-bold">Sale phụ trách</th>
                  <th className="px-4 py-3 font-bold"></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-outline bg-white">
                {inboxes.map((inbox) => (
                  <tr key={inbox.id} className="hover:bg-surface-container-low">
                    <td className="px-4 py-4 font-semibold text-secondary">
                      {inbox.name}
                      <span className="ml-2 font-mono text-mono-status text-on-surface-variant">
                        {inbox.externalPageId}
                      </span>
                    </td>
                    <td className="px-4 py-4 text-body-md text-secondary capitalize">{inbox.platform}</td>
                    <td className="px-4 py-4">
                      <span
                        className={
                          "inline-flex items-center rounded-full px-2 py-0.5 text-label-sm " +
                          (inbox.isActive
                            ? "bg-success-container text-success"
                            : "bg-surface-variant text-on-surface-variant")
                        }
                      >
                        {inbox.isActive ? "Hoạt động" : "Tắt"}
                      </span>
                    </td>
                    <td className="px-4 py-4">
                      <span
                        className={
                          "inline-flex items-center rounded-full px-2 py-0.5 text-label-xs " +
                          (inbox.hasToken
                            ? "bg-success-container text-success"
                            : "bg-warning-container text-warning")
                        }
                      >
                        {inbox.hasToken ? "Có token" : "Thiếu token"}
                      </span>
                    </td>
                    <td className="px-4 py-4 text-body-md text-on-surface-variant">{memberInfo(inbox)}</td>
                    <td className="px-4 py-4 text-right">
                      <Button type="button" variant="outline" onClick={() => resetEdit(inbox)}>
                        Chỉnh sửa
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {/* Edit modal */}
      <Modal
        open={editInboxId !== null}
        onClose={() => setEditInboxId(null)}
        title="Chỉnh sửa kênh"
        footer={
          <>
            <Button type="button" variant="ghost" onClick={() => setEditInboxId(null)} disabled={saveMutation.isPending}>
              Hủy
            </Button>
            <Button type="submit" form="channel-edit-form" disabled={saveMutation.isPending}>
              Lưu
            </Button>
          </>
        }
      >
        {saveMutation.error ? <Alert tone="error">{errorMessage(saveMutation.error)}</Alert> : null}
        {usersQuery.error ? <Alert tone="error">{errorMessage(usersQuery.error)}</Alert> : null}
        <form
          id="channel-edit-form"
          onSubmit={(event) => {
            event.preventDefault();
            saveMutation.mutate();
          }}
          className="space-y-4"
        >
          <label className="block">
            <span className="mb-1 block text-label-sm font-semibold text-secondary">Sale phụ trách</span>
            <select
              className="w-full rounded border border-outline bg-surface-container-lowest px-3 py-2 text-body-md"
              value={selectedAgentId ?? ""}
              onChange={(e) => setSelectedAgentId(e.target.value || null)}
            >
              <option value="">-- Chọn sale --</option>
              {users.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.displayName || u.email}
                </option>
              ))}
            </select>
          </label>
          <label className="block">
            <span className="mb-1 block text-label-sm font-semibold text-secondary">Pancake Page Access Token</span>
            <input
              type="password"
              className="w-full rounded border border-outline bg-surface-container-lowest px-3 py-2 text-body-md"
              placeholder="Nhập token từ Pancake (để trống nếu không đổi)"
              value={tokenInput}
              onChange={(e) => setTokenInput(e.target.value)}
            />
            <p className="mt-1 text-label-xs text-on-surface-variant">Token được lưu trữ bảo mật.</p>
          </label>
        </form>
      </Modal>

      {/* Create modal */}
      <Modal
        open={showCreate}
        onClose={() => setShowCreate(false)}
        title="Tạo kênh mới"
        footer={
          <>
            <Button type="button" variant="ghost" onClick={() => setShowCreate(false)} disabled={createMutation.isPending}>
              Hủy
            </Button>
            <Button type="submit" form="channel-create-form" disabled={createMutation.isPending || !createForm.externalPageId}>
              Tạo kênh
            </Button>
          </>
        }
      >
        {createMutation.error ? <Alert tone="error">{errorMessage(createMutation.error)}</Alert> : null}
        <form
          id="channel-create-form"
          onSubmit={(event) => {
            event.preventDefault();
            createMutation.mutate();
          }}
          className="space-y-4"
        >


          <label className="block">
            <span className="mb-1 block text-label-sm font-semibold text-secondary">Nền tảng</span>
            <select
              className="w-full rounded border border-outline bg-surface-container-lowest px-3 py-2 text-body-md"
              value={createForm.platform}
              onChange={(e) => setCreateForm({ ...createForm, platform: e.target.value })}
            >
              <option value="facebook">Facebook</option>
              <option value="zalo">Zalo OA</option>
              <option value="web">Website</option>
            </select>
          </label>

          <label className="block">
            <span className="mb-1 block text-label-sm font-semibold text-secondary">External Page ID *</span>
            <input
              type="text"
              className="w-full rounded border border-outline bg-surface-container-lowest px-3 py-2 text-body-md"
              placeholder="ID trang Facebook / Zalo OA"
              value={createForm.externalPageId}
              onChange={(e) => setCreateForm({ ...createForm, externalPageId: e.target.value })}
              required
            />
          </label>

          <label className="block">
            <span className="mb-1 block text-label-sm font-semibold text-secondary">Pancake Page Access Token</span>
            <input
              type="password"
              className="w-full rounded border border-outline bg-surface-container-lowest px-3 py-2 text-body-md"
              placeholder="Token từ Pancake (có thể để trống)"
              value={createForm.pageAccessToken}
              onChange={(e) => setCreateForm({ ...createForm, pageAccessToken: e.target.value })}
            />
            <p className="mt-1 text-label-xs text-on-surface-variant">Token được lưu trữ bảo mật. Có thể thêm sau.</p>
          </label>

          <label className="block">
            <span className="mb-1 block text-label-sm font-semibold text-secondary">Gán cho sale</span>
            <select
              className="w-full rounded border border-outline bg-surface-container-lowest px-3 py-2 text-body-md"
              value={createForm.agentId}
              onChange={(e) => setCreateForm({ ...createForm, agentId: e.target.value })}
            >
              <option value="">-- Chọn sale (không bắt buộc) --</option>
              {users.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.displayName || u.email}
                </option>
              ))}
            </select>
          </label>
        </form>
      </Modal>
    </AppShell>
  );
}
