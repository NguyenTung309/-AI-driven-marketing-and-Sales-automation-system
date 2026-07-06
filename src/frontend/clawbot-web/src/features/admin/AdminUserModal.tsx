import { Alert, Button, Modal } from "@/shared/ui";
import { adminFormErrorMessage, Field, inputClass, tempPasswordHint, tempPasswordPattern, type AdminUserFormState } from "./adminHelpers";
import type { AdminUser, Role } from "@/shared/api/admin";

export type UserModalMode = "create" | "edit" | null;

interface AdminUserModalProps {
  readonly mode: UserModalMode;
  readonly userForm: AdminUserFormState;
  readonly onChange: (patch: Partial<AdminUserFormState>) => void;
  readonly canManageUsers: boolean;
  readonly editingUser: AdminUser | null;
  readonly roles: readonly Role[];
  readonly onToggleRoleName: (name: string) => void;
  readonly pending: boolean;
  readonly error: unknown;
  readonly onClose: () => void;
  readonly onSubmit: () => void;
}

export function AdminUserModal({
  mode,
  userForm,
  onChange,
  canManageUsers,
  roles,
  onToggleRoleName,
  pending,
  error,
  onClose,
  onSubmit,
}: AdminUserModalProps) {
  return (
    <Modal
      open={mode !== null}
      onClose={onClose}
      title={mode === "edit" ? "Cập nhật người dùng" : "Thêm người dùng"}
      footer={
        <>
          <Button type="button" variant="ghost" onClick={onClose} disabled={pending}>Hủy</Button>
          <Button type="submit" form="admin-user-form" disabled={pending}>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">save</span>
            Lưu
          </Button>
        </>
      }
    >
      {error ? <Alert tone="error">{adminFormErrorMessage(error)}</Alert> : null}
      <form
        id="admin-user-form"
        className="space-y-4"
        onSubmit={(event) => {
          event.preventDefault();
          onSubmit();
        }}
      >
        <Field label="Tên hiển thị">
          <input
            className={inputClass}
            required={canManageUsers}
            disabled={!canManageUsers}
            value={userForm.displayName}
            onChange={(event) => onChange({ displayName: event.target.value })}
          />
        </Field>
        {mode === "create" ? (
          <>
            <Field label="Email">
              <input className={inputClass} required type="email" value={userForm.email} onChange={(event) => onChange({ email: event.target.value })} />
            </Field>
            <Field label="Mật khẩu tạm">
              <input
                className={inputClass}
                required
                type="password"
                minLength={8}
                pattern={tempPasswordPattern}
                title={tempPasswordHint}
                value={userForm.password}
                onChange={(event) => onChange({ password: event.target.value })}
              />
              <p className="mt-1 text-label-sm text-on-surface-variant">{tempPasswordHint}</p>
            </Field>
            <div>
              <p className="mb-2 text-label-sm font-semibold text-secondary">Vai trò ban đầu</p>
              <div className="flex flex-wrap gap-2">
                {roles.map((role) => (
                  <label key={role.id} className="inline-flex items-center gap-2 rounded border border-outline px-3 py-2 text-body-md">
                    <input type="checkbox" className="size-4 accent-primary" checked={userForm.roles.includes(role.name)} onChange={() => onToggleRoleName(role.name)} />
                    {role.name}
                  </label>
                ))}
              </div>
            </div>
          </>
        ) : canManageUsers ? (
          <label className="inline-flex items-center gap-2 text-body-md font-semibold text-secondary">
            <input type="checkbox" className="size-4 accent-primary" checked={userForm.isActive} onChange={(event) => onChange({ isActive: event.target.checked })} />
            Người dùng đang hoạt động
          </label>
        ) : null}
        {/* Token Pancake cau hinh theo kenh (trang "Kenh giao tiep"), khong theo tung sale */}
      </form>
    </Modal>
  );
}
