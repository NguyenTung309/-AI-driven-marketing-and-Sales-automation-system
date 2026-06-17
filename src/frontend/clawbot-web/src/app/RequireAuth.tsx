import type { ReactElement } from "react";
import { Navigate } from "react-router-dom";
import { useAuth } from "@/shared/auth/authState";

export interface RequireAuthProps {
  readonly children: ReactElement;
}

export function RequireAuth({ children }: RequireAuthProps) {
  const { token } = useAuth();
  return token ? children : <Navigate to="/login" replace />;
}
