import { Button, Card, StatusPill } from "@/shared/ui";
import type { Permission, Role } from "../admin.types";
import { roleTone } from "../admin.types";
import { EmptyState } from "./EmptyState";

interface RolesPanelProps {
  readonly roles: readonly Role[];
  readonly permissionsByGroup: readonly [string, readonly Permission[]][];
  readonly selectedRoleId: string | null;
  readonly selectedRole: Role | null;
  readonly checkedPermissionIds: readonly string[];
  readonly rolePermissionsFetching: boolean;
  readonly onSelectRole: (id: string) => void;
  readonly onCreateRole: () => void;
  readonly onEditRole: (role: Role) => void;
  readonly onDeleteRole: (id: string) => void;
  readonly onTogglePermission: (id: string) => void;
  readonly onSavePermissions: () => void;
  readonly isPermissionsPending: boolean;
}

export function RolesPanel({
  roles,
  permissionsByGroup,
  selectedRoleId,
  selectedRole,
  checkedPermissionIds,
  rolePermissionsFetching,
  onSelectRole,
  onCreateRole,
  onEditRole,
  onDeleteRole,
  onTogglePermission,
  onSavePermissions,
  isPermissionsPending,
}: RolesPanelProps) {
  return (
    <section className="grid grid-cols-1 gap-gutter xl:grid-cols-[320px_minmax(0,1fr)]">
      <Card>
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-headline-sm text-secondary">Vai trò</h2>
          <Button type="button" size="sm" onClick={onCreateRole}>
            <span className="material-symbols-outlined text-[18px]">add</span>
          </Button>
        </div>
        <div className="space-y-1">
          {roles.map((role) => (
            <button
              key={role.id}
              type="button"
              onClick={() => onSelectRole(role.id)}
              className={`flex w-full items-center justify-between rounded px-3 py-2 text-left text-body-md transition-colors ${
                selectedRoleId === role.id
                  ? "bg-primary/10 text-primary"
                  : "text-secondary hover:bg-surface-variant"
              }`}
            >
              <div className="flex items-center gap-2">
                <StatusPill tone={roleTone(role)}>{role.isSystem ? "System" : "Custom"}</StatusPill>
                <span className="font-semibold">{role.name}</span>
              </div>
              <div className="flex items-center gap-1">
                <span
                  role="button"
                  tabIndex={0}
                  className="material-symbols-outlined cursor-pointer text-[18px] text-on-surface-variant hover:text-secondary"
                  onClick={(e) => {
                    e.stopPropagation();
                    onEditRole(role);
                  }}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") {
                      e.stopPropagation();
                      onEditRole(role);
                    }
                  }}
                >
                  edit
                </span>
                {!role.isSystem ? (
                  <span
                    role="button"
                    tabIndex={0}
                    className="material-symbols-outlined cursor-pointer text-[18px] text-on-surface-variant hover:text-error"
                    onClick={(e) => {
                      e.stopPropagation();
                      onDeleteRole(role.id);
                    }}
                    onKeyDown={(e) => {
                      if (e.key === "Enter" || e.key === " ") {
                        e.stopPropagation();
                        onDeleteRole(role.id);
                      }
                    }}
                  >
                    delete
                  </span>
                ) : null}
              </div>
            </button>
          ))}
        </div>
        {!roles.length ? (
          <div className="mt-3">
            <EmptyState>Chưa có vai trò nào.</EmptyState>
          </div>
        ) : null}
      </Card>

      <Card>
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-headline-sm text-secondary">
            Quyền
            {selectedRole ? (
              <span className="ml-2 text-body-md text-on-surface-variant"> {selectedRole.name}</span>
            ) : null}
          </h2>
        </div>
        {selectedRole ? (
          <div className="space-y-5">
            {permissionsByGroup.map(([group, groupPerms]) => (
              <div key={group}>
                <h3 className="mb-2 text-label-sm font-semibold uppercase text-secondary">{group}</h3>
                <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3">
                  {groupPerms.map((perm) => {
                    const checked = checkedPermissionIds.includes(perm.id);
                    return (
                      <label
                        key={perm.id}
                        className={`flex cursor-pointer items-start gap-2 rounded border px-3 py-2 text-body-md transition-colors ${
                          checked
                            ? "border-primary bg-primary/5 text-primary"
                            : "border-outline text-secondary hover:bg-surface-variant"
                        }`}
                      >
                        <input
                          type="checkbox"
                          className="mt-0.5 size-4 accent-primary"
                          checked={checked}
                          onChange={() => onTogglePermission(perm.id)}
                        />
                        <div>
                          <p className="font-medium">{perm.code}</p>
                          {perm.description ? (
                            <p className="text-body-sm text-on-surface-variant">{perm.description}</p>
                          ) : null}
                        </div>
                      </label>
                    );
                  })}
                </div>
              </div>
            ))}
            {!permissionsByGroup.length ? (
              <EmptyState>Chưa có quyền nào được định nghĩa.</EmptyState>
            ) : null}
          </div>
        ) : (
          <EmptyState>Chọn một vai trò để xem quyền.</EmptyState>
        )}
        <div className="mt-4 flex justify-end">
          <Button
            type="button"
            onClick={onSavePermissions}
            disabled={!selectedRole || rolePermissionsFetching || isPermissionsPending}
          >
            <span className="material-symbols-outlined text-[18px]">save</span>
            Lưu quyền
          </Button>
        </div>
      </Card>
    </section>
  );
}