import type { ReactElement } from "react";
import { Navigate, useLocation } from "react-router-dom";
import { canAccessRoute } from "@/shared/auth/access";
import { useAuthStore } from "@/shared/auth/authStore";

interface RequireAuthProps {
  readonly children: ReactElement;
}

export function RequireAuth({ children }: RequireAuthProps) {
  const token = useAuthStore((s) => s.accessToken);
  const role = useAuthStore((s) => s.role);
  const { pathname } = useLocation();
  if (!token) return <Navigate to="/login" replace />;
  // Fail-open khi role chưa load được (/auth/me lỗi) — backend vẫn enforce quyền thật.
  if (role != null && !canAccessRoute(role, pathname)) return <Navigate to="/" replace />;
  return children;
}
