import { NAV_ITEMS, NAV_SYSTEM } from "@/shared/layout/nav";
import { useRole } from "./authStore";

// Role-based UI visibility (RBAC_Redesign). Backend Identity role names (RbacSeeder).
// Đây chỉ là ẩn/hiện UI — backend vẫn enforce quyền thật trên API.
export type AppRole = "Admin" | "SalesLead" | "QA" | "Sale" | "Marketer" | "Viewer";

const ALL: readonly AppRole[] = ["Admin", "SalesLead", "QA", "Sale", "Marketer", "Viewer"];

// Route (prefix) → các role được thấy/vào. Admin short-circuit trong canAccessRoute
// nên không cần liệt kê, nhưng liệt kê cho dễ đọc. Sửa phân quyền UI: chỉ sửa ở đây.
export const ROUTE_ACCESS: Record<string, readonly AppRole[]> = {
  "/": ALL,
  "/leads": ["Admin", "SalesLead", "Sale"],
  "/conversations": ["Admin", "SalesLead", "Sale"],
  "/inbox": ["Admin", "SalesLead", "Sale"],
  "/agent-hub": ["Admin", "SalesLead", "Sale"],
  "/content": ["Admin", "Marketer"],
  "/documents": ["Admin", "SalesLead", "Sale", "Marketer"],
  "/analytics": ALL,
  "/notifications": ALL,
  "/agents": ["Admin", "SalesLead", "QA", "Marketer"],
  "/agents-office": ["Admin", "SalesLead", "QA", "Marketer"],
  "/workflow": ["Admin", "SalesLead", "QA", "Marketer"],
  "/orchestration": ["Admin", "SalesLead", "QA", "Marketer"],
  "/llm-providers": ["Admin"],
  "/kb": ["Admin", "SalesLead", "QA", "Sale", "Marketer"],
  "/system": ["Admin", "SalesLead"],
  "/logs": ["Admin"],
  "/tokens": ["Admin"],
  "/prompts": ["Admin"],
  "/profile": ALL,
};

// Quy tắc con trong trang (không có route riêng).
export const FEATURE_ACCESS = {
  // Tab "Hiệu suất Agent" trong Báo cáo thống kê: ẩn với Sale & Marketer.
  "analytics.tab.agent": ["SalesLead", "QA", "Viewer"] as readonly AppRole[],
} satisfies Record<string, readonly AppRole[]>;

export type FeatureKey = keyof typeof FEATURE_ACCESS;

/** Longest-prefix match nên "/system/channels" ăn theo "/system". Route ngoài ma trận: không chặn. */
export function canAccessRoute(role: string | null, pathname: string): boolean {
  if (role === "Admin") return true;
  const key =
    pathname === "/"
      ? "/"
      : Object.keys(ROUTE_ACCESS)
          .filter((k) => k !== "/" && (pathname === k || pathname.startsWith(`${k}/`)))
          .sort((a, b) => b.length - a.length)[0];
  if (!key) return true;
  return role != null && ROUTE_ACCESS[key].includes(role as AppRole);
}

export function canUseFeature(role: string | null, feature: FeatureKey): boolean {
  if (role === "Admin") return true;
  return role != null && FEATURE_ACCESS[feature].includes(role as AppRole);
}

/** Nav đã lọc theo role hiện tại — dùng chung cho Sidebar (desktop) và Topbar (drawer mobile). */
export function useVisibleNav() {
  const role = useRole();
  return {
    items: NAV_ITEMS.filter((item) => canAccessRoute(role, item.to)),
    system: canAccessRoute(role, NAV_SYSTEM.to) ? NAV_SYSTEM : null,
  };
}
