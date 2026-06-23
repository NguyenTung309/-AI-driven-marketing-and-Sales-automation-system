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
  return toUserFriendlyError(error, "Khong xu ly duoc yeu cau. Vui long thu lai.");
}

const emptyForm = {
  name: "",
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
      setNotice("Da cap nhat sale cho kenh.");
      void queryClient.invalidateQueries({ queryKey: ["admin", "inboxes"] });
    },
  });

  const createMutation = useMutation({
    mutationFn: () =>
      createInbox({
        name: createForm.name,
        platform: createForm.platform,
        externalPageId: createForm.externalPageId,
        pageAccessToken: createForm.pageAccessToken || null,
        agentId: createForm.agentId || null,
      }),
    onSuccess: () => {
      setShowCreate(false);
      setCreateForm(emptyForm);
      setNotice("Da tao kenh moi.");
      void queryClient.invalidateQueries({ queryKey: ["admin", "inboxes"] });
    },
  });

  function memberInfo(inbox: InboxItem): string {
    return inbox.memberCount > 0 ? inbox.memberCount + " sale" : "Chua gan";
  }

  function resetEdit(inbox: InboxItem) {
    setEditInboxId(inbox.id);
    setTokenInput("");
  }

  return (
    <AppShell title="Kenh giao tiep">
      <section className="mb-gutter rounded-lg border border-primary/20 bg-primary/5 p-4">
        <div className="flex flex-col gap-3 xl:flex-row xl:items-center xl:justify-between">
          <div>
            <h1 className="text-headline-md text-secondary">Kenh giao tiep</h1>
            <p className="mt-1 text-body-md text-on-surface-variant">
              Quan ly inbox va gan sale vao tung kenh.
            </p>
          </div>
          <Button type="button" onClick={() => setShowCreate(true)}>
            + Tao kenh
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
          <h2 className="text-headline-sm text-secondary">Danh sach kenh</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">
            Nhan vao mot kenh de gan sale phu trach.
          </p>
        </div>

        {inboxes.length === 0 ? (
          <div className="p-card-padding">
            <div className="rounded-lg border border-dashed border-outline bg-surface p-6 text-center text-body-md text-on-surface-variant">
              Chua co kenh giao tiep nao.
            </div>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-[700px] w-full border-collapse text-left">
              <thead className="bg-surface-variant text-label-sm uppercase text-secondary">
                <tr>
                  <th className="px-4 py-3 font-bold">Ten kenh</th>
                  <th className="px-4 py-3 font-bold">Nen tang</th>
                  <th className="px-4 py-3 font-bold">Trang thai</th>
                  <th className="px-4 py-3 font-bold">Token</th>
                  <th className="px-4 py-3 font-bold">Sale phu trach</th>
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
                        {inbox.isActive ? "Hoat dong" : "Tat"}
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
                        {inbox.hasToken ? "Co token" : "Thieu token"}
                      </span>
                    </td>
                    <td className="px-4 py-4 text-body-md text-on-surface-variant">{memberInfo(inbox)}</td>
                    <td className="px-4 py-4 text-right">
                      <Button type="button" variant="outline" onClick={() => resetEdit(inbox)}>
                        Chinh sua
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
        title="Chinh sua kenh"
        footer={
          <>
            <Button type="button" variant="ghost" onClick={() => setEditInboxId(null)} disabled={saveMutation.isPending}>
              Huy
            </Button>
            <Button type="submit" form="channel-edit-form" disabled={saveMutation.isPending}>
              Luu
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
            <span className="mb-1 block text-label-sm font-semibold text-secondary">Sale phu trach</span>
            <select
              className="w-full rounded border border-outline bg-surface-container-lowest px-3 py-2 text-body-md"
              value={selectedAgentId ?? ""}
              onChange={(e) => setSelectedAgentId(e.target.value || null)}
            >
              <option value="">-- Chon sale --</option>
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
              placeholder="Nhap token tu Pancake (de trong neu khong doi)"
              value={tokenInput}
              onChange={(e) => setTokenInput(e.target.value)}
            />
            <p className="mt-1 text-label-xs text-on-surface-variant">Token duoc luu tru bao mat.</p>
          </label>
        </form>
      </Modal>

      {/* Create modal */}
      <Modal
        open={showCreate}
        onClose={() => setShowCreate(false)}
        title="Tao kenh moi"
        footer={
          <>
            <Button type="button" variant="ghost" onClick={() => setShowCreate(false)} disabled={createMutation.isPending}>
              Huy
            </Button>
            <Button type="submit" form="channel-create-form" disabled={createMutation.isPending || !createForm.name || !createForm.externalPageId}>
              Tao kenh
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
            <span className="mb-1 block text-label-sm font-semibold text-secondary">Ten kenh *</span>
            <input
              type="text"
              className="w-full rounded border border-outline bg-surface-container-lowest px-3 py-2 text-body-md"
              placeholder="VD: Facebook Page Chinh"
              value={createForm.name}
              onChange={(e) => setCreateForm({ ...createForm, name: e.target.value })}
              required
            />
          </label>

          <label className="block">
            <span className="mb-1 block text-label-sm font-semibold text-secondary">Nen tang</span>
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
              placeholder="Token tu Pancake (co the de trong)"
              value={createForm.pageAccessToken}
              onChange={(e) => setCreateForm({ ...createForm, pageAccessToken: e.target.value })}
            />
            <p className="mt-1 text-label-xs text-on-surface-variant">Token duoc luu tru bao mat. Co them sau.</p>
          </label>

          <label className="block">
            <span className="mb-1 block text-label-sm font-semibold text-secondary">Gan cho sale</span>
            <select
              className="w-full rounded border border-outline bg-surface-container-lowest px-3 py-2 text-body-md"
              value={createForm.agentId}
              onChange={(e) => setCreateForm({ ...createForm, agentId: e.target.value })}
            >
              <option value="">-- Chon sale (khong bat buoc) --</option>
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
