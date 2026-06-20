import { createBrowserRouter } from "react-router-dom";
import {
  AdminConsolePage,
  AgentDashboardPage,
  AnalyticsReportsPage,
  ContentWorkspacePage,
  ConversationsPage,
  DashboardPage,
  DocumentsPage,
  ForgotPasswordPage,
  KnowledgeBasePage,
  LeadsPage,
  LlmProvidersPage,
  LoginPage,
  NotificationsPage,
  PixelAgentsOfficePage,
  ProfilePage,
  PromptConfigurationPage,
  SupportFaqPage,
  TaskLogsPage,
  TokenManagementPage,
  WidgetDemoPage,
} from "./lazyPages";
import { RequireAuth } from "./RequireAuth";

export const router = createBrowserRouter([
  { path: "/login", element: <LoginPage /> },
  { path: "/forgot-password", element: <ForgotPasswordPage /> },
  { path: "/support", element: <SupportFaqPage /> },
  { path: "/support/:tenantSlug", element: <SupportFaqPage /> },
  { path: "/chat-widget", element: <WidgetDemoPage /> },
  { path: "/chat-widget/:tenantSlug", element: <WidgetDemoPage /> },
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
    path: "/agents-office",
    element: (
      <RequireAuth>
        <PixelAgentsOfficePage />
      </RequireAuth>
    ),
  },
  {
    path: "/logs",
    element: (
      <RequireAuth>
        <TaskLogsPage />
      </RequireAuth>
    ),
  },
  {
    path: "/tokens",
    element: (
      <RequireAuth>
        <TokenManagementPage />
      </RequireAuth>
    ),
  },
  {
    path: "/prompts",
    element: (
      <RequireAuth>
        <PromptConfigurationPage />
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
    path: "/llm-providers",
    element: (
      <RequireAuth>
        <LlmProvidersPage />
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
