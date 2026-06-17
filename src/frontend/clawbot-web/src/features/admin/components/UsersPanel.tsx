import { Button, Card, StatusPill } from "@/shared/ui";
import { formatDateTime } from "../admin.types";
import type { AdminUser } from "../admin.types";
import { inputClass } from "../admin.types";

interface UsersPanelProps {
  readonly users: readonly AdminUser[];
  readonly search: string;
  readonly onSearchChange: (q: string) => void;
  readonly onCreateUser: () => void;
  readonly onEditUser: (user: AdminUser) => void;
  readonly onToggleActive: (id: string, active: boolean) => void;
  readonly onResetPassword: (id: string) => void;
  readonly isActivePending: boolean;
  readonly isResetPending: boolean;
}

export function UsersPanel({
  users,
  search,
  onSearchChange,
  onCreateUser,
  onEditUser,
  onToggleActive,
  onResetPassword,
  isActivePending,
  isResetPending,
}: UsersPanelProps) {
  return (
    <section className="space-y-gutter">
      <Card className="p-0">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-outline p-card-padding">
          <div>
            <h2 className="text-headline-sm text-secondary">Người dùng</h2>
            <p className="mt-1 text-body-md text-on-surface-variant">
              Danh sách tài khoản người dùng trong tenant hiện tại.
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <input
              className={inputClass}
              style={{ maxWidth: 260 }}
              placeholder="Tìm kiếm..."
              value={search}
              onChange={(e) => onSearchChange(e.target.value)}
            />
            <Button type="button" onClick={onCreateUser}>
              <span className="material-symbols-outlined text-[18px]">add</span>
              Thêm người dùng
            </Button>
          </div>
        </div>
        <div className="overflow-x-auto">
          <table className="min-w-[940px] w-full border-collapse text-left">
            <thead className="bg-surface-variant text-label-sm uppercase text-secondary">
              <tr>
                <th className="px-4 py-3 font-bold">Họ và tên</th>
                <th className="px-4 py-3 font-bold">Email</th>
                <th className="px-4 py-3 font-bold">Trạng thái</th>
                <th className="px-4 py-3 font-bold">Lần cuối</th>
                <th className="px-4 py-3 text-right font-bold">Hành động</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-outline bg-white">
              {users.map((user) => (
                <tr key={user.id} className="hover:bg-surface-container-low">
                  <td className="px-4 py-4 font-semibold text-secondary">{user.displayName}</td>
                  <td className="px-4 py-4 text-body-md text-on-surface-variant">{user.email}</td>
                  <td className="px-4 py-4">
                    <StatusPill tone={user.isActive ? "success" : "error"}>
                      {user.isActive ? "Hoạt động" : "Đã khóa"}
                    </StatusPill>
                  </td>
                  <td className="px-4 py-4 text-body-md text-on-surface-variant">
                    {formatDateTime(user.lastLoginAt)}
                  </td>
                  <td className="px-4 py-4 text-right">
                    <div className="inline-flex items-center gap-1">
                      <Button
                        type="button"
                        size="sm"
                        variant="ghost"
                        onClick={() => onEditUser(user)}
                      >
                        <span className="material-symbols-outlined text-[18px]">edit</span>
                      </Button>
                      <Button
                        type="button"
                        size="sm"
                        variant="ghost"
                        onClick={() => onToggleActive(user.id, !user.isActive)}
                        disabled={isActivePending}
                      >
                        <span className="material-symbols-outlined text-[18px]">
                          {user.isActive ? "lock" : "lock_open"}
                        </span>
                      </Button>
                      <Button
                        type="button"
                        size="sm"
                        variant="ghost"
                        onClick={() => onResetPassword(user.id)}
                        disabled={isResetPending}
                      >
                        <span className="material-symbols-outlined text-[18px]">key</span>
                      </Button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {!users.length ? (
          <div className="p-card-padding">
            <p className="text-center text-body-md text-on-surface-variant">
              {search ? "Không tìm thấy người dùng phù hợp." : "Chưa có người dùng nào."}
            </p>
          </div>
        ) : null}
      </Card>
    </section>
  );
}