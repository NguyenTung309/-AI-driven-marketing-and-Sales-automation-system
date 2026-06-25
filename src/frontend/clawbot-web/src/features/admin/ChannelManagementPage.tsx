import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AppShell } from "@/shared/layout/AppShell";
import { Alert, Button, Card, Modal } from "@/shared/ui";
import { toUserFriendlyError } from "@/shared/utils/userText";
import {
  getSimpleUserList,
  listInboxes,
  getInboxMembers,
  updateInboxMember,
  createInbox,
  type InboxItem,
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
    mutationFn: () => {
      if (!editInboxId) throw new Error("No inbox selected");
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
            <span className="mb-1 block text-label-sm font-semibold text-secondary">Page Access Token</span>
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
            <span className="mb-1 block text-label-sm font-semibold text-secondary">Page Access Token</span>
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
