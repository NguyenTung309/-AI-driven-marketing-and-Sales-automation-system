import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { isAxiosError } from "axios";
import { AppShell } from "@/shared/layout/AppShell";
import { Alert, Button, Card, Modal, StatusPill, type StatusTone } from "@/shared/ui";
import { toUserFriendlyError } from "@/shared/utils/userText";
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

type AdminTab = "users" | "roles" | "keys" | "integrations" | "audit";
type UserModalMode = "create" | "edit" | null;
type RoleModalMode = "create" | "edit" | null;

const EMPTY_USERS: readonly AdminUser[] = [];
const EMPTY_ROLES: readonly Role[] = [];
const EMPTY_PERMISSIONS: readonly Permission[] = [];
const EMPTY_KEYS: readonly ApiKeyItem[] = [];
const EMPTY_AUDIT_LOGS: readonly AuditLog[] = [];

const DEFAULT_PANCAKE_FORM = {
  baseUrl: "https://pancake.vn",
  accessToken: "",
  webhookSecret: "",
  signatureHeader: "X-Pancake-Signature",
  signatureAlgo: "HMACSHA256",
  signatureEncoding: "hex",
  sendPathTemplate: "",
  authMode: "bearer",
  isActive: false,
};

const DEFAULT_BRANDING_FORM = {
  brandName: "",
  logoUrl: "",
  primaryColor: "#d32f2f",
  accentColor: "#f59e0b",
  supportName: "",
  widgetGreeting: "",
};

function formatDateTime(value: string | null | undefined): string {
  if (!value) return "—";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("vi-VN", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}

function formatDate(value: string | null | undefined): string {
  if (!value) return "Không giới hạn";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("vi-VN", { day: "2-digit", month: "2-digit", year: "numeric" }).format(date);
}

function errorMessage(error: unknown): string {
  return toUserFriendlyError(error, "Không xử lý được yêu cầu quản trị. Vui lòng thử lại.");
}

function adminFormErrorMessage(error: unknown): string {
  const data = isAxiosError(error) ? error.response?.data : null;
  if (Array.isArray(data) && data.every((item) => typeof item === "string")) return data.join("\n");
  return errorMessage(error);
}

function parseScopes(value: string): readonly string[] {
  return value
    .split(/[\n,]/)
    .map((item) => item.trim())
    .filter(Boolean);
}

function roleTone(role: Role): StatusTone {
  return role.isSystem ? "warning" : "neutral";
}

function keyTone(key: ApiKeyItem): StatusTone {
  if (key.revokedAt) return "error";
  if (key.expiresAt && new Date(key.expiresAt).getTime() < Date.now()) return "warning";
  return "success";
}

function keyStatus(key: ApiKeyItem): string {
  if (key.revokedAt) return "Đã thu hồi";
  if (key.expiresAt && new Date(key.expiresAt).getTime() < Date.now()) return "Hết hạn";
  return "Đang hoạt động";
}

function MetricTile({
  icon,
  label,
  value,
  tone = "neutral",
}: {
  readonly icon: string;
  readonly label: string;
  readonly value: string;
  readonly tone?: StatusTone;
}) {
  return (
    <Card>
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-label-caps uppercase text-on-surface-variant">{label}</p>
          <p className="mt-2 text-telemetry-data text-secondary">{value}</p>
        </div>
        <span aria-hidden="true" className="material-symbols-outlined rounded bg-primary/10 p-2 text-primary">{icon}</span>
      </div>
      <div className="mt-3">
        <StatusPill tone={tone}>Quản trị</StatusPill>
      </div>
    </Card>
  );
}

function EmptyState({ children }: { readonly children: string }) {
  return (
    <div className="rounded-lg border border-dashed border-outline bg-surface p-6 text-center text-body-md text-on-surface-variant">
      {children}
    </div>
  );
}

function TabButton({
  active,
  icon,
  label,
  onClick,
}: {
  readonly active: boolean;
  readonly icon: string;
  readonly label: string;
  readonly onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`inline-flex items-center gap-2 border-b-2 px-4 py-3 text-label-caps uppercase ${
        active ? "border-primary text-primary" : "border-transparent text-on-surface-variant hover:text-secondary"
      }`}
    >
      <span aria-hidden="true" className="material-symbols-outlined text-[18px]">{icon}</span>
      {label}
    </button>
  );
}

function Field({
  label,
  children,
}: {
  readonly label: string;
  readonly children: React.ReactNode;
}) {
  return (
    <label className="block">
      <span className="mb-1 block text-label-sm font-semibold text-secondary">{label}</span>
      {children}
    </label>
  );
}

const inputClass = "w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary";
const tempPasswordPattern = "(?=.*[a-z])(?=.*[A-Z])(?=.*\\d)(?=.*[^A-Za-z0-9]).{8,}";
const tempPasswordHint = "Ít nhất 8 ký tự, gồm chữ hoa, chữ thường, số và ký tự đặc biệt.";

export default function AdminConsolePage() {
  const queryClient = useQueryClient();
  const [tab, setTab] = useState<AdminTab>("users");
  const [search, setSearch] = useState("");
  const [notice, setNotice] = useState<string | null>(null);
  const [userModal, setUserModal] = useState<UserModalMode>(null);
  const [editingUser, setEditingUser] = useState<AdminUser | null>(null);
  const [userForm, setUserForm] = useState({
    displayName: "",
    email: "",
    password: "",
    isActive: true,
    roles: [] as string[],
  });
  const [roleModal, setRoleModal] = useState<RoleModalMode>(null);
  const [editingRole, setEditingRole] = useState<Role | null>(null);
  const [roleForm, setRoleForm] = useState({ name: "", description: "" });
  const [selectedRoleId, setSelectedRoleId] = useState<string | null>(null);
  const [permissionDraft, setPermissionDraft] = useState<{ readonly roleId: string; readonly ids: readonly string[] } | null>(null);
  const [keyModalOpen, setKeyModalOpen] = useState(false);
  const [keyForm, setKeyForm] = useState({ name: "", scopes: "admin.system", expiresAt: "" });
  const [createdKey, setCreatedKey] = useState<CreatedApiKey | null>(null);
  const [brandingDraft, setBrandingDraft] = useState<Partial<typeof DEFAULT_BRANDING_FORM>>({});
  const [pancakeDraft, setPancakeDraft] = useState<Partial<typeof DEFAULT_PANCAKE_FORM>>({});

  const usersQuery = useQuery({
    queryKey: ["admin", "users", search],
    queryFn: () => listAdminUsers({ q: search || undefined, page: 1, pageSize: 50 }),
  });
  const rolesQuery = useQuery({
    queryKey: ["admin", "roles"],
    queryFn: listRoles,
  });
  const permissionsQuery = useQuery({
    queryKey: ["admin", "permissions"],
    queryFn: listPermissions,
  });
  const apiKeysQuery = useQuery({
    queryKey: ["admin", "api-keys"],
    queryFn: listApiKeys,
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
    enabled: tab === "roles" && Boolean(effectiveSelectedRoleId),
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
        displayName: userForm.displayName.trim(),
        isActive: userForm.isActive,
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

  function openCreateUser() {
    setEditingUser(null);
    setUserForm({ displayName: "", email: "", password: "", isActive: true, roles: [] });
    setUserModal("create");
  }

  function openEditUser(user: AdminUser) {
    setEditingUser(user);
    setUserForm({ displayName: user.displayName, email: user.email, password: "", isActive: user.isActive, roles: [] });
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

  function updatePancakeForm(patch: Partial<typeof DEFAULT_PANCAKE_FORM>) {
    setPancakeDraft((current) => ({ ...current, ...patch }));
  }

  function updateBrandingForm(patch: Partial<typeof DEFAULT_BRANDING_FORM>) {
    setBrandingDraft((current) => ({ ...current, ...patch }));
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
        <TabButton active={tab === "roles"} icon="admin_panel_settings" label="Phân quyền" onClick={() => setTab("roles")} />
        <TabButton active={tab === "keys"} icon="vpn_key" label="Khóa tích hợp" onClick={() => setTab("keys")} />
        <TabButton active={tab === "integrations"} icon="hub" label="Tích hợp" onClick={() => setTab("integrations")} />
        <TabButton active={tab === "audit"} icon="receipt_long" label="Nhật ký quản trị" onClick={() => setTab("audit")} />
      </div>

      {tab === "users" ? (
        <section className="space-y-gutter">
          <Card className="p-0">
            <div className="flex flex-col gap-3 border-b border-outline p-card-padding lg:flex-row lg:items-center lg:justify-between">
              <div>
                <h2 className="text-headline-sm text-secondary">Quản lý người dùng</h2>
                <p className="mt-1 text-body-md text-on-surface-variant">Danh sách tài khoản có quyền truy cập hệ thống.</p>
              </div>
              <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
                <input className={inputClass} value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Tìm email hoặc tên..." />
                <Button type="button" className="shrink-0 whitespace-nowrap" onClick={openCreateUser}>
                  <span aria-hidden="true" className="material-symbols-outlined text-[18px]">person_add</span>
                  Thêm người dùng
                </Button>
              </div>
            </div>
            <div className="overflow-x-auto">
              <table className="min-w-[860px] w-full border-collapse text-left">
                <thead className="bg-surface-variant text-label-sm uppercase text-secondary">
                  <tr>
                    <th className="px-4 py-3 font-bold">Người dùng</th>
                    <th className="px-4 py-3 font-bold">Email</th>
                    <th className="px-4 py-3 font-bold">Đăng nhập cuối</th>
                    <th className="px-4 py-3 font-bold">Trạng thái</th>
                    <th className="px-4 py-3 text-right font-bold">Hành động</th>
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
                      <td className="px-4 py-4 text-body-md text-on-surface-variant">{formatDateTime(user.lastLoginAt)}</td>
                      <td className="px-4 py-4">
                        <StatusPill tone={user.isActive ? "success" : "error"}>{user.isActive ? "Hoạt động" : "Đã khóa"}</StatusPill>
                      </td>
                      <td className="px-4 py-4">
                        <div className="flex justify-end gap-2">
                          <Button type="button" size="sm" variant="ghost" onClick={() => openEditUser(user)} aria-label={`Sửa ${user.displayName}`}>
                            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">edit</span>
                          </Button>
                          <Button
                            type="button"
                            size="sm"
                            variant="ghost"
                            onClick={() => activeMutation.mutate({ id: user.id, active: !user.isActive })}
                            disabled={activeMutation.isPending}
                            aria-label={user.isActive ? `Khóa ${user.displayName}` : `Mở khóa ${user.displayName}`}
                          >
                            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">{user.isActive ? "lock" : "lock_open"}</span>
                          </Button>
                          <Button
                            type="button"
                            size="sm"
                            variant="ghost"
                            onClick={() => resetPasswordMutation.mutate(user.id)}
                            disabled={resetPasswordMutation.isPending}
                            aria-label={`Reset mật khẩu ${user.displayName}`}
                          >
                            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">restart_alt</span>
                          </Button>
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
      ) : null}

      {tab === "roles" ? (
        <section className="grid grid-cols-1 gap-gutter xl:grid-cols-[minmax(0,1fr)_430px]">
          <Card className="p-0">
            <div className="flex flex-wrap items-center justify-between gap-3 border-b border-outline p-card-padding">
              <div>
                <h2 className="text-headline-sm text-secondary">Quản lý phân quyền</h2>
                <p className="mt-1 text-body-md text-on-surface-variant">Vai trò và phạm vi quyền được gán cho từng nhóm nhân sự.</p>
              </div>
              <Button type="button" onClick={openCreateRole}>
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
                      onClick={() => setSelectedRoleId(role.id)}
                    >
                      <td className="px-4 py-4">
                        <button
                          type="button"
                          className="text-left font-semibold text-secondary hover:text-primary"
                          onClick={(event) => {
                            event.stopPropagation();
                            setSelectedRoleId(role.id);
                          }}
                        >
                          {role.name}
                        </button>
                      </td>
                      <td className="px-4 py-4 text-body-md text-on-surface-variant">{role.description ?? "Chưa có mô tả"}</td>
                      <td className="px-4 py-4"><StatusPill tone={roleTone(role)}>{role.isSystem ? "Hệ thống" : "Tùy chỉnh"}</StatusPill></td>
                      <td className="px-4 py-4">
                        <div className="flex justify-end gap-2">
                          <Button type="button" size="sm" variant="ghost" onClick={(event) => { event.stopPropagation(); openEditRole(role); }} disabled={role.isSystem}>
                            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">edit</span>
                          </Button>
                          <Button
                            type="button"
                            size="sm"
                            variant="ghost"
                            onClick={(event) => {
                              event.stopPropagation();
                              if (window.confirm(`Xóa vai trò ${role.name}?`)) deleteRoleMutation.mutate(role.id);
                            }}
                            disabled={role.isSystem || deleteRoleMutation.isPending}
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
                {rolePermissionsQuery.isFetching ? "Đang tải" : `${checkedPermissionIds.length} quyền`}
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
                            disabled={rolePermissionsQuery.isFetching}
                            onChange={() => togglePermission(permission.id)}
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
              <Button type="button" onClick={() => permissionsMutation.mutate()} disabled={!selectedRole || rolePermissionsQuery.isFetching || permissionsMutation.isPending}>
                <span aria-hidden="true" className="material-symbols-outlined text-[18px]">save</span>
                Lưu quyền
              </Button>
            </div>
          </Card>
        </section>
      ) : null}

      {tab === "keys" ? (
        <section className="space-y-gutter">
          <Card className="p-0">
            <div className="flex flex-wrap items-center justify-between gap-3 border-b border-outline p-card-padding">
              <div>
                <h2 className="text-headline-sm text-secondary">Khóa tích hợp</h2>
                <p className="mt-1 text-body-md text-on-surface-variant">Khóa tích hợp giữa các hệ thống. Mã bí mật chỉ hiển thị một lần khi phát hành.</p>
              </div>
              <Button type="button" onClick={() => setKeyModalOpen(true)}>
                <span aria-hidden="true" className="material-symbols-outlined text-[18px]">add</span>
                Phát hành khóa
              </Button>
            </div>
            {createdKey ? (
              <div className="border-b border-outline p-card-padding">
                <Alert tone="warning">
                  Khóa tích hợp mới: <span className="font-mono">{createdKey.plaintextKey}</span>
                </Alert>
              </div>
            ) : null}
            <div className="overflow-x-auto">
              <table className="min-w-[820px] w-full border-collapse text-left">
                <thead className="bg-surface-variant text-label-sm uppercase text-secondary">
                  <tr>
                    <th className="px-4 py-3 font-bold">Tên khóa</th>
                    <th className="px-4 py-3 font-bold">Quyền truy cập</th>
                    <th className="px-4 py-3 font-bold">Ngày tạo</th>
                    <th className="px-4 py-3 font-bold">Hết hạn</th>
                    <th className="px-4 py-3 font-bold">Trạng thái</th>
                    <th className="px-4 py-3 text-right font-bold">Hành động</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-outline bg-white">
                  {apiKeys.map((key) => (
                    <tr key={key.id} className="hover:bg-surface-container-low">
                      <td className="px-4 py-4 font-semibold text-secondary">{key.name}</td>
                      <td className="px-4 py-4">
                        <div className="flex max-w-[320px] flex-wrap gap-1">
                          {(key.scopes ?? []).length ? (
                            key.scopes?.map((scope) => <StatusPill key={scope} tone="neutral">Quyền tích hợp</StatusPill>)
                          ) : (
                            <span className="text-body-md text-on-surface-variant">Không giới hạn quyền</span>
                          )}
                        </div>
                      </td>
                      <td className="px-4 py-4 text-body-md text-on-surface-variant">{formatDateTime(key.createdAt)}</td>
                      <td className="px-4 py-4 text-body-md text-on-surface-variant">{formatDate(key.expiresAt)}</td>
                      <td className="px-4 py-4"><StatusPill tone={keyTone(key)}>{keyStatus(key)}</StatusPill></td>
                      <td className="px-4 py-4 text-right">
                        <Button
                          type="button"
                          size="sm"
                          variant="ghost"
                          onClick={() => revokeKeyMutation.mutate(key.id)}
                          disabled={Boolean(key.revokedAt) || revokeKeyMutation.isPending}
                        >
                          <span aria-hidden="true" className="material-symbols-outlined text-[18px]">block</span>
                          Thu hồi
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {!apiKeys.length ? <div className="p-card-padding"><EmptyState>Chưa phát hành khóa tích hợp.</EmptyState></div> : null}
          </Card>
        </section>
      ) : null}

      {tab === "integrations" ? (
        <section className="space-y-gutter">
          <Card>
            <div className="mb-5 flex flex-wrap items-start justify-between gap-3">
              <div>
                <h2 className="text-headline-sm text-secondary">Thương hiệu đơn vị</h2>
                <p className="mt-1 text-body-md text-on-surface-variant">Tên, logo và màu hiển thị trên trang hỗ trợ khách hàng.</p>
              </div>
              <div className="flex items-center gap-2 rounded border border-outline bg-surface px-3 py-2">
                <span className="size-5 rounded" style={{ backgroundColor: brandingForm.primaryColor }} />
                <span className="size-5 rounded" style={{ backgroundColor: brandingForm.accentColor }} />
              </div>
            </div>
            {brandingMutation.error ? <Alert tone="error">{errorMessage(brandingMutation.error)}</Alert> : null}
            <form
              className="mt-4 grid grid-cols-1 gap-4 lg:grid-cols-2"
              onSubmit={(event) => {
                event.preventDefault();
                brandingMutation.mutate();
              }}
            >
              <Field label="Tên thương hiệu">
                <input className={inputClass} value={brandingForm.brandName} onChange={(event) => updateBrandingForm({ brandName: event.target.value })} />
              </Field>
              <Field label="Tên hỗ trợ">
                <input className={inputClass} value={brandingForm.supportName} onChange={(event) => updateBrandingForm({ supportName: event.target.value })} />
              </Field>
              <Field label="Logo hiển thị">
                <input className={inputClass} value={brandingForm.logoUrl} onChange={(event) => updateBrandingForm({ logoUrl: event.target.value })} />
              </Field>
              <div className="grid grid-cols-2 gap-3">
                <Field label="Màu chính">
                  <input className={`${inputClass} h-11 p-1`} type="color" value={brandingForm.primaryColor} onChange={(event) => updateBrandingForm({ primaryColor: event.target.value })} />
                </Field>
                <Field label="Màu nhấn">
                  <input className={`${inputClass} h-11 p-1`} type="color" value={brandingForm.accentColor} onChange={(event) => updateBrandingForm({ accentColor: event.target.value })} />
                </Field>
              </div>
              <Field label="Lời chào khung chat">
                <textarea
                  className={`${inputClass} min-h-24`}
                  value={brandingForm.widgetGreeting}
                  onChange={(event) => updateBrandingForm({ widgetGreeting: event.target.value })}
                />
              </Field>
              <div className="flex items-end justify-end">
                <Button type="submit" disabled={brandingMutation.isPending || brandingQuery.isFetching}>
                  <span aria-hidden="true" className="material-symbols-outlined text-[18px]">palette</span>
                  Lưu thương hiệu
                </Button>
              </div>
            </form>
          </Card>

          <div className="grid grid-cols-1 gap-gutter xl:grid-cols-[minmax(0,1fr)_420px]">
          <Card>
            <div className="mb-5 flex flex-wrap items-center justify-between gap-3">
              <div>
                <h2 className="text-headline-sm text-secondary">Kênh Pancake</h2>
                <p className="mt-1 text-body-md text-on-surface-variant">Cấu hình gửi/nhận hội thoại qua Pancake cho đơn vị hiện tại.</p>
              </div>
              <StatusPill tone={pancakeQuery.data?.isActive ? "success" : "warning"}>
                {pancakeQuery.data?.isActive ? "Hoạt động" : "Chưa bật"}
              </StatusPill>
            </div>
            <form
              className="grid grid-cols-1 gap-4 lg:grid-cols-2"
              onSubmit={(event) => {
                event.preventDefault();
                pancakeMutation.mutate();
              }}
            >
              <Field label="Cổng Pancake">
                <input className={inputClass} value={pancakeForm.baseUrl} onChange={(event) => updatePancakeForm({ baseUrl: event.target.value })} />
              </Field>
              <Field label="Cách xác thực">
                <select className={inputClass} value={pancakeForm.authMode} onChange={(event) => updatePancakeForm({ authMode: event.target.value })}>
                  <option value="bearer">Mã truy cập</option>
                  <option value="header">Trường gửi kèm tùy chỉnh</option>
                </select>
              </Field>
              <Field label="Mã truy cập">
                <input
                  className={inputClass}
                  type="password"
                  value={pancakeForm.accessToken}
                  onChange={(event) => updatePancakeForm({ accessToken: event.target.value })}
                  placeholder={pancakeQuery.data?.hasAccessToken ? "Đã lưu mã, nhập để thay thế" : "Nhập mã truy cập"}
                />
              </Field>
              <Field label="Mã bí mật nhận sự kiện">
                <input
                  className={inputClass}
                  type="password"
                  value={pancakeForm.webhookSecret}
                  onChange={(event) => updatePancakeForm({ webhookSecret: event.target.value })}
                  placeholder={pancakeQuery.data?.hasWebhookSecret ? "Đã lưu mã bí mật, nhập để thay thế" : "Nhập mã bí mật nhận sự kiện"}
                />
              </Field>
              <Field label="Tên thông tin xác minh">
                <input className={inputClass} value={pancakeForm.signatureHeader} onChange={(event) => updatePancakeForm({ signatureHeader: event.target.value })} />
              </Field>
              <Field label="Kiểu xác minh">
                <input className={inputClass} value={pancakeForm.signatureAlgo} onChange={(event) => updatePancakeForm({ signatureAlgo: event.target.value })} />
              </Field>
              <Field label="Dạng mã xác minh">
                <select className={inputClass} value={pancakeForm.signatureEncoding} onChange={(event) => updatePancakeForm({ signatureEncoding: event.target.value })}>
                  <option value="hex">Dạng chuẩn</option>
                  <option value="base64">Dạng mã hóa</option>
                </select>
              </Field>
              <Field label="Mẫu gửi tin nhắn">
                <input className={inputClass} placeholder="Nhập mẫu gửi tin do Pancake cung cấp" value={pancakeForm.sendPathTemplate} onChange={(event) => updatePancakeForm({ sendPathTemplate: event.target.value })} />
              </Field>
              <label className="inline-flex items-center gap-2 text-body-md font-semibold text-secondary">
                <input
                  type="checkbox"
                  className="size-4 accent-primary"
                  checked={pancakeForm.isActive}
                  onChange={(event) => updatePancakeForm({ isActive: event.target.checked })}
                />
                Bật kết nối Pancake
              </label>
              <div className="flex flex-wrap justify-end gap-2 lg:col-span-2">
                <Button type="button" variant="outline" onClick={() => deletePancakeMutation.mutate()} disabled={!pancakeQuery.data || deletePancakeMutation.isPending}>
                  <span aria-hidden="true" className="material-symbols-outlined text-[18px]">link_off</span>
                  Ngắt kết nối
                </Button>
                <Button type="submit" disabled={pancakeMutation.isPending}>
                  <span aria-hidden="true" className="material-symbols-outlined text-[18px]">save</span>
                  Lưu cấu hình
                </Button>
              </div>
            </form>
          </Card>

          <Card>
            <h2 className="text-headline-sm text-secondary">Nhận tín hiệu từ Pancake</h2>
            <p className="mt-1 text-body-md text-on-surface-variant">Mã kết nối đã được tạo cho đơn vị hiện tại.</p>
            <div className="mt-4 rounded-lg border border-outline bg-surface p-3">
              <p className="text-label-caps uppercase text-on-surface-variant">Đơn vị</p>
              <p className="mt-1 font-mono text-mono-status text-secondary">{webhookQuery.data?.tenantSlug ?? "—"}</p>
            </div>
            <div className="mt-3 rounded-lg border border-outline bg-surface p-3">
              <p className="text-label-caps uppercase text-on-surface-variant">Mã kết nối</p>
              <p className="mt-1 text-body-md text-secondary">{webhookQuery.data?.webhookUrl ? "Sẵn sàng sao chép" : "Đang tải..."}</p>
            </div>
            <div className="mt-4">
              <Button
                type="button"
                variant="outline"
                onClick={() => {
                  if (webhookQuery.data?.webhookUrl) void navigator.clipboard?.writeText(webhookQuery.data.webhookUrl);
                  setNotice("Đã sao chép mã kết nối.");
                }}
                disabled={!webhookQuery.data?.webhookUrl}
              >
                <span aria-hidden="true" className="material-symbols-outlined text-[18px]">content_copy</span>
                Sao chép
              </Button>
            </div>
            <div className="mt-5 space-y-2 text-body-md text-on-surface-variant">
              <p>Mã truy cập và mã bí mật nhận sự kiện được mã hóa; giao diện quản trị chỉ gửi giá trị mới khi bạn nhập.</p>
              <p>Phần này chỉ quản lý kết nối Pancake cho đơn vị hiện tại.</p>
            </div>
          </Card>
          </div>
        </section>
      ) : null}

      {tab === "audit" ? (
        <section className="space-y-gutter">
          <Card className="p-0">
            <div className="border-b border-outline p-card-padding">
              <h2 className="text-headline-sm text-secondary">Nhật ký quản trị</h2>
              <p className="mt-1 text-body-md text-on-surface-variant">50 sự kiện quản trị gần nhất.</p>
            </div>
            <div className="overflow-x-auto">
              <table className="min-w-[900px] w-full border-collapse text-left">
                <thead className="bg-surface-variant text-label-sm uppercase text-secondary">
                  <tr>
                    <th className="px-4 py-3 font-bold">Thời điểm</th>
                    <th className="px-4 py-3 font-bold">Hành động</th>
                    <th className="px-4 py-3 font-bold">Đối tượng</th>
                    <th className="px-4 py-3 font-bold">IP</th>
                    <th className="px-4 py-3 font-bold">Thay đổi</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-outline bg-white">
                  {auditLogs.map((log) => (
                    <tr key={log.id} className="hover:bg-surface-container-low">
                      <td className="px-4 py-4 text-body-md text-on-surface-variant">{formatDateTime(log.occurredAt)}</td>
                      <td className="px-4 py-4 font-semibold text-secondary">{log.action}</td>
                      <td className="px-4 py-4 text-body-md text-secondary">
                        {log.resourceType}
                        {log.resourceId ? <span className="ml-2 font-mono text-mono-status text-on-surface-variant">{log.resourceId.slice(0, 8)}</span> : null}
                      </td>
                      <td className="px-4 py-4 text-body-md text-on-surface-variant">{log.ipAddress ?? "—"}</td>
                      <td className="max-w-[320px] truncate px-4 py-4 text-body-md text-on-surface-variant">{log.diffJson ? "Đã ghi nhận thay đổi" : "—"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {!auditLogs.length ? <div className="p-card-padding"><EmptyState>Chưa có nhật ký quản trị.</EmptyState></div> : null}
          </Card>
        </section>
      ) : null}

      <Modal
        open={userModal !== null}
        onClose={() => setUserModal(null)}
        title={userModal === "edit" ? "Cập nhật người dùng" : "Thêm người dùng"}
        footer={
          <>
            <Button type="button" variant="ghost" onClick={() => setUserModal(null)} disabled={userMutation.isPending}>Hủy</Button>
            <Button type="submit" form="admin-user-form" disabled={userMutation.isPending}>
              <span aria-hidden="true" className="material-symbols-outlined text-[18px]">save</span>
              Lưu
            </Button>
          </>
        }
      >
        {userMutation.error ? <Alert tone="error">{adminFormErrorMessage(userMutation.error)}</Alert> : null}
        <form
          id="admin-user-form"
          className="space-y-4"
          onSubmit={(event) => {
            event.preventDefault();
            userMutation.mutate();
          }}
        >
          <Field label="Tên hiển thị">
            <input className={inputClass} required value={userForm.displayName} onChange={(event) => setUserForm({ ...userForm, displayName: event.target.value })} />
          </Field>
          {userModal === "create" ? (
            <>
              <Field label="Email">
                <input className={inputClass} required type="email" value={userForm.email} onChange={(event) => setUserForm({ ...userForm, email: event.target.value })} />
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
                  onChange={(event) => setUserForm({ ...userForm, password: event.target.value })}
                />
                <p className="mt-1 text-label-sm text-on-surface-variant">{tempPasswordHint}</p>
              </Field>
              <div>
                <p className="mb-2 text-label-sm font-semibold text-secondary">Vai trò ban đầu</p>
                <div className="flex flex-wrap gap-2">
                  {roles.map((role) => (
                    <label key={role.id} className="inline-flex items-center gap-2 rounded border border-outline px-3 py-2 text-body-md">
                      <input type="checkbox" className="size-4 accent-primary" checked={userForm.roles.includes(role.name)} onChange={() => toggleRoleName(role.name)} />
                      {role.name}
                    </label>
                  ))}
                </div>
              </div>
            </>
          ) : (
            <label className="inline-flex items-center gap-2 text-body-md font-semibold text-secondary">
              <input type="checkbox" className="size-4 accent-primary" checked={userForm.isActive} onChange={(event) => setUserForm({ ...userForm, isActive: event.target.checked })} />
              Người dùng đang hoạt động
            </label>
          )}
        </form>
      </Modal>

      <Modal
        open={roleModal !== null}
        onClose={() => setRoleModal(null)}
        title={roleModal === "edit" ? "Cập nhật vai trò" : "Thêm vai trò"}
        footer={
          <>
            <Button type="button" variant="ghost" onClick={() => setRoleModal(null)} disabled={roleMutation.isPending}>Hủy</Button>
            <Button type="submit" form="admin-role-form" disabled={roleMutation.isPending}>
              <span aria-hidden="true" className="material-symbols-outlined text-[18px]">save</span>
              Lưu
            </Button>
          </>
        }
      >
        {roleMutation.error ? <Alert tone="error">{errorMessage(roleMutation.error)}</Alert> : null}
        <form
          id="admin-role-form"
          className="space-y-4"
          onSubmit={(event) => {
            event.preventDefault();
            roleMutation.mutate();
          }}
        >
          <Field label="Tên vai trò">
            <input className={inputClass} required value={roleForm.name} onChange={(event) => setRoleForm({ ...roleForm, name: event.target.value })} />
          </Field>
          <Field label="Mô tả">
            <textarea className={`${inputClass} min-h-24`} value={roleForm.description} onChange={(event) => setRoleForm({ ...roleForm, description: event.target.value })} />
          </Field>
        </form>
      </Modal>

      <Modal
        open={keyModalOpen}
        onClose={() => setKeyModalOpen(false)}
        title="Phát hành khóa tích hợp"
        footer={
          <>
            <Button type="button" variant="ghost" onClick={() => setKeyModalOpen(false)} disabled={keyMutation.isPending}>Hủy</Button>
            <Button type="submit" form="admin-key-form" disabled={keyMutation.isPending}>
              <span aria-hidden="true" className="material-symbols-outlined text-[18px]">vpn_key</span>
              Phát hành
            </Button>
          </>
        }
      >
        {keyMutation.error ? <Alert tone="error">{errorMessage(keyMutation.error)}</Alert> : null}
        <form
          id="admin-key-form"
          className="space-y-4"
          onSubmit={(event) => {
            event.preventDefault();
            keyMutation.mutate();
          }}
        >
          <Field label="Tên khóa">
            <input className={inputClass} required value={keyForm.name} onChange={(event) => setKeyForm({ ...keyForm, name: event.target.value })} />
          </Field>
          <Field label="Quyền truy cập">
            <textarea className={`${inputClass} min-h-24`} value={keyForm.scopes} onChange={(event) => setKeyForm({ ...keyForm, scopes: event.target.value })} />
          </Field>
          <Field label="Ngày hết hạn">
            <input className={inputClass} type="date" value={keyForm.expiresAt} onChange={(event) => setKeyForm({ ...keyForm, expiresAt: event.target.value })} />
          </Field>
        </form>
      </Modal>

      {actionPending ? (
        <div className="fixed bottom-4 right-4 z-50 rounded bg-secondary px-4 py-2 text-body-md text-white shadow-xl">Đang xử lý...</div>
      ) : null}
    </AppShell>
  );
}
