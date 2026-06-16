export interface NavItem {
  readonly icon: string;
  readonly label: string;
  readonly to: string;
}

export const NAV_ITEMS: readonly NavItem[] = [
  { icon: "dashboard", label: "Dashboard tổng quan", to: "/" },
  { icon: "person_search", label: "Khách hàng tiềm năng", to: "/leads" },
  { icon: "all_inbox", label: "Hội thoại đa kênh", to: "/conversations" },
  { icon: "campaign", label: "Quản lý nội dung", to: "/content" },
  { icon: "description", label: "Thư viện tài liệu", to: "/documents" },
  { icon: "monitoring", label: "Báo cáo thống kê", to: "/analytics" },
  { icon: "notifications", label: "Trung tâm thông báo", to: "/notifications" },
  { icon: "account_tree", label: "Sơ đồ tiến trình", to: "/workflow" },
  { icon: "receipt_long", label: "Nhật ký tác vụ", to: "/logs" },
  { icon: "toll", label: "Quản lý hạn ngạch Token", to: "/tokens" },
  { icon: "inventory_2", label: "Kho tri thức Markdown", to: "/kb" },
  { icon: "settings_suggest", label: "Cấu hình Prompt gốc", to: "/prompts" },
];

export const NAV_SYSTEM: NavItem = { icon: "settings", label: "Hệ thống", to: "/system" };
