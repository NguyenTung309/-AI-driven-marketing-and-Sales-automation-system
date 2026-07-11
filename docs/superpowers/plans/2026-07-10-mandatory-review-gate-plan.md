# Plan: Mandatory Agent-Review Gate + Manual-Approval Mode + Schedule Notifications

> Yêu cầu: (1) mọi content output (chat reply + bài đăng) BẮT BUỘC có 1 agent review trước khi xuất ra; (2) con người có thể chỉnh sang chế độ duyệt thủ công khi cần; (3) thông báo cho người khi review/duyệt pending để không trễ lịch đăng.
>
> Nguồn: workflow 5-mapper + design + adversarial critique (2026-07-10). Critique phát hiện 2 lỗi chí mạng ở bản design đầu — đã gộp vào bản này.

---

## 0. Phát hiện quan trọng nhất (từ critique)

**Giả định "mọi send đi qua 1 chỗ" là SAI.** Có ≥6 đường bắn content ra ngoài:

| # | Đường | File | Gate hiện tại |
|---|-------|------|---------------|
| 1 | Chat auto-reply | `ChatAgentGrpcService.cs:128` (`SendAsync` khi `!reply.Blocked`) | toxicity/injection/PII deterministic, KHÔNG có LLM review |
| 2 | Bài đăng scheduled | `ContentPublishJob.cs:58` (Hangfire) | KHÔNG check gì — kể cả `ApprovedByAgentId` |
| 3 | Agent publish trực tiếp | `ContentPublishTool` (ContentTools.cs:126) | High-risk gate chỉ ở ReAct path |
| 4 | **Comment auto-reply** | `CommentAutoReplyJob.cs:54,57` — bắn 2 tin (comment + DM) | **KHÔNG gate nào** — không toxicity, không PII, không đọc `AiAutoReplyEnabled` |
| 5 | **Drip sequence** | `DripSequenceJob.cs:95` — marketing message định kỳ | **KHÔNG gate nào**, không đọc manual-mode |
| 6 | Document delivery | `DocumentDeliveryService.cs:46,81` — email + Zalo | Không gate |

→ **Quyết định kiến trúc: gate đặt ở BIÊN adapter** (mọi call `IChannelAdapter.SendAsync` / `ISocialPublisher.PublishAsync` / `IEmailSender` outbound tới khách), không đặt rải rác per-feature. Path mới thêm sau này tự động bị chặn thay vì lặng lẽ mở lỗ mới.

---

## 1. Hiện trạng — tái dùng được gì

Đã có sẵn (KHÔNG dựng lại):
- `ContentItem.ApproveByAgent(agentDefinitionId)` + cột `ApprovedByAgentId` — chữ ký agent, wired end-to-end qua tool `content.approve` → `reviewer-agent` (DevDataSeeder.cs:274, type=`reviewer`). **Hiện chỉ audit-only — chưa ai enforce làm điều kiện publish.**
- `Conversation.AiAutoReplyEnabled` (default true) — công tắc manual-mode per-conversation; `Escalate()` tự tắt; sale gửi tay tự tắt (handover).
- `Tenant.RequireOrchestrationApproval` + `EfOrchestrationApprovalResolver` — pattern flag tenant-level.
- `INotificationPublisher` → persist + SignalR NotificationHub (API: `DbNotificationPublisher`; AgentService: Redis bridge).
- `IdleConversationAlertJob` — template SLA job: banded query, tiered, dual-channel, recipient resolver.
- Chat đã tính sẵn cờ `Escalate` (intent escalation / KB score <0.35) nhưng **chưa nối vào send gate** — gate deterministic gần-miễn-phí.

### Gap chính
| Gap | Anchor |
|-----|--------|
| G1 `ContentPublishJob` không check review/approve gì | ContentPublishJob.cs:58 |
| G2 Chat reply không có review + không có trạng thái draft-hold | ChatAgentGrpcService.cs:128 |
| G3 Comment/drip/document senders không gate, không đọc manual-mode | bảng trên #4-6 |
| G4 Không có flag tenant "manual approval mode" cho chat/content | ChannelInboundMessageConsumer.cs:67 |
| G5 `reviewer-agent` không nằm trong `RbacSeeder.DefaultAgents` → prod không bind LLM → catalog lọc mất → **gate no-op lặng lẽ ở prod** | RbacSeeder.cs:72 |
| G6 Autonomous run tự approve+schedule (tool `content.approve`/`content.schedule` RiskLevel.Low) | ToolRegistry.cs:92 |
| G7 Item kẹt review không có deadline đo (không có `DesiredPublishAt` trước khi có schedule row) | ContentItem.cs:10 |
| G8 Item scheduled-nhưng-chưa-review bị job skip mãi mãi mà không ai biết | — |
| G9 `ApproveItemAsync` không check `Status==draft` | ContentEndpoints.cs:352 |
| G10 `Reject()` không lưu lý do | ContentItem.cs:62 |

---

## 2. Thiết kế đích

### 2.1 Quyết định lõi
1. **1 reviewer-agent dùng chung** (tái dùng definition có sẵn), 2 chế độ gọi: async cho content (theo nhịp Hangfire), sync-bounded cho chat (timeout cứng, **fail-closed về hold-for-human** khi LLM down/timeout — không fail-open).
2. **Verdict chuẩn 3 giá trị**: `approve` → cho xuất; `reject` → chặn + lưu lý do + notify; `needs_human` → chặn + vào queue duyệt tay + notify.
3. **Content gate: TÁI DÙNG `ApprovedByAgentId`** làm điều kiện publish (critique #10 — không thêm 4 cột review song song). "Human + agent đều ký" = `ApprovedBy != null AND ApprovedByAgentId != null`. Chỉ nới guard tool `content.approve` chấp nhận cả status `approved` (hiện đòi `draft` — 1 dòng). Cột score/verdict chỉ thêm khi product cần analytics.
4. **Chat gate: tiered** (critique #11) — tầng 1 deterministic gần-free: nối cờ `Escalate` + toxicity có sẵn vào `reply.Blocked`; tầng 2 LLM critic chỉ chạy phần còn lại. Không bắt 100% reply trả thêm 1 LLM call trên hot-path `ConcurrentMessageLimit=1`.
5. **Reviewer độc lập** (critique #3 — bắt buộc, không phải open question): reviewer definition ≠ definition sinh content; reviewer không được cầm `content.publish`/`content.schedule` trong cùng run. Không thì gate chỉ là hình thức.

### 2.2 Điểm chặn (defence in depth)

**Adapter boundary (mới — trả lời G3):**
- `IReviewedOutboundSender` wrapper quanh `IChannelAdapter.SendAsync`/`ISocialPublisher.PublishAsync` cho MỌI đường tự động (job/agent). Wrapper check: tenant flag + review đã pass chưa + manual-mode hold.
- Text template cứng (drip, document delivery, comment template): **duyệt template 1 lần** (approve template = review), không review từng lần bắn — ghi rõ policy này.
- `CommentAutoReplyJob` + `DripSequenceJob`: thêm check `AiAutoReplyEnabled`/`Status`/tenant flag trước `SendAsync` (hiện đang fight lại handover của human).

**Content (3 lớp):**
1. Domain invariant: `ContentItem.MarkPublished` (ContentItem.cs:80) throw nếu thiếu `ApprovedByAgentId` (khi tenant flag on) — chốt chặn cuối, mọi call-site đều dính.
2. `ContentPublishJob.cs:58`: skip-and-hold item thiếu chữ ký agent + notify (thay vì lặng lẽ đăng); thêm re-check `Status==scheduled` (chặn stale schedule sau `RevertToApproved`).
3. `ContentPublishTool`/`ContentScheduleTool`: mirror điều kiện — autonomous run không tự schedule content chưa review (G6).

**Chat (1 điểm):**
- `ChatAgent.cs:255-266` (sau outbound toxicity, trước return): review step → set `reply.Blocked=true` + `BlockReason="review_rejected"/"review_needs_human"`. `ChatAgentGrpcService.cs:128` đã tôn trọng `Blocked` → không sửa caller.
- Khi `needs_human`: persist Message với status mới `pending_approval` (draft, chưa gửi) + trace `held_for_review`.
- **Idempotency** (critique #6): reply/hold phải key theo inbound `external_message_id` — MassTransit redelivery không được tạo draft đôi / gửi đôi.

### 2.3 Sync review từ HTTP request
`ScheduleItemAsync` gọi reviewer sync → phải có timeout + fail-closed: LLM chậm/down → item vào trạng thái `needs_review` + SLA job dẫn tiếp, KHÔNG treo/500 request của human (critique #5).

---

## 3. Manual-approval mode (Deliverable 2)

- 2 cột typed trên `Tenant` (theo pattern `RequireOrchestrationApproval`, KHÔNG overload flag đó):
  - `RequireChatReplyApproval` (default **false**) — on = AI reply nào cũng hold thành `pending_approval` chờ người duyệt.
  - `RequireContentReview` (default **false** + backfill + opt-in per tenant — flip default sau; xem §6.1).
- Scope: tenant-level trước (per-channel/per-agent để sau).
- Storage: migration 2 cột (**1 SqlCommand/file, không GO** + vá repair block run-all.bat — memory `run-all-skips-migration-replay`).
- API: GET/PUT cạnh `/api/admin/tenant/orchestration` (AdminEndpoints.cs:20); PUT gate `system:config`.
- Resolver: sibling của `EfOrchestrationApprovalResolver`; **cache cẩn thận — consumer chạy 2 host** (split-brain).
- Chat: `ChannelInboundMessageConsumer.cs:67` đọc thêm tenant flag; `Conversation.Open` seed `AiAutoReplyEnabled` từ tenant default thay vì hard-code true.
- Duyệt draft chat: `POST /api/inbox/conversations/{id}/drafts/{msgId}/approve` — perm `conversations:write`, chạy `OutboundMessageSafetyService` → `adapter.SendAsync` → stamp `external_message_id` → mark sent. Reject = discard.
- **Exemption ghi rõ** (critique #9): tin sale gõ tay + sale-assist draft do người bấm gửi = human là reviewer → miễn agent review. Ghi thành policy trong doc/UI.
- Fix G9 tiện tay: `ApproveItemAsync` thêm precondition `Status==draft`.

---

## 4. Notification giữ lịch (Deliverable 3)

- Thêm `ContentItem.DesiredPublishAt` + `LastReviewAlertAt` (idempotent alert). Capture lúc generate/submit + lúc schedule; enforce lead-time tối thiểu.
- Job mới `ContentReviewSlaJob` (model theo `IdleConversationAlertJob`): đăng ký `HangfireModule.cs:79`, cron `*/5 * * * *`, banded query, `DisableConcurrentExecution`.
- **Scan set phải gồm cả `scheduled AND ApprovedByAgentId==null`** (critique #4 — không thì item bị publish-job skip mãi mà không ai được báo = chính cái lỗi requirement muốn tránh). Chốt 1 nguồn deadline: `DesiredPublishAt` là source of truth SLA.
- Tiers: T1 (trước deadline lead-time) → notify creator/reviewer in-app; T2 (sát/quá deadline) → escalate content-lead qua `IContentReviewEscalationRecipientResolver` (mirror idle-job) + optional email; fallback tenant-broadcast khi role chưa seed.
- **Không auto-approve khi trễ deadline** — hold + escalate to (mặc định). `AutoApproveOnDeadline` opt-in nếu product muốn (§6).
- Chạy trong API Hangfire (dùng `DbNotificationPublisher` trực tiếp). Deadline math **UTC** (tránh lệch 7h). PII-redact nội dung item trong body notification (memory `pii-redact-derived-content`).

---

## 5. Phase (mỗi phase ship được)

**Phase 0 — Reviewer plumbing (enabler) — ✅ DONE 2026-07-10**
- ✅ `RbacSeeder.DefaultAgents` += `("reviewer-agent", "Agent-Review", "reviewer")` — AgentConfig row + auto-bind LLM mọi tenant (fix G5; catalog cần binding, agent_definitions đã có sẵn qua deploy/seed/agent-definitions.sql + DevDataSeeder).
- ✅ `AgentPromptDefaults.DefaultFor("reviewer-agent")` — rubric 5 tiêu chí (an toàn/chính sách/thương hiệu/chính xác/chất lượng), verdict approve|reject|needs_human, cấm tự sửa nội dung + cấm duyệt nội dung mình tạo (reviewer independence QĐ khóa).
- ✅ `ContentApproveTool` guard nhận `draft|approved` (re-review item human đã duyệt để thêm chữ ký agent — tiền đề Phase 1).
- ✅ Tests: 2 test mới (re-review approve + reject demote), RED→GREEN; suites 201+72 pass.

**Phase 1 — Content gate (Deliverable 1a) — ✅ DONE 2026-07-10**
- ✅ Domain: `ContentItem` +`CreatedByAgentId` (độc lập reviewer≠creator) +`RejectedReason` (G10); `Reject(at, reason)`; `MarkPublished(at, requireAgentReview)` throw khi thiếu chữ ký — backstop mọi call-site. `Tenant.RequireContentReview` + setter (default OFF, QĐ1).
- ✅ Resolver: `IContentReviewPolicyResolver` (SharedKernel) + `EfContentReviewPolicyResolver`, DI cả 2 host.
- ✅ `ContentPublishJob`: re-check `Status==scheduled` (chặn stale sau RevertToApproved, G8 nửa đầu) + hold item thiếu `ApprovedByAgentId` khi flag on (G1) — schedule giữ pending, log warn (notify tiered = Phase 4).
- ✅ Tools: `ContentGenerateTool` stamp `CreatedByAgentId`; `ContentScheduleTool`/`ContentPublishTool` chặn item chưa ký khi flag on (G6); `ContentApproveTool` chặn self-approval (`reviewer_independence`).
- ✅ Reviewer: `ContentReviewer` (Agents.Core, LLM binding qua `LlmCallScope("reviewer-agent")`, verdict JSON parse fail-closed→needs_human); proto `ContentAgent.Review` rpc; `ContentAgentGrpcService.Review` (timeout 20s server-side, stamp/demote/hold).
- ✅ API: `ScheduleItemAsync` sync-review qua gRPC (deadline 25s, mọi lỗi → 422 không tạo schedule — fail-closed QĐ3); `ApproveItemAsync` chặn re-approve scheduled/published (G9); `RejectItemAsync` lưu reason.
- ✅ DB: migration `0050_content_review_gate.sql` (3 cột) + run-all repair block + `deploy/backfill_content_agent_review.sql` (one-shot data_patches, stamp item approved/scheduled/published cũ) wired vào `:apply_data_patches`.
- ✅ Tests 16 mới: domain backstop (3), tools gate + independence + stamp (4), job hold/signed/stale (3), reviewer parse fail-closed (6). Suites: Domain 84, Agents 7, AgentService 76, Infrastructure 204, Api 192 — all green. (Sửa kèm 1 test pre-existing fail từ 5cee084: assert `config.SystemPrompt` → `systemPrompt`.)

**Phase 2 — Chat gate (Deliverable 1b) — ✅ DONE 2026-07-10**
- ✅ `Message.Status` (`sent|pending_approval|blocked`, default sent) + `MarkSent()` (Phase 3 approve dùng); `AppendMessage(status:)`; EF config; migration `0051_messages_status.sql` + repair block run-all.
- ✅ Tầng 1 deterministic: `ChatReplyReviewTrigger.NeedsLlmReview` (Agents.Core) — cờ `Escalate` có sẵn (intent lạ / KB score <0.35 / KB rỗng) OR regex nội dung rủi ro (số tiền/%, giá, học phí, cam kết, khuyến mãi...). Chạy 100% reply, miễn phí.
- ✅ Tầng 2 LLM critic: reuse `ContentReviewer` (binding reviewer-agent), gọi trong `ChatAgentGrpcService` SAU final reply (ngoài LlmCallScope chat-agent), timeout 8s. Verdict: approve→gửi; reject→persist `blocked` + trace `review_rejected`, KHÔNG gửi; needs_human→persist `pending_approval` + trace `held_for_review`, KHÔNG gửi. Fail-closed mọi lỗi (QĐ3).
- ✅ Send gate: `SendAsync` chỉ chạy khi `!Blocked && status=="sent"`. Reply bị toxicity-block giờ persist status `blocked` (fix luôn bug cũ: tin blocked hiện như đã gửi).
- ✅ Idempotency: consumer chỉ reply khi ingest `!Deduplicated` (có sẵn) — thêm test chốt redelivery không sinh draft đôi.
- ✅ DTO/FE: `MessageDto.Status` + `InboxMessage.status` + `GetAsync` map + `MessageBubble` badge "Chờ duyệt (chưa gửi)" (warning) / "Đã chặn (không gửi)" (error).
- ✅ Tests 10 mới: trigger 6 (escalate/risky/smalltalk/blocked/empty), grpc 3 (needs_human hold, reject block, LLM-down fail-closed), consumer dedup 1. Suites: Agents 9, AgentService 79, Infrastructure 205, Domain 84, Api 192 — all green.
- Lưu ý vận hành: tenant KB rỗng → mọi reply escalate → critic chạy 100% (đúng QĐ2 — không KB đối chiếu thì phải soi); nạp KB làm giảm tỷ lệ critic. Approve/reject draft `pending_approval` = Phase 3.

**Phase 3 — Manual mode (Deliverable 2) — ✅ DONE 2026-07-10**
- ✅ `Tenant.RequireChatReplyApproval` (default OFF) + migration `0052` + repair block; `IChatApprovalPolicyResolver` + Ef impl + DI AgentService.
- ✅ Hold-all: `ChatAgentGrpcService` — flag on → MỌI AI reply persist `pending_approval` + trace `held_for_approval`, skip LLM critic (người là reviewer cuối), không gửi. Đặt gate ở service = mọi đường vào chat agent đều dính, consumer không phải sửa.
- ✅ Gate comment/drip (critique #2): `CommentAutoReplyJob` skip khi `!AiAutoReplyEnabled || resolved`; `DripSequenceJob` hold enrollment (không cancel — AI bật lại thì sequence tiếp) khi conversation manual-mode. Bot hết fight handover của sale.
- ✅ Admin API: GET/PUT `/api/admin/tenant/orchestration` mang thêm `requireContentReview` + `requireChatReplyApproval` (nullable — client cũ không gửi thì giữ nguyên); PUT gate `system:config` (có sẵn).
- ✅ FE: 2 toggle mới cạnh nút duyệt-phiên ở `/agents` (AgentDashboardPage); admin.ts client mở rộng.
- ✅ Draft approve/reject: `POST /api/inbox/conversations/{id}/drafts/{msgId}/approve|reject` (perm `conversations:write`, check inbox membership) — approve chạy lại `OutboundMessageSafetyService` → `SendAsync` → stamp `external_message_id` + `MarkSent` + notify SignalR; reject → `MarkBlocked` (giữ audit). FE: nút "Duyệt & gửi" / "Bỏ tin này" ngay trên bubble chờ duyệt.
- ✅ Exemption QĐ5 inherent: tin sale gõ tay (`SendOutboundAsync`) + sale-assist (người bấm gửi) không đi qua đường review — human là reviewer.
- ✅ Template QĐ6: drip/comment template là text tĩnh do dev/admin seed = "đã duyệt 1 lần"; chưa cần cột approved trên template (thêm khi có UI sửa template).
- Skip (nói trước): `Conversation.Open` seed `AiAutoReplyEnabled` từ tenant default — không cần nữa vì manual-mode = hold-all ở tầng reply, không phải tắt AI per-conversation.
- ✅ Tests 4 mới: hold-all (phase held_for_approval chứng minh skip critic), comment skip manual, drip hold giữ enrollment active, + suites: AgentService 80, Infrastructure 207, Api 192, Domain 84 — green; FE tsc 0 lỗi.

**Phase 4 — SLA notifications (Deliverable 3) — ✅ DONE 2026-07-10**
- ✅ `ContentItem.DesiredPublishAt` + `LastReviewAlertAt` + `SetDesiredPublishAt`/`MarkReviewAlerted`; migration `0053` + repair block run-all.
- ✅ Capture deadline TRƯỚC review-gate: `ScheduleItemAsync` (đảo thứ tự — resolution + SetDesiredPublishAt + save trước khi gọi sync review; review fail vẫn giữ mốc) + `ContentScheduleTool` (gate fail → save deadline rồi Fail); schedule thành công cũng stamp.
- ✅ `ContentReviewSlaJob` (cron `*/5`, queue content, `DisableConcurrentExecution`, UTC): scan item unsigned + có deadline + status draft/approved/**scheduled** (critique #4 — publish job skip scheduled-unreviewed mỗi pass, không có job này là miss âm thầm). Chỉ nhắc tenant có flag ON (per-tenant cache). Idempotent 2 nấc qua `LastReviewAlertAt`: T1 (trước hạn ≤60') notify creator `content_review_pending` (warning) 1 lần; T2 (sát/quá hạn) escalate `content_review_overdue` (error) tới `IContentReviewEscalationRecipientResolver` (Marketer∪Admin, mirror idle-job) 1 lần, rỗng → tenant-broadcast. Body không chứa nội dung bài (khỏi dính PII), chỉ platform + giờ.
- ✅ QĐ4 giữ nguyên: không auto-approve khi trễ — chỉ hold + notify to dần.
- ✅ Tests 4 mới: T1 once (pass 2 không re-alert), T2 escalate scheduled-unreviewed + không auto-approve, broadcast fallback, signed/flag-off skip. Suites: Infrastructure 211, AgentService 80, Api 192, Domain 84 — green.

**Phase 5 — Adapter-boundary hardening (chốt G3 triệt để) — ✅ DONE 2026-07-10**
- **Đổi thiết kế so plan gốc** (ghi nhận): bỏ `IReviewedOutboundSender` DI wrapper — các call-site đã gate xong từng chỗ ở P2/P3/P5, wrapper chỉ còn là indirection; giá trị thật là CHẶN DRIFT → thay bằng **architecture guard test** `OutboundSenderBoundaryTests` (Infrastructure.Tests): quét `src/**/*.cs`, file nào chạm `IChannelAdapter` + `.SendAsync(` phải nằm trong whitelist 7 file kèm chú thích gate của từng file — sender mới quên gate → test đỏ. Wrapper làm thật khi có sender thứ 8+ cần policy chung runtime.
- ✅ Toxicity gate cho `DripSequenceJob` (lỗ thật duy nhất còn, critique #7): template duyệt-1-lần NHƯNG `{lead_name}` interpolate tên contact (dữ liệu khách tự đặt) → bản render qua `IToxicityFilter` (OutboundBlockThreshold) trước send; toxic → cancel enrollment + log error (retry vô ích, lỗi template/tên phải sửa tay). Deps optional — DI API host có sẵn Detoxify singleton.
- ✅ Template-approved policy (QĐ6) ghi thành comment chuẩn tại 2 chỗ template tĩnh: `CommentAutoReplyJob` (text 100% tĩnh — không toxicity per-send) + `DocumentDeliveryService` (biến nội suy chỉ URL/ngày hệ thống sinh); cả hai ghi rõ: thêm biến từ dữ liệu khách/LLM thì bản render PHẢI qua toxicity (mẫu ở DripSequenceJob).
- ✅ Tests 2 mới: drip toxic → không gửi + enrollment cancelled; boundary guard whitelist khớp thực tế. Suites: Infrastructure 213, AgentService 80, Api 192, Domain 84, Agents 298 — all green.

---

## 6. Quyết định product — ĐÃ CHỐT 2026-07-10 (toàn bộ theo đề xuất)

### QĐ1 — Bật gate content mặc định thế nào khi deploy?

**Vấn đề**: gate content = bài chỉ được đăng khi có chữ ký agent (`ApprovedByAgentId`). Nhưng các bài **đang scheduled hiện tại** chưa có chữ ký này. Nếu bật gate ngay, publish job sẽ hold toàn bộ hàng đang chờ đăng → lịch đăng của khách đứng hết ngay ngày deploy.

| Phương án | Được | Mất |
|-----------|------|-----|
| A. Bật ON ngay mọi tenant | Bảo vệ tuyệt đối từ ngày 1 | Sự cố vận hành: mọi bài đã lên lịch bị treo |
| B. **OFF mặc định + backfill + bật opt-in từng tenant** (đề xuất) | Không vỡ gì; bật dần có kiểm soát | Có khoảng thời gian chưa được bảo vệ |
| C. ON ngay + backfill đóng dấu "đã review" cho bài cũ | Bảo vệ ngay, không treo bài | Chữ ký backfill là "giả" — bài cũ chưa từng được agent review thật, audit sai lệch |

**Đề xuất B**: deploy OFF → chạy backfill đóng dấu bài cũ → bật thử 1 tenant → bật rộng. Cần anh xác nhận trình tự + thời điểm bật.

### QĐ2 — Chat reply: LLM review 100% hay chỉ case nghi ngờ?

**Vấn đề**: chat reply chạy trong consumer xử lý tuần tự (`ConcurrentMessageLimit=1`). Thêm 1 LLM call review cho MỖI reply = khách chờ thêm ~1–3s mỗi tin + chi phí LLM ~x2. Trong khi hệ thống đã tính sẵn cờ `Escalate` (intent lạ / KB không khớp) và toxicity — miễn phí — nhưng chưa nối vào gate.

| Phương án | Được | Mất |
|-----------|------|-----|
| A. LLM reviewer duyệt 100% reply (sync, timeout cứng) | Đúng nghĩa đen "mọi output có agent review" | Chậm + đắt trên mọi tin, kể cả "chào anh" |
| B. **Tiered: deterministic 100% (Escalate + toxicity) → LLM critic chỉ cho tin nghi ngờ** (có giá/cam kết/số liệu, KB score thấp, intent lạ) (đề xuất) | Nhanh, rẻ; tin rủi ro vẫn bị soi kỹ | Phần lớn tin thường chỉ qua gate máy, không qua LLM reviewer |
| C. Tenant bật manual-mode thì khỏi LLM review — hold hết chờ người | Đơn giản nhất | Không còn "AI trả lời tự động" ở tenant đó |

**Đề xuất B.** Câu hỏi cần anh chốt: chấp nhận cách hiểu "agent review = gate máy + LLM cho case nghi" thay vì LLM cho từng tin? Nếu yêu cầu nghĩa đen 100% LLM → chọn A và chấp nhận latency/chi phí.

### QĐ3 — Reviewer chết (LLM down/timeout) thì làm gì?

**Vấn đề**: review là BẮT BUỘC, vậy khi chính reviewer không chạy được (hết quota, API down, timeout) thì output đi đâu?

| Phương án | Được | Mất |
|-----------|------|-----|
| **Fail-closed: không gửi, hold chờ người + notify ngay** (đề xuất) | Không bao giờ có output chưa duyệt lọt ra; đúng tinh thần "bắt buộc" | Chat: khách không được auto-reply cho tới khi người vào hoặc LLM sống lại. Content: bài chờ |
| Fail-open: reviewer lỗi thì cứ gửi như cũ | Khách không bị chờ | Gate thành "tùy chọn" — đúng lúc hệ thống bất ổn nhất thì mất kiểm soát |

**Đề xuất fail-closed** + giảm đau: timeout ngắn (2–5s), retry 1 lần, alert riêng "hold vì reviewer down" để người vào xử lý ngay. Cần anh xác nhận: chấp nhận khách chờ người khi LLM down (thường vài phút–giờ), đổi lấy không bao giờ gửi tin chưa duyệt.

### QĐ4 — Tới giờ đăng mà chưa ai review xong thì sao?

**Vấn đề**: bài có giờ đăng (`DesiredPublishAt`), review/duyệt chưa xong khi tới giờ. Requirement nói "đảm bảo đúng lịch" — nhưng đúng lịch bằng cách nào?

| Phương án | Được | Mất |
|-----------|------|-----|
| **Hold + escalate: KHÔNG đăng, notify leo thang T1 (trước deadline) → T2 (sát/quá deadline, ping content-lead + email)** (đề xuất) | Không bao giờ đăng bài chưa duyệt. "Đúng lịch" đạt bằng cách báo người SỚM để duyệt kịp | Nếu người vẫn không duyệt → bài trễ lịch (nhưng có chủ đích, có log) |
| `AutoApproveOnDeadline` (opt-in per tenant): tới giờ chưa duyệt thì tự đăng | Lịch không bao giờ trễ | Đăng bài không ai duyệt đúng lúc không ai nhìn — phá mandatory review |

**Đề xuất hold + escalate, KHÔNG làm auto-approve** (kể cả opt-in) ở phase đầu — thêm sau nếu có nhu cầu thật. Cần anh xác nhận: trễ lịch chấp nhận được khi không ai duyệt, miễn là được báo sớm.

### QĐ5 — Tin sale gõ tay có phải qua agent review không?

**Vấn đề**: requirement "MỌI content output" — đọc nghĩa đen thì tin nhân viên tự gõ trong inbox + sale-assist draft (người bấm gửi) cũng phải qua agent duyệt → máy duyệt người, chat chậm, sale khó chịu.

| Phương án | Được | Mất |
|-----------|------|-----|
| **Miễn: human = reviewer. Agent review chỉ áp cho output MÁY tự sinh-tự gửi** (đề xuất) | Sale chat mượt; đã có sẵn `OutboundMessageSafetyService` chặn toxicity tin gõ tay | Tin người gõ chỉ qua gate toxicity, không qua LLM reviewer |
| Không miễn: mọi tin kể cả gõ tay đều qua reviewer | Nhất quán tuyệt đối | Mỗi tin sale gõ chờ thêm vài giây; sale bị máy "chấm bài" |

**Đề xuất miễn.** Ranh giới rõ: tự động sinh + tự động gửi → review bắt buộc; người gõ hoặc người bấm gửi → người chịu trách nhiệm. Cần anh xác nhận ranh giới này.

### QĐ6 — Template cố định (drip, giao tài liệu, comment mẫu) review kiểu gì?

**Vấn đề**: drip sequence + document delivery + comment auto-reply bắn text viết sẵn, lặp hàng trăm lần. LLM review từng lần bắn = review đi review lại cùng 1 đoạn text → vô nghĩa + đốt tiền.

| Phương án | Được | Mất |
|-----------|------|-----|
| **Duyệt template 1 lần khi tạo/sửa; lúc bắn chỉ check "template đã duyệt chưa"** (đề xuất) | Đúng bản chất: nội dung không đổi giữa các lần bắn; rẻ | Biến số nội suy (tên khách, link) không được review per-send — chấp nhận vì không đổi bản chất |
| Review mỗi lần bắn | Che cả trường hợp template có đoạn LLM sinh động | Đốt LLM vô ích với template tĩnh |

**Đề xuất duyệt 1 lần** + quy tắc bổ sung: nếu sau này template có đoạn LLM điền động → riêng đoạn đó phải qua review per-send. Cần anh xác nhận.

### Bảng tóm tắt đề xuất

| # | Câu hỏi | Quyết định | Chốt |
|---|---------|-----------|------|
| 1 | Default gate content | OFF + backfill + opt-in dần | ☑ 2026-07-10 |
| 2 | Chat review | Tiered (máy 100%, LLM cho case nghi) | ☑ 2026-07-10 |
| 3 | Reviewer down | Fail-closed (hold + alert) | ☑ 2026-07-10 |
| 4 | Trễ deadline | Hold + escalate, không auto-approve | ☑ 2026-07-10 |
| 5 | Sale gõ tay | Miễn agent review (human = reviewer) | ☑ 2026-07-10 |
| 6 | Template tĩnh | Duyệt 1 lần khi tạo/sửa | ☑ 2026-07-10 |

## Rủi ro kỹ thuật đã ghi nhận
- 2 publish path + 6 sender path — gate phải phủ hết, domain invariant là backstop.
- Migration: no GO, 1 câu/file, vá repair block run-all.bat.
- Consumer 2 host — cache flag phải nhất quán.
- `TryAutoReplyAsync` nuốt exception — reviewer phải return hold tường minh, không throw.
- Reviewer khó tính reject liên tục → cap retry, tránh burn `MaxRounds` (lỗi max_rounds cũ).
- R6: `content.approve` đòi draft — nới nhận `approved` (1 dòng).
