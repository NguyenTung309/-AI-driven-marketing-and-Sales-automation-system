import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AppShell } from '@/shared/layout/AppShell';
import { Alert, Button, Card, Modal } from '@/shared/ui';
import { toUserFriendlyError } from '@/shared/utils/userText';
import {
  getSimpleUserList,
  listInboxes,
  getInboxMembers,
  updateInboxMember,
  type InboxItem,
} from '@/shared/api/admin';

function errorMessage(error: unknown): string {
  return toUserFriendlyError(error, 'Khong xu ly duoc yeu cau. Vui long thu lai.');
}

export default function ChannelManagementPage() {
  const queryClient = useQueryClient();
  const [editInboxId, setEditInboxId] = useState<string | null>(null);
  const [selectedAgentId, setSelectedAgentId] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const inboxesQuery = useQuery({
    queryKey: ['admin', 'inboxes'],
    queryFn: listInboxes,
  });
  const usersQuery = useQuery({
    queryKey: ['admin', 'users-simple'],
    queryFn: getSimpleUserList,
  });
  const membersQuery = useQuery({
    queryKey: ['admin', 'inbox-members', editInboxId],
    queryFn: () => getInboxMembers(editInboxId!),
    enabled: editInboxId !== null,
  });

  const inboxes = inboxesQuery.data ?? [];
  const users = usersQuery.data ?? [];

  // Dong bo selectedAgentId khi load members xong
  useEffect(() => {
    if (membersQuery.data) {
      setSelectedAgentId(membersQuery.data.length > 0 ? membersQuery.data[0] : null);
    }
  }, [membersQuery.data]);

  const saveMutation = useMutation({
    mutationFn: () => {
      if (!editInboxId) throw new Error('No inbox selected');
      return updateInboxMember(editInboxId, selectedAgentId);
    },
    onSuccess: () => {
      setEditInboxId(null);
      setNotice('Da cap nhat sale cho kenh.');
      void queryClient.invalidateQueries({ queryKey: ['admin', 'inboxes'] });
    },
  });

  function openEdit(inbox: InboxItem) {
    setEditInboxId(inbox.id);
  }

  function memberInfo(inbox: InboxItem): string {
    const members = membersQuery.data;
    if (editInboxId === inbox.id && members && members.length > 0) {
      const u = users.find(u2 => u2.id === members[0]);
      return u ? u.displayName || u.email : '';
    }
    return inbox.memberCount > 0 ? inbox.memberCount + ' sale' : 'Chua gan';
  }

  return (
    <AppShell title='Kenh giao tiep'>
      <section className='mb-gutter rounded-lg border border-primary/20 bg-primary/5 p-4'>
        <div className='flex flex-col gap-3 xl:flex-row xl:items-center xl:justify-between'>
          <div>
            <h1 className='text-headline-md text-secondary'>Kenh giao tiep</h1>
            <p className='mt-1 text-body-md text-on-surface-variant'>
              Quan ly inbox va gan sale vao tung kenh.
            </p>
          </div>
        </div>
      </section>

      {notice ? (
        <div className='mb-gutter'>
          <Alert tone='success'>{notice}</Alert>
        </div>
      ) : null}

      {inboxesQuery.error ? (
        <div className='mb-gutter'>
          <Alert tone='error'>{errorMessage(inboxesQuery.error)}</Alert>
        </div>
      ) : null}

      <Card className='overflow-hidden p-0'>
        <div className='border-b border-outline p-card-padding'>
          <h2 className='text-headline-sm text-secondary'>Danh sach kenh</h2>
          <p className='mt-1 text-body-md text-on-surface-variant'>
            Nhan vao mot kenh de gan sale phu trach.
          </p>
        </div>

        {inboxes.length === 0 ? (
          <div className='p-card-padding'>
            <div className='rounded-lg border border-dashed border-outline bg-surface p-6 text-center text-body-md text-on-surface-variant'>
              Chua co kenh giao tiep nao.
            </div>
          </div>
        ) : (
          <div className='overflow-x-auto'>
            <table className='min-w-[700px] w-full border-collapse text-left'>
              <thead className='bg-surface-variant text-label-sm uppercase text-secondary'>
                <tr>
                  <th className='px-4 py-3 font-bold'>Ten kenh</th>
                  <th className='px-4 py-3 font-bold'>Nen tang</th>
                  <th className='px-4 py-3 font-bold'>Trang thai</th>
                  <th className='px-4 py-3 font-bold'>Sale phu trach</th>
                  <th className='px-4 py-3 font-bold'></th>
                </tr>
              </thead>
              <tbody className='divide-y divide-outline bg-white'>
                {inboxes.map((inbox) => (
                  <tr key={inbox.id} className='hover:bg-surface-container-low'>
                    <td className='px-4 py-4 font-semibold text-secondary'>
                      {inbox.name}
                      <span className='ml-2 font-mono text-mono-status text-on-surface-variant'>
                        {inbox.externalPageId}
                      </span>
                    </td>
                    <td className='px-4 py-4 text-body-md text-secondary'>{inbox.platform}</td>
                    <td className='px-4 py-4'>
                      <span
                        className={
                          'inline-flex items-center rounded-full px-2 py-0.5 text-label-sm ' +
                          (inbox.isActive
                            ? 'bg-success-container text-success'
                            : 'bg-surface-variant text-on-surface-variant')
                        }
                      >
                        {inbox.isActive ? 'Hoat dong' : 'Tat'}
                      </span>
                    </td>
                    <td className='px-4 py-4 text-body-md text-on-surface-variant'>{memberInfo(inbox)}</td>
                    <td className='px-4 py-4 text-right'>
                      <Button type='button' variant='outline' onClick={() => openEdit(inbox)}>
                        Gan sale
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <Modal
        open={editInboxId !== null}
        onClose={() => setEditInboxId(null)}
        title='Gan sale vao kenh'
        footer={
          <>
            <Button type='button' variant='ghost' onClick={() => setEditInboxId(null)} disabled={saveMutation.isPending}>
              Huy
            </Button>
            <Button type='submit' form='channel-members-form' disabled={saveMutation.isPending}>
              Luu
            </Button>
          </>
        }
      >
        {saveMutation.error ? <Alert tone='error'>{errorMessage(saveMutation.error)}</Alert> : null}
        {usersQuery.error ? <Alert tone='error'>{errorMessage(usersQuery.error)}</Alert> : null}
        <form
          id='channel-members-form'
          onSubmit={(event) => {
            event.preventDefault();
            saveMutation.mutate();
          }}
        >
          <label className='block'>
            <span className='mb-1 block text-label-sm font-semibold text-secondary'>Chon sale phu trach</span>
            {users.length === 0 ? (
              <p className='rounded border border-outline p-2 text-body-md text-on-surface-variant'>Khong co nguoi dung.</p>
            ) : (
              <select
                className='w-full rounded border border-outline bg-surface-container-lowest px-3 py-2 text-body-md'
                value={selectedAgentId ?? ''}
                onChange={(e) => setSelectedAgentId(e.target.value || null)}
              >
                <option value=''>-- Chon sale --</option>
                {users.map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.displayName || u.email}
                  </option>
                ))}
              </select>
            )}
          </label>
        </form>
      </Modal>
    </AppShell>
  );
}
