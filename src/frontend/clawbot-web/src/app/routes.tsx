import { createBrowserRouter } from "react-router-dom";
import AdminConsolePage from "@/features/admin/AdminConsolePage";
import LoginPage from "@/features/auth/LoginPage";
import ForgotPasswordPage from "@/features/auth/ForgotPasswordPage";
import AgentDashboardPage from "@/features/agents/AgentDashboardPage";
import DashboardPage from "@/features/dashboard/DashboardPage";
import ConversationsPage from "@/features/conversations/ConversationsPage";
import ContentWorkspacePage from "@/features/content/ContentWorkspacePage";
import DocumentsPage from "@/features/documents/DocumentsPage";
import AnalyticsReportsPage from "@/features/analytics/AnalyticsReportsPage";
import KnowledgeBasePage from "@/features/kb/KnowledgeBasePage";
import LeadsPage from "@/features/leads/LeadsPage";
import NotificationsPage from "@/features/notifications/NotificationsPage";
import ProfilePage from "@/features/profile/ProfilePage";
import { RequireAuth } from "./RequireAuth";

export const router = createBrowserRouter([
  { path: "/login", element: <LoginPage /> },
  { path: "/forgot-password", element: <ForgotPasswordPage /> },
  {
    path: "/",
    element: (
      <RequireAuth>
        <DashboardPage />
      </RequireAuth>
    ),
  },
  {
    path: "/conversations",
    element: (
      <RequireAuth>
        <ConversationsPage />
      </RequireAuth>
    ),
  },
  {
    path: "/leads",
    element: (
      <RequireAuth>
        <LeadsPage />
      </RequireAuth>
    ),
  },
  {
    path: "/content",
    element: (
      <RequireAuth>
        <ContentWorkspacePage />
      </RequireAuth>
    ),
  },
  {
    path: "/documents",
    element: (
      <RequireAuth>
        <DocumentsPage />
      </RequireAuth>
    ),
  },
  {
    path: "/analytics",
    element: (
      <RequireAuth>
        <AnalyticsReportsPage />
      </RequireAuth>
    ),
  },
  {
    path: "/kb",
    element: (
      <RequireAuth>
        <KnowledgeBasePage />
      </RequireAuth>
    ),
  },
  {
    path: "/notifications",
    element: (
      <RequireAuth>
        <NotificationsPage />
      </RequireAuth>
    ),
  },
  {
    path: "/workflow",
    element: (
      <RequireAuth>
        <AgentDashboardPage />
      </RequireAuth>
    ),
  },
  {
    path: "/profile",
    element: (
      <RequireAuth>
        <ProfilePage />
      </RequireAuth>
    ),
  },
  {
    path: "/system",
    element: (
      <RequireAuth>
        <AdminConsolePage />
      </RequireAuth>
    ),
  },
]);
