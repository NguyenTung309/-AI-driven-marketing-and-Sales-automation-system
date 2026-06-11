// Sidebar navigation model — labels/icons from the Stitch "Học Bá Admin Dashboard" sidebar.
export interface NavItem {
  readonly icon: string; // Material Symbols name
  readonly label: string;
  readonly to: string;
}

export const NAV_ITEMS: readonly NavItem[] = [
  { icon: "dashboard", label: "Dashboard tổng quan", to: "/" },
  { icon: "account_tree", label: "Sơ đồ tiến trình", to: "/workflow" },
  { icon: "receipt_long", label: "Nhật ký tác vụ", to: "/logs" },
  { icon: "toll", label: "Quản lý hạn ngạch Token", to: "/tokens" },
  { icon: "inventory_2", label: "Kho tri thức Markdown", to: "/kb" },
  { icon: "settings_suggest", label: "Cấu hình Prompt gốc", to: "/prompts" },
];

export const NAV_SYSTEM: NavItem = { icon: "settings", label: "Hệ thống", to: "/system" };
