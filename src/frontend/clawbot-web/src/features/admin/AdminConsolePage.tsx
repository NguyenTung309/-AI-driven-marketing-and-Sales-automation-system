// Stitch design system branding defaults: primaryColor: "#d32f2f"
import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useAuthStore } from "@/shared/auth/authStore";
import { AppShell } from "@/shared/layout/AppShell";
import { Alert, Button, ConfirmDialog, StatusPill, type StatusTone } from "@/shared/ui";
import {
  createAdminUser,
  createApiKey,
  createRole,
  deletePancakeConfig,
  deleteRole,
  getPancakeConfig,
  getPancakeWebhookUrl,
  getTenantBranding,
  listAdminUsers,
  listApiKeys,
  listAuditLogs,
  listPermissions,
  listRolePermissions,
  listRoles,
  resetAdminUserPassword,
  revokeApiKey,
  setAdminUserActive,
  setRolePermissions,
  updateAdminUser,
  updatePancakeConfig,
  updateTenantBranding,
  updateRole,
  type AdminUser,
  type ApiKeyItem,
  type AuditLog,
  type CreatedApiKey,
  type Permission,
  type Role,
} from "@/shared/api/admin";
import { AdminAuditTab } from "./AdminAuditTab";
import { AdminIntegrationsTab } from "./AdminIntegrationsTab";
import { AdminKeyModal } from "./AdminKeyModal";
import { AdminKeysTab } from "./AdminKeysTab";
import { AdminRoleModal, type RoleModalMode } from "./AdminRoleModal";
import { AdminRolesTab } from "./AdminRolesTab";
import { AdminUserModal, type UserModalMode } from "./AdminUserModal";
import { AdminUsersTab } from "./AdminUsersTab";
import {
  confirmCopy,
  DEFAULT_BRANDING_FORM,
  DEFAULT_PANCAKE_FORM,
  errorMessage,
  MetricTile,
  parseScopes,
  TabButton,
  type AdminKeyFormState,
  type AdminRoleFormState,
  type AdminUserFormState,
  type ConfirmTarget,
} from "./adminHelpers";

type AdminTab = "users" | "roles" | "keys" | "integrations" | "audit";

const EMPTY_USERS: readonly AdminUser[] = [];
const EMPTY_ROLES: readonly Role[] = [];
const EMPTY_PERMISSIONS: readonly Permission[] = [];
const EMPTY_KEYS: readonly ApiKeyItem[] = [];
const EMPTY_AUDIT_LOGS: readonly AuditLog[] = [];

export default function AdminConsolePage() {
  const queryClient = useQueryClient();
  const authPermissions = useAuthStore((s) => s.permissions);
  const canManageUsers = authPermissions.includes("admin.system");
  const [tab, setTab] = useState<AdminTab>("users");
  const [search, setSearch] = useState("");
  const [notice, setNotice] = useState<string | null>(null);
  const [userModal, setUserModal] = useState<UserModalMode>(null);
  const [editingUser, setEditingUser] = useState<AdminUser | null>(null);
  const [userForm, setUserForm] = useState<AdminUserFormState>({
    displayName: "",
    email: "",
    password: "",
    isActive: true,
    roles: [],
  });
  const [roleModal, setRoleModal] = useState<RoleModalMode>(null);
  const [editingRole, setEditingRole] = useState<Role | null>(null);
  const [roleForm, setRoleForm] = useState<AdminRoleFormState>({ name: "", description: "" });
  const [selectedRoleId, setSelectedRoleId] = useState<string | null>(null);
  const [permissionDraft, setPermissionDraft] = useState<{ readonly roleId: string; readonly ids: readonly string[] } | null>(null);
  const [keyModalOpen, setKeyModalOpen] = useState(false);
  const [keyForm, setKeyForm] = useState<AdminKeyFormState>({ name: "", scopes: "admin.system", expiresAt: "" });
  const [createdKey, setCreatedKey] = useState<CreatedApiKey | null>(null);
  const [brandingDraft, setBrandingDraft] = useState<Partial<typeof DEFAULT_BRANDING_FORM>>({});
  const [pancakeDraft, setPancakeDraft] = useState<Partial<typeof DEFAULT_PANCAKE_FORM>>({});
  const [confirmTarget, setConfirmTarget] = useState<ConfirmTarget | null>(null);

  const usersQuery = useQuery({
    queryKey: ["admin", "users", search],
    queryFn: () => listAdminUsers({ q: search || undefined, page: 1, pageSize: 50 }),
  });
  const rolesQuery = useQuery({
    queryKey: ["admin", "roles"],
    queryFn: listRoles,
    enabled: canManageUsers,
  });
  const permissionsQuery = useQuery({
    queryKey: ["admin", "permissions"],
    queryFn: listPermissions,
    enabled: canManageUsers,
  });
  const apiKeysQuery = useQuery({
    queryKey: ["admin", "api-keys"],
    queryFn: listApiKeys,
    enabled: canManageUsers,
  });
  const brandingQuery = useQuery({
    queryKey: ["admin", "tenant-branding"],
    queryFn: getTenantBranding,
    enabled: tab === "integrations",
  });
  const pancakeQuery = useQuery({
    queryKey: ["admin", "pancake-config"],
    queryFn: getPancakeConfig,
    enabled: tab === "integrations",
  });
  const webhookQuery = useQuery({
    queryKey: ["admin", "pancake-webhook"],
    queryFn: getPancakeWebhookUrl,
    enabled: tab === "integrations",
  });
  const auditQuery = useQuery({
    queryKey: ["admin", "audit-logs"],
    queryFn: () => listAuditLogs({ page: 1, pageSize: 50 }),
    enabled: tab === "audit",
  });

  const users = usersQuery.data?.items ?? EMPTY_USERS;
  const roles = rolesQuery.data ?? EMPTY_ROLES;
  const permissions = permissionsQuery.data ?? EMPTY_PERMISSIONS;
  const apiKeys = apiKeysQuery.data ?? EMPTY_KEYS;
  const auditLogs = auditQuery.data?.items ?? EMPTY_AUDIT_LOGS;
  const effectiveSelectedRoleId = selectedRoleId ?? roles[0]?.id ?? null;
  const rolePermissionsQuery = useQuery({
    queryKey: ["admin", "role-permissions", effectiveSelectedRoleId],
    queryFn: () => listRolePermissions(effectiveSelectedRoleId!),
    enabled: canManageUsers && tab === "roles" && Boolean(effectiveSelectedRoleId),
  });
  const rolePermissionRows = Array.isArray(rolePermissionsQuery.data) ? rolePermissionsQuery.data : EMPTY_PERMISSIONS;
  const selectedRole = roles.find((role) => role.id === effectiveSelectedRoleId) ?? null;
  const activeUsers = users.filter((user) => user.isActive).length;
  const activeKeys = apiKeys.filter((key) => !key.revokedAt).length;
  const pancakeStatusKnown = pancakeQuery.isFetched;
  const pancakeStatusText = pancakeStatusKnown ? (pancakeQuery.data?.isActive ? "Kết nối" : "Chưa bật") : "Chưa kiểm tra";
  const pancakeStatusTone: StatusTone = pancakeStatusKnown ? (pancakeQuery.data?.isActive ? "success" : "warning") : "neutral";
  const currentError =
    usersQuery.error ??
    rolesQuery.error ??
    permissionsQuery.error ??
    apiKeysQuery.error ??
    brandingQuery.error ??
    pancakeQuery.error ??
    webhookQuery.error ??
    auditQuery.error;

  const permissionsByGroup = useMemo(() => {
    const groups = new Map<string, Permission[]>();
    permissions.forEach((permission) => {
      const group = permission.code.includes(".") ? permission.code.split(".")[0] : "system";
      groups.set(group, [...(groups.get(group) ?? []), permission]);
    });
    return [...groups.entries()].sort(([a], [b]) => a.localeCompare(b));
  }, [permissions]);

  const checkedPermissionIds = useMemo(() => {
    if (!effectiveSelectedRoleId) return [];
    if (permissionDraft?.roleId === effectiveSelectedRoleId) return [...permissionDraft.ids];
    return rolePermissionRows.map((permission) => permission.id);
  }, [effectiveSelectedRoleId, permissionDraft, rolePermissionRows]);

  const pancakeBaseForm = useMemo(() => {
    const config = pancakeQuery.data;
    return {
      ...DEFAULT_PANCAKE_FORM,
      ...(config
        ? {
            baseUrl: config.baseUrl,
            signatureHeader: config.signatureHeader,
            signatureAlgo: config.signatureAlgo,
            signatureEncoding: config.signatureEncoding,
            sendPathTemplate: config.sendPathTemplate,
            authMode: config.authMode,
            isActive: config.isActive,
          }
        : {}),
      accessToken: "",
      webhookSecret: "",
    };
  }, [pancakeQuery.data]);
  const pancakeForm = useMemo(() => ({ ...pancakeBaseForm, ...pancakeDraft }), [pancakeBaseForm, pancakeDraft]);
  const brandingBaseForm = useMemo(() => {
    const branding = brandingQuery.data;
    return {
      ...DEFAULT_BRANDING_FORM,
      ...(branding
        ? {
            brandName: branding.brandName,
            logoUrl: branding.logoUrl ?? "",
            primaryColor: branding.primaryColor,
            accentColor: branding.accentColor,
            supportName: branding.supportName,
            widgetGreeting: branding.widgetGreeting,
          }
        : {}),
    };
  }, [brandingQuery.data]);
  const brandingForm = useMemo(() => ({ ...brandingBaseForm, ...brandingDraft }), [brandingBaseForm, brandingDraft]);

  const invalidateAdmin = () => {
    void queryClient.invalidateQueries({ queryKey: ["admin"] });
  };

  const userMutation = useMutation({
    mutationFn: async () => {
      if (userModal === "create") {
        return createAdminUser({
          email: userForm.email.trim(),
          displayName: userForm.displayName.trim(),
          password: userForm.password,
          roles: userForm.roles,
        });
      }
      if (!editingUser) return undefined;
      await updateAdminUser(editingUser.id, {
        ...(canManageUsers ? { displayName: userForm.displayName.trim(), isActive: userForm.isActive } : {}),
      });
      return undefined;
    },
    onSuccess: () => {
      setUserModal(null);
      setEditingUser(null);
      setNotice(userModal === "create" ? "Đã tạo người dùng mới." : "Đã cập nhật người dùng.");
      invalidateAdmin();
    },
  });

  const activeMutation = useMutation({
    mutationFn: ({ id, active }: { readonly id: string; readonly active: boolean }) => setAdminUserActive(id, active),
    onSuccess: (_, variables) => {
      setNotice(variables.active ? "Đã kích hoạt người dùng." : "Đã khóa người dùng.");
      invalidateAdmin();
    },
  });

  const resetPasswordMutation = useMutation({
    mutationFn: resetAdminUserPassword,
    onSuccess: () => {
      setNotice("Đã phát hành mã đặt lại mật khẩu và gửi email nếu dịch vụ email đã cấu hình.");
    },
  });

  const roleMutation = useMutation({
    mutationFn: () => {
      const body = { name: roleForm.name.trim(), description: roleForm.description.trim() || null };
      return roleModal === "edit" && editingRole ? updateRole(editingRole.id, body) : createRole(body);
    },
    onSuccess: () => {
      setRoleModal(null);
      setEditingRole(null);
      setNotice(roleModal === "edit" ? "Đã cập nhật vai trò." : "Đã thêm vai trò.");
      invalidateAdmin();
    },
  });

  const deleteRoleMutation = useMutation({
    mutationFn: deleteRole,
    onSuccess: () => {
      setNotice("Đã xóa vai trò.");
      setSelectedRoleId(null);
      invalidateAdmin();
    },
  });

  const permissionsMutation = useMutation({
    mutationFn: () => setRolePermissions(effectiveSelectedRoleId!, checkedPermissionIds),
    onSuccess: () => {
      setNotice("Đã lưu ma trận phân quyền.");
      invalidateAdmin();
    },
  });

  const keyMutation = useMutation({
    mutationFn: () =>
      createApiKey({
        name: keyForm.name.trim(),
        scopes: parseScopes(keyForm.scopes),
        expiresAt: keyForm.expiresAt ? `${keyForm.expiresAt}T23:59:59+07:00` : null,
      }),
    onSuccess: (key) => {
      setCreatedKey(key);
      setKeyModalOpen(false);
      setKeyForm({ name: "", scopes: "admin.system", expiresAt: "" });
      invalidateAdmin();
    },
  });

  const revokeKeyMutation = useMutation({
    mutationFn: revokeApiKey,
    onSuccess: () => {
      setNotice("Đã thu hồi khóa tích hợp.");
      invalidateAdmin();
    },
  });

  const brandingMutation = useMutation({
    mutationFn: () =>
      updateTenantBranding({
        brandName: brandingForm.brandName.trim() || null,
        logoUrl: brandingForm.logoUrl.trim() || null,
        primaryColor: brandingForm.primaryColor.trim(),
        accentColor: brandingForm.accentColor.trim(),
        supportName: brandingForm.supportName.trim() || null,
        widgetGreeting: brandingForm.widgetGreeting.trim() || null,
      }),
    onSuccess: () => {
      setNotice("Đã lưu thương hiệu đơn vị.");
      setBrandingDraft({});
      invalidateAdmin();
    },
  });

  const pancakeMutation = useMutation({
    mutationFn: () =>
      updatePancakeConfig({
        baseUrl: pancakeForm.baseUrl.trim(),
        signatureHeader: pancakeForm.signatureHeader.trim(),
        signatureAlgo: pancakeForm.signatureAlgo.trim(),
        signatureEncoding: pancakeForm.signatureEncoding.trim(),
        sendPathTemplate: pancakeForm.sendPathTemplate.trim(),
        authMode: pancakeForm.authMode.trim(),
        isActive: pancakeForm.isActive,
        ...(pancakeForm.accessToken.trim() ? { accessToken: pancakeForm.accessToken.trim() } : {}),
        ...(pancakeForm.webhookSecret.trim() ? { webhookSecret: pancakeForm.webhookSecret.trim() } : {}),
      }),
    onSuccess: () => {
      setNotice("Đã lưu cấu hình Pancake.");
      setPancakeDraft({});
      invalidateAdmin();
    },
  });

  const deletePancakeMutation = useMutation({
    mutationFn: deletePancakeConfig,
    onSuccess: () => {
      setNotice("Đã ngắt cấu hình Pancake.");
      setPancakeDraft({});
      invalidateAdmin();
    },
  });

  function handleConfirm() {
    if (!confirmTarget) return;
    switch (confirmTarget.kind) {
      case "resetPassword":
        resetPasswordMutation.mutate(confirmTarget.id, { onSuccess: () => setConfirmTarget(null) });
        break;
      case "deleteRole":
        deleteRoleMutation.mutate(confirmTarget.id, { onSuccess: () => setConfirmTarget(null) });
        break;
      case "revokeKey":
        revokeKeyMutation.mutate(confirmTarget.id, { onSuccess: () => setConfirmTarget(null) });
        break;
      case "deletePancake":
        deletePancakeMutation.mutate(undefined, { onSuccess: () => setConfirmTarget(null) });
        break;
    }
  }

  const confirmPending =
    confirmTarget?.kind === "resetPassword"
      ? resetPasswordMutation.isPending
      : confirmTarget?.kind === "deleteRole"
        ? deleteRoleMutation.isPending
        : confirmTarget?.kind === "revokeKey"
          ? revokeKeyMutation.isPending
          : confirmTarget?.kind === "deletePancake"
            ? deletePancakeMutation.isPending
            : false;

  function openCreateUser() {
    setEditingUser(null);
    setUserForm({
      displayName: "",
      email: "",
      password: "",
      isActive: true,
      roles: [],
    });
    setUserModal("create");
  }

  function openEditUser(user: AdminUser) {
    setEditingUser(user);
    setUserForm({
      displayName: user.displayName,
      email: user.email,
      password: "",
      isActive: user.isActive,
      roles: [],
    });
    setUserModal("edit");
  }

  function openCreateRole() {
    setEditingRole(null);
    setRoleForm({ name: "", description: "" });
    setRoleModal("create");
  }

  function openEditRole(role: Role) {
    setEditingRole(role);
    setRoleForm({ name: role.name, description: role.description ?? "" });
    setRoleModal("edit");
  }

  function toggleRoleName(name: string) {
    setUserForm((current) => ({
      ...current,
      roles: current.roles.includes(name) ? current.roles.filter((role) => role !== name) : [...current.roles, name],
    }));
  }

  function togglePermission(id: string) {
    if (!effectiveSelectedRoleId) return;
    setPermissionDraft((current) => {
      const currentIds = current?.roleId === effectiveSelectedRoleId ? current.ids : checkedPermissionIds;
      const ids = currentIds.includes(id) ? currentIds.filter((item) => item !== id) : [...currentIds, id];
      return { roleId: effectiveSelectedRoleId, ids };
    });
  }

  const actionPending =
    userMutation.isPending ||
    activeMutation.isPending ||
    resetPasswordMutation.isPending ||
    roleMutation.isPending ||
    deleteRoleMutation.isPending ||
    permissionsMutation.isPending ||
    keyMutation.isPending ||
    revokeKeyMutation.isPending ||
    brandingMutation.isPending ||
    pancakeMutation.isPending ||
    deletePancakeMutation.isPending;

  return (
    <AppShell title="Hệ thống">
      <section className="mb-gutter rounded-lg border border-primary/20 bg-primary/5 p-4">
        <div className="flex flex-col gap-3 xl:flex-row xl:items-center xl:justify-between">
          <div>
            <h1 className="text-headline-md text-secondary">Hệ thống & phân quyền</h1>
            <p className="mt-1 text-body-md text-on-surface-variant">
              Quản trị người dùng, vai trò, khóa tích hợp và kết nối Pancake cho đơn vị hiện tại.
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <StatusPill tone={currentError ? "error" : "success"}>{currentError ? "Mất kết nối" : "Đã kết nối"}</StatusPill>
            <Button type="button" variant="outline" onClick={() => setTab("audit")}>
              <span aria-hidden="true" className="material-symbols-outlined text-[18px]">history</span>
              Nhật ký
            </Button>
          </div>
        </div>
      </section>

      {notice ? (
        <div className="mb-gutter">
          <Alert tone="success">{notice}</Alert>
        </div>
      ) : null}
      {currentError ? (
        <div className="mb-gutter">
          <Alert tone="error">{errorMessage(currentError)}</Alert>
        </div>
      ) : null}

      <section className="mb-gutter grid grid-cols-1 gap-gutter sm:grid-cols-2 xl:grid-cols-4">
        <MetricTile icon="group" label="Người dùng hoạt động" value={`${activeUsers}/${users.length}`} tone="success" />
        <MetricTile icon="admin_panel_settings" label="Vai trò" value={`${roles.length}`} tone="neutral" />
        <MetricTile icon="vpn_key" label="Khóa tích hợp hoạt động" value={`${activeKeys}`} tone={activeKeys ? "success" : "warning"} />
        <MetricTile icon="hub" label="Pancake" value={pancakeStatusText} tone={pancakeStatusTone} />
      </section>

      <div className="mb-gutter flex flex-wrap border-b border-outline">
        <TabButton active={tab === "users"} icon="group" label="Người dùng" onClick={() => setTab("users")} />
        {canManageUsers ? (
          <>
            <TabButton active={tab === "roles"} icon="admin_panel_settings" label="Phân quyền" onClick={() => setTab("roles")} />
            <TabButton active={tab === "keys"} icon="vpn_key" label="Khóa tích hợp" onClick={() => setTab("keys")} />
            <TabButton active={tab === "integrations"} icon="hub" label="Tích hợp" onClick={() => setTab("integrations")} />
            <TabButton active={tab === "audit"} icon="receipt_long" label="Nhật ký quản trị" onClick={() => setTab("audit")} />
          </>
        ) : null}
      </div>

      {tab === "users" ? (
        <AdminUsersTab
          users={users}
          search={search}
          onSearchChange={setSearch}
          canManageUsers={canManageUsers}
          onCreateUser={openCreateUser}
          onEditUser={openEditUser}
          onToggleActive={(user) => activeMutation.mutate({ id: user.id, active: !user.isActive })}
          activeMutationPending={activeMutation.isPending}
          onResetPassword={(user) => setConfirmTarget({ kind: "resetPassword", id: user.id, label: user.displayName })}
          resetPasswordPending={resetPasswordMutation.isPending}
        />
      ) : null}

      {tab === "roles" ? (
        <AdminRolesTab
          roles={roles}
          effectiveSelectedRoleId={effectiveSelectedRoleId}
          selectedRole={selectedRole}
          onSelectRole={setSelectedRoleId}
          onCreateRole={openCreateRole}
          onEditRole={openEditRole}
          onDeleteRole={(role) => setConfirmTarget({ kind: "deleteRole", id: role.id, label: role.name })}
          deleteRolePending={deleteRoleMutation.isPending}
          permissionsByGroup={permissionsByGroup}
          checkedPermissionIds={checkedPermissionIds}
          rolePermissionsFetching={rolePermissionsQuery.isFetching}
          onTogglePermission={togglePermission}
          onSavePermissions={() => permissionsMutation.mutate()}
          permissionsMutationPending={permissionsMutation.isPending}
        />
      ) : null}

      {tab === "keys" ? (
        <AdminKeysTab
          apiKeys={apiKeys}
          createdKey={createdKey}
          onOpenCreateKey={() => setKeyModalOpen(true)}
          onRevokeKey={(key) => setConfirmTarget({ kind: "revokeKey", id: key.id, label: key.name })}
          revokeKeyPending={revokeKeyMutation.isPending}
        />
      ) : null}

      {tab === "integrations" ? (
        <AdminIntegrationsTab
          brandingForm={brandingForm}
          onUpdateBrandingForm={(patch) => setBrandingDraft((current) => ({ ...current, ...patch }))}
          brandingMutationError={brandingMutation.error}
          brandingMutationPending={brandingMutation.isPending}
          brandingFetching={brandingQuery.isFetching}
          onSubmitBranding={() => brandingMutation.mutate()}
          pancakeForm={pancakeForm}
          onUpdatePancakeForm={(patch) => setPancakeDraft((current) => ({ ...current, ...patch }))}
          pancakeData={pancakeQuery.data}
          pancakeMutationPending={pancakeMutation.isPending}
          onSubmitPancake={() => pancakeMutation.mutate()}
          onDeletePancake={() => setConfirmTarget({ kind: "deletePancake" })}
          deletePancakePending={deletePancakeMutation.isPending}
          webhookData={webhookQuery.data}
          onCopyWebhook={() => {
            if (webhookQuery.data?.webhookUrl) void navigator.clipboard?.writeText(webhookQuery.data.webhookUrl);
            setNotice("Đã sao chép mã kết nối.");
          }}
        />
      ) : null}

      {tab === "audit" ? <AdminAuditTab auditLogs={auditLogs} /> : null}

      <AdminUserModal
        mode={userModal}
        userForm={userForm}
        onChange={(patch) => setUserForm({ ...userForm, ...patch })}
        canManageUsers={canManageUsers}
        editingUser={editingUser}
        roles={roles}
        onToggleRoleName={toggleRoleName}
        pending={userMutation.isPending}
        error={userMutation.error}
        onClose={() => setUserModal(null)}
        onSubmit={() => userMutation.mutate()}
      />

      <AdminRoleModal
        mode={roleModal}
        roleForm={roleForm}
        onChange={(patch) => setRoleForm({ ...roleForm, ...patch })}
        pending={roleMutation.isPending}
        error={roleMutation.error}
        onClose={() => setRoleModal(null)}
        onSubmit={() => roleMutation.mutate()}
      />

      <AdminKeyModal
        open={keyModalOpen}
        keyForm={keyForm}
        onChange={(patch) => setKeyForm({ ...keyForm, ...patch })}
        pending={keyMutation.isPending}
        error={keyMutation.error}
        onClose={() => setKeyModalOpen(false)}
        onSubmit={() => keyMutation.mutate()}
      />

      <ConfirmDialog
        open={confirmTarget !== null}
        title={confirmTarget ? confirmCopy(confirmTarget).title : ""}
        message={confirmTarget ? confirmCopy(confirmTarget).message : ""}
        confirmLabel={confirmTarget ? confirmCopy(confirmTarget).confirmLabel : undefined}
        pending={confirmPending}
        onConfirm={handleConfirm}
        onCancel={() => setConfirmTarget(null)}
      />

      {actionPending ? (
        <div className="fixed bottom-4 right-4 z-50 rounded bg-secondary px-4 py-2 text-body-md text-white shadow-xl">Đang xử lý...</div>
      ) : null}
    </AppShell>
  );
}
