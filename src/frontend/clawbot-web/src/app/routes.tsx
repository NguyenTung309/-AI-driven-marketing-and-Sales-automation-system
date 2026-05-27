import { createBrowserRouter, Navigate } from "react-router-dom";
import LoginPage from "@/features/auth/LoginPage";
import DashboardPage from "@/features/dashboard/DashboardPage";
import ConversationsPage from "@/features/conversations/ConversationsPage";
import { useAuth } from "@/shared/auth/AuthContext";
import type { ReactElement } from "react";

function RequireAuth({ children }: { children: ReactElement }) {
  const { token } = useAuth();
  return token ? children : <Navigate to="/login" replace />;
}

export const router = createBrowserRouter([
  { path: "/login", element: <LoginPage /> },
  { path: "/", element: <RequireAuth><DashboardPage /></RequireAuth> },
  { path: "/conversations", element: <RequireAuth><ConversationsPage /></RequireAuth> },
]);
