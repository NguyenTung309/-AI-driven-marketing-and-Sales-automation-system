import os

out = r'E:\DoAn\-AI-driven-marketing-and-Sales-automation-system\docs\screen-flows\USER-FLOWS.md'

part3 = """## Flow F-04 — Omnichannel Inbox: Chat and Resolve

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

"""

with open(out, 'a', encoding='utf-8') as f:
    f.write(part3)
print("Part 3 done:", len(part3), "chars")
