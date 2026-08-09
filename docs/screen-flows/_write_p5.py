import os

out = r'E:\DoAn\-AI-driven-marketing-and-Sales-automation-system\docs\screen-flows\USER-FLOWS.md'

part5 = """## Flow F-08 — Agent Orchestration and Monitoring

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

"""

with open(out, 'a', encoding='utf-8') as f:
    f.write(part5)
print("Part 5 done:", len(part5), "chars")
