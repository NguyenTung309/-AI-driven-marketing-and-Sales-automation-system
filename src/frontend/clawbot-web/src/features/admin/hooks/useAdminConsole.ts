import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { StatusTone } from "@/shared/ui";
import {
  createAdminUser,
  createApiKey,
  createRole,
  deletePancakeConfig,
  deleteRole,
  getPancakeConfig,
  getPancakeWebhookUrl,
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
  updateRole,
} from "@/shared/api/admin";
import type {
  AdminUser,
  CreatedApiKey,
  Permission,
  Role,
} from "@/shared/api/admin";
import {
  DEFAULT_PANCAKE_FORM,
  parseScopes,
  EMPTY_AUDIT_LOGS,
  EMPTY_KEYS,
  EMPTY_PERMISSIONS,
  EMPTY_ROLES,
  EMPTY_USERS,
} from "../admin.types"
import type { AdminTab, UserModalMode, RoleModalMode } from "../admin.types"

export function useAdminConsole() {
  const queryClient = useQueryClient();
  const [tab, setTab] = useState<AdminTab>("users");
  const [search, setSearch] = useState("");
  const [notice, setNotice] = useState<string | null>(null);

  // --- User modal state ---
  const [userModal, setUserModal] = useState<UserModalMode>(null);
  const [editingUser, setEditingUser] = useState<AdminUser | null>(null);
  const [userForm, setUserForm] = useState({
    displayName: "",
    email: "",
    password: "",
    isActive: true,
    roles: [] as string[],
  });

  // --- Role modal state ---
  const [roleModal, setRoleModal] = useState<RoleModalMode>(null);
  const [editingRole, setEditingRole] = useState<Role | null>(null);
  const [roleForm, setRoleForm] = useState({ name: "", description: "" });
  const [selectedRoleId, setSelectedRoleId] = useState<string | null>(null);
  const [permissionDraft, setPermissionDraft] = useState<{
    readonly roleId: string;
    readonly ids: readonly string[];
  } | null>(null);

  // --- Key modal state ---
  const [keyModalOpen, setKeyModalOpen] = useState(false);
  const [keyForm, setKeyForm] = useState({ name: "", scopes: "admin.system", expiresAt: "" });
  const [createdKey, setCreatedKey] = useState<CreatedApiKey | null>(null);

  // --- Pancake form state ---
  const [pancakeDraft, setPancakeDraft] = useState<Partial<Record<string, string | boolean>>>({});

  // --- Queries ---
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

  // --- Derived data ---
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
  const rolePermissionRows = Array.isArray(rolePermissionsQuery.data)
    ? rolePermissionsQuery.data
    : EMPTY_PERMISSIONS;
  const selectedRole = roles.find((role) => role.id === effectiveSelectedRoleId) ?? null;
  const activeUsers = users.filter((user) => user.isActive).length;
  const activeKeys = apiKeys.filter((key) => !key.revokedAt).length;
  const pancakeStatusKnown = pancakeQuery.isFetched;
  const pancakeStatusText = pancakeStatusKnown
    ? pancakeQuery.data?.isActive
      ? "Kết nối"
      : "Chưa bật"
    : "Chưa kiểm tra";
  const pancakeStatusTone: StatusTone = pancakeStatusKnown
    ? pancakeQuery.data?.isActive
      ? "success"
      : "warning"
    : "neutral";

  const currentError =
    usersQuery.error ??
    rolesQuery.error ??
    permissionsQuery.error ??
    apiKeysQuery.error ??
    pancakeQuery.error ??
    webhookQuery.error ??
    auditQuery.error;

  // --- Memos ---
  const permissionsByGroup = useMemo(() => {
    const groups = new Map<string, Permission[]>();
    permissions.forEach((permission) => {
      const group = permission.code.includes(".")
        ? permission.code.split(".")[0]
        : "system";
      groups.set(group, [...(groups.get(group) ?? []), permission]);
    });
    return [...groups.entries()].sort(([a], [b]) => a.localeCompare(b));
  }, [permissions]);

  const checkedPermissionIds = useMemo(() => {
    if (!effectiveSelectedRoleId) return [];
    if (permissionDraft?.roleId === effectiveSelectedRoleId) return [...permissionDraft.ids];
    return rolePermissionRows.map((permission) => permission.id);
  }, [effectiveSelectedRoleId, permissionDraft, rolePermissionRows]);

  type PancakeFormValues = {
  readonly baseUrl: string;
  readonly accessToken: string;
  readonly webhookSecret: string;
  readonly signatureHeader: string;
  readonly signatureAlgo: string;
  readonly signatureEncoding: string;
  readonly sendPathTemplate: string;
  readonly authMode: string;
  readonly isActive: boolean;
};

const pancakeBaseForm: PancakeFormValues = useMemo(() => {
    const config = pancakeQuery.data;
    return {
      ...DEFAULT_PANCAKE_FORM,
  parseScopes,
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

  const pancakeForm: PancakeFormValues = useMemo(
    () => ({ ...pancakeBaseForm, ...pancakeDraft } as typeof DEFAULT_PANCAKE_FORM),
    [pancakeBaseForm, pancakeDraft]
  );

  // --- Helpers ---
  const invalidateAdmin = () => {
    void queryClient.invalidateQueries({ queryKey: ["admin"] });
  };

  // --- Mutations ---
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
      setNotice(userModal === "create" ? " tạo người dùng mới." : " cập nhật người dùng.");
      invalidateAdmin();
    },
  });

  const activeMutation = useMutation({
    mutationFn: ({ id, active }: { readonly id: string; readonly active: boolean }) =>
      setAdminUserActive(id, active),
    onSuccess: (_, variables) => {
      setNotice(variables.active ? "Đã kích hoạt người dùng." : "Đã khóa người dùng.");
      invalidateAdmin();
    },
  });

  const resetPasswordMutation = useMutation({
    mutationFn: resetAdminUserPassword,
    onSuccess: () => {
      setNotice("Đã phát hành mã đặt lại mật khẩu và gửi email nếu SMTP đã được cấu hình.");
    },
  });

  const roleMutation = useMutation({
    mutationFn: () => {
      const body = {
        name: roleForm.name.trim(),
        description: roleForm.description.trim() || null,
      };
      return roleModal === "edit" && editingRole
        ? updateRole(editingRole.id, body)
        : createRole(body);
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
      setNotice("Đã lưu mã trận phân quyền.");
      invalidateAdmin();
    },
  });

  const keyMutation = useMutation({
    mutationFn: () =>
      createApiKey({
        name: keyForm.name.trim(),
        scopes: parseScopes(keyForm.scopes),
        expiresAt: keyForm.expiresAt
          ? `${keyForm.expiresAt as string}T23:59:59+07:00`
          : null,
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
      setNotice("Đã thu hồi API key.");
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
        ...(pancakeForm.accessToken?.trim()
          ? { accessToken: pancakeForm.accessToken.trim() }
          : {}),
        ...(pancakeForm.webhookSecret?.trim()
          ? { webhookSecret: pancakeForm.webhookSecret.trim() }
          : {}),
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
      setNotice("Đã xóa cấu hình Pancake.");
      setPancakeDraft({});
      invalidateAdmin();
    },
  });

  // --- Handlers ---
  function openCreateUser() {
    setEditingUser(null);
    setUserForm({ displayName: "", email: "", password: "", isActive: true, roles: [] });
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
      roles: current.roles.includes(name)
        ? current.roles.filter((r) => r !== name)
        : [...current.roles, name],
    }));
  }

  function togglePermission(id: string) {
    if (!effectiveSelectedRoleId) return;
    setPermissionDraft((current) => {
      const currentIds =
        current?.roleId === effectiveSelectedRoleId ? current.ids : checkedPermissionIds;
      const ids = currentIds.includes(id)
        ? currentIds.filter((item) => item !== id)
        : [...currentIds, id];
      return { roleId: effectiveSelectedRoleId, ids };
    });
  }

  function updatePancakeForm(patch: Record<string, string | boolean>) {
    setPancakeDraft((current) => ({ ...current, ...patch }));
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
    pancakeMutation.isPending ||
    deletePancakeMutation.isPending;

  return {
    // tab
    tab,
    setTab,
    search,
    setSearch,
    notice,
    setNotice,

    // users
    users,
    userModal,
    setUserModal,
    editingUser,
    userForm,
    setUserForm,
    openCreateUser,
    openEditUser,
    toggleRoleName,
    userMutation,
    activeMutation,
    resetPasswordMutation,

    // roles
    roles,
    permissions,
    permissionsByGroup,
    roleModal,
    setRoleModal,
    editingRole,
    roleForm,
    setRoleForm,
    selectedRoleId,
    setSelectedRoleId,
    selectedRole,
    checkedPermissionIds,
    rolePermissionsQuery,
    openCreateRole,
    openEditRole,
    togglePermission,
    roleMutation,
    deleteRoleMutation,
    permissionsMutation,

    // keys
    apiKeys,
    keyModalOpen,
    setKeyModalOpen,
    keyForm,
    setKeyForm,
    createdKey,
    setCreatedKey,
    keyMutation,
    revokeKeyMutation,

    // pancake
    pancakeQuery,
    webhookQuery,
    pancakeForm,
    pancakeMutation,
    deletePancakeMutation,
    updatePancakeForm,

    // audit
    auditLogs,

    // error / metrics
    currentError,
    activeUsers,
    activeKeys,
    pancakeStatusKnown,
    pancakeStatusText,
    pancakeStatusTone,
    actionPending,
  };
}







