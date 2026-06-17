import { Alert, Button, StatusPill } from "@/shared/ui";
import { AppShell } from "@/shared/layout/AppShell";
import {

  MetricTile,
  TabButton,
  UsersPanel,
  RolesPanel,
  KeysPanel,
  IntegrationsPanel,
  AuditPanel,
  UserModal,
  RoleModal,
  KeyModal,
} from "./components";
import { useAdminConsole } from "./hooks/useAdminConsole";

export default function AdminConsolePage() {
  const ctx = useAdminConsole();


  return (
    <AppShell title="Hệ thống & phân quyền">
      {/* Header */}
      <section className="mb-gutter rounded-lg border border-primary/20 bg-primary/5 p-4">
        <div className="flex flex-col gap-3 xl:flex-row xl:items-center xl:justify-between">
          <div>
            <h1 className="text-headline-md text-secondary">Hệ thống & phân quyền</h1>
            <p className="mt-1 text-body-md text-on-surface-variant">
              Pancake theo tenant hiện tại: <span className="font-medium">{ctx.pancakeStatusText}</span>.
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <StatusPill tone={ctx.currentError ? "error" : "success"}>
              {ctx.currentError ? "Admin API lỗi" : "Admin API online"}
            </StatusPill>
            <Button type="button" variant="outline" onClick={() => ctx.setTab("audit")}>
              <span className="material-symbols-outlined text-[18px]">history</span>
              Nhật ký
            </Button>
          </div>
        </div>
      </section>

      {/* Notifications */}
      {ctx.notice ? (
        <div className="mb-gutter">
          <Alert tone="success">{ctx.notice}</Alert>
        </div>
      ) : null}
      {ctx.currentError ? (
        <div className="mb-gutter">
          <Alert tone="error">{ctx.currentError.message}</Alert>
        </div>
      ) : null}

      {/* Metric tiles */}
      <section className="mb-gutter grid grid-cols-1 gap-gutter sm:grid-cols-2 xl:grid-cols-4">
        <MetricTile
          icon="group"
          label="Người dùng hoạt động"
          value={`${ctx.activeUsers}/${ctx.users.length}`}
          tone="success"
        />
        <MetricTile icon="admin_panel_settings" label="Vai trò" value={`${ctx.roles.length}`} tone="neutral" />
        <MetricTile
          icon="vpn_key"
          label="API key hoạt động"
          value={`${ctx.activeKeys}`}
          tone={ctx.activeKeys ? "success" : "warning"}
        />
        <MetricTile
          icon="hub"
          label="Pancake"
          value={ctx.pancakeStatusText}
          tone={ctx.pancakeStatusTone}
        />
      </section>

      {/* Tab bar */}
      <div className="mb-gutter flex flex-wrap border-b border-outline">
        <TabButton active={ctx.tab === "users"} icon="group" label="Người dùng" onClick={() => ctx.setTab("users")} />
        <TabButton active={ctx.tab === "roles"} icon="admin_panel_settings" label="Phân quyền" onClick={() => ctx.setTab("roles")} />
        <TabButton active={ctx.tab === "keys"} icon="vpn_key" label="API keys" onClick={() => ctx.setTab("keys")} />
        <TabButton active={ctx.tab === "integrations"} icon="hub" label="Tích hợp" onClick={() => ctx.setTab("integrations")} />
        <TabButton active={ctx.tab === "audit"} icon="receipt_long" label="Audit logs" onClick={() => ctx.setTab("audit")} />
      </div>

      {/* Tab content */}
      {ctx.tab === "users" ? (
        <UsersPanel
          users={ctx.users}
          search={ctx.search}
          onSearchChange={ctx.setSearch}
          onCreateUser={ctx.openCreateUser}
          onEditUser={ctx.openEditUser}
          onToggleActive={(id, active) => ctx.activeMutation.mutate({ id, active })}
          onResetPassword={(id) => ctx.resetPasswordMutation.mutate(id)}
          isActivePending={ctx.activeMutation.isPending}
          isResetPending={ctx.resetPasswordMutation.isPending}
        />
      ) : null}

      {ctx.tab === "roles" ? (
        <RolesPanel
          roles={ctx.roles}
          permissionsByGroup={ctx.permissionsByGroup}
          selectedRoleId={ctx.selectedRoleId}
          selectedRole={ctx.selectedRole}
          checkedPermissionIds={ctx.checkedPermissionIds}
          rolePermissionsFetching={ctx.rolePermissionsQuery.isFetching}
          onSelectRole={ctx.setSelectedRoleId}
          onCreateRole={ctx.openCreateRole}
          onEditRole={ctx.openEditRole}
          onDeleteRole={(id) => ctx.deleteRoleMutation.mutate(id)}
          onTogglePermission={ctx.togglePermission}
          onSavePermissions={() => ctx.permissionsMutation.mutate()}
          isPermissionsPending={ctx.permissionsMutation.isPending}
        />
      ) : null}

      {ctx.tab === "keys" ? (
        <KeysPanel
          apiKeys={ctx.apiKeys}
          createdKey={ctx.createdKey}
          onRevoke={(id) => ctx.revokeKeyMutation.mutate(id)}
          onOpenCreate={() => ctx.setKeyModalOpen(true)}
          isRevokePending={ctx.revokeKeyMutation.isPending}
        />
      ) : null}


      {ctx.tab === "integrations" ? (
        <IntegrationsPanel
          pancakeQuery={ctx.pancakeQuery}
          webhookQuery={ctx.webhookQuery}
          pancakeForm={ctx.pancakeForm}
          onFormChange={ctx.updatePancakeForm}
          onSave={() => ctx.pancakeMutation.mutate()}
          onDisconnect={() => ctx.deletePancakeMutation.mutate()}
          onCopyWebhook={() => {
            if (ctx.webhookQuery.data?.webhookUrl)
              void navigator.clipboard?.writeText(ctx.webhookQuery.data.webhookUrl);
            ctx.setNotice("Đã sao chép URL webhook.");

          }}
          isSavePending={ctx.pancakeMutation.isPending}
          isDisconnectPending={ctx.deletePancakeMutation.isPending}
        />
      ) : null}

      {ctx.tab === "audit" ? <AuditPanel auditLogs={ctx.auditLogs} /> : null}

      {/* Modals */}
      <UserModal
        mode={ctx.userModal}
        onClose={() => ctx.setUserModal(null)}
        form={ctx.userForm}
        roles={ctx.roles}
        onFormChange={ctx.setUserForm}
        onToggleRole={ctx.toggleRoleName}
        onSubmit={() => ctx.userMutation.mutate()}
        isPending={ctx.userMutation.isPending}
        error={ctx.userMutation.error}
      />

      <RoleModal
        mode={ctx.roleModal}
        onClose={() => ctx.setRoleModal(null)}
        form={ctx.roleForm}
        onFormChange={ctx.setRoleForm}
        onSubmit={() => ctx.roleMutation.mutate()}
        isPending={ctx.roleMutation.isPending}
        error={ctx.roleMutation.error}
      />

      <KeyModal
        open={ctx.keyModalOpen}
        onClose={() => ctx.setKeyModalOpen(false)}
        form={ctx.keyForm}
        onFormChange={ctx.setKeyForm}
        onSubmit={() => ctx.keyMutation.mutate()}
        isPending={ctx.keyMutation.isPending}
        error={ctx.keyMutation.error}
      />

      {/* Processing overlay */}
      {ctx.actionPending ? (
        <div className="fixed bottom-4 right-4 z-50 rounded bg-secondary px-4 py-2 text-body-md text-white shadow-xl">
          <span className="material-symbols-outlined animate-spin text-[18px]">autorenew</span>
          <span className="ml-2">Đang xử lý...</span>
        </div>
      ) : null}
    </AppShell>
  );
}