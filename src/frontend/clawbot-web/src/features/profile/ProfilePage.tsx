import { useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AxiosError } from "axios";
import { AppShell } from "@/shared/layout/AppShell";
import { Alert, Button, Card, DataTable, StatusPill, ToggleSwitch, type Column } from "@/shared/ui";
import { disableTwoFactor, getMe } from "@/shared/api/auth";
import {
  getProfile,
  listLoginHistory,
  updateProfile,
  uploadProfileAvatar,
  type LoginHistoryItem,
  type UserProfile,
} from "@/shared/api/profile";
import { ChangePasswordDialog } from "./ChangePasswordDialog";
import { TwoFactorSetupDialog } from "./TwoFactorSetupDialog";

type ProfileTab = "info" | "permissions" | "security";

interface ProfileForm {
  readonly displayName: string;
  readonly phone: string;
  readonly dateOfBirth: string;
}

const TABS: readonly { readonly key: ProfileTab; readonly label: string }[] = [
  { key: "info", label: "Thông tin cơ bản" },
  { key: "permissions", label: "Phân quyền trực thuộc" },
  { key: "security", label: "Nhật ký bảo mật" },
];

const FALLBACK_PERMS: readonly string[] = [
  "Truy cập báo cáo KPI",
  "Cấu hình khuôn mẫu Prompt",
  "Quản lý Kho tri thức",
];

const fieldInput =
  "w-full px-4 py-3 border border-outline-variant rounded text-body-lg focus:outline-none focus:border-primary focus:ring-1 focus:ring-primary/20 transition-all";

function errorMessage(error: unknown): string {
  if (!error) return "";
  if (error instanceof AxiosError) {
    const data = error.response?.data as { error?: string; title?: string; detail?: string; message?: string } | string[] | string | undefined;
    if (Array.isArray(data)) return data.join(", ");
    if (typeof data === "string") return data;
    return data?.message ?? data?.error ?? data?.title ?? data?.detail ?? error.message;
  }
  if (error instanceof Error) return error.message;
  return "Không xử lý được yêu cầu. Vui lòng thử lại.";
}

function formatDateTime(value: string): string {
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

function initials(profile: UserProfile | undefined): string {
  const source = profile?.displayName || profile?.email || "NA";
  const chars = source
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join("");
  return chars || "NA";
}

function profileSourceKey(profile: UserProfile | undefined): string {
  if (!profile) return "empty";
  return [profile.id, profile.displayName, profile.phone ?? "", profile.dateOfBirth ?? ""].join("|");
}

function profileToForm(profile: UserProfile | undefined): ProfileForm {
  return {
    displayName: profile?.displayName ?? "",
    phone: profile?.phone ?? "",
    dateOfBirth: profile?.dateOfBirth ?? "",
  };
}

function Field({
  label,
  value,
  disabled = false,
  type = "text",
  onChange,
}: {
  readonly label: string;
  readonly value: string;
  readonly disabled?: boolean;
  readonly type?: string;
  readonly onChange?: (value: string) => void;
}) {
  return (
    <div className="flex flex-col gap-1">
      <label className="text-label-lg text-on-surface">
        {label}
        <div className="relative mt-1">
          <input
            type={type}
            value={value}
            disabled={disabled}
            onChange={(event) => onChange?.(event.target.value)}
            className={`${fieldInput} ${disabled ? "bg-surface-container-low text-on-surface-variant cursor-not-allowed" : ""}`}
          />
          {disabled ? (
            <span className="absolute right-3 top-1/2 -translate-y-1/2 material-symbols-outlined text-[18px] text-on-surface-variant">lock</span>
          ) : null}
        </div>
      </label>
    </div>
  );
}

function TwoFactorRow() {
  const [on, setOn] = useState(false);
  const [setupOpen, setSetupOpen] = useState(false);

  async function toggle(next: boolean) {
    if (next) {
      setSetupOpen(true);
      return;
    }

    try {
      await disableTwoFactor();
    } finally {
      setOn(false);
    }
  }

  return (
    <>
      <div className="flex items-center justify-between rounded-xl border border-dashed border-outline-variant bg-surface-container-low p-6">
        <div className="flex items-center gap-4">
          <div className="flex size-12 items-center justify-center rounded-full bg-white shadow-sm">
            <span className="material-symbols-outlined text-primary">security</span>
          </div>
          <div>
            <h5 className="font-bold text-on-surface">Xác thực 2 yếu tố (2FA)</h5>
            <p className="text-body-md text-on-surface-variant">Tăng cường bảo mật cho tài khoản quản trị của bạn.</p>
          </div>
        </div>
        <ToggleSwitch checked={on} onChange={toggle} />
      </div>
      <TwoFactorSetupDialog
        key={setupOpen ? "2fa-open" : "2fa-closed"}
        open={setupOpen}
        onClose={() => setSetupOpen(false)}
        onVerified={() => setOn(true)}
      />
    </>
  );
}

function InfoTab({
  email,
  form,
  onFormChange,
  onSave,
  saving,
  canSave,
  notice,
  error,
}: {
  readonly email: string;
  readonly form: ProfileForm;
  readonly onFormChange: (patch: Partial<ProfileForm>) => void;
  readonly onSave: () => void;
  readonly saving: boolean;
  readonly canSave: boolean;
  readonly notice: string | null;
  readonly error: unknown;
}) {
  return (
    <div className="space-y-6">
      {notice ? <Alert tone="success">{notice}</Alert> : null}
      {error ? <Alert tone="error">{errorMessage(error)}</Alert> : null}
      <div className="grid grid-cols-1 gap-x-6 gap-y-6 md:grid-cols-2">
        <Field label="Họ và tên" value={form.displayName} onChange={(displayName) => onFormChange({ displayName })} />
        <Field label="Địa chỉ Email" value={email} type="email" disabled />
        <Field label="Số điện thoại Zalo" value={form.phone} onChange={(phone) => onFormChange({ phone })} />
        <Field label="Phòng ban / Bộ phận" value="Phân hệ Quản trị & Điều hành" disabled />
        <Field label="Ngày sinh" value={form.dateOfBirth} type="date" onChange={(dateOfBirth) => onFormChange({ dateOfBirth })} />
        <div className="flex flex-col gap-1">
          <label className="text-label-lg text-on-surface">
            Vị trí công tác
            <select className={`${fieldInput} mt-1 bg-white`} defaultValue="main">
              <option value="main">Cơ sở chính - TP. Hồ Chí Minh</option>
              <option value="hn">Cơ sở 2 - Hà Nội</option>
              <option value="dn">Cơ sở 3 - Đà Nẵng</option>
            </select>
          </label>
        </div>
      </div>
      <div className="flex justify-end">
        <Button type="button" className="px-8 py-4 shadow-lg active:scale-95" onClick={onSave} disabled={saving || !canSave}>
          <span className="material-symbols-outlined">save</span>
          {saving ? "ĐANG LƯU..." : "LƯU THÔNG TIN CÁ NHÂN"}
        </Button>
      </div>
      <TwoFactorRow />
    </div>
  );
}

function PermissionsTab({ perms }: { readonly perms: readonly string[] }) {
  const list = perms.length > 0 ? perms : FALLBACK_PERMS;
  return (
    <div className="flex flex-col gap-6">
      <h4 className="text-body-lg font-bold text-on-surface">Danh sách quyền hạn được cấp ({list.length})</h4>
      <div className="space-y-4">
        {list.map((permission) => (
          <div key={permission} className="flex items-center gap-4">
            <input type="checkbox" checked disabled readOnly className="size-5 cursor-not-allowed rounded border-outline-variant text-primary" />
            <span className="font-mono text-body-lg text-on-surface">{permission}</span>
          </div>
        ))}
      </div>
      <p className="text-body-md italic text-on-surface-variant/70">
        Quyền hạn do hệ thống cấp phát theo vai trò. Vui lòng liên hệ Admin để thay đổi.
      </p>
    </div>
  );
}

const LOG_COLUMNS: readonly Column<LoginHistoryItem>[] = [
  { key: "occurredAt", header: "Thời gian", render: (row) => formatDateTime(row.occurredAt) },
  { key: "ipAddress", header: "IP Truy cập", render: (row) => <span className="font-mono">{row.ipAddress ?? "Không rõ"}</span> },
  { key: "userAgent", header: "Thiết bị / Trình duyệt", render: (row) => row.userAgent ?? "Không rõ" },
  { key: "status", header: "Trạng thái", className: "text-right", render: () => <StatusPill tone="success">Thành công</StatusPill> },
];

function SecurityTab({
  rows,
  loading,
  error,
}: {
  readonly rows: readonly LoginHistoryItem[];
  readonly loading: boolean;
  readonly error: unknown;
}) {
  if (loading) return <div className="rounded-lg border border-outline bg-white p-6 text-body-md text-on-surface-variant">Đang tải nhật ký bảo mật...</div>;
  if (error) return <Alert tone="error">{errorMessage(error)}</Alert>;
  if (!rows.length) return <div className="rounded-lg border border-outline bg-white p-6 text-body-md text-on-surface-variant">Chưa có lần đăng nhập nào được ghi nhận.</div>;
  return <DataTable columns={LOG_COLUMNS} rows={rows} rowKey={(row) => row.id} />;
}

export default function ProfilePage() {
  const queryClient = useQueryClient();
  const avatarInputRef = useRef<HTMLInputElement | null>(null);
  const [tab, setTab] = useState<ProfileTab>("info");
  const [pwOpen, setPwOpen] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [failedAvatarUrl, setFailedAvatarUrl] = useState<string | null>(null);
  const [profileDraft, setProfileDraft] = useState<{ readonly sourceKey: string; readonly form: ProfileForm } | null>(null);
  const { data: me } = useQuery({ queryKey: ["me"], queryFn: getMe });
  const profileQuery = useQuery({ queryKey: ["profile"], queryFn: getProfile });
  const loginHistoryQuery = useQuery({
    queryKey: ["profile", "login-history"],
    queryFn: () => listLoginHistory({ page: 1, pageSize: 20 }),
    enabled: tab === "security",
  });

  const profile = profileQuery.data;
  const sourceKey = profileSourceKey(profile);
  const serverForm = profileToForm(profile);
  const profileForm = profileDraft?.sourceKey === sourceKey ? profileDraft.form : serverForm;

  const saveProfileMutation = useMutation({
    mutationFn: () =>
      updateProfile({
        displayName: profileForm.displayName.trim(),
        phone: profileForm.phone.trim() || null,
        dateOfBirth: profileForm.dateOfBirth || null,
      }),
    onSuccess: async () => {
      setProfileDraft(null);
      setNotice("Đã lưu thông tin cá nhân.");
      await queryClient.invalidateQueries({ queryKey: ["profile"] });
    },
  });

  const uploadAvatarMutation = useMutation({
    mutationFn: uploadProfileAvatar,
    onSuccess: async () => {
      setNotice("Đã cập nhật ảnh đại diện.");
      await queryClient.invalidateQueries({ queryKey: ["profile"] });
    },
  });

  const roleBadge = profile?.roles?.[0] ?? me?.roles?.[0] ?? "Quản trị viên hệ thống";
  const perms = me?.permissions ?? [];
  const avatarLabel = initials(profile);
  const avatarUrl = profile?.avatarUrl ?? null;
  const showAvatar = Boolean(avatarUrl && failedAvatarUrl !== avatarUrl);
  const displayName = profile?.displayName || profileForm.displayName || "Tài khoản Học Bá";
  const activeTenant = profile?.tenantSlug ?? me?.tenantSlug;
  const loginHistory = loginHistoryQuery.data?.items ?? [];
  const infoError = profileQuery.error ?? saveProfileMutation.error ?? uploadAvatarMutation.error;

  const roleCount = profile?.roles?.length ?? me?.roles?.length ?? 0;
  const canSave = profileForm.displayName.trim().length > 0 && !saveProfileMutation.isPending;

  return (
    <AppShell title="Cài đặt tài khoản">
      <header className="mb-8">
        <h1 className="text-headline-lg text-on-surface">Cài đặt tài khoản</h1>
        <p className="text-body-lg text-on-surface-variant">Quản lý thông tin cá nhân và cấu hình bảo mật hệ thống.</p>
      </header>

      <div className="grid grid-cols-1 items-start gap-6 lg:grid-cols-10">
        <div className="flex flex-col gap-6 lg:col-span-3">
          <Card className="flex flex-col items-center">
            <div className="relative mb-6">
              <div className="flex size-32 items-center justify-center overflow-hidden rounded-full border-4 border-surface-container bg-primary text-4xl font-bold text-on-primary">
                {showAvatar ? (
                  <img
                    src={avatarUrl!}
                    alt=""
                    className="size-full object-cover"
                    onError={() => setFailedAvatarUrl(avatarUrl)}
                  />
                ) : (
                  avatarLabel
                )}
              </div>
              <button
                type="button"
                aria-label="Đổi ảnh đại diện"
                onClick={() => avatarInputRef.current?.click()}
                disabled={uploadAvatarMutation.isPending}
                className="absolute bottom-1 right-1 flex size-10 items-center justify-center rounded-full border-4 border-white bg-primary text-on-primary shadow-lg transition-transform hover:scale-110 disabled:pointer-events-none disabled:opacity-60"
              >
                <span className="material-symbols-outlined text-[18px]">photo_camera</span>
              </button>
              <input
                ref={avatarInputRef}
                type="file"
                accept="image/*"
                className="hidden"
                onChange={(event) => {
                  const file = event.target.files?.[0];
                  if (file) uploadAvatarMutation.mutate(file);
                  event.target.value = "";
                }}
              />
            </div>
            <div className="mb-8 text-center">
              <h3 className="mb-1 text-headline-md font-bold text-on-surface">{displayName}</h3>
              <div className="mb-2 inline-flex items-center rounded bg-primary/10 px-3 py-1 text-label-lg font-bold text-primary">{roleBadge}</div>
              <div className="flex items-center justify-center gap-1 text-body-md text-on-surface-variant">
                <span className="size-2 rounded-full bg-success" />
                <span>{activeTenant ? `Tenant: ${activeTenant}` : "Đang hoạt động"}</span>
              </div>
            </div>
            <button
              type="button"
              onClick={() => setPwOpen(true)}
              className="flex w-full items-center justify-center gap-2 rounded border border-primary bg-white px-4 py-3 text-label-lg font-bold text-primary transition-colors hover:bg-primary/5"
            >
              <span className="material-symbols-outlined text-[20px]">lock_reset</span>
              ĐỔI MẬT KHẨU
            </button>
          </Card>

          <div className="rounded-lg border border-outline border-l-4 border-l-primary bg-surface-container-lowest p-card-padding">
            <h4 className="mb-4 text-label-lg uppercase text-on-surface-variant">Thống kê hoạt động</h4>
            <div className="grid grid-cols-2 gap-4">
              <div>
                <p className="text-body-md text-on-surface-variant">Vai trò</p>
                <p className="font-bold text-on-surface">{roleCount}</p>
              </div>
              <div>
                <p className="text-body-md text-on-surface-variant">Quyền hạn</p>
                <p className="font-bold text-on-surface">{perms.length}</p>
              </div>
            </div>
          </div>
        </div>

        <div className="lg:col-span-7">
          <div className="overflow-hidden rounded-lg border border-outline bg-surface-container-lowest">
            <div className="flex gap-2 overflow-x-auto border-b border-outline px-6 pt-4">
              {TABS.map((item) => (
                <button
                  key={item.key}
                  type="button"
                  onClick={() => setTab(item.key)}
                  className={`shrink-0 px-4 py-4 text-label-lg font-bold transition-colors ${
                    tab === item.key ? "border-b-2 border-primary text-primary" : "text-on-surface-variant hover:text-primary"
                  }`}
                >
                  {item.label}
                </button>
              ))}
            </div>
            <div className="p-6">
              {tab === "info" ? (
                <InfoTab
                  email={profile?.email ?? ""}
                  form={profileForm}
                  onFormChange={(patch) => {
                    setNotice(null);
                    setProfileDraft((current) => {
                      const base = current?.sourceKey === sourceKey ? current.form : profileForm;
                      return { sourceKey, form: { ...base, ...patch } };
                    });
                  }}
                  onSave={() => saveProfileMutation.mutate()}
                  saving={saveProfileMutation.isPending}
                  canSave={canSave}
                  notice={notice}
                  error={infoError}
                />
              ) : null}
              {tab === "permissions" ? <PermissionsTab perms={perms} /> : null}
              {tab === "security" ? (
                <SecurityTab rows={loginHistory} loading={loginHistoryQuery.isLoading} error={loginHistoryQuery.error} />
              ) : null}
            </div>
          </div>
        </div>
      </div>

      <ChangePasswordDialog
        open={pwOpen}
        onClose={() => setPwOpen(false)}
        onChanged={() => {
          setNotice("Đã cập nhật mật khẩu.");
          setTab("info");
        }}
      />
    </AppShell>
  );
}
