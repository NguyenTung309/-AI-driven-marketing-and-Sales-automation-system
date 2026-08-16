# Screen Flow tong the - ClawBot SaleMkt

> Cap nhat: 2026-08-06. Grill session da xac nhan va loai bo cac screen thua.

## 1. Tong quan kien truc UI

Tat ca authenticated pages deu dung `<AppShell>` (sidebar 260px + topbar 64px + content).
Ngoai le: `ConversationsPage` dung AppShell voi `noPadding` (full-height flex layout cho inbox).

Public pages (`/login`, `/forgot-password`) khong co Sidebar/Topbar.

## 2. Danh sach Screen & Route

### 2.1 Public Pages (khong can auth)

| Route | Component | Ghi chu |
|-------|-----------|---------|
| `/login` | LoginPage | Buoc 1: credentials -> buoc 2 (neu co 2FA): OTP 6 so -> redirect `/` |
| `/forgot-password` | ForgotPasswordPage | 4 buoc: request email -> nhap OTP -> dat mat khau moi -> thanh cong |

### 2.2 Authenticated Pages (RequireAuth + AppShell)

#### Sidebar Navigation (NAV_ITEMS - 10 muc)

| Route | Icon | Label | Component | Ghi chu |
|-------|------|-------|-----------|---------|
| `/` | dashboard | Tong quan | DashboardPage | Metric cards + channel chart + funnel + forecast + agent status + anomaly table |
| `/leads` | person_search | Khach hang tiem nang | LeadsPage | Table + Kanban 5 cot (Nong/Am/Lanh/Khach hang/Da mat) + LeadDrawer 3 tab |
| `/conversations` | all_inbox | Hoi thoai da kenh | ConversationsPage | 3 cot: list + chat + context panel; alias `/inbox` |
| `/content` | campaign | Quan ly noi dung | ContentWorkspacePage | 3 tabs: queue (draft/approved/scheduled/published) + calendar + metrics |
| `/documents` | description | Thu vien tai lieu | DocumentsPage | Templates CRUD + generate + preview + download |
| `/analytics` | monitoring | Bao cao thong ke | AnalyticsReportsPage | 3 tabs: overview + agent + lead; export CSV/PDF |
| `/notifications` | notifications | Trung tam thong bao | NotificationsPage | 4 tabs: all/unread/system/lead; realtime push |
| `/agents` | smart_toy | Agents | AgentDashboardPage | 8 agents dashboard + orchestration + schedules + config + traces + job center |
| `/llm-providers` | tune | Cau hinh LLM | LlmProvidersPage | LLM configs (add/test/rotate key) + Embedding configs |
| `/kb` | inventory_2 | Kho tri thuc | KnowledgeBasePage | Module rail + version rail + editor + QA test cases + accuracy panel + diff drawer |

#### Sidebar Footer (NAV_SYSTEM)

| Route | Icon | Label | Component | Ghi chu |
|-------|------|-------|-----------|---------|
| `/system` | settings | He thong | AdminConsolePage | 6 tabs: Users, Roles, API Keys, Integrations, System Logs, Audit |

#### Topbar User Menu

| Route | Label | Component | Ghi chu |
|-------|-------|-----------|---------|
| `/profile` | Ho so | ProfilePage | 3 tabs: Thong tin / Phan quyen / Bao mat; change password + 2FA |

#### Sub-routes (linked tu ben trong pages, khong co trong sidebar)

| Route | Component | Ghi chu |
|-------|-----------|---------|
| `/agents/runs` | AgentRunsPage | Danh sach phien chay agent: all/mine/pending/archived |
| `/agents/runs/:sessionId` | AgentRunDetailPage | Chi tiet DAG tasks + traces + export CSV |

#### Deep-links (tu notification/toast)

| Route | Component | Ghi chu |
|-------|-----------|---------|
| `/leads/:leadId` | LeadsPage | Pre-select lead trong drawer |
| `/conversations/:conversationId` | ConversationsPage | Pre-select conversation |

#### Legacy Redirects

| Tu | Den | Ly do |
|----|-----|-------|
| `/workflow` | `/agents` | Da hop nhat vao Agents page |
| `/orchestration` | `/agents` | Da hop nhat vao Agents page |

## 3. Auth Flow

```
Visitor
  |
  +--> /login
  |      |
  |      +-- POST /auth/login (email + password)
  |      |     200 -> setAuth(accessToken) -> loadPermissions() -> /
  |      |     202 (requiresTwoFactor) -> chuyen sang step "2FA"
  |      |     423 -> "Tai khoan bi khoa"
  |      |
  |      +-- 2FA step
  |            POST /auth/login/2fa (email + password + code)
  |            OK -> setAuth() -> /
  |            Fail -> "Ma xac thuc khong dung"
  |
  +--> /forgot-password
         |
         Step 1: requestPasswordReset(email) -> OTP sent
         Step 2: nhap OTP 6 so
         Step 3: confirmPasswordReset(email, code, newPassword)
         Step 4: success -> link ve /login
```

## 4. Core User Flow: Inbox -> Lead

Day la flow chinh cua ung dung - khach message qua kenh nao do, AI xu ly, tu tao lead.

```
Khach hang (Zalo/Facebook/TikTok/Instagram/YouTube)
  |
  v
Pancake webhook -> Backend (MassTransit / RabbitMQ)
  |
  v
ConversationsPage (/conversations)
+--------------------------------------------------+
| LEFT COLUMN: Danh sach hoi thoai                  |
|   - Filter theo status: all/open/escalated/mine   |
|   - Filter theo kenh (channel)                    |
|   - Search theo ten, SDT, ma hoi thoai            |
|   - Badge: "AI dang chat" / "Can ho tro"          |
|                                                   |
| CENTER COLUMN: Chat pane                          |
|   - Message bubbles (inbound = khach, outbound = AI/sale) |
|   - AI draft co nut "Duyet & gui" / "Bo tin nay" |
|   - Nut "Tao lai phan hoi AI" khi tin bi chan     |
|   - Composer voi AI toggle on/off                  |
|                                                   |
| RIGHT COLUMN (2xl): Context panel                 |
|   - Sale Assist suggestions                        |
|   - Contact memory panel                           |
|   - Action buttons: Escalate / Resolve             |
+--------------------------------------------------+
  |
  |  Backend: AI tu phan tich -> tao Lead (auto-create)
  |
  v
LeadsPage (/leads)
+--------------------------------------------------+
| HEADER: Metric cards (Tong lead / Lead nong /     |
|         Chua phan cong / Du bao 7 ngay)           |
|                                                   |
| TABLE: Filter theo ten/nguon/trang thai/phu trach |
|   - Score bar + Status pill + Source + Owner       |
|   - Click "Chi tiet" -> mo LeadDrawer             |
|                                                   |
| KANBAN BOARD 5 cot:                               |
|   Nong -> Am -> Lanh -> Khach hang -> Da mat       |
|   - Moi cot: avg score, click card -> LeadDrawer  |
|                                                   |
| LeadDrawer (slide-in tu phai, 440px):             |
|   Tab Timeline: hoat dong lead, nut ghi nhan      |
|   Tab Context: goi y tiep theo + thong tin lien he |
|   Tab Revenue: doanh thu (approve/reject)         |
|   Actions: Da thanh toan / Danh mat / Mo lai      |
|   Link bottom: "Xem chi tiet cuoc tro chuyen"     |
+--------------------------------------------------+
```

## 5. Agent Dashboard Flow

```
/agents -> AgentDashboardPage
+--------------------------------------------------+
| - 8 Agent cards (Chat, Sale, Content, Lead, etc.)|
| - Enable/disable/toggle each agent                |
| - OrchestrationPanel: plan suggestions + run plan |
| - SchedulesCard: cron-based auto-run              |
| - Config Drawer: prompt / model / tools per agent |
| - Sandbox test button per agent                   |
| - Terminal: events / queue / errors tabs          |
| - JobCenterDialog: active jobs tracker            |
| - Cost summary per agent                          |
|                                                   |
| Links:                                            |
|   "Xem phien chay" -> /agents/runs               |
|   "So sanh" -> RunCompareDialog (inline)          |
+--------------------------------------------------+
  |
  v
/agents/runs -> AgentRunsPage
+--------------------------------------------------+
| Filter: all / mine / pending / archived           |
| Bang phien chay: status + duration + owner        |
| Click row -> /agents/runs/:sessionId              |
+--------------------------------------------------+
  |
  v
/agents/runs/:sessionId -> AgentRunDetailPage
+--------------------------------------------------+
| - Task DAG Canvas (visual graph cac tasks)        |
| - TaskResultDetails: output tung task             |
| - Traces timeline                                 |
| - Export CSV                                      |
+--------------------------------------------------+
```

## 6. Content Management Flow

```
/content -> ContentWorkspacePage
+--------------------------------------------------+
| Tab "Queue":                                      |
|   - Filter: all / draft / approved / scheduled /  |
|     published / rejected                          |
|   - Content cards: hook + platform + status       |
|   - Actions: approve / reject / schedule / repurpose |
|   - "Tao noi dung" -> createContentBrief          |
|   - ContentPublishingPolicyControl                |
|   - TrendSettingsDialog (scan trends)             |
|                                                   |
| Tab "Calendar":                                   |
|   - ContentCalendarView (scheduled items)         |
|                                                   |
| Tab "Metrics":                                    |
|   - ContentChainMetrics                           |
+--------------------------------------------------+
```

## 7. Knowledge Base Flow

```
/kb -> KnowledgeBasePage
+--------------------------------------------------+
| Module Rail (left):                               |
|   - Danh sach modules (HSK, Lo trinh, Gia, FAQ..)|
|   - Create / Archive module                       |
|   - KbAutoClassifyModal (AI phan loai)            |
|   - KbSuggestionsPanel (goi y KB)                 |
|                                                   |
| Version Rail (center):                            |
|   - Version history per module                    |
|   - Create / Deploy / Rollback version            |
|   - Diff drawer (so sanh versions)                |
|                                                   |
| Editor Workspace (right):                         |
|   - EditorWorkspace: edit KB content              |
|   - QA test cases: add / generate / run           |
|   - AccuracyPanel: ket qua test set               |
+--------------------------------------------------+
```

## 8. Admin Console Flow

```
/system -> AdminConsolePage
+--------------------------------------------------+
| Tab "Users": CRUD admin users + reset password    |
| Tab "Roles": CRUD roles + assign permissions      |
| Tab "API Keys": create / revoke / rotate keys    |
| Tab "Integrations": Pancake config + webhook URL |
| Tab "System Logs": system log cursor pagination   |
| Tab "Audit": audit log trail                      |
+--------------------------------------------------+
```

## 9. Cross-page Navigation Map

```
                      +----------+
                      |  /login  |
                      +----+-----+
                           | auth OK
                           v
                 +------------------+
                 |       /          |  Dashboard
                 |   DashboardPage  |
                 +--------+---------+
                           |
           +---------------+-------------------+
           |               |                   |
           v               v                   v
    +-------------+  +--------------+    +-------------+
    |   /leads    |  |/conversations|    |  /content   |
    |  LeadsPage  |  | Conversations|    | ContentPage |
    |             |  |    Page      |    |             |
    | +--------+ |  +------+-------+    +-------------+
    | |LeadDraw| |         |
    | |-> /inbox|<+--------+  (lead link ve conversations)
    | +--------+ |
    +-------------+
           |               |                   |
           +---------------+-------------------+
                           |
           +---------------+-----------------------+
           |               |                       |
           v               v                       v
    +-------------+  +--------------+    +-----------------+
    |  /documents |  |  /analytics  |    | /conversations/:id|
    | DocumentsPg |  | AnalyticsPg  |    | /leads/:id       |
    +-------------+  +--------------+    | (pre-select item)|
                                         +-----------------+
           |               |
           +---------------+----------------------+
           |               |                      |
           v               v                      v
    +-------------+  +--------------+    +-------------+
    |   /agents   |  |/llm-providers|    |/notifications|
    | AgentDashPg |  | LlmProviders |    | Notifications |
    |             |  |     Page     |    |    Page       |
    | + /agents/runs|+--------------+    +---------------+
    | + /agents/runs/:id|
    +------------------+
           |
           v
    +-------------+
    |    /kb      |
    | KnowledgeBase|
    |    Page     |
    +-------------+

    +-------------+  +--------------+
    |  /system    |  |  /profile    |
    | AdminConsole|  | ProfilePage  |
    | (6 tabs)    |  | (3 tabs)     |
    +-------------+  +--------------+
```

## 10. Screen da loai bo (khong dung)

| Screen | Route | Ly do loai |
|--------|-------|-----------|
| AgentHubLayout | `/agent-hub`, `/inbox/:channelId` | Thua, ConversationsPage da bao phu |
| PixelAgentsOfficePage | `/agents-office` | Demo/giai tri, khong phai production |
| ChannelManagementPage | `/system/channels` | Khong can thiet |
| WidgetDemoPage | `/chat-widget/*` | Public widget, bo |
| SupportFaqPage | `/support/*` | Public FAQ, bo |
| TaskLogsPage | `/logs` | Khong can UI rieng |
| TokenManagementPage | `/tokens` | Khong can UI rieng |
| PromptConfigurationPage | `/prompts` | Khong can UI rieng |

## 11. AppShell Layout Spec

```
+------------------------------------------------------+
| Sidebar (260px, fixed, bg-primary)                    |
|   +--------------------------+                        |
|   | "Hoc Ba AI"              |                        |
|   | "Trung tam van hanh AI"  |                        |
|   +--------------------------+                        |
|   | Tong quan                |                        |
|   | Khach hang tiem nang     |                        |
|   | Hoi thoai da kenh        |                        |
|   | Quan ly noi dung         |                        |
|   | Thu vien tai lieu        |                        |
|   | Bao cao thong ke         |                        |
|   | Trung tam thong bao      |                        |
|   | Agents                   |                        |
|   | Cau hinh LLM            |                        |
|   | Kho tri thuc             |                        |
|   +--------------------------+                        |
|   | He thong (mt-auto)        |                        |
|   +--------------------------+                        |
|   | He thong: Dang hoat dong  |                        |
|   +--------------------------+                        |
+------------------------------------------------------+
| Topbar (64px, fixed, bg-surface)                      |
|   [menu] [search] ... [notifications] [jobs] [HB]    |
+------------------------------------------------------+
| main (pt-[80px] offset cho topbar)                    |
|   +--------------------------------------------------+|
|   |                                                  ||
|   |  Page content (varies per route)                  ||
|   |                                                  ||
|   +--------------------------------------------------+|
+------------------------------------------------------+
```

## 12. Realtime Features

| Feature | Scope | Mechanism |
|---------|-------|-----------|
| Notification push | Topbar toast + badge | SignalR hub, 30s polling fallback |
| Active jobs badge | Topbar icon | 15s polling `listJobs("active")` |
| Inbox messages | ConversationsPage | `useInboxRealtime` - message auto-append + typing indicator |
| Dashboard data | DashboardPage | 30s-120s refetch intervals per metric |
| Agent traces | AgentDashboardPage | `useOrchestrationRealtime` |

## 13. Permission Model (frontend)

| Page | Required Permission | Notes |
|------|---------------------|-------|
| LeadsPage - edit actions | `leads:write` | Button enable/disable |
| AdminConsolePage | `admin:inboxes` (partials) | Tab visibility varies |
| LlmProvidersPage | `llm-configs:manage` | CRUD controls |
| All pages | JWT + `RequireAuth` | Unauthenticated -> redirect `/login` |

## 14. Total Screen Count

| Category | Count |
|----------|-------|
| Public (no auth) | 2 |
| Authenticated (sidebar nav) | 10 |
| System admin | 1 |
| User profile | 1 |
| Sub-routes (agents) | 2 |
| Deep-link routes | 2 |
| **Total unique screens** | **18** |
| Legacy redirects | 2 (not real screens) |
