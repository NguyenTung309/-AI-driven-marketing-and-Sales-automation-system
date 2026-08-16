import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useAuthStore } from "@/shared/auth/authStore";
import { AppShell } from "@/shared/layout/AppShell";
import {
  Alert,
  Button,
  ConfirmDialog,
  InfiniteScrollSentinel,
  StatusPill,
  useDebounce,
  useInfiniteList,
  type StatusTone,
} from "@/shared/ui";
import {
  createAdminUser,
  createRole,
  deletePancakeConfig,
  deleteRole,
  getPancakeConfig,
  getPancakeWebhookUrl,
  getSimpleUserList,
  listAdminUsers,
  listAuditLogs,
  listPermissions,
  listRolePermissions,
  listRoles,
  listSystemLogs,
  resetAdminUserPassword,
  setAdminUserActive,
  setRolePermissions,
  unlinkInboxMember,
  updateAdminUser,
  updateInboxMember,
  updatePancakeChannel,
  updatePancakeConfig,
  updateRole,
  type AdminUser,
  type PancakeChannelInfo,
  type AuditLog,
  type PagedResponse,
  type Permission,
  type Role,
  type SimpleUser,
  type SystemLogCursorPage,
  type SystemLogEntry,
  type UpdatePancakeChannelRequest,
} from "@/shared/api/admin";
import { AdminAuditTab } from "./AdminAuditTab";
import { AdminIntegrationsTab } from "./AdminIntegrationsTab";
import { AdminJobsTab } from "./AdminJobsTab";
import { AdminPancakeChannelModal, type PancakeChannelTarget } from "./AdminPancakeChannelModal";
import { AdminRoleModal, type RoleModalMode } from "./AdminRoleModal";
import { AdminRolesTab } from "./AdminRolesTab";
import { AdminSystemLogsTab } from "./AdminSystemLogsTab";
import { AdminUserModal, type UserModalMode } from "./AdminUserModal";
import { AdminUsersTab } from "./AdminUsersTab";
import {
  confirmCopy,
  DEFAULT_PANCAKE_FORM,
  errorMessage,
  type AdminRoleFormState,
  type AdminUserFormState,
  type ConfirmTarget,
} from "./adminHelpers";
import { MetricTile, TabButton } from "./adminUi";

type AdminTab = "users" | "roles" | "integrations" | "errors" | "audit" | "jobs";

const EMPTY_USERS: readonly AdminUser[] = [];
const EMPTY_ROLES: readonly Role[] = [];
const EMPTY_PERMISSIONS: readonly Permission[] = [];
const EMPTY_AUDIT_LOGS: readonly AuditLog[] = [];
const EMPTY_SYSTEM_LOGS: readonly SystemLogEntry[] = [];
const EMPTY_SIMPLE_USERS: readonly SimpleUser[] = [];

export default function AdminConsolePage() {
  const queryClient = useQueryClient();
  const authPermissions = useAuthStore((s) => s.permissions);
  const canManageUsers = authPermissions.includes("admin:users-manage");
  const canManageSales = authPermissions.includes("admin:sale-manage");
  const canViewUsersTab = canManageUsers || canManageSales;
  const canManagePancakeToken = canManageUsers || authPermissions.includes("users:pancake-token:manage");
  const canManageInboxOwners = authPermissions.includes("admin:inboxes");
  // Must match BE gate (system.logs only) — do not open the tab on legacy admin.* alone.
  const canViewSystemLogs = authPermissions.includes("system.logs");
  const canManageRoles = authPermissions.includes("rbac:manage");
  const canManageIntegrations = authPermissions.includes("admin:integration");
  const canViewJobs = authPermissions.includes("admin:jobs-hangfires");

  const defaultTab: AdminTab = canViewUsersTab
    ? "users"
    : canManageIntegrations
      ? "integrations"
      : canManageRoles
        ? "roles"
        : canViewJobs
          ? "jobs"
          : canViewSystemLogs
            ? "errors"
            : "users";

  const [tab, setTab] = useState<AdminTab>(defaultTab);
  const [search, setSearch] = useState("");
  const [auditAction, setAuditAction] = useState("");
  const [auditResourceType, setAuditResourceType] = useState("");
  const [systemLevel, setSystemLevel] = useState("");
  const [systemStatusGroup, setSystemStatusGroup] = useState("");
  const [systemSource, setSystemSource] = useState("");
  const [systemFrom, setSystemFrom] = useState("");
  const [systemTo, setSystemTo] = useState("");
  const [systemSearch, setSystemSearch] = useState("");
  const debouncedSystemSearch = useDebounce(systemSearch, 300);
  const [notice, setNotice] = useState<string | null>(null);
  const [userModal, setUserModal] = useState<UserModalMode>(null);
  const [editingUser, setEditingUser] = useState<AdminUser | null>(null);
  const [channelTarget, setChannelTarget] = useState<PancakeChannelTarget | null>(null);
  const [isChannelUnlinkConfirmOpen, setIsChannelUnlinkConfirmOpen] = useState(false);
  const [userForm, setUserForm] = useState<AdminUserFormState>({
    displayName: "",
    email: "",
    password: "",
    isActive: true,
    roles: [],
    pancakePageId: "",
    pancakeChannelName: "",
    pancakePlatform: "zalo",
    pancakeAccessToken: "",
  });
  const [roleModal, setRoleModal] = useState<RoleModalMode>(null);
  const [editingRole, setEditingRole] = useState<Role | null>(null);
  const [roleForm, setRoleForm] = useState<AdminRoleFormState>({ name: "", description: "" });
  const [selectedRoleId, setSelectedRoleId] = useState<string | null>(null);
  const [permissionDraft, setPermissionDraft] = useState<{ readonly roleId: string; readonly ids: readonly string[] } | null>(null);
  const [pancakeDraft, setPancakeDraft] = useState<Partial<typeof DEFAULT_PANCAKE_FORM>>({});
  const [confirmTarget, setConfirmTarget] = useState<ConfirmTarget | null>(null);

  const debouncedSearch = useDebounce(search, 300);
  const usersList = useInfiniteList<AdminUser, PagedResponse<AdminUser>>({
    queryKey: ["admin", "users", debouncedSearch],
    initialPageParam: 1,
    queryFn: (pageParam) =>
      listAdminUsers({
        q: debouncedSearch || undefined,
        page: typeof pageParam === "number" ? pageParam : 1,
        pageSize: 50,
      }),
  });
  const usersQuery = usersList.query;
  const rolesQuery = useQuery({
    queryKey: ["admin", "roles"],
    queryFn: listRoles,
    enabled: canViewUsersTab || canManageRoles,
  });
  const permissionsQuery = useQuery({
    queryKey: ["admin", "permissions"],
    queryFn: listPermissions,
    enabled: canManageRoles,
  });
  const ownerOptionsQuery = useQuery({
    queryKey: ["admin", "users-simple"],
    queryFn: getSimpleUserList,
    enabled: Boolean(channelTarget) && canManageInboxOwners,
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
  const auditList = useInfiniteList<AuditLog, PagedResponse<AuditLog>>({
    queryKey: ["admin", "audit-logs", auditAction, auditResourceType],
    initialPageParam: 1,
    queryFn: (pageParam) =>
      listAuditLogs({
        page: typeof pageParam === "number" ? pageParam : 1,
        pageSize: 50,
        action: auditAction || undefined,
        resourceType: auditResourceType || undefined,
      }),
    enabled: tab === "audit" && canViewSystemLogs,
  });
  const auditQuery = auditList.query;
  const systemFromIso = systemFrom ? new Date(systemFrom).toISOString() : undefined;
  const systemToIso = systemTo ? new Date(systemTo).toISOString() : undefined;
  const systemLogsList = useInfiniteList<SystemLogEntry, SystemLogCursorPage>({
    queryKey: [
      "admin",
      "system-logs",
      systemLevel,
      systemStatusGroup,
      systemSource,
      systemFrom,
      systemTo,
      debouncedSystemSearch,
    ],
    initialPageParam: null,
    queryFn: (pageParam) =>
      listSystemLogs({
        cursor: typeof pageParam === "string" ? pageParam : null,
        pageSize: 50,
        level: systemLevel || undefined,
        statusGroup: systemStatusGroup || undefined,
        source: systemSource || undefined,
        from: systemFromIso,
        to: systemToIso,
        q: debouncedSystemSearch || undefined,
      }),
    enabled: tab === "errors" && canViewSystemLogs,
  });
  const systemLogsQuery = systemLogsList.query;
  const systemSummary = systemLogsQuery.data?.pages[0]?.summary ?? null;

  const users = usersList.items.length ? usersList.items : EMPTY_USERS;
  const roles = rolesQuery.data ?? EMPTY_ROLES;
  const permissions = permissionsQuery.data ?? EMPTY_PERMISSIONS;
  const ownerOptions = ownerOptionsQuery.data ?? EMPTY_SIMPLE_USERS;
  const auditLogs = auditList.items.length ? auditList.items : EMPTY_AUDIT_LOGS;
  const systemLogs = systemLogsList.items.length ? systemLogsList.items : EMPTY_SYSTEM_LOGS;
  const effectiveSelectedRoleId = selectedRoleId ?? roles[0]?.id ?? null;
  const rolePermissionsQuery = useQuery({
    queryKey: ["admin", "role-permissions", effectiveSelectedRoleId],
    queryFn: () => listRolePermissions(effectiveSelectedRoleId!),
    enabled: canManageRoles && tab === "roles" && Boolean(effectiveSelectedRoleId),
  });
  const rolePermissionRows = Array.isArray(rolePermissionsQuery.data) ? rolePermissionsQuery.data : EMPTY_PERMISSIONS;
  const selectedRole = roles.find((role) => role.id === effectiveSelectedRoleId) ?? null;
  const activeUsers = users.filter((user) => user.isActive).length;
  const pancakeStatusKnown = pancakeQuery.isFetched;
  const pancakeStatusText = pancakeStatusKnown ? (pancakeQuery.data?.isActive ? "Kết nối" : "Chưa bật") : "Chưa kiểm tra";
  const pancakeStatusTone: StatusTone = pancakeStatusKnown ? (pancakeQuery.data?.isActive ? "success" : "warning") : "neutral";
  const currentError =
    usersQuery.error ??
    rolesQuery.error ??
    permissionsQuery.error ??
    pancakeQuery.error ??
    webhookQuery.error ??
    auditQuery.error ??
    systemLogsQuery.error;

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
          ...(canManagePancakeToken && userForm.pancakePageId.trim()
            ? {
                pancakePageId: userForm.pancakePageId.trim(),
                pancakePlatform: userForm.pancakePlatform,
                ...(userForm.pancakeChannelName.trim() ? { pancakeChannelName: userForm.pancakeChannelName.trim() } : {}),
                ...(userForm.pancakeAccessToken.trim() ? { pancakeAccessToken: userForm.pancakeAccessToken.trim() } : {}),
              }
            : {}),
        });
      }
      if (!editingUser) return undefined;
      await updateAdminUser(editingUser.id, {
        ...(canManageUsers ? { displayName: userForm.displayName.trim(), isActive: userForm.isActive } : {}),
        ...(canManagePancakeToken && userForm.pancakePageId.trim()
          ? {
              pancakePageId: userForm.pancakePageId.trim(),
              pancakePlatform: userForm.pancakePlatform,
              ...(userForm.pancakeChannelName.trim() ? { pancakeChannelName: userForm.pancakeChannelName.trim() } : {}),
              ...(userForm.pancakeAccessToken.trim() ? { pancakeAccessToken: userForm.pancakeAccessToken.trim() } : {}),
            }
          : {}),
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

  const channelMetadataMutation = useMutation({
    mutationFn: ({ inboxId, body }: { readonly inboxId: string; readonly body: UpdatePancakeChannelRequest }) =>
      updatePancakeChannel(inboxId, body),
    onSuccess: () => {
      setChannelTarget(null);
      setNotice("Đã cập nhật thông tin kênh.");
      invalidateAdmin();
    },
  });

  const channelOwnerMutation = useMutation({
    mutationFn: ({ inboxId, agentId }: { readonly inboxId: string; readonly agentId: string }) =>
      updateInboxMember(inboxId, agentId),
    onSuccess: () => {
      setChannelTarget(null);
      setNotice("Đã đổi người phụ trách kênh.");
      invalidateAdmin();
    },
  });

  const channelUnlinkMutation = useMutation({
    mutationFn: ({ inboxId, agentId }: { readonly inboxId: string; readonly agentId: string }) =>
      unlinkInboxMember(inboxId, agentId),
    onSuccess: () => {
      setIsChannelUnlinkConfirmOpen(false);
      setChannelTarget(null);
      setNotice("Đã gỡ người dùng khỏi kênh. Kênh vẫn được giữ lại.");
      invalidateAdmin();
    },
    onError: () => setIsChannelUnlinkConfirmOpen(false),
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
      pancakePageId: "",
      pancakeChannelName: "",
      pancakePlatform: "zalo",
      pancakeAccessToken: "",
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
      pancakePageId: "",
      pancakeChannelName: "",
      pancakePlatform: "zalo",
      pancakeAccessToken: "",
    });
    setUserModal("edit");
  }

  function openManageChannel(user: AdminUser, channel: PancakeChannelInfo) {
    channelMetadataMutation.reset();
    channelOwnerMutation.reset();
    channelUnlinkMutation.reset();
    setIsChannelUnlinkConfirmOpen(false);
    setChannelTarget({ userId: user.id, userDisplayName: user.displayName, channel });
  }

  function closeChannelModal() {
    if (channelMetadataMutation.isPending || channelOwnerMutation.isPending || channelUnlinkMutation.isPending) return;
    setIsChannelUnlinkConfirmOpen(false);
    setChannelTarget(null);
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
    channelMetadataMutation.isPending ||
    channelOwnerMutation.isPending ||
    channelUnlinkMutation.isPending ||
    userMutation.isPending ||
    activeMutation.isPending ||
    resetPasswordMutation.isPending ||
    roleMutation.isPending ||
    deleteRoleMutation.isPending ||
    permissionsMutation.isPending ||
    pancakeMutation.isPending ||
    deletePancakeMutation.isPending;

  return (
    <AppShell title="Hệ thống">
      <section className="mb-gutter rounded-lg border border-primary/20 bg-primary/5 p-4">
        <div className="flex flex-col gap-3 xl:flex-row xl:items-center xl:justify-between">
          <div>
            <h1 className="text-headline-md text-secondary">Hệ thống & phân quyền</h1>
            <p className="mt-1 text-body-md text-on-surface-variant">
              Quản trị người dùng, vai trò và kết nối Pancake cho đơn vị hiện tại.
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

      <section className="mb-gutter grid grid-cols-1 gap-gutter sm:grid-cols-2 xl:grid-cols-3">
        <MetricTile icon="group" label="Người dùng hoạt động" value={`${activeUsers}/${users.length}`} tone="success" />
        <MetricTile icon="admin_panel_settings" label="Vai trò" value={`${roles.length}`} tone="neutral" />
        <MetricTile icon="hub" label="Pancake" value={pancakeStatusText} tone={pancakeStatusTone} />
      </section>

      <div className="mb-gutter flex flex-wrap border-b border-outline">
        {canViewUsersTab ? (
          <TabButton active={tab === "users"} icon="group" label="Người dùng" onClick={() => setTab("users")} />
        ) : null}
        {canManageRoles ? (
          <TabButton active={tab === "roles"} icon="admin_panel_settings" label="Phân quyền" onClick={() => setTab("roles")} />
        ) : null}
        {canManageIntegrations ? (
          <TabButton active={tab === "integrations"} icon="hub" label="Tích hợp" onClick={() => setTab("integrations")} />
        ) : null}
        {canViewJobs ? (
          <TabButton active={tab === "jobs"} icon="schedule" label="Tác vụ tự động" onClick={() => setTab("jobs")} />
        ) : null}
        {canViewSystemLogs ? (
          <>
            <TabButton active={tab === "errors"} icon="bug_report" label="Lỗi hệ thống" onClick={() => setTab("errors")} />
            <TabButton active={tab === "audit"} icon="receipt_long" label="Nhật ký quản trị" onClick={() => setTab("audit")} />
          </>
        ) : null}
      </div>

      {tab === "users" && canViewUsersTab ? (
        <>
          <AdminUsersTab
            users={users}
            search={search}
            onSearchChange={setSearch}
            canManageUsers={canManageUsers}
            canManagePancakeToken={canManagePancakeToken}
            canManageInboxOwners={canManageInboxOwners}
            onCreateUser={openCreateUser}
            onManageChannel={openManageChannel}
            onEditUser={openEditUser}
            onToggleActive={(user) => activeMutation.mutate({ id: user.id, active: !user.isActive })}
            activeMutationPending={activeMutation.isPending}
            onResetPassword={(user) => setConfirmTarget({ kind: "resetPassword", id: user.id, label: user.displayName })}
            resetPasswordPending={resetPasswordMutation.isPending}
          />
          <InfiniteScrollSentinel
            hasNextPage={usersList.hasNextPage}
            isFetchingNextPage={usersList.isFetchingNextPage}
            onLoadMore={usersList.fetchNextPage}
          />
        </>
      ) : null}

      {tab === "roles" && canManageRoles ? (
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

      {tab === "integrations" && canManageIntegrations ? (
        <AdminIntegrationsTab
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

      {tab === "errors" && canViewSystemLogs ? (
        <>
          <AdminSystemLogsTab
            logs={systemLogs}
            summary={systemSummary}
            level={systemLevel}
            statusGroup={systemStatusGroup}
            source={systemSource}
            from={systemFrom}
            to={systemTo}
            q={systemSearch}
            onLevelChange={setSystemLevel}
            onStatusGroupChange={setSystemStatusGroup}
            onSourceChange={setSystemSource}
            onFromChange={setSystemFrom}
            onToChange={setSystemTo}
            onSearchChange={setSystemSearch}
            isLoading={systemLogsList.isLoading}
            canLoadStats={canViewSystemLogs}
          />
          <InfiniteScrollSentinel
            hasNextPage={systemLogsList.hasNextPage}
            isFetchingNextPage={systemLogsList.isFetchingNextPage}
            onLoadMore={systemLogsList.fetchNextPage}
          />
        </>
      ) : null}

      {tab === "audit" && canViewSystemLogs ? (
        <>
          <AdminAuditTab
            auditLogs={auditLogs}
            action={auditAction}
            resourceType={auditResourceType}
            onActionChange={setAuditAction}
            onResourceTypeChange={setAuditResourceType}
          />
          <InfiniteScrollSentinel
            hasNextPage={auditList.hasNextPage}
            isFetchingNextPage={auditList.isFetchingNextPage}
            onLoadMore={auditList.fetchNextPage}
          />
        </>
      ) : null}

      {tab === "jobs" && canViewJobs ? <AdminJobsTab /> : null}

      <AdminUserModal
        mode={userModal}
        userForm={userForm}
        onChange={(patch) => setUserForm({ ...userForm, ...patch })}
        canManageUsers={canManageUsers}
        canManagePancakeToken={canManagePancakeToken}
        editingUser={editingUser}
        roles={roles}
        onToggleRoleName={toggleRoleName}
        pending={userMutation.isPending}
        error={userMutation.error}
        onClose={() => setUserModal(null)}
        onSubmit={() => userMutation.mutate()}
      />

      <AdminPancakeChannelModal
        target={channelTarget}
        canManagePancakeToken={canManagePancakeToken}
        canManageInboxOwners={canManageInboxOwners}
        ownerOptions={ownerOptions}
        ownerOptionsLoading={ownerOptionsQuery.isLoading}
        metadataPending={channelMetadataMutation.isPending}
        ownerPending={channelOwnerMutation.isPending || channelUnlinkMutation.isPending}
        metadataError={channelMetadataMutation.error}
        ownerError={ownerOptionsQuery.error ?? channelOwnerMutation.error ?? channelUnlinkMutation.error}
        onSaveMetadata={(body) => {
          if (!channelTarget) return;
          channelMetadataMutation.mutate({ inboxId: channelTarget.channel.inboxId, body });
        }}
        onChangeOwner={(agentId) => {
          if (!channelTarget) return;
          channelOwnerMutation.mutate({ inboxId: channelTarget.channel.inboxId, agentId });
        }}
        onRequestUnlink={() => setIsChannelUnlinkConfirmOpen(true)}
        onClose={closeChannelModal}
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

      <ConfirmDialog
        open={confirmTarget !== null}
        title={confirmTarget ? confirmCopy(confirmTarget).title : ""}
        message={confirmTarget ? confirmCopy(confirmTarget).message : ""}
        confirmLabel={confirmTarget ? confirmCopy(confirmTarget).confirmLabel : undefined}
        pending={confirmPending}
        onConfirm={handleConfirm}
        onCancel={() => setConfirmTarget(null)}
      />

      <ConfirmDialog
        open={isChannelUnlinkConfirmOpen && channelTarget !== null}
        title="Gỡ người dùng khỏi kênh?"
        message={
          channelTarget
            ? `${channelTarget.userDisplayName} sẽ không còn phụ trách kênh ${channelTarget.channel.name || channelTarget.channel.pageId}. Các hội thoại đang gán cho người này trong kênh sẽ được bỏ gán. Kênh không bị xóa.`
            : ""
        }
        confirmLabel="Gỡ khỏi kênh"
        pending={channelUnlinkMutation.isPending}
        onConfirm={() => {
          if (!channelTarget) return;
          channelUnlinkMutation.mutate({ inboxId: channelTarget.channel.inboxId, agentId: channelTarget.userId });
        }}
        onCancel={() => {
          if (!channelUnlinkMutation.isPending) setIsChannelUnlinkConfirmOpen(false);
        }}
      />

      {actionPending ? (
        <div className="fixed bottom-4 right-4 z-50 rounded bg-secondary px-4 py-2 text-body-md text-white shadow-xl">Đang xử lý...</div>
      ) : null}
    </AppShell>
  );
}
