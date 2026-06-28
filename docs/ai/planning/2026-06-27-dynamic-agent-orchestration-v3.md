# Kế hoạch: Dynamic Agent Orchestration V3 — Agent-to-Agent thực thụ

> Ngày: 2026-06-27 · Nhánh: `merge-chat-v2-no-copilot` · Tiền nhiệm: [v2 planning](2026-06-24-feature-dynamic-agent-orchestration-v2.md)
>
> Mục tiêu tối thượng: hệ thống vận hành **agents-to-agents**. Con người chỉ **lập kế hoạch, theo dõi, ra quyết định phê duyệt**. Giảm tối đa thao tác tay.

---

## 1. Chẩn đoán hiện trạng (vì sao "chưa đúng ý")

### 1.1 Bug đang chặn — `content-agent failed` + `max_rounds`
Hai lỗi là **một chuỗi nhân quả**, không phải hai lỗi rời:

| # | Triệu chứng | Gốc rễ | Vị trí |
|---|---|---|---|
| B1 | `HttpClient.Timeout of 30 seconds` | Timeout LLM hard-code 30s. Task "30 ngày content" sinh dài > 30s nên cắt. | [ChatModule.cs:21](../../../src/agents/Clawbot.Agents.Core/Chat/ChatModule.cs#L21) `c.Timeout = TimeSpan.FromSeconds(30)` |
| B2 | `Retry failed after 4 tries` | Polly retry 3 lần (=4 lượt), mỗi lượt lại đụng timeout 30s. | [HttpResiliencePolicies.cs:14](../../../src/shared/Clawbot.Infrastructure/Resilience/HttpResiliencePolicies.cs#L14) `WaitAndRetryAsync(3, ...)` |
| B3 | `[Lỗi] max_rounds` | `MaxRounds=3`. Mỗi vòng task-1 timeout → "failed" → **replan** → vòng sau lại timeout → hết 3 vòng → `max_rounds`. | [AutonomousRunContracts.cs:20](../../../src/agents/Clawbot.Agents.Core/Orchestrator/AutonomousRunContracts.cs#L20) + [AutonomousOrchestrator.cs:145](../../../src/agents/Clawbot.Agents.Core/Orchestrator/AutonomousOrchestrator.cs#L145) |

Replan loop còn sai về bản chất: **lỗi timeout là lỗi tạm thời (transient)**, nhưng orchestrator xử như lỗi logic và đi *replan* (gọi lại LLM lập kế hoạch) thay vì *retry task*. Tốn vòng + tốn tiền + không bao giờ hết lỗi.

### 1.2 Hạn chế kiến trúc — agent động **chỉ sinh text, không thao tác hệ thống**
[GenericLlmAgentWorker.cs](../../../src/agents/Clawbot.Agents.Core/Orchestrator/GenericLlmAgentWorker.cs) chỉ làm: RAG → `chatClient.CompleteAsync(...)` → trả `reply.Text`. Không có tool, không ghi DB, không gọi Pancake, không enqueue job. Tức là:
- content-agent "sinh content" = trả 1 đoạn text rồi **vứt đi** (chỉ lưu vào trace).
- `AllowedToolsJson` có trên `AgentDefinition` nhưng là **dead data**: catalog không SELECT, worker không đọc, API không set được. → agent động **không thể gọi tool nào**.
- `IClaudeChatClient` **không có tham số tools / không trả tool_use** → chưa có kênh function-calling.

> ⚠️ **Bẫy shadowing:** [AutonomousOrchestrator.ResolveAgent](../../../src/agents/Clawbot.Agents.Core/Orchestrator/AutonomousOrchestrator.cs#L213) ưu tiên DB definition. Nếu tenant tạo agent động trùng code adapter tĩnh (vd `content-agent`, `docs-agent`) → **âm thầm hạ cấp** xuống text-only thay vì chạy adapter thật.

### 1.3 ⭐ Phát hiện then chốt: **"tay chân" phần lớn ĐÃ CÓ — chỉ thiếu dây nối**
Đây là điểm đảo chiều cả kế hoạch. Hệ thống **đã có** năng lực thao tác thật, chỉ là agent động không chạm tới:

| Năng lực | Đã tồn tại ở đâu | Trạng thái |
|---|---|---|
| **Đăng/trả lời** FB/Zalo | `PancakeChannelAdapter.SendAsync` (`reply_inbox`, rate-limit, token mã hoá) | ✅ Hoạt động, có người/job gọi |
| **Đăng bài social** | `HttpSocialPublisher.PublishAsync` + `ContentPublishJob` (cron */5p) | ✅ Có; endpoint publisher **chưa cấu hình mặc định** |
| **Vòng đời content** | `content_items`/`content_schedules`, state machine draft→approved→scheduled→published | ✅ Đủ; **chỉ persist qua `ContentAgentGrpcService`** |
| **Ads thật** | Meta/TikTok connector + `AdsRuleEvaluationJob` (apply pause/scale) | ✅ Closed-loop; lookalike/remarketing còn stub |
| **Báo cáo/KPI** | `DailyKpiRollupJob`→`KpiDailies`, `ReportAgent` (anomaly/forecast), `/api/analytics` | ✅ Đầy đủ |
| **Adapter hành động** | `AgentAdapters.cs`: chat/content/research/docs/**ads**/**saleassist** | ✅ Chỉ chúng mới gọi năng lực thật |
| **Thông báo realtime** | `INotificationPublisher` + SignalR `NotificationHub` + FE `useNotificationsRealtime` | ✅ Có; orchestrator **không gọi** |
| **Lịch chạy agent** | `AgentSchedules` + `AgentScheduleWorker` (poll 1p) → `AutonomousOrchestrator` | ✅ End-to-end |

**Hệ quả:** phần lớn V3 là **wiring + cầu nối**, không phải xây mới. Đây là khoảng cách thật sự:
1. Agent động không có lớp tool → không chạm 8 năng lực trên.
2. Orchestrator content path **không persist** (worker trả text; registry route `content`→stub no-op; `ContentAgentAdapter` trả JSON nhưng không lưu `ContentItem`).
3. **Không có system-actor**: `ContentItem.Approve` đòi `userId` người thật → chặn luồng tự duyệt→đăng.
4. Orchestrator **không bắn thông báo** lúc xong/lỗi (`AutonomousRunSink`/`OrchestratorGrpcService` không inject `INotificationPublisher`); `AgentSession` **thiếu cột UserId** nên chỉ broadcast được toàn tenant.
5. AI chat reply được **lưu mà không gửi** (`ChatAgentGrpcService` persist `out` message, không gọi `SendAsync`).

### 1.3 UI theo dõi — [OrchestrationPanel.tsx](../../../src/frontend/clawbot-web/src/features/agents/OrchestrationPanel.tsx)
- Kế hoạch hiển thị **raw JSON** trong `<textarea>` (dòng 248) → khó nhìn.
- Đã có poll 3s + `sessionId` lưu trong URL (dòng 86, 94-103) → **F5 đáng lẽ sống**. Cần xác minh deploy hiện tại; nếu vẫn mất là do bản build cũ hoặc `goal`/trạng thái planning reset (session vẫn còn).
- Chưa có: sơ đồ agent (số lần dùng, đang làm gì), thông báo (toast/push), tóm tắt người-đọc-được.
- Tiến trình hiện chỉ là trace text; không có streaming thật, không "bắn thông báo".

---

## 2. Tầm nhìn đích

```
Người dùng: nhập mục tiêu  ─►  Orchestrator: lập kế hoạch DAG  ─►  báo cáo lại người dùng ("đã lập N task, dự kiến X")
                                          │
                                          ▼
        ┌──────────── Agent con (tái sử dụng / sinh mới) — MỖI agent có TOOLS thật ───────────┐
        │ content-agent  → sinh bài → LƯU ContentDraft → lên lịch                              │
        │ marketing/sale → ĐĂNG qua Pancake → log → đẩy số liệu                                │
        │ reporter-agent → đọc số liệu → tóm tắt → báo cáo                                     │
        └──── mỗi agent: thao tác hệ thống độc lập + báo cáo kết quả có cấu trúc về Orchestrator ┘
                                          │
                                          ▼
        Người dùng: chỉ THEO DÕI (graph + log realtime) + PHÊ DUYỆT hành động rủi ro cao
```

---

## 3. Kế hoạch theo phase

### Phase 0 — Hotfix unblock (nhỏ, làm ngay)
- **A1. Timeout cấu hình được.** Đưa timeout LLM ra options (vd `Llm:HttpTimeoutSeconds`, default 120s). Sửa [ChatModule.cs:19-22](../../../src/agents/Clawbot.Agents.Core/Chat/ChatModule.cs#L19-L22). Lý tưởng: **streaming** để task dài không bao giờ đụng wall-clock timeout.
- **A2. Phân biệt transient vs logical trong vòng lặp.** Trong [AutonomousOrchestrator.ExecuteTaskAsync](../../../src/agents/Clawbot.Agents.Core/Orchestrator/AutonomousOrchestrator.cs#L163): timeout/5xx/429 → **retry chính task** (backoff, cap 2), KHÔNG replan. Chỉ replan khi lỗi logic (agent trả fail có lý do nghiệp vụ). Tránh đốt vòng + tiền.
- **A3. Trace báo "đang chạy lâu".** Trong lúc gọi LLM dài, emit heartbeat trace để UI không tưởng treo.

> Phase 0 đủ để mục tiêu "Lên kế hoạch đăng bài tuyển sinh hằng ngày" chạy hết DAG thay vì chết ở task-1.

### Phase 1 — Lớp Tool (agent hành động được) ⭐ trọng tâm
Mục tiêu: agent động **gọi được** 8 năng lực ở §1.3, có RBAC + cost-guard.
- **B1. Tool Registry.** Định nghĩa `IAgentTool` (name + JSON input schema + handler). Bọc **cái đã có** trước khi viết mới: 6 `AgentAdapters` (content/ads/saleassist/docs/research/chat), `PancakeChannelAdapter.SendAsync`, `ISocialPublisher`, các action content (approve/schedule), `ReportAgentRunner`. Tận dụng `AgentTaskInput` helper sẵn có để bind tham số.
- **B2. Vòng thực thi tool.** Thay/bọc `GenericLlmAgentWorker` bằng vòng **ReAct/JSON-action** trên `CompleteAsync` (provider hiện tại an toàn, không phụ thuộc native tool-use), HOẶC mở rộng `IClaudeChatClient` nhận `tools` + trả `tool_use` nếu provider hỗ trợ. Bắt đầu ReAct cho nhanh.
- **B3. Đọc `AllowedToolsJson` thật.** Catalog SELECT lại field này (đang bị drop), worker chỉ cho gọi tool trong allow-list, API `UpsertAgent` cho set được. Sửa **bẫy shadowing** §1.2: agent động có thể *delegate* sang adapter tĩnh thay vì hạ cấp.
- **B4. AgentResult có kênh structured.** Thêm chỗ mang output có cấu trúc + tool-trace qua DAG (hiện `Output` chỉ là string).

### Phase 2 — Khép vòng hành động + báo cáo
- **B5. content path persist thật.** content-agent → tool gọi luồng persist (`ContentAgentGrpcService` hoặc tạo `ContentItem` trực tiếp) → tool `schedule`. Bỏ stub `content` trong `DefaultAgentRegistry`.
- **B6. System-actor để tự duyệt.** Cho `ContentItem.Approve` nhận actor hệ thống (không bắt buộc userId người thật) + gắn sau cổng phê duyệt rủi ro (D2). Mở khoá luồng draft→approve→publish tự động.
- **B7. sale/marketing đăng thật.** Tool `SendAsync`/`Publish` + log → đẩy số liệu vào KPI. Vá luôn `ChatAgentGrpcService`: sau khi persist reply thì **gọi `SendAsync`** (qua cổng `OutboundMessageSafetyService`).
- **B8. Agent-to-agent reporting.** Agent trả kết quả có cấu trúc về Orchestrator; Orchestrator tổng hợp → **báo cáo người-đọc-được** cho user sau lập kế hoạch và sau hoàn tất.

### Phase 3 — UI theo dõi (đúng các ý bạn nêu)
- **C1. Plan đẹp:** render DAG/card thay raw JSON ([OrchestrationPanel L248-255](../../../src/frontend/clawbot-web/src/features/agents/OrchestrationPanel.tsx#L248)); "sửa JSON" giấu sau toggle nâng cao (giữ optimistic etag).
- **C2. Tracking sống, chịu F5:** `sessionId` **đã** lưu URL → F5 sống sẵn (cảm giác "mất" có thể do build cũ hoặc bấm "Mục tiêu mới"/tab mới làm rớt URL). Bổ sung: **danh sách run gần đây / đang chạy** (+localStorage) để không phụ thuộc URL. Cân nhắc đẩy SignalR thay poll 3s.
- **C3. Sơ đồ agent thật:** hiện "Sơ đồ agent" chỉ vẽ **1 node orchestrator** ([AgentDashboardPage L577-604](../../../src/frontend/clawbot-web/src/features/agents/AgentDashboardPage.tsx)); cần node cho từng agent con + **số lần dùng** + **đang làm task gì** + trạng thái, cạnh = `dependsOn`. Cần thêm usage/call-count theo session vào `OrchestrationTaskDto` (hiện không có). Nút "đề xuất thêm agent".
- **C4. Thông báo (tái dùng hạ tầng sẵn có):** inject `INotificationPublisher` vào `AutonomousRunSink.CompleteAsync/FailAsync/CancelAsync` — **copy y hệt `AdsAgentGrpcService`** (cùng project AgentService). Thêm cột `UserId` vào `AgentSession` để target đúng người gửi (nay chỉ broadcast tenant). Mount `useNotificationsRealtime` ở AppShell/Topbar (đang chỉ ở Dashboard/Notifications) để chuông sống mọi trang. Thêm toast trong panel.
- **C5. Orchestrator báo cáo lại user** ngay sau lập kế hoạch (trace `planning_completed` đã có → nâng thành tóm tắt người-đọc-được + notification).

### Phase 4 — Tự hành (autonomy)
- **D1.** Nối lịch chạy sẵn có ([OrchestrationV2 schedules](../../../src/frontend/clawbot-web/src/features/orchestration/OrchestrationV2Page.tsx)) vào agent có-tool.
- **D2. Cổng phê duyệt theo rủi ro:** người chỉ duyệt hành động rủi ro cao (đăng bài, chi tiền ads); còn lại tự chạy. (Hạ tầng approval đã có: `RequiresApproval`, `Approve`.)
- **D3. Guardrail:** cost guard (có), PII redact (có), quyền theo tool, **dry-run mode** để xem trước hành động.

---

## 4. Thứ tự ưu tiên đề xuất
1. **Phase 0** (vài giờ) — hết `max_rounds`/timeout, DAG chạy thông.
2. **Phase 1** (lõi giá trị) — agent có tay.
3. **Phase 2** — content/sale chạm hệ thống thật + báo cáo.
4. **Phase 3** — UI.
5. **Phase 4** — tự hành.

## 5. Kết luận đã xác minh (workflow map 6 vùng)
- ✅ **Outbound CÓ:** `PancakeChannelAdapter.SendAsync` (reply FB/Zalo, rate-limit, token mã hoá) + `HttpSocialPublisher.PublishAsync` (đăng social) + `ContentPublishJob` (cron */5p). → B7 nối được ngay. **Cảnh báo:** endpoint `Content:Publisher` chưa cấu hình mặc định (trả `publisher_not_configured`) → cần config provider thật.
- ✅ **Content store CÓ:** `content_items`/`content_schedules`, state machine đủ; chỉ persist qua `ContentAgentGrpcService` → B5 trỏ tool vào đó.
- ✅ **Tool-calling — chọn ReAct trước:** `IClaudeChatClient.CompleteAsync` chưa có `tools`/`tool_use`. Không phụ thuộc provider → **làm ReAct/JSON-action loop** (B2) là an toàn nhất; nâng native tool-use sau nếu cần.
- ✅ **Notification CÓ, chỉ chưa gọi:** mẫu copy là `AdsAgentGrpcService` (cùng AgentService). Chặn duy nhất: `AgentSession` thiếu `UserId` (migration nhỏ).

### Rủi ro còn lại
- **System-actor cho auto-approve** (B6): `ContentItem.Approve` đang đòi userId người thật → cần actor hệ thống + cổng rủi ro, nếu không luồng tự đăng bị chặn by-design.
- **lookalike/remarketing stub** ở cả Meta & TikTok connector → 2 op ads này là no-op thật.
- **Hai scheduler tách rời:** agent **không** trigger được Hangfire job trực tiếp; muốn vậy phải bọc `IBackgroundJobClient.Enqueue` thành tool. Đường tự hành chuẩn là OrchestrationV2 (`AgentSchedules`→`AutonomousOrchestrator`→adapter), không phải Hangfire.
- **Hai model agent song song** (`agent_configs` V1 vs `agent_definitions` V2). V2 là đường orchestrator dùng — nên hợp nhất/đánh dấu V1 deprecated để tránh nhầm `SkillFilesJson` vs `AllowedToolsJson`.

## 6. Bước kế tiếp đề xuất
1. Bạn duyệt phạm vi + thứ tự phase (đặc biệt: làm B6 system-actor tới đâu — tự đăng hoàn toàn hay luôn chờ duyệt cho hành động rủi ro cao?).
2. Tôi tách Phase 0 + Phase 1 thành SPEC chi tiết (theo quy ước `.sdd/specs/`) để bạn implement.
