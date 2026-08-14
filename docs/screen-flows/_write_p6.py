import os

out = r'E:\DoAn\-AI-driven-marketing-and-Sales-automation-system\docs\screen-flows\USER-FLOWS.md'

part6 = """## Cross-Flow Transitions

| From Flow | To Flow | Trigger | Condition |
|---|---|---|---|
| F-01 (Login) | F-03 (Dashboard) | Login success | Always |
| F-01 (Login) | F-02 (Password Recovery) | "Quen mat khau?" | Forgot password |
| F-03 (Dashboard) | F-04 (Inbox) | Quick Action "Mo hop thu" | Handle conversations |
| F-03 (Dashboard) | F-07 (KB) | Quick Action "Cap nhat tri thuc" | Update KB |
| F-03 (Dashboard) | F-05 (Leads) | Quick Action "Xem canh bao" | Check alerts |
| F-04 (Inbox) | F-05 (Leads) | Lead link in context | Conversation has lead |
| F-05 (Leads) | F-04 (Inbox) | "Xem cuoc tro chuyen" | Lead -> conversation |
| F-07 (KB) | F-08 (Agents) | After KB deploy | Agent uses new KB |
| F-08 (Agents) | F-15 (Run History) | "Xem phien chay" | Admin wants runs |
| F-15 (Run History) | F-16 (Run Detail) | Click run row | Admin drills in |

---

## Screens Index

| ID | Screen Name | Primary Flow(s) | Notes |
|---|---|---|---|
| P-01 | LoginPage | F-01 | Login + 2FA step |
| P-02 | ForgotPasswordPage | F-02 | 4-step wizard |
| P-03 | DashboardPage | F-03 | Metrics + charts + quick actions |
| P-04 | LeadsPage | F-05 | Table + Kanban + LeadDrawer |
| P-05 | ConversationsPage | F-04 | 3-col inbox (alias /inbox) |
| P-06 | ContentWorkspacePage | F-06 | Queue + Calendar + Metrics |
| P-07 | DocumentsPage | F-09 | Templates + Generate + Preview |
| P-08 | AnalyticsReportsPage | F-03 | Overview + Agent + Lead tabs |
| P-09 | NotificationsPage | F-03 | All/Unread/System/Lead tabs |
| P-10 | ProfilePage | F-01 | Info + Permissions + Security |
| P-11 | AdminConsolePage | F-10 | 6-tab admin console |
| P-12 | AgentDashboardPage | F-08 | 8 agents + orchestration |
| P-13 | LlmProvidersPage | F-10 | LLM + Embedding configs |
| P-14 | KnowledgeBasePage | F-07 | Module + Version + Editor + QA |
| P-15 | AgentRunsPage | F-08 | Run history table |
| P-16 | AgentRunDetailPage | F-08 | DAG + Traces + Export |
| S-01 | LoginPage (2FA step) | F-01 | OTP input overlay |
| S-02 | ForgotPasswordPage (Step 2) | F-02 | OTP input |
| S-03 | ForgotPasswordPage (Step 4) | F-02 | Success confirmation |
| S-04 | LeadDrawer (timeline) | F-05 | Activity log |
| S-05 | LeadDrawer (context) | F-05 | AI suggestion |
| S-06 | LeadDrawer (revenue) | F-05 | Approve/reject |
| S-07 | LeadDrawer (payment-form) | F-05 | Amount input |
| S-08 | ConversationsPage (draft) | F-04 | AI draft approve/reject |
| S-09 | ConversationsPage (send-error) | F-04 | Retry button |
| S-10 | ContentWorkspacePage (schedule-dialog) | F-06 | Date/time picker |
| S-11 | DocumentsPage (preview) | F-09 | PDF preview |
| S-12 | AdminConsolePage (confirm) | F-10 | Destructive dialog |
| S-13 | AgentDashboardPage (config) | F-08 | Config drawer |
| S-14 | AgentDashboardPage (terminal) | F-08 | Real-time traces |
| L-01 | DashboardPage (loading) | F-03 | Skeleton cards |
| L-02 | LeadsPage (loading) | F-05 | Skeleton table |
| L-03 | ConversationsPage (list-loading) | F-04 | Skeleton rows |
| L-04 | ContentWorkspacePage (loading) | F-06 | Skeleton cards |
| L-05 | KnowledgeBasePage (deploying) | F-07 | Progress indicator |
| L-06 | DocumentsPage (generating) | F-09 | Job progress |
| L-07 | AgentRunDetailPage (loading) | F-16 | Skeleton |

---

## Assumptions and Open Questions

### Assumptions

1. **A-01:** JWT TTL 15 min with silent refresh (BR-26). Token in-memory only. Refresh token in HttpOnly cookie handled by backend.
2. **A-02:** AI drafts not persisted to messages DB until sale clicks send (BR-13). Held in frontend state or temp backend store.
3. **A-03:** Kanban drag-and-drop not implemented. Leads moved via stage buttons in LeadDrawer. Drag-drop is future enhancement.
4. **A-04:** Content Calendar (P-06 Tab 2) shows scheduled items visually.
5. **A-05:** Notification bell in Topbar uses SignalR push for unread count.

### Open Questions

1. **OQ-01:** Should LeadDrawer support Kanban drag-and-drop? Current uses stage buttons only.
2. **OQ-02:** Is Content Calendar full interactive or simple list view?
3. **OQ-03:** AgentDashboardPage: combined cost dashboard or per-agent only?
4. **OQ-04:** Maximum test cases per KB module? Currently no limit.
5. **OQ-05:** ProfilePage notification preferences section? Or handled elsewhere?

---

## Versioning and Changelog

| Version | Date | Change Type | Description |
|---|---|---|---|
| 1.0.0 | 2026-08-06 | Initial baseline | 10 flows; 16 screens + 7 states + 7 loading states indexed |
"""

with open(out, 'a', encoding='utf-8') as f:
    f.write(part6)
print("Part 6 done:", len(part6), "chars")
print("Total file:", os.path.getsize(out), "bytes")
