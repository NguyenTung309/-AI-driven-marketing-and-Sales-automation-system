import { Alert, Button, Modal, StatusPill } from "@/shared/ui";
import { adminFormErrorMessage, Field, inputClass, tempPasswordHint, tempPasswordPattern, type AdminUserFormState } from "./adminHelpers";
import type { AdminUser, Role } from "@/shared/api/admin";

export type UserModalMode = "create" | "edit" | null;

interface AdminUserModalProps {
  readonly mode: UserModalMode;
  readonly userForm: AdminUserFormState;
  readonly onChange: (patch: Partial<AdminUserFormState>) => void;
  readonly canManageUsers: boolean;
  readonly canManagePancakeToken: boolean;
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
  canManagePancakeToken,
  editingUser,
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
        {canManagePancakeToken ? (
          <div className="space-y-3 rounded-lg border border-outline bg-surface p-3">
            <div className="flex items-center justify-between gap-3">
              <p className="text-label-sm font-semibold text-secondary">Access token Pancake của nhân viên sale</p>
              {editingUser ? (
                <StatusPill tone={editingUser.hasPancakeAccessToken ? "success" : "warning"}>
                  {editingUser.hasPancakeAccessToken ? "Đã cấu hình" : "Chưa có"}
                </StatusPill>
              ) : null}
            </div>
            <input
              className={inputClass}
              type="password"
              value={userForm.pancakeAccessToken}
              onChange={(event) => onChange({ pancakeAccessToken: event.target.value, clearPancakeAccessToken: false })}
              placeholder={editingUser?.hasPancakeAccessToken ? "Đã lưu token, nhập để thay thế" : "Nhập access token Pancake"}
            />
            {editingUser?.hasPancakeAccessToken ? (
              <label className="inline-flex items-center gap-2 text-body-md text-secondary">
                <input
                  type="checkbox"
                  className="size-4 accent-primary"
                  checked={userForm.clearPancakeAccessToken}
                  onChange={(event) => onChange({ clearPancakeAccessToken: event.target.checked, pancakeAccessToken: "" })}
                />
                Xóa token hiện tại
              </label>
            ) : null}
            <p className="text-label-sm text-on-surface-variant">Token được mã hóa khi lưu; giao diện không hiển thị lại token đã lưu.</p>
          </div>
        ) : null}
      </form>
    </Modal>
  );
}
