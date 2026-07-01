import { Button, Card, StatusPill } from "@/shared/ui";
import { EmptyState, roleTone } from "./adminHelpers";
import type { Permission, Role } from "@/shared/api/admin";

interface AdminRolesTabProps {
  readonly roles: readonly Role[];
  readonly effectiveSelectedRoleId: string | null;
  readonly selectedRole: Role | null;
  readonly onSelectRole: (id: string) => void;
  readonly onCreateRole: () => void;
  readonly onEditRole: (role: Role) => void;
  readonly onDeleteRole: (role: Role) => void;
  readonly deleteRolePending: boolean;
  readonly permissionsByGroup: readonly (readonly [string, Permission[]])[];
  readonly checkedPermissionIds: readonly string[];
  readonly rolePermissionsFetching: boolean;
  readonly onTogglePermission: (id: string) => void;
  readonly onSavePermissions: () => void;
  readonly permissionsMutationPending: boolean;
}

export function AdminRolesTab({
  roles,
  effectiveSelectedRoleId,
  selectedRole,
  onSelectRole,
  onCreateRole,
  onEditRole,
  onDeleteRole,
  deleteRolePending,
  permissionsByGroup,
  checkedPermissionIds,
  rolePermissionsFetching,
  onTogglePermission,
  onSavePermissions,
  permissionsMutationPending,
}: AdminRolesTabProps) {
  return (
    <section className="grid grid-cols-1 gap-gutter xl:grid-cols-[minmax(0,1fr)_430px]">
      <Card className="p-0">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-outline p-card-padding">
          <div>
            <h2 className="text-headline-sm text-secondary">Quản lý phân quyền</h2>
            <p className="mt-1 text-body-md text-on-surface-variant">Vai trò và phạm vi quyền được gán cho từng nhóm nhân sự.</p>
          </div>
          <Button type="button" onClick={onCreateRole}>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">add</span>
            Thêm vai trò
          </Button>
        </div>
        <div className="overflow-x-auto">
          <table className="min-w-[720px] w-full border-collapse text-left">
            <thead className="bg-surface-variant text-label-sm uppercase text-secondary">
              <tr>
                <th className="px-4 py-3 font-bold">Tên vai trò</th>
                <th className="px-4 py-3 font-bold">Mô tả</th>
                <th className="px-4 py-3 font-bold">Loại</th>
                <th className="px-4 py-3 text-right font-bold">Hành động</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-outline bg-white">
              {roles.map((role) => (
                <tr
                  key={role.id}
                  className={`cursor-pointer hover:bg-surface-container-low ${effectiveSelectedRoleId === role.id ? "bg-primary/5" : ""}`}
                  onClick={() => onSelectRole(role.id)}
                >
                  <td className="px-4 py-4">
                    <button
                      type="button"
                      className="text-left font-semibold text-secondary hover:text-primary"
                      onClick={(event) => {
                        event.stopPropagation();
                        onSelectRole(role.id);
                      }}
                    >
                      {role.name}
                    </button>
                  </td>
                  <td className="px-4 py-4 text-body-md text-on-surface-variant">{role.description ?? "Chưa có mô tả"}</td>
                  <td className="px-4 py-4"><StatusPill tone={roleTone(role)}>{role.isSystem ? "Hệ thống" : "Tùy chỉnh"}</StatusPill></td>
                  <td className="px-4 py-4">
                    <div className="flex justify-end gap-2">
                      <Button type="button" size="sm" variant="ghost" onClick={(event) => { event.stopPropagation(); onEditRole(role); }} disabled={role.isSystem}>
                        <span aria-hidden="true" className="material-symbols-outlined text-[18px]">edit</span>
                      </Button>
                      <Button
                        type="button"
                        size="sm"
                        variant="ghost"
                        onClick={(event) => {
                          event.stopPropagation();
                          onDeleteRole(role);
                        }}
                        disabled={role.isSystem || deleteRolePending}
                      >
                        <span aria-hidden="true" className="material-symbols-outlined text-[18px]">delete</span>
                      </Button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {!roles.length ? <div className="p-card-padding"><EmptyState>Chưa có vai trò.</EmptyState></div> : null}
      </Card>

      <Card>
        <div className="flex items-start justify-between gap-3">
          <div>
            <h2 className="text-headline-sm text-secondary">Ma trận quyền</h2>
            <p className="mt-1 text-body-md text-on-surface-variant">{selectedRole ? selectedRole.name : "Chọn vai trò để chỉnh quyền."}</p>
          </div>
          <StatusPill tone={selectedRole ? "success" : "neutral"}>
            {rolePermissionsFetching ? "Đang tải" : `${checkedPermissionIds.length} quyền`}
          </StatusPill>
        </div>
        <div className="mt-4 max-h-[620px] space-y-4 overflow-y-auto pr-1">
          {selectedRole ? (
            permissionsByGroup.map(([group, groupPermissions]) => (
              <div key={group} className="rounded-lg border border-outline bg-surface p-3">
                <p className="mb-3 text-label-caps uppercase text-secondary">{group}</p>
                <div className="space-y-2">
                  {groupPermissions.map((permission) => (
                    <label key={permission.id} className="flex items-start gap-2 text-body-md">
                      <input
                        type="checkbox"
                        className="mt-1 size-4 accent-primary"
                        checked={checkedPermissionIds.includes(permission.id)}
                        disabled={rolePermissionsFetching}
                        onChange={() => onTogglePermission(permission.id)}
                      />
                      <span>
                        <span className="block font-semibold text-secondary">{permission.code}</span>
                        <span className="block text-label-sm text-on-surface-variant">{permission.description ?? "Không có mô tả"}</span>
                      </span>
                    </label>
                  ))}
                </div>
              </div>
            ))
          ) : (
            <EmptyState>Chọn một vai trò để xem quyền.</EmptyState>
          )}
        </div>
        <div className="mt-4 flex justify-end">
          <Button type="button" onClick={onSavePermissions} disabled={!selectedRole || rolePermissionsFetching || permissionsMutationPending}>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">save</span>
            Lưu quyền
          </Button>
        </div>
      </Card>
    </section>
  );
}
