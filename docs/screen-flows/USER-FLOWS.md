# User Flows Document

> **Version:** 1.0.0 | **Date:** 2026-08-06 | **Status:** Draft
> **Methodology:** authoring-user-flows skill v1.3.0

---

## Coverage Map

| Goal / Persona | Flow ID | Flow Name | SRS Scenario |
|---|---|---|---|
| Secure access (User) | F-01 | System Login and 2FA | SC-05 |
| Password reset (User) | F-02 | Password Recovery | SC-05 |
| Monitor operations (Admin/Sale) | F-03 | Dashboard Overview | SC-01, SC-02 |
| Handle conversations (Sale) | F-04 | Omnichannel Inbox | SC-01, SC-02 |
| Manage sales pipeline (Sale) | F-05 | Lead Pipeline Management | SC-06 |
| Create content (Marketer) | F-06 | Content Creation and Scheduling | SC-03 |
| Manage KB (QA Admin) | F-07 | KB Authoring, Testing and Deployment | SC-05 |
| Operate AI agents (Admin) | F-08 | Agent Orchestration and Monitoring | SC-05 |
| Generate documents (Sale) | F-09 | Document Generation and Download | SC-05 |
| Configure system (Admin) | F-10 | System Settings and Admin | SC-05 |

---

## Navigation and IA Frame

### Entry-Point Taxonomy

| Entry | Landed State | Notes |
|---|---|---|
| `/login` | LoginPage (P-01) | Public, no auth |
| `/forgot-password` | ForgotPasswordPage (P-02) | Public, 4-step wizard |
| `/leads/:id` | LeadsPage + LeadDrawer (P-04) | Requires auth; deep-link |
| `/conversations/:id` | ConversationsPage (P-05) | Requires auth; deep-link |
| `/` after login | DashboardPage (P-03) | Default after login |

### Navigation / App-Shell Model

All authenticated pages use AppShell: Sidebar (260px) + Topbar (64px) + main content.

**Sidebar:** Tong quan, Khach hang tiem nang, Hoi thoai da kenh, Quan ly noi dung, Thu vien tai lieu, Bao cao thong ke, Trung tam thong bao, Agents, Cau hinh LLM, Kho tri thuc. Footer: He thong.

**Topbar:** search, notification bell (unread badge), active jobs badge, account dropdown.

### Deep-Linking

| Route | Target | Guard |
|---|---|---|
| `/leads/:leadId` | P-04 LeadDrawer | Not found -> error, redirect |
| `/conversations/:id` | P-05 selected | Not found -> error, show list |
| `/agents/runs/:id` | P-16 run detail | Not found -> error, redirect |

### Cross-Device

- **Mobile:** Sidebar hamburger. ConversationsPage single-col. LeadDrawer full-screen.
- **Tablet:** ConversationsPage 2-col. Context as drawer.
- **Desktop (2xl+):** ConversationsPage 3-col (list + chat + context fixed).

---

## Flow F-01 — System Login and 2FA

**Related SRS Scenario:** SC-05
**Primary Actor:** User (Sale Agent / Admin / QA Admin)
**Entry Point:** `/login` (P-01)
**Success End State:** JWT issued; user lands on DashboardPage (P-03)

### Flow Diagram — F-01

```
[Entry: LoginPage (P-01)]
         |
         v
[Step 1: Enter email + password, click DANG NHAP]
         |
         +--- [Branch: Account locked (HTTP 423)]
         |           |
         |           v
         |  [Error: "Tai khoan bi khoa. Lien he admin."]
         |           |
         |           +--> [End: Cannot proceed]
         |
         +--- [Branch: Invalid credentials (HTTP 401)]
         |           |
         |           v
         |  [Error: "Dang nhap that bai. Kiem tra lai."]
         |           |
         |           +--> [Back to Step 1; fields retained]
         |
         +--- [Branch: 2FA required (HTTP 202)]
         |           |
         |           v
         |  [Step 2: Enter 6-digit TOTP code]
         |           |
         |           +--- [Branch: Invalid code]
         |           |           |
         |           |           v
         |           |  [Error: "Ma xac thuc khong dung."]
         |           |           |
         |           |           +--> [Back to Step 2; code cleared]
         |           |
         |           v
         |  [Step 3: Click XAC NHAN -> Auth OK]
         |
         v
[Success: setAuth() -> loadPermissions() -> /]
         |
         v
[End: DashboardPage (P-03)]
```

### Narrative

1. User navigates to `/login` (P-01). Brand panel + credential form render. Focus on email input.
2. User enters email + password, clicks DANG NHAP.
3. **Happy path:** Server returns 200 with accessToken. setAuth() stores token; loadPermissions() fetches /auth/me; navigate to `/` (P-03).
4. **2FA branch:** Server returns 202. Form transitions to 6-digit OTP. User enters code, clicks XAC NHAN. Success = same as happy path.
5. **Error branches:** Invalid credentials show inline error. Account locked (423) shows different message. Both retain fields for retry.

### Branch and Error List

| Branch | Condition | Outcome |
|---|---|---|
| B-1 | Account locked (HTTP 423) | Lock message; no retry until admin unlocks |
| B-2 | Invalid credentials (HTTP 401) | Inline error; password cleared |
| B-3 | 2FA required (HTTP 202) | Transition to OTP step |
| B-4 | 2FA code invalid | Error; code cleared |
| B-5 | Session timeout | Redirect to P-01 with notice |

### Resilience and System-Status

- **Undo/Confirm:** N/A (login is atomic).
- **Resume vs Restart:** Each attempt is atomic; no persistence needed.
- **What Changed:** On success, Topbar shows user initials; Sidebar renders full nav.
- **Optimistic vs Confirmed:** Confirmed — waits for server (security-critical).

### Flow-Level Accessibility

- **Keyboard:** All fields keyboard-operable. Tab: email -> password -> remember-me -> submit -> forgot-password.
- **Focus Order:** On route to P-03, focus moves to main heading "Tong quan van hanh".
- **AT Completable:** Errors use Alert (aria-live). OTP uses inputMode="numeric".
- **No Mouse-Only:** Show/hide password has aria-label.

### Screens / States

| Screen / State | Name | Notes |
|---|---|---|
| Login form | LoginPage | P-01; email + password |
| 2FA OTP form | LoginPage (2FA) | P-01; 6-digit code |
| Error: locked | LoginPage (locked) | P-01; error alert |
| Error: invalid | LoginPage (invalid) | P-01; inline error |
| Loading | LoginPage (submitting) | P-01; button disabled |
| Success redirect | DashboardPage | P-03; default landing |

### Success Criteria

User authenticated with valid JWT. Permissions loaded; sidebar correct. 2FA users complete both steps.

---

## Flow F-02 — Password Recovery

**Related SRS Scenario:** SC-05
**Primary Actor:** User
**Entry Point:** "Quen mat khau?" from P-01 -> `/forgot-password` (P-02)
**Success End State:** Password reset; user returns to LoginPage

### Flow Diagram — F-02

```
[Entry: ForgotPasswordPage (P-02) — Step 1]
         |
         v
[Step 1: Enter email, click GUI MA OTP]
         |
         +--- [Branch: Email not found]
         |           |
         |           v
         |  [Error: "Khong tim thay tai khoan voi email nay."]
         |           |
         |           +--> [Back to Step 1]
         |
         v
[Step 2: Enter 6-digit OTP from email]
         |
         +--- [Branch: OTP expired (> 10 min)]
         |           |
         |           v
         |  [Error: "Ma OTP da het han. Yeu cau lai."]
         |           |
         |           +--> [Back to Step 1]
         |
         v
[Step 3: Enter new password + confirm password]
         |
         +--- [Branch: Passwords do not match]
         |           |
         |           v
         |  [Error: "Mat khau xac nhan khong khop."]
         |           |
         |           +--> [Stay Step 3; confirm cleared]
         |
         v
[Step 4: Success screen]
         |
         v
[End: "Dat lai mat khau thanh cong" + link to /login]
```

### Narrative

1. User clicks "Quen mat khau?" on P-01 -> P-02 (4-step wizard).
2. **Step 1:** Enter email, click GUI MA OTP. Backend sends OTP. UI transitions to OTP input.
3. **Step 2:** Enter 6-digit OTP. TTL 600s (10 min). On success, transition to password form.
4. **Step 3:** Enter new password + confirmation. Validation: passwords match, strength requirements.
5. **Step 4:** Success screen with checkmark. Link "Quay lai dang nhap" -> P-01.

### Branch and Error List

| Branch | Condition | Outcome |
|---|---|---|
| B-1 | Email not found | Error; remain Step 1 |
| B-2 | OTP expired (TTL > 600s) | Error; redirect Step 1; new OTP sent |
| B-3 | OTP incorrect | Error; code cleared |
| B-4 | Passwords mismatch | Error; confirm cleared |
| B-5 | Network error | Error with retry; form preserved |

### Resilience and System-Status

- **Resume vs Restart:** Progress lost if navigate away. Acceptable for low-frequency flow.
- **What Changed:** Step 4 shows explicit success confirmation.

### Flow-Level Accessibility

- **Keyboard:** All inputs, buttons, back link keyboard-operable.
- **Focus Order:** Step transition moves focus to first input of new step.
- **AT Completable:** Errors via Alert. OTP uses inputMode="numeric".

### Screens / States

| Screen / State | Name | Notes |
|---|---|---|
| Request OTP | ForgotPasswordPage (Step 1) | P-02; email input |
| OTP input | ForgotPasswordPage (Step 2) | P-02; 6-digit code |
| Password form | ForgotPasswordPage (Step 3) | P-02; new + confirm |
| Success | ForgotPasswordPage (Step 4) | P-02; checkmark + link |
| Loading | ForgotPasswordPage (submitting) | P-02; button disabled |

### Success Criteria

User receives OTP, validates, sets new password. Can log in with new credentials.

---

## Flow F-03 — Dashboard Overview

**Related SRS Scenario:** SC-01, SC-02
**Primary Actor:** Admin / Sale Agent / Marketer
**Entry Point:** `/` after login, or Sidebar "Tong quan"
**Success End State:** User views real-time metrics; navigates to detail screens

### Flow Diagram — F-03

```
[Entry: DashboardPage (P-03)]
         |
         v
[Step 1: 6 API queries fire in parallel]
         |
         +--> [Loading: Skeleton cards]
         |
         v
[Step 2: 4 Metric cards rendered]
         |   - Hoi thoai AI xu ly
         |   - Lead moi
         |   - Chuyen doi
         |   - Phan hoi trung binh
         |
         v
[Step 3: Channel chart + Quick Actions + Funnel]
         |
         v
[Step 4: Forecast + Agent status + Anomaly table]
         |
         +--- [Branch: API error on any query]
         |           |
         |           v
         |  [Error banner + Retry; unaffected blocks render normally]
         |
         +--- [Branch: Quick Action click]
         |           |
         |           v
         |  [Navigate to /inbox (P-05) or /kb (P-14) or /notifications (P-09)]
         |
         v
[Success: Metrics visible; operational overview complete]
```

### Narrative

1. User lands on P-03. 6 parallel queries: omnichannel, delta, funnel, agent-performance, forecast, anomalies.
2. **Loading:** MetricCard shows skeletons. Realtime pill shows connection state.
3. **Happy path:** All resolve. MetricCards with delta comparison. ChannelChart bars per platform. FunnelPanel conversion funnel. ForecastChart SVG line. AgentStatus top 4 agents. LiveTaskTable anomalies.
4. **Error branch:** Failing block shows own error. Only affected block impacted. Global error if omnichannel fails.
5. **Quick Actions:** Three cards link to P-05, P-14, P-09.

### Branch and Error List

| Branch | Condition | Outcome |
|---|---|---|
| B-1 | Single API fail | That block shows error + retry |
| B-2 | All API fail | Global error banner; shell renders |
| B-3 | Session expired (401) | Redirect P-01 |
| B-4 | SignalR disconnected | "Cap nhat tuc thi gian doan" |
| B-5 | Quick Action click | Navigate to target |

### Resilience and System-Status

- **What Changed:** MetricCard delta ("+12.3%"). Tone positive/negative.
- **System Status:** Realtime pill shows SignalR state. Stale indicator if data stale.

### Flow-Level Accessibility

- **Keyboard:** Quick Actions keyboard-operable. Table rows tabbable.
- **Focus Order:** Route to P-03, focus -> h1 "Tong quan van hanh".
- **AT Completable:** MetricCard values announced. StatusPill uses text.

### Screens / States

| Screen / State | Name | Notes |
|---|---|---|
| Loading skeleton | DashboardPage (loading) | P-03; skeletons |
| Full dashboard | DashboardPage (loaded) | P-03; all metrics |
| Partial error | DashboardPage (partial-error) | P-03; some blocks error |
| Full error | DashboardPage (error) | P-03; global banner |

### Success Criteria

User sees real-time metrics within 3 seconds. Anomalies visible; quick-action navigation works.

---

## Flow F-04 — Omnichannel Inbox: Chat and Resolve

**Related SRS Scenario:** SC-01, SC-02
**Primary Actor:** Sale Agent
**Entry Point:** Sidebar "Hoi thoai da kenh" (P-05) or `/conversations/:id`
**Success End State:** Conversation handled; status updated to Resolved or Escalated

### Flow Diagram — F-04

```
[Entry: ConversationsPage (P-05)]
         |
         v
[Step 1: Conversation list loads (left column)]
         |   - Filter: Tat ca / AI dang chat / Can ho tro / Cua toi / Da xu ly
         |   - Channel filter: per-platform chips
         |   - Search: name, phone, code
         |
         +--- [Branch: Empty list]
         |           |
         |           v
         |  [Empty state: "Khong co hoi thoai phu hop voi bo loc."]
         |
         v
[Step 2: User selects a conversation]
         |
         v
[Step 3: Chat pane loads (center column)]
         |   - Messages: inbound=customer, outbound=AI/sale
         |   - AI drafts: "Duyet gui" / "Bo tin nay"
         |   - Failed messages: "Gui lai" retry
         |
         +--- [Branch: AI auto-reply ON]
         |           |
         |           v
         |  [AI drafts as pending_approval]
         |           |
         |           +--> "Duyet gui" -> sent
         |           +--> "Bo tin nay" -> discarded
         |
         +--- [Branch: AI auto-reply OFF (sale handover)]
         |           |
         |           v
         |  [Sale types manually in composer]
         |
         v
[Step 4: Sale types in ComposerWithAI]
         |
         +--- [Branch: AI toggle ON]
         |           |
         |           v
         |  [AI draft above composer]
         |           |
         |           +--> Accept draft -> inserted
         |           +--> Edit draft -> custom
         |
         v
[Step 5: Sale sends message]
         |
         +--- [Branch: Send fails]
         |           |
         |           v
         |  [Error: "Gui tin nhan that bai" + "Gui lai" retry]
         |
         v
[Step 6: Context panel (right column, 2xl+)]
         |   - Sale Assist suggestions
         |   - Contact memory
         |   - Escalate / Resolve buttons
         |
         +--- [Branch: "Escalate"]
         |           |
         |           v
         |  [Status -> "escalated"; badge -> "Can ho tro"]
         |
         +--- [Branch: "Resolve"]
         |           |
         |           v
         |  [Status -> "resolved"; badge -> "Da xu ly"]
         |
         v
[Success: Conversation handled; status updated]
```

### Narrative

1. User opens P-05. ConversationList loads infinite scroll. Filter/channel chips. Debounced search.
2. **Loading:** Skeleton rows + "Dang tai hoi thoai..." placeholder.
3. User selects conversation. Chat pane loads history. Bubbles: inbound left (customer avatar), outbound right (AI color vs sale color).
4. **AI draft (UC-30, FT-32):** AI generates draft (<= 80 words, BR-12). pending_approval with approve/reject. User accepts, edits, or rejects.
5. **Manual reply (UC-15, FT-15):** Sale types. AI toggle = suggestion above. Toggle off = manual.
6. **Send:** Pancake API dispatch. Failure = error + retry. Success = optimistic append (BR-13: not in DB until sent).
7. **Context panel (2xl+):** ContactMemoryPanel, SaleAssistPanel, Escalate/Resolve (UC-16, FT-16).
8. **Realtime:** useInboxRealtime via SignalR. Typing indicators.

### Branch and Error List

| Branch | Condition | Outcome |
|---|---|---|
| B-1 | Empty list | Empty state with guidance |
| B-2 | AI draft generated | pending_approval + approve/reject |
| B-3 | AI draft blocked (toxicity) | "Tao lai phan hoi AI" button |
| B-4 | Send fails | Error + retry; content preserved |
| B-5 | Retry fails | Error persists; user types new |
| B-6 | Escalate | Badge -> "Can ho tro"; re-filtered |
| B-7 | Resolve | Badge -> "Da xu ly"; re-filtered |
| B-8 | SLA alert (>5min, BR-14) | Toast; conversation highlighted |
| B-9 | Session expired | Redirect P-01 |
| B-10 | Deep link invalid ID | Error; show list |

### Resilience and System-Status

- **Undo/Confirm:** Resolve/Escalate reversible (reopen). No confirm.
- **Resume vs Restart:** Server-persisted. URL state restores on return.
- **What Changed:** Optimistic append on send. Badge updates on status change. Toast confirms.
- **Optimistic vs Confirmed:** Send optimistic. Status change confirmed.

### Flow-Level Accessibility

- **Keyboard:** List arrow-navigable. Send with Enter. Filters keyboard-operable.
- **Focus Order:** Select -> chat pane. Send -> stay in composer.
- **AT Completable:** Bubbles semantic. AI drafts announced. Errors via Alert.

### Screens / States

| Screen / State | Name | Notes |
|---|---|---|
| List loading | ConversationsPage (list-loading) | P-05; skeleton |
| List loaded | ConversationsPage (list) | P-05; filtered list |
| List empty | ConversationsPage (list-empty) | P-05; empty state |
| Chat loading | ConversationsPage (chat-loading) | P-05; placeholder |
| Chat loaded | ConversationsPage (chat) | P-05; bubbles |
| Draft pending | ConversationsPage (draft) | P-05; approve/reject |
| Send error | ConversationsPage (send-error) | P-05; retry |
| Context panel | ConversationsPage (context) | P-05; right col (2xl) |
| Sent | ConversationsPage (sent) | P-05; toast |
| Status changed | ConversationsPage (status-changed) | P-05; badge update |

### Success Criteria

Sale handles conversation end-to-end. AI approve/reject works < 2s (NFR-PER-02). Realtime messages without refresh.

---

## Flow F-05 — Lead Pipeline Management

**Related SRS Scenario:** SC-06
**Primary Actor:** Sale Agent
**Entry Point:** Sidebar "Khach hang tiem nang" (P-04) or `/leads/:id`
**Success End State:** Lead reviewed; stage updated; activities recorded

### Flow Diagram — F-05

```
[Entry: LeadsPage (P-04)]
         |
         v
[Step 1: Lead list loads]
         |   - 4 metric cards: Tong / Nong / Chua phan cong / Du bao 7 ngay
         |   - Table: search + filter (source, stage, owner)
         |   - Kanban: Nong / Am / Lanh / Khach hang / Da mat
         |
         +--- [Branch: Empty]
         |           |
         |           v
         |  [Empty state: "Khong co lead phu hop voi bo loc."]
         |
         v
[Step 2: Click "Chi tiet" on lead]
         |
         v
[Step 3: LeadDrawer slides in (440px)]
         |   - Tab Timeline / Context / Revenue
         |
         +--- [Branch: Timeline]
         |           |
         |           v
         |  [Activity list + "Ghi nhan hoat dong"]
         |           |
         |           +--> Select event + notes -> "Ghi nhan"
         |           |         -> Success: toast
         |           |
         |           +--> [Branch: Fail]
         |                     -> Error + retry
         |
         +--- [Branch: Context]
         |           |
         |           v
         |  [AI goi y + Contact info]
         |
         +--- [Branch: Revenue]
         |           |
         |           v
         |  [Revenue list: approve / reject]
         |
         v
[Step 4: Stage actions]
         |
         +--- [Branch: "Da thanh toan"]
         |           |
         |           v
         |  [Payment form: amount optional (AI estimates)]
         |           -> Submit: stage = "customer"
         |
         +--- [Branch: "Danh mat"]
         |           |
         |           v
         |  [Mark as lost]
         |           -> Submit: stage = "lost"
         |
         +--- [Branch: "Mo lai"]
         |           |
         |           v
         |  [Reopen from terminal stage]
         |
         v
[Step 5: "Xem cuoc tro chuyen" -> /inbox (P-05)]
         |
         v
[Success: Lead managed; stage updated; activities logged]
```

### Narrative

1. P-04 loads 4 metrics + table (server-side filter) + Kanban (5 columns).
2. **Loading:** "Dang tai danh sach lead..." Skeleton metrics.
3. Click "Chi tiet". LeadDrawer slides in (3 tabs).
4. **Timeline:** Activity log + record form. Select event type, notes, submit. Mutation -> toast.
5. **Context:** AI next-step suggestion + contact info.
6. **Revenue:** Approve/reject entries. Amount pre-filled by AI if blank.
7. **Stage actions:** "Da thanh toan" (payment form, amount optional, AI estimates per BR-15). "Danh mat" (mark lost). "Mo lai" (reopen). All server-confirmed.
8. **Nav:** Bottom link -> P-05 for conversation context.

### Branch and Error List

| Branch | Condition | Outcome |
|---|---|---|
| B-1 | Empty list | Empty state |
| B-2 | Record success | Toast; list invalidated |
| B-3 | Record fails | Error toast; form retains |
| B-4 | Stage -> "customer" | Payment form; amount optional |
| B-5 | Stage -> "lost" | Optimistic; reopenable |
| B-6 | Revenue approve (custom) | Must be > 0 |
| B-7 | Revenue reject | Confirmation; list invalidated |
| B-8 | Deep link invalid | Error; redirect `/leads` |

### Resilience and System-Status

- **Undo/Confirm:** Stage reversible (reopen). No confirm modal.
- **What Changed:** Toast confirms. Kanban re-renders updated positions.
- **Optimistic vs Confirmed:** Activity optimistic. Stage confirmed. Revenue confirmed.

### Flow-Level Accessibility

- **Keyboard:** Table rows navigable. Drawer close returns focus. Stage buttons keyboard-operable.
- **Focus Order:** Drawer open -> heading. Close -> triggering row.
- **AT Completable:** Stage via StatusPill. Errors via Alert.

### Screens / States

| Screen / State | Name | Notes |
|---|---|---|
| Table loading | LeadsPage (loading) | P-04; skeleton |
| Table loaded | LeadsPage (table) | P-04; data table |
| Table empty | LeadsPage (empty) | P-04; empty |
| Kanban | LeadsPage (kanban) | P-04; 5-col board |
| Drawer timeline | LeadsPage (drawer-timeline) | P-04; activity log |
| Drawer context | LeadsPage (drawer-context) | P-04; AI suggestion |
| Drawer revenue | LeadsPage (drawer-revenue) | P-04; approve/reject |
| Payment form | LeadsPage (payment-form) | P-04; amount input |
| Activity recorded | LeadsPage (activity-recorded) | P-04; toast |
| Stage changed | LeadsPage (stage-changed) | P-04; toast + kanban |

### Success Criteria

Sale views pipeline, finds lead, records activity, changes stage, manages revenue. Kanban reflects real-time changes.

---

## Flow F-06 — Content Creation and Scheduling

**Related SRS Scenario:** SC-03
**Primary Actor:** Marketer
**Entry Point:** Sidebar "Quan ly noi dung" (P-06)
**Success End State:** Content created, approved, scheduled for publishing

### Flow Diagram — F-06

```
[Entry: ContentWorkspacePage (P-06)]
         |
         v
[Step 1: Content queue loads (tab "Queue")]
         |   - Filter: all/draft/approved/scheduled/published/rejected
         |   - Trend scan + ContentPublishingPolicyControl
         |
         +--- [Branch: Empty queue]
         |           |
         |           v
         |  [Empty state: "Chua co noi dung. Tao brief moi."]
         |
         v
[Step 2: Create content brief]
         |   - Click "Tao noi dung"
         |   - Fill: topic, platform, tone, target
         |   - Submit -> brief saved
         |
         +--- [Branch: Brief fails]
         |           |
         |           v
         |  [Error toast; form retains]
         |
         v
[Step 3: AI generates content items (UC-46, FT-49)]
         |   - Content Agent generates per platform
         |   - Job tracker shows progress
         |
         +--- [Branch: Generation fails]
         |           |
         |           v
         |  [Error: "Tao noi dung that bai" + "Thu lai"]
         |
         v
[Step 4: Items in queue as "draft"]
         |   - Actions: approve/reject/edit/schedule/repurpose
         |
         +--- [Branch: Approve]
         |           -> Status "approved"
         |
         +--- [Branch: Reject]
         |           -> Status "rejected"
         |
         +--- [Branch: Schedule]
         |           |
         |           v
         |  [Dialog: date/time or "golden hour" (BR-21)]
         |           -> Submit: "scheduled"
         |
         v
[Step 5: Calendar tab — scheduled posts]
         |
         v
[Step 6: Metrics tab — performance]
         |
         v
[Success: Content created, approved, scheduled]
```

### Narrative

1. P-06 Tab "Queue" loads items infinite scroll. Filter chips for status. TrendSettingsDialog for trends.
2. User creates brief (topic, platform, tone, target). Submits.
3. **AI generation (UC-46):** Content Agent drafts per platform. Job tracker progress.
4. **Queue management:** Cards with approve/reject/edit/schedule/repurpose.
5. **Scheduling (UC-48, FT-51):** Golden hour auto (BR-21) or specific date/time.
6. **Calendar tab:** Visual calendar of scheduled posts.
7. **Metrics tab:** ContentChainMetrics performance data.

### Branch and Error List

| Branch | Condition | Outcome |
|---|---|---|
| B-1 | Empty queue | Empty state with create action |
| B-2 | Brief fails | Error toast; form preserved |
| B-3 | AI generation fails | Error with retry |
| B-4 | Approve | Status "approved" |
| B-5 | Reject | Status "rejected" |
| B-6 | Schedule golden hour | Auto-picked best time |
| B-7 | Schedule specific | User-selected date/time |
| B-8 | Trend scan | TrendSettingsDialog |

### Resilience and System-Status

- **Undo/Confirm:** Approve reversible (change status again). No confirm.
- **What Changed:** Toast confirms. Status pill updates.
- **Resume vs Restart:** Brief is single-page; no persistence needed.

### Flow-Level Accessibility

- **Keyboard:** Cards, buttons, schedule dialog all keyboard-operable.
- **Focus Order:** Tab switch -> tab content.
- **AT Completable:** Status announced. Errors via Alert.

### Screens / States

| Screen / State | Name | Notes |
|---|---|---|
| Queue loading | ContentWorkspacePage (loading) | P-06; skeleton |
| Queue loaded | ContentWorkspacePage (queue) | P-06; card list |
| Queue empty | ContentWorkspacePage (empty) | P-06; empty |
| Brief form | ContentWorkspacePage (brief-form) | P-06; create brief |
| Generating | ContentWorkspacePage (generating) | P-06; job progress |
| Schedule dialog | ContentWorkspacePage (schedule-dialog) | P-06; date/time |
| Calendar | ContentWorkspacePage (calendar) | P-06; calendar tab |
| Metrics | ContentWorkspacePage (metrics) | P-06; metrics tab |

### Success Criteria

Marketer creates brief, AI generates, approves and schedules. Posts publish at golden hour.

---

## Flow F-07 — KB Authoring, Testing and Deployment

**Related SRS Scenario:** SC-05
**Primary Actor:** QA Admin
**Entry Point:** Sidebar "Kho tri thuc" (P-14)
**Success End State:** KB version tested (>= 85%, BR-05) and deployed

### Flow Diagram — F-07

```
[Entry: KnowledgeBasePage (P-14)]
         |
         v
[Step 1: Module Rail loads (left)]
         |   - Modules: HSK, Lo trinh, Gia, FAQ, GV
         |   - Create / Archive actions
         |
         v
[Step 2: Select a module]
         |
         v
[Step 3: Version Rail (center)]
         |   - Version history
         |   - Create / Deploy / Rollback
         |
         +--- [Branch: Create new version]
         |           -> New draft created
         |
         v
[Step 4: Editor Workspace (right)]
         |   - Edit KB content
         |   - BR-06: deployed versions immutable
         |
         +--- [Branch: Edit deployed version]
         |           |
         |           v
         |  [Blocked: "Phien ban da phat hanh khong the chinh sua."]
         |
         v
[Step 5: QA Test Cases (UC-23, FT-24)]
         |   - Add / Generate test cases (AI)
         |   - AccuracyPanel results
         |
         v
[Step 6: Run Accuracy Test (UC-24, FT-25)]
         |
         +--- [Branch: Accuracy < 85%]
         |           |
         |           v
         |  [Error: "Do chinh xac khong dat 85%."]
         |           |
         |           +--> [Return to Step 4]
         |
         +--- [Branch: Accuracy >= 85%]
         |           |
         |           v
         |  [Deploy enabled]
         |
         v
[Step 7: Deploy (UC-21, FT-22)]
         |   - Embed + store Qdrant
         |   - BR-07: Zero-downtime
         |
         +--- [Branch: Deploy fails]
         |           -> Error + retry
         |
         v
[Step 8: Diff Drawer — compare versions]
         |
         v
[Success: KB deployed; AI Agent uses new version]
```

### Narrative

1. P-14 three-panel: Module Rail (left), Version Rail (center), Editor (right).
2. Select module. Version history loads. Create new draft (UC-20, FT-21).
3. Editor loads. Edit content. Deployed immutable (BR-06).
4. **Testing (UC-23/24):** Add/generate test cases. AccuracyPanel. Run test.
5. **Accuracy gate (BR-05):** < 85% = blocked. >= 85% = deploy enabled.
6. **Deployment (UC-21):** Embed to Qdrant. Zero-downtime (BR-07). Cache cleared.
7. **Diff:** DiffDrawer compares versions.

### Branch and Error List

| Branch | Condition | Outcome |
|---|---|---|
| B-1 | Edit deployed | Blocked; create new version (BR-06) |
| B-2 | Accuracy < 85% | Deploy blocked (BR-05) |
| B-3 | Accuracy >= 85% | Deploy enabled |
| B-4 | Deploy fails | Error + retry |
| B-5 | Rollback | Previous version activated (BR-07) |
| B-6 | Generate test cases | AI generates; progress |

### Resilience and System-Status

- **Undo/Confirm:** Rollback reversible (deploy forward). No confirm.
- **What Changed:** Version status updates. Accuracy score updates. Deployed indicator.
- **Resume vs Restart:** Drafts persist. User can leave and return.

### Flow-Level Accessibility

- **Keyboard:** All panels navigable. Module/version selection keyboard. Editor = text area.
- **Focus Order:** Module select -> Version Rail -> Editor.
- **AT Completable:** Version status announced. Accuracy announced. Deploy announced.

### Screens / States

| Screen / State | Name | Notes |
|---|---|---|
| Modules loading | KnowledgeBasePage (modules-loading) | P-14; skeleton |
| Modules loaded | KnowledgeBasePage (modules) | P-14; module list |
| Versions | KnowledgeBasePage (versions) | P-14; version history |
| Editor | KnowledgeBasePage (editor) | P-14; content editor |
| Editor locked | KnowledgeBasePage (editor-locked) | P-14; deployed |
| Accuracy | KnowledgeBasePage (accuracy) | P-14; test results |
| Diff | KnowledgeBasePage (diff) | P-14; comparison |
| Deploying | KnowledgeBasePage (deploying) | P-14; progress |
| Deployed | KnowledgeBasePage (deployed) | P-14; toast |
| Deploy error | KnowledgeBasePage (deploy-error) | P-14; error + retry |

### Success Criteria

QA Admin creates/edits KB, tests (>= 85%), deploys. AI Agent uses updated KB on next query.

---

## Flow F-08 — Agent Orchestration and Monitoring

**Related SRS Scenario:** SC-05
**Primary Actor:** Admin
**Entry Point:** Sidebar "Agents" (P-12)
**Success End State:** Agent configured, monitored; run history reviewed

### Flow Diagram — F-08

```
[Entry: AgentDashboardPage (P-12)]
         |
         v
[Step 1: Dashboard loads]
         |   - 8 agent cards + OrchestrationPanel + SchedulesCard + JobCenter
         |
         v
[Step 2: View agent status]
         |
         +--- [Branch: Agent error state (BR-08)]
         |           -> Error indicator + "Can kiem tra"
         |
         v
[Step 3: Configure agent (Config Drawer)]
         |   - Tab Prompt / Model / Tools
         |
         +--- [Branch: Save config]
         |           -> Toast: "Da cap nhat cau hinh"
         |
         +--- [Branch: Enable/Disable]
         |           -> Toggle on/off; status updates
         |
         v
[Step 4: Run orchestration plan]
         |   - Plan suggestions generated
         |   - High-risk paused (BR-29)
         |
         +--- [Branch: Approved]
         |           -> Run starts; JobCenter tracks
         |
         +--- [Branch: Rejected]
         |           -> Run cancelled
         |
         v
[Step 5: Monitor run (Terminal)]
         |   - Events / Queue / Errors tabs
         |
         +--- [Branch: Agent crash (BR-08)]
         |           -> Auto-restart (3x in 5min); then Error + Alert
         |
         v
[Step 6: Run history /agents/runs (P-15)]
         |   - Click row -> /agents/runs/:sessionId (P-16)
         |
         v
[AgentRunDetailPage (P-16)]
         |   - Task DAG + Traces + Export CSV
         |
         v
[Success: Agent managed; runs monitored; history reviewed]
```

### Narrative

1. P-12 loads 8 agent cards + OrchestrationPanel + SchedulesCard + JobCenterDialog.
2. Admin views status. Error state = attention needed (BR-08: auto-restart 3x/5min).
3. **Config:** Drawer per agent. Edit prompt (UC-26), model (FT-27), tools (UC-27, FT-28). Toast confirms.
4. **Orchestration (UC-59):** Plan generation. Admin selects. High-risk paused (BR-29). Run tracked.
5. **Monitoring (UC-28, FT-29):** Terminal events/queue/errors. OpenTelemetry traces (BR-10).
6. **Run history (P-15/P-16):** Filter, drill in. DAG canvas. Export CSV.

### Branch and Error List

| Branch | Condition | Outcome |
|---|---|---|
| B-1 | Agent error | Indicator; admin investigates |
| B-2 | Crash (auto-restart) | 3x/5min; then Error + Alert (BR-08) |
| B-3 | Plan needs approval | Paused; admin approves/rejects (BR-29) |
| B-4 | Cost quota reached | Alert 80% ($160); stop 100% (BR-09/31) |
| B-5 | Config save | Toast; new config next call |
| B-6 | Run history empty | Empty state |
| B-7 | Sandbox test | Test mode; result inline |

### Resilience and System-Status

- **Undo/Confirm:** Enable/disable reversible. Config overwritable. Plan approval guards high-risk.
- **What Changed:** Status pill real-time. Config saved state. Run status via realtime.
- **Optimistic vs Confirmed:** Config confirmed. Enable/disable optimistic.

### Flow-Level Accessibility

- **Keyboard:** Agent cards, config tabs, toggle keyboard-operable.
- **Focus Order:** Drawer -> first tab. Run detail -> DAG canvas.
- **AT Completable:** Agent status announced. Errors announced.

### Screens / States

| Screen / State | Name | Notes |
|---|---|---|
| Dashboard loaded | AgentDashboardPage (loaded) | P-12; 8 cards |
| Config prompt | AgentDashboardPage (config-prompt) | P-12; prompt editor |
| Config model | AgentDashboardPage (config-model) | P-12; model selector |
| Config tools | AgentDashboardPage (config-tools) | P-12; tool toggles |
| Orchestration | AgentDashboardPage (orchestration) | P-12; plan suggestions |
| Job Center | AgentDashboardPage (job-center) | P-12; active jobs |
| Terminal | AgentDashboardPage (terminal) | P-12; trace log |
| Run list | AgentRunsPage | P-15; history table |
| Run detail | AgentRunDetailPage | P-16; DAG + traces |
| Run loading | AgentRunDetailPage (loading) | P-16; skeleton |

### Success Criteria

Admin configures agents, monitors runs, reviews history. Auto-recovery < 10s (NFR-REL-01).

---

## Flow F-09 — Document Generation and Download

**Related SRS Scenario:** SC-05
**Primary Actor:** Sale Agent / Marketer
**Entry Point:** Sidebar "Thu vien tai lieu" (P-07)
**Success End State:** Document generated from template and downloaded

### Flow Diagram — F-09

```
[Entry: DocumentsPage (P-07)]
         |
         v
[Step 1: Template list + Generated docs]
         |   - Presets: hop-so, bien-ban, phieu-dang-ky
         |
         v
[Step 2: Create or select template]
         |
         +--- [Branch: Create new]
         |           |
         |           v
         |  [TemplateFieldsEditor: define fields]
         |           -> Save: template created
         |
         +--- [Branch: Select existing]
         |           -> Template loads
         |
         v
[Step 3: Fill DocumentFieldsForm]
         |   - Required fields validated (BR-23)
         |
         +--- [Branch: Missing required fields]
         |           |
         |           v
         |  [422 error: list of missing fields (BR-23)]
         |
         v
[Step 4: Click "Tao tai lieu"]
         |   - Single or kit
         |   - Target: < 30s (BR-22)
         |
         +--- [Branch: Generation fails]
         |           -> Error + retry
         |
         +--- [Branch: Storage unavailable]
         |           -> Local disk fallback (BR-22)
         |
         v
[Step 5: DocumentPreview renders PDF]
         |
         v
[Step 6: Download PDF]
         |
         v
[Success: Document generated and downloaded]
```

### Narrative

1. P-07 two panels: template management + generated documents.
2. Create or select template. TemplateFieldsEditor defines fields.
3. Fill DocumentFieldsForm. Required validated (BR-23).
4. Click "Tao tai lieu". QuestPDF renders. Job tracker. Target: < 30s (BR-22).
5. **Missing fields (BR-23):** 422 with missing keys. Fill and retry.
6. **Storage down (BR-22):** Local disk fallback. User notified.
7. DocumentPreview inline. Download button.

### Branch and Error List

| Branch | Condition | Outcome |
|---|---|---|
| B-1 | Missing required fields | 422 error; missing fields list (BR-23) |
| B-2 | Timeout (> 30s) | Error + retry (BR-22) |
| B-3 | Storage unavailable | Local disk fallback (BR-22) |
| B-4 | Document kit | Multiple docs generated |
| B-5 | Edit immutable | Blocked; create new |

### Resilience and System-Status

- **What Changed:** Generated doc appears in list. Preview inline.
- **Resume vs Restart:** Async (job-based). Continues if user navigates away.

### Flow-Level Accessibility

- **Keyboard:** Template list, form, download all keyboard-operable.
- **AT Completable:** Validation errors announced. Preview has alt text.

### Screens / States

| Screen / State | Name | Notes |
|---|---|---|
| Template list | DocumentsPage (templates) | P-07; CRUD |
| Template editor | DocumentsPage (template-editor) | P-07; fields editor |
| Fields form | DocumentsPage (fields-form) | P-07; fill fields |
| Fields error | DocumentsPage (fields-error) | P-07; validation |
| Generating | DocumentsPage (generating) | P-07; job progress |
| Preview | DocumentsPage (preview) | P-07; PDF preview |
| Generated list | DocumentsPage (generated-list) | P-07; download links |

### Success Criteria

User fills template, generates PDF < 30s, previews and downloads. Missing field validation works.

---

## Flow F-10 — System Settings and Admin

**Related SRS Scenario:** SC-05
**Primary Actor:** Admin
**Entry Point:** Sidebar "He thong" (P-11)
**Success End State:** Settings configured; users/roles/API keys managed

### Flow Diagram — F-10

```
[Entry: AdminConsolePage (P-11)]
         |
         v
[Step 1: 6 tabs load]
         |   - Users / Roles / API Keys / Integrations / System Logs / Audit
         |
         +--- [Tab: Users]
         |           |
         |           v
         |  [CRUD: Create/Edit/Reset password/Active toggle]
         |
         +--- [Tab: Roles]
         |           |
         |           v
         |  [CRUD + permission assignment]
         |           |
         |           +--> Delete role -> confirmation dialog
         |
         +--- [Tab: API Keys]
         |           |
         |           v
         |  [Create/Revoke/Rotate]
         |           |
         |           +--> Create -> key shown once (SHA-256)
         |           +--> Revoke -> confirmation
         |
         +--- [Tab: Integrations]
         |           |
         |           v
         |  [Pancake config + Webhook URL]
         |           |
         |           +--> Connect Pancake -> OAuth -> pages
         |           +--> Delete integration -> confirmation
         |
         +--- [Tab: System Logs]
         |           -> Cursor-based pagination
         |
         +--- [Tab: Audit]
         |           -> Trail: IP, action, timestamp
         |
         v
[Success: System configured]
```

### Narrative

1. P-11 6-tab AdminConsolePage. Tab "Users" default.
2. **Users (UC-04):** CRUD admin users. Create: modal. Edit: update. Reset: confirmation. Toggle active.
3. **Roles (UC-05):** CRUD + permission checkboxes. Delete requires confirmation.
4. **API Keys (UC-06):** Create SHA-256 (shown once). Revoke: confirm. Rotate: new key.
5. **Integrations (UC-09):** Pancake OAuth. Webhook configurable. Delete with confirm.
6. **System Logs (UC-28):** Cursor pagination. Filterable.
7. **Audit (UC-08):** Trail with IP, action, timestamp.

### Branch and Error List

| Branch | Condition | Outcome |
|---|---|---|
| B-1 | Create user fails | Error toast; modal retains |
| B-2 | Reset password | Shown once; admin copies |
| B-3 | Delete role with users | Blocked; reassign first |
| B-4 | API key created | Shown once; cannot retrieve |
| B-5 | Revoke key | Confirmation; immediate invalid |
| B-6 | Pancake OAuth fails | Error; retry |
| B-7 | Delete integration | Confirmation dialog |

### Resilience and System-Status

- **Undo/Confirm:** Delete/revoke carry confirmation (irreversible). User CRUD reversible.
- **What Changed:** Toast confirms. Lists refresh.
- **Resume vs Restart:** Tab state not persisted (resets).

### Flow-Level Accessibility

- **Keyboard:** Tabs, modals, confirmations all keyboard-operable.
- **Focus Order:** Tab switch -> content. Modal open -> trapped. Close -> returns.
- **AT Completable:** Tab content announced. Confirmations announced.

### Screens / States

| Screen / State | Name | Notes |
|---|---|---|
| Users tab | AdminConsolePage (users) | P-11; user list |
| Roles tab | AdminConsolePage (roles) | P-11; role list |
| API Keys tab | AdminConsolePage (api-keys) | P-11; key list |
| Integrations tab | AdminConsolePage (integrations) | P-11; Pancake config |
| System Logs tab | AdminConsolePage (system-logs) | P-11; log list |
| Audit tab | AdminConsolePage (audit) | P-11; audit trail |
| Create user modal | AdminConsolePage (create-user) | P-11; user form |
| Create role modal | AdminConsolePage (create-role) | P-11; role + perms |
| Create key modal | AdminConsolePage (create-key) | P-11; key gen |
| Confirm dialog | AdminConsolePage (confirm) | P-11; destructive confirm |

### Success Criteria

Admin manages users, roles, keys, integrations. Destructive actions guarded. Keys shown once.

---

## Cross-Flow Transitions

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
