export interface NavItem {
  readonly icon: string;
  readonly label: string;
  readonly to: string;
}

export const NAV_ITEMS: readonly NavItem[] = [
  { icon: "dashboard", label: "Tổng quan", to: "/" },
  { icon: "person_search", label: "Khách hàng tiềm năng", to: "/leads" },
  { icon: "all_inbox", label: "Hội thoại đa kênh", to: "/conversations" },
  { icon: "campaign", label: "Quản lý nội dung", to: "/content" },
  { icon: "description", label: "Thư viện tài liệu", to: "/documents" },
  { icon: "monitoring", label: "Báo cáo thống kê", to: "/analytics" },
  { icon: "notifications", label: "Trung tâm thông báo", to: "/notifications" },
  { icon: "smart_toy", label: "Agents", to: "/agents" },
  { icon: "tune", label: "Cấu hình LLM", to: "/llm-providers" },
  { icon: "inventory_2", label: "Kho tri thức", to: "/kb" },
];

export const NAV_SYSTEM: NavItem = { icon: "settings", label: "Hệ thống", to: "/system" };
