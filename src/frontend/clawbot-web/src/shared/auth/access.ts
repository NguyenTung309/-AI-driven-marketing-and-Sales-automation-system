import { NAV_ITEMS, NAV_SYSTEM } from "@/shared/layout/nav";
import { useAuthStore, useRole } from "./authStore";

// Role-based UI visibility (RBAC_Redesign). Backend Identity role names (RbacSeeder).
// Đây chỉ là ẩn/hiện UI — backend vẫn enforce quyền thật trên API.
export type AppRole = "Admin" | "SalesLead" | "QA" | "Sale" | "Marketer" | "Viewer";

const ALL: readonly AppRole[] = ["Admin", "SalesLead", "QA", "Sale", "Marketer", "Viewer"];

// Route (prefix) → danh sách các mã quyền hợp lệ (chỉ cần có 1 trong số này là vào được).
// Nếu mảng rỗng [] có nghĩa là mọi role đều có thể truy cập mà không cần quyền cụ thể.
// Được map dựa theo file src/shared/Clawbot.Infrastructure/Identity/RbacSeeder.cs
export const ROUTE_PERMISSIONS: Record<string, string[]> = {
  "/": [],
  "/leads": ["leads:read", "leads:read:all", "lead.read"],
  "/conversations": ["conversations:read", "inbox.read"],
  "/inbox": ["conversations:read", "inbox.read"],
  "/agent-hub": ["sale-assist:use", "orchestration:view"],
  "/content": ["content:read"],
  "/documents": ["docs:read"],
  "/analytics": ["analytics:read"],
  "/notifications": [],
  "/agents": ["orchestration:view", "agent.read"],
  "/agents-office": ["orchestration:view", "agent.read"],
  "/workflow": ["orchestration:view"],
  "/orchestration": ["orchestration:view"],
  "/llm-providers": ["llm-configs:manage"],
  "/kb": ["kb:read"],
  "/system": ["system:config", "admin:users-manage", "admin:sale-manage", "rbac:manage", "admin:integration", "admin:jobs-hangfires", "system.logs"],
  "/logs": ["system.logs", "admin.audit"],
  "/tokens": ["api-keys:manage", "users:pancake-token:manage"],
  "/prompts": ["system:config", "admin:users-manage"],
  "/profile": [],
};

// Quy tắc con trong trang (không có route riêng).
export const FEATURE_ACCESS = {
  // Tab "Hiệu suất Agent" trong Báo cáo thống kê: ẩn với Sale & Marketer.
  "analytics.tab.agent": ["SalesLead", "QA", "Viewer"] as readonly AppRole[],
} satisfies Record<string, readonly AppRole[]>;

export type FeatureKey = keyof typeof FEATURE_ACCESS;

/** Longest-prefix match nên "/system/channels" ăn theo "/system". Route ngoài ma trận: không chặn. */
export function canAccessRoute(permissions: string[], role: string | null, pathname: string): boolean {
  if (role === "Admin") return true;
  const key =
    pathname === "/"
      ? "/"
      : Object.keys(ROUTE_PERMISSIONS)
          .filter((k) => k !== "/" && (pathname === k || pathname.startsWith(`${k}/`)))
          .sort((a, b) => b.length - a.length)[0];
  if (!key) return true;
  
  const requiredPerms = ROUTE_PERMISSIONS[key];
  if (requiredPerms.length === 0) return true;

  return requiredPerms.some((p) => permissions.includes(p));
}

export function canUseFeature(role: string | null, feature: FeatureKey): boolean {
  if (role === "Admin") return true;
  return role != null && FEATURE_ACCESS[feature].includes(role as AppRole);
}

/** Nav đã lọc theo role hiện tại — dùng chung cho Sidebar (desktop) và Topbar (drawer mobile). */
export function useVisibleNav() {
  const role = useRole();
  const permissions = useAuthStore((s) => s.permissions);
  
  return {
    items: NAV_ITEMS.filter((item) => canAccessRoute(permissions, role, item.to)),
    system: canAccessRoute(permissions, role, NAV_SYSTEM.to) ? NAV_SYSTEM : null,
  };
}
