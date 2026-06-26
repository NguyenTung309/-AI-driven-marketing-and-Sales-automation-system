export interface NavItem {
  readonly icon: string;
  readonly label: string;
  readonly to: string;
}

export const NAV_ITEMS: readonly NavItem[] = [
  { icon: "dashboard", label: "Tổng quan", to: "/" },
  { icon: "person_search", label: "Khách hàng tiềm năng", to: "/leads" },
  { icon: "all_inbox", label: "Hội thoại đa kênh", to: "/conversations" },
  { icon: "support_agent", label: "Agent Hub", to: "/agent-hub" },
  { icon: "campaign", label: "Quản lý nội dung", to: "/content" },
  { icon: "description", label: "Thư viện tài liệu", to: "/documents" },
  { icon: "monitoring", label: "Báo cáo thống kê", to: "/analytics" },
  { icon: "notifications", label: "Trung tâm thông báo", to: "/notifications" },
  { icon: "account_tree", label: "Sơ đồ tiến trình", to: "/workflow" },
  { icon: "apartment", label: "Pixel Agents Office", to: "/agents-office" },
  { icon: "receipt_long", label: "Nhật ký tác vụ", to: "/logs" },
  { icon: "toll", label: "Quản lý chi phí AI", to: "/tokens" },
  { icon: "inventory_2", label: "Kho tri thức", to: "/kb" },
  { icon: "settings_suggest", label: "Hướng dẫn agent", to: "/prompts" },
];

export const NAV_SYSTEM: NavItem = { icon: "settings", label: "Hệ thống", to: "/system" };
export const NAV_CHANNELS: NavItem = { icon: "lan", label: "Kênh giao tiếp", to: "/system/channels" };
