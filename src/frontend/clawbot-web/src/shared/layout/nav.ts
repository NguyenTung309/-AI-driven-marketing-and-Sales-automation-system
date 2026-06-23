export interface NavItem {
  readonly icon: string;
  readonly label: string;
  readonly to: string;
}

export const NAV_ITEMS: readonly NavItem[] = [
  { icon: "dashboard", label: "Tá»•ng quan", to: "/" },
  { icon: "person_search", label: "KhÃ¡ch hÃ ng tiá»m nÄƒng", to: "/leads" },
  { icon: "all_inbox", label: "Hội thoại đa kênh", to: "/conversations" },
  { icon: "support_agent", label: "Agent Hub", to: "/agent-hub" },
  { icon: "campaign", label: "Quản lý nội dung", to: "/content" },
  { icon: "description", label: "ThÆ° viá»‡n tÃ i liá»‡u", to: "/documents" },
  { icon: "monitoring", label: "BÃ¡o cÃ¡o thá»‘ng kÃª", to: "/analytics" },
  { icon: "notifications", label: "Trung tÃ¢m thÃ´ng bÃ¡o", to: "/notifications" },
  { icon: "account_tree", label: "SÆ¡ Ä‘á»“ tiáº¿n trÃ¬nh", to: "/workflow" },
  { icon: "apartment", label: "Pixel Agents Office", to: "/agents-office" },
  { icon: "receipt_long", label: "Nháº­t kÃ½ tÃ¡c vá»¥", to: "/logs" },
  { icon: "toll", label: "Quáº£n lÃ½ chi phÃ­ AI", to: "/tokens" },
  { icon: "inventory_2", label: "Kho tri thá»©c", to: "/kb" },
  { icon: "settings_suggest", label: "HÆ°á»›ng dáº«n agent", to: "/prompts" },
];

export const NAV_SYSTEM: NavItem = { icon: "settings", label: "Há»‡ thá»‘ng", to: "/system" };
export const NAV_CHANNELS: NavItem = { icon: "lan", label: "Kenh giao tiep", to: "/system/channels" };
