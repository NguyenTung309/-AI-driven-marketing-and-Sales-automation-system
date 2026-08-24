import { Button, Card, StatusPill } from "@/shared/ui";
import { formatDateTime, inputClass } from "./adminHelpers";
import { EmptyState } from "./adminUi";
import type { AdminUser, PancakeChannelInfo } from "@/shared/api/admin";

interface AdminUsersTabProps {
  readonly users: readonly AdminUser[];
  readonly search: string;
  readonly onSearchChange: (value: string) => void;
  readonly canManageUsers: boolean;
  readonly canManagePancakeToken: boolean;
  readonly canManageInboxOwners: boolean;
  readonly onCreateUser: () => void;
  readonly onManageChannel: (user: AdminUser, channel: PancakeChannelInfo) => void;
  readonly onEditUser: (user: AdminUser) => void;
  readonly onToggleActive: (user: AdminUser) => void;
  readonly activeMutationPending: boolean;
  readonly onResetPassword: (user: AdminUser) => void;
  readonly resetPasswordPending: boolean;
}

export function AdminUsersTab({
  users,
  search,
  onSearchChange,
  canManageUsers,
  canManagePancakeToken,
  canManageInboxOwners,
  onCreateUser,
  onManageChannel,
  onEditUser,
  onToggleActive,
  activeMutationPending,
  onResetPassword,
  resetPasswordPending,
}: AdminUsersTabProps) {
  return (
    <section className="space-y-gutter">
      <Card className="p-0">
        <div className="flex flex-col gap-3 border-b border-outline p-card-padding lg:flex-row lg:items-center lg:justify-between">
          <div>
            <h2 className="text-headline-sm text-secondary">Quản lý người dùng</h2>
            <p className="mt-1 text-body-md text-on-surface-variant">Danh sách tài khoản có quyền truy cập hệ thống.</p>
          </div>
          <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
            <input className={inputClass} value={search} onChange={(event) => onSearchChange(event.target.value)} placeholder="Tìm email hoặc tên..." />
            {canManageUsers ? (
              <Button type="button" className="shrink-0 whitespace-nowrap" onClick={onCreateUser}>
                <span aria-hidden="true" className="material-symbols-outlined text-[18px]">person_add</span>
                Thêm người dùng
              </Button>
            ) : null}
          </div>
        </div>
        <div className="overflow-x-auto">
          <table className="min-w-[1180px] w-full border-collapse text-left">
            <thead className="bg-surface-variant text-label-sm uppercase text-secondary">
              <tr>
                <th scope="col" className="px-4 py-3 font-bold">Người dùng</th>
                <th scope="col" className="px-4 py-3 font-bold">Email</th>
                <th scope="col" className="px-4 py-3 font-bold">Vai trò</th>
                {/* <th scope="col" className="px-4 py-3 font-bold">Đăng nhập cuối</th> */}
                <th scope="col" className="px-4 py-3 font-bold">Kênh Pancake</th>
                <th scope="col" className="px-4 py-3 font-bold">Trạng thái</th>
                <th scope="col" className="px-4 py-3 text-right font-bold">Hành động</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-outline bg-white">
              {users.map((user) => (
                <tr key={user.id} className="hover:bg-surface-container-low">
                  <td className="px-4 py-4">
                    <div className="flex items-center gap-3">
                      <span className="flex size-9 items-center justify-center rounded-full bg-primary/10 text-label-sm font-bold text-primary">
                        {user.displayName.slice(0, 1).toUpperCase()}
                      </span>
                      <div>
                        <p className="font-semibold text-secondary">{user.displayName}</p>
                        <p className="text-label-sm text-on-surface-variant">{user.phone ?? "Chưa có số điện thoại"}</p>
                      </div>
                    </div>
                  </td>
                  <td className="px-4 py-4 text-body-md text-secondary">{user.email}</td>
                  <td className="px-4 py-4">
                    {user.roles === null ? (
                      <span className="text-body-md text-on-surface-variant">Không có quyền xem</span>
                    ) : user.roles.length ? (
                      <div className="flex flex-wrap gap-1.5">
                        {user.roles.map((role) => <StatusPill key={role}>{role}</StatusPill>)}
                      </div>
                    ) : (
                      <span className="text-body-md text-on-surface-variant">Chưa gán</span>
                    )}
                  </td>
                  {/* <td className="px-4 py-4 text-body-md text-on-surface-variant">{formatDateTime(user.lastLoginAt)}</td> */}
                  <td className="px-4 py-4">
                    {user.pancakeChannels && user.pancakeChannels.length > 0 ? (
                      <div className="flex min-w-[330px] flex-col gap-2">
                        {user.pancakeChannels.map((channel) => (
                          <div key={channel.inboxId} className="rounded border border-outline bg-surface px-3 py-2">
                            <div className="flex items-start justify-between gap-3">
                              <div className="min-w-0">
                                <p className="truncate text-body-md font-semibold text-secondary">{channel.name || channel.pageId}</p>
                                <div className="mt-1 flex flex-wrap items-center gap-2">
                                  <span className="text-label-sm font-medium uppercase text-on-surface-variant">{channel.platform}</span>
                                  <span className="font-mono text-mono-status text-on-surface-variant">{channel.pageId}</span>
                                  <StatusPill tone={channel.hasToken ? "success" : "warning"}>
                                    {channel.hasToken ? "Có token" : "Thiếu token"}
                                  </StatusPill>
                                </div>
                              </div>
                              {canManagePancakeToken || canManageInboxOwners ? (
                                <Button
                                  type="button"
                                  size="sm"
                                  variant="ghost"
                                  className="shrink-0"
                                  onClick={() => onManageChannel(user, channel)}
                                  aria-label={`Quản lý kênh ${channel.name || channel.pageId} của ${user.displayName}`}
                                >
                                  <span aria-hidden="true" className="material-symbols-outlined text-[18px]">tune</span>
                                </Button>
                              ) : null}
                            </div>
                          </div>
                        ))}
                      </div>
                    ) : (
                      <StatusPill tone="warning">Chưa có</StatusPill>
                    )}
                  </td>
                  <td className="px-4 py-4">
                    <StatusPill tone={user.isActive ? "success" : "error"}>{user.isActive ? "Hoạt động" : "Đã khóa"}</StatusPill>
                  </td>
                  <td className="px-4 py-4">
                    <div className="flex justify-end gap-2">
                      <Button type="button" size="sm" variant="ghost" onClick={() => onEditUser(user)} aria-label={`Sửa ${user.displayName}`}>
                        <span aria-hidden="true" className="material-symbols-outlined text-[18px]">edit</span>
                      </Button>
                      {canManageUsers ? (
                        <>
                          <Button
                            type="button"
                            size="sm"
                            variant="ghost"
                            onClick={() => onToggleActive(user)}
                            disabled={activeMutationPending}
                            aria-label={user.isActive ? `Khóa ${user.displayName}` : `Mở khóa ${user.displayName}`}
                          >
                            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">{user.isActive ? "lock" : "lock_open"}</span>
                          </Button>
                          <Button
                            type="button"
                            size="sm"
                            variant="ghost"
                            onClick={() => onResetPassword(user)}
                            disabled={resetPasswordPending}
                            aria-label={`Reset mật khẩu ${user.displayName}`}
                          >
                            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">restart_alt</span>
                          </Button>
                        </>
                      ) : null}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {!users.length ? <div className="p-card-padding"><EmptyState>Chưa có người dùng phù hợp bộ lọc.</EmptyState></div> : null}
      </Card>
    </section>
  );
}
