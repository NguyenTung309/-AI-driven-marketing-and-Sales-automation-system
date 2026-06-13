import { useState } from "react";
import { AppShell } from "@/shared/layout/AppShell";
import { Card, DataTable, StatusPill, ToggleSwitch, type Column } from "@/shared/ui";
import { ChangePasswordDialog } from "./ChangePasswordDialog";

type ProfileTab = "info" | "permissions" | "security";

const TABS: readonly { readonly key: ProfileTab; readonly label: string }[] = [
  { key: "info", label: "Thông tin cơ bản" },
  { key: "permissions", label: "Phân quyền trực thuộc" },
  { key: "security", label: "Nhật ký bảo mật" },
];

interface Permission {
  readonly label: string;
  readonly granted: boolean;
}

const PERMISSIONS: readonly Permission[] = [
  { label: "Truy cập báo cáo KPI (Agent-Report)", granted: true },
  { label: "Cấu hình khuôn mẫu Prompt (Agent-Config)", granted: true },
  { label: "Quản lý Kho tri thức (Knowledge Base)", granted: true },
  { label: "Thiết lập cổng thanh toán", granted: false },
];

interface LoginLog {
  readonly id: string;
  readonly time: string;
  readonly ip: string;
  readonly device: string;
}

const LOGIN_LOGS: readonly LoginLog[] = [
  { id: "1", time: "15:30 10/10/2026", ip: "192.168.1.5", device: "Chrome - Windows" },
  { id: "2", time: "10:15 09/10/2026", ip: "192.168.1.5", device: "Firefox - macOS" },
  { id: "3", time: "08:45 08/10/2026", ip: "203.113.1.2", device: "Safari - iPhone" },
];

const LOG_COLUMNS: readonly Column<LoginLog>[] = [
  { key: "time", header: "Thời gian", render: (r) => r.time },
  { key: "ip", header: "IP Truy cập", render: (r) => <span className="font-mono">{r.ip}</span> },
  { key: "device", header: "Thiết bị / Trình duyệt", render: (r) => r.device },
  {
    key: "status",
    header: "Trạng thái",
    className: "text-right",
    render: () => <StatusPill tone="success">Thành công</StatusPill>,
  },
];

const fieldInput =
  "w-full px-4 py-3 border border-outline-variant rounded text-body-lg focus:outline-none focus:border-primary focus:ring-1 focus:ring-primary/20 transition-all";

function Field({
  label,
  value,
  disabled = false,
  type = "text",
}: {
  readonly label: string;
  readonly value: string;
  readonly disabled?: boolean;
  readonly type?: string;
}) {
  return (
    <div className="flex flex-col gap-1">
      <label className="text-label-lg text-on-surface">{label}</label>
      <div className="relative">
        <input
          type={type}
          defaultValue={value}
          disabled={disabled}
          className={`${fieldInput} ${disabled ? "bg-surface-container-low text-on-surface-variant cursor-not-allowed" : ""}`}
        />
        {disabled ? (
          <span className="absolute right-3 top-1/2 -translate-y-1/2 material-symbols-outlined text-[18px] text-on-surface-variant">lock</span>
        ) : null}
      </div>
    </div>
  );
}

function TwoFactorRow() {
  const [on, setOn] = useState(true);
  return (
    <div className="p-6 bg-surface-container-low rounded-xl border border-dashed border-outline-variant flex items-center justify-between">
      <div className="flex items-center gap-4">
        <div className="w-12 h-12 rounded-full bg-white flex items-center justify-center shadow-sm">
          <span className="material-symbols-outlined text-primary">security</span>
        </div>
        <div>
          <h5 className="font-bold text-on-surface">Xác thực 2 yếu tố (2FA)</h5>
          <p className="text-body-md text-on-surface-variant">Tăng cường bảo mật cho tài khoản quản trị của bạn.</p>
        </div>
      </div>
      <ToggleSwitch checked={on} onChange={setOn} />
    </div>
  );
}

function InfoTab() {
  return (
    <div className="space-y-6">
      <div className="grid grid-cols-1 md:grid-cols-2 gap-x-6 gap-y-6">
        <Field label="Họ và tên" value="Nguyễn Văn A" />
        <Field label="Địa chỉ Email" value="admin@hoc-ba.edu.vn" type="email" disabled />
        <Field label="Số điện thoại Zalo" value="0987654321" />
        <Field label="Phòng ban / Bộ phận" value="Phân hệ Quản trị & Điều hành" disabled />
        <Field label="Ngày sinh" value="1990-01-01" type="date" />
        <div className="flex flex-col gap-1">
          <label className="text-label-lg text-on-surface">Vị trí công tác</label>
          <select className={`${fieldInput} bg-white`}>
            <option>Cơ sở chính - TP. Hồ Chí Minh</option>
            <option>Cơ sở 2 - Hà Nội</option>
            <option>Cơ sở 3 - Đà Nẵng</option>
          </select>
        </div>
      </div>
      <div className="flex justify-end">
        <button
          type="button"
          className="px-8 py-4 bg-primary text-on-primary font-bold text-label-lg rounded shadow-lg hover:bg-primary-hover active:scale-95 transition-all flex items-center gap-2"
        >
          <span className="material-symbols-outlined">save</span>
          LƯU THÔNG TIN CÁ NHÂN
        </button>
      </div>
      <TwoFactorRow />
    </div>
  );
}

function PermissionsTab() {
  return (
    <div className="flex flex-col gap-6">
      <h4 className="font-bold text-on-surface text-body-lg">Danh sách quyền hạn được cấp</h4>
      <div className="space-y-4">
        {PERMISSIONS.map((p) => (
          <div key={p.label} className="flex items-center gap-4">
            <input
              type="checkbox"
              checked={p.granted}
              disabled
              readOnly
              className="w-5 h-5 rounded border-outline-variant text-primary cursor-not-allowed"
            />
            <span className={`text-body-lg ${p.granted ? "text-on-surface" : "text-on-surface-variant"}`}>{p.label}</span>
          </div>
        ))}
      </div>
      <p className="text-body-md text-on-surface-variant/70 italic">
        Quyền hạn do Hệ thống cấp phát. Vui lòng liên hệ Admin để thay đổi.
      </p>
    </div>
  );
}

export default function ProfilePage() {
  const [tab, setTab] = useState<ProfileTab>("info");
  const [pwOpen, setPwOpen] = useState(false);

  return (
    <AppShell title="Cài đặt tài khoản">
      <header className="mb-8">
        <h1 className="text-headline-lg text-on-surface">Cài đặt tài khoản</h1>
        <p className="text-body-lg text-on-surface-variant">Quản lý thông tin cá nhân và cấu hình bảo mật hệ thống.</p>
      </header>

      <div className="grid grid-cols-1 lg:grid-cols-10 gap-6 items-start">
        {/* Left: identity + stats */}
        <div className="lg:col-span-3 flex flex-col gap-6">
          <Card className="flex flex-col items-center">
            <div className="relative mb-6">
              <div className="w-32 h-32 rounded-full border-4 border-surface-container bg-primary text-on-primary flex items-center justify-center text-4xl font-bold">
                NA
              </div>
              <button
                type="button"
                aria-label="Đổi ảnh đại diện"
                className="absolute bottom-1 right-1 w-10 h-10 bg-primary text-on-primary rounded-full flex items-center justify-center border-4 border-white hover:scale-110 transition-transform shadow-lg"
              >
                <span className="material-symbols-outlined text-[18px]">photo_camera</span>
              </button>
            </div>
            <div className="text-center mb-8">
              <h3 className="text-headline-md font-bold text-on-surface mb-1">Nguyễn Văn A</h3>
              <div className="inline-flex items-center px-3 py-1 bg-primary/10 text-primary rounded font-bold text-label-lg mb-2">
                Quản trị viên hệ thống
              </div>
              <div className="flex items-center justify-center gap-1 text-body-md text-on-surface-variant">
                <span className="w-2 h-2 rounded-full bg-success" />
                <span>Đang hoạt động</span>
              </div>
            </div>
            <button
              type="button"
              onClick={() => setPwOpen(true)}
              className="w-full py-3 px-4 border border-primary text-primary font-bold text-label-lg rounded bg-white hover:bg-primary/5 transition-colors flex items-center justify-center gap-2"
            >
              <span className="material-symbols-outlined text-[20px]">lock_reset</span>
              ĐỔI MẬT KHẨU
            </button>
          </Card>

          <div className="bg-surface-container-lowest rounded-lg p-card-padding border border-outline border-l-4 border-l-primary">
            <h4 className="text-label-lg text-on-surface-variant uppercase mb-4">Thống kê hoạt động</h4>
            <div className="grid grid-cols-2 gap-4">
              <div>
                <p className="text-body-md text-on-surface-variant">Lần cuối</p>
                <p className="font-bold text-on-surface">2 phút trước</p>
              </div>
              <div>
                <p className="text-body-md text-on-surface-variant">Thiết bị</p>
                <p className="font-bold text-on-surface">MacBook Pro</p>
              </div>
            </div>
          </div>
        </div>

        {/* Right: tabbed panel */}
        <div className="lg:col-span-7">
          <div className="bg-surface-container-lowest rounded-lg border border-outline overflow-hidden">
            <div className="flex border-b border-outline px-6 pt-4 gap-2">
              {TABS.map((t) => (
                <button
                  key={t.key}
                  type="button"
                  onClick={() => setTab(t.key)}
                  className={`px-4 py-4 font-bold text-label-lg transition-colors ${
                    tab === t.key
                      ? "text-primary border-b-2 border-primary"
                      : "text-on-surface-variant hover:text-primary"
                  }`}
                >
                  {t.label}
                </button>
              ))}
            </div>
            <div className="p-6">
              {tab === "info" ? <InfoTab /> : null}
              {tab === "permissions" ? <PermissionsTab /> : null}
              {tab === "security" ? (
                <DataTable columns={LOG_COLUMNS} rows={LOGIN_LOGS} rowKey={(r) => r.id} />
              ) : null}
            </div>
          </div>
        </div>
      </div>

      <ChangePasswordDialog open={pwOpen} onClose={() => setPwOpen(false)} />
    </AppShell>
  );
}
