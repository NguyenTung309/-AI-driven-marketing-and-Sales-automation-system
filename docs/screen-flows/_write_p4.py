import os

out = r'E:\DoAn\-AI-driven-marketing-and-Sales-automation-system\docs\screen-flows\USER-FLOWS.md'

part4 = """## Flow F-06 — Content Creation and Scheduling

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

"""

with open(out, 'a', encoding='utf-8') as f:
    f.write(part4)
print("Part 4 done:", len(part4), "chars")
