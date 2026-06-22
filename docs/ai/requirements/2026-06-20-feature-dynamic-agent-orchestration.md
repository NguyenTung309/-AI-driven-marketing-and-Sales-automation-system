---
phase: requirements
title: Dynamic Agent Orchestration — Requirements & Problem Understanding
feature: dynamic-agent-orchestration
date: 2026-06-20
status: in-review
---

# Dynamic Agent Orchestration — Requirements & Problem Understanding

> Decision so far (brainstorm + requirements review 2026-06-20): **Approach A** — Semantic-Kernel planner/plugin-host over the existing 8 gRPC agents, agents exposed as a **data-resolvable catalog**, NL goal → SK-drafted **editable plan** → run, **autonomous with human override**, guardrails on.
> Vision end-state = **dynamic personas-as-data** (orchestrator composes new agents = persona + skills + KB, no codegen). **Iteration 1 (this spec) does NOT build dynamic persona creation** — it builds the real SK orchestrator over the fixed 8 and the catalog seam so personas drop in later.
>
> **Locked decisions (review 2026-06-20):**
> - **Planner:** hybrid — single-shot structured plan (editable) + LLM **re-plan only on agent failure** (Q9/Q7).
> - **Execution:** **parallel DAG fan-out** (independent tasks run concurrently per dependency graph) (Q3).
> - **Autonomy:** **auto-run by default**, with a **per-tenant `require-approval` opt-out toggle** (Q4).
> - **Persistence:** **reuse `AgentSession` + `agent_traces`** — task DAG in `AgentSession.PlanJson`, state in `Status`, events in `AgentTrace` (Q2). Verified: [AgentSession.cs](../../src/shared/Clawbot.Domain/Agents/AgentSession.cs) already has `Goal/Status/PlanJson/Traces`.
> - **SK chat path:** custom `IChatCompletionService` adapter wrapping `ScopedLlmChatClient` (no preview connector) (Q5).
> - **Scope:** dynamic persona creation = **iteration 2** (Q1).

## Problem Statement
**What problem are we solving?**

Hệ thống hiện **hard-code agent và luồng điều phối**, không thể thêm/đổi agent hoặc thay đổi cách phối hợp mà không sửa code + redeploy:

- **Agent cố định:** [`DefaultAgentRegistry`](../../src/agents/Clawbot.AgentService/Services/DefaultAgentRegistry.cs) nhồi cứng 8 loại agent trong 1 mảng tĩnh (`chat, sale_assist, lead, content, research, docs, report, ads`); mỗi agent là 1 gRPC service map cứng trong [`Program.cs`](../../src/agents/Clawbot.AgentService/Program.cs).
- **"Orchestrator" không có trí tuệ:** [`PlanningOrchestrator`](../../src/agents/Clawbot.Agents.Core/Orchestrator/PlanningOrchestrator.cs) **không gọi LLM** — nó chọn agent bằng `string.IndexOf(goal, agentName)` rồi chạy tuần tự. Không lập kế hoạch, không re-plan, không phối hợp thực sự.
- **Không khớp Constitution:** Article 1 mandate **Microsoft Semantic Kernel** làm orchestration layer; [RFC-001](../../.sdd/rfcs/001-semantic-kernel-vs-direct-anthropic.md) chốt "SK làm planner/plugin host" nhưng **planner thật bị defer** và package SK hiện **không còn được tham chiếu** trong `Clawbot.Agents.Core.csproj`.

Hệ quả: muốn 1 luồng đa-agent mới (vd "khởi động chiến dịch ra mắt khoá học") phải lập trình tay từng bước; không thể để người dùng đẩy mục tiêu/kế hoạch lên và hệ thống tự điều phối.

**Mục tiêu mong muốn (user):** 1 agent điều phối (Semantic Kernel) nhận **kế hoạch/mục tiêu người dùng đẩy lên**, **tự lập kế hoạch và điều phối** các agent chạy **tự động**, con người vẫn **can thiệp được khi cần**.

**Affected users:**
- **Trưởng phòng KD / Marketing / Quản lý (tenant operator):** người đẩy mục tiêu và duyệt/sửa kế hoạch.
- **Admin / AI infra:** vận hành orchestrator, theo dõi trace, quản cost.
- **Dev:** hiện phải sửa code mỗi khi đổi luồng → được giải phóng.

**Current workaround:** sửa code orchestrator/registry + redeploy cho mỗi luồng mới; điều phối "giả" bằng keyword match.

## Goals & Objectives

### Primary goals — Iteration 1 (this spec)
1. **SK orchestrator thật:** thay cơ chế keyword-match trong `PlanningOrchestrator` bằng planner dựa trên **Semantic Kernel** (LLM-driven), chạy trong `AgentService` (RFC-001 Option B: SK = planner/plugin host).
2. **Agents-as-catalog:** thay mảng tĩnh `DefaultAgentRegistry` bằng `IAgentCatalog` **resolve từ dữ liệu** (seed 8 agent hiện có). Iteration 1 đưa mô tả catalog/input schema vào prompt của SK chat planner để planner chọn agent; SK `KernelFunction` plugin-host thực thi trực tiếp được để iteration sau nếu cần.
3. **NL → editable plan → run:** user đẩy **mục tiêu ngôn ngữ tự nhiên** → SK sinh **plan có cấu trúc** (task graph: task, agent, input, dependency) → **persist** → FE/API cho **review/edit/approve** → orchestrator thực thi.
4. **Autonomy + human override:** **auto-run mặc định** sau khi lập plan; **per-tenant toggle `require-approval`** để bắt buộc dừng ở `PendingApproval`. Người dùng **can thiệp bất kỳ lúc nào** qua state machine (`Draft → PendingApproval → Running → Paused → Completed/Failed`): edit khi chưa chạy, pause/resume/cancel khi đang chạy.
5. **Execution = parallel DAG:** task độc lập chạy **đồng thời** theo dependency graph; có **giới hạn concurrency**. Mỗi task ghi state qua `AgentTrace` insert + cập nhật trạng thái task có chủ đích (tránh ghi đè cả `PlanJson` khi song song); aggregate có **concurrency token**.
6. **Guardrails (hard rules giữ nguyên):** tenant-scope (`ITenantOwned`), RBAC perm cho submit/approve/run/cancel ([[rbac-perm-seed-required]]), **graceful degradation/re-plan** khi 1 agent fail (Constitution §9).
7. **Cost-surprise guard (BẮT BUỘC — do parallel + auto-run + cap):** (a) **ước tính cost pre-flight** trước khi auto-run; (b) **giới hạn số task LLM chạy song song**; (c) **atomic/locked cap check** dùng chung giữa các task song song qua `IClaudeCostTracker` (chống race "N task cùng pass cap rồi cùng tiêu"). Chạm cap → dừng có kiểm soát `Failed(cost_cap)`.
8. **LLM per-tenant (ADR-010):** planner gọi LLM qua **adapter `IChatCompletionService` bọc `ScopedLlmChatClient`** ([ScopedLlmChatClient.cs](../../src/agents/Clawbot.Agents.Core/Chat/ScopedLlmChatClient.cs)) — model resolve theo `(tenant, agentCode="orchestrator")` từ `llm_configs`; **không** dùng community Anthropic connector (preview).
9. **Trace + persistence reuse:** tái dùng `AgentSession` (DAG trong `PlanJson`, state trong `Status`) + `agent_traces` — mỗi plan/task ghi phase (planned/started/completed/failed/re-planned). Mở rộng `AgentSession` thêm state-transition methods + cờ autonomy + concurrency token (DDL `000X_*.sql`, **no GO**).

### Secondary goals
- Cấu trúc seam (`IAgentCatalog`, plan persistence, SK adapter) sao cho **iteration 2 (dynamic personas)** = thêm row/loader, **không** rewrite orchestrator.
- Giữ nguyên gRPC transport (ADR-008), direct chat client (RFC-001), telemetry hiện có.

### Non-goals (out of scope — iteration 1)
- **Dynamic persona creation** (orchestrator tự tạo `AgentConfig` mới = persona + skills + KB lúc runtime) — **đây là end-state, để iteration 2**. Iteration 1 chỉ chạy trên 8 agent cố định + seam catalog.
- **Full SK multi-agent autonomous A2A loop** (SK Process framework / Magentic group-chat) — iteration 3 target.
- **Codegen agents** (sinh/biên dịch/deploy tool hay code mới lúc runtime) — bị loại vĩnh viễn (vi phạm gating dependency + sandbox).
- **Frontend mới đầy đủ** cho plan editor — iteration 1 chỉ cần API + tích hợp tối thiểu vào [AgentDashboardPage](../../src/frontend/clawbot-web/src/features/agents/AgentDashboardPage.tsx) (chốt ở review).
- Parallel/đa-luồng task execution nâng cao (chốt sequential-with-deps trước — xem Q3).
- Thêm loại agent/kênh mới.

## User Stories & Use Cases

- **US-1 (operator):** *Là trưởng phòng KD*, tôi đẩy mục tiêu "chuẩn bị ra mắt khoá HSK4: nghiên cứu đối thủ, viết 3 bài, lên gợi ý ads" và hệ thống **tự lập kế hoạch** gồm các bước cho research/content/ads, để tôi không phải bấm từng agent.
- **US-2 (operator):** *Là quản lý*, tôi muốn **xem và sửa** kế hoạch (bỏ/sửa task, đổi thứ tự) **trước khi chạy**, để kiểm soát chi phí và phạm vi.
- **US-3 (operator):** *Là operator*, khi plan đang chạy tôi muốn **tạm dừng/huỷ** nếu thấy sai, để không tốn cost/đưa ra output sai.
- **US-4 (autonomy):** *Là operator*, với luồng tin cậy tôi muốn plan **tự chạy ngay sau khi lập** (không cần duyệt tay), nhưng vẫn dừng được giữa chừng.
- **US-5 (resilience):** *Là hệ thống*, khi 1 agent fail tôi muốn orchestrator **re-plan hoặc skip có kiểm soát**, không vỡ cả pipeline.
- **US-6 (admin):** *Là admin*, tôi muốn **xem trace điều phối** (kế hoạch nào, task nào chạy/treo/lỗi, cost) để audit.

### Edge cases
- Goal rỗng / vô nghĩa → planner trả plan rỗng + lý do, không tạo task rác.
- Goal tham chiếu agent không tồn tại trong catalog → planner bỏ qua + ghi trace, không crash.
- Plan bị edit thành rỗng / vòng lặp dependency → validate, từ chối run.
- Cost-cap chạm giữa chừng → orchestrator dừng có kiểm soát, đánh dấu `Failed(cost_cap)`, không gọi LLM tiếp.
- Pause/cancel khi 1 task đang chạy → task hiện hoàn tất hoặc bị cancel theo `CancellationToken`, không để trạng thái nửa vời.
- Plan text chứa PII (mục tiêu user nhập) → redact trước khi persist ([[pii-redact-derived-content]]).
- 2 operator cùng approve/edit 1 plan → optimistic concurrency / version check.

## Success Criteria

- **SC-1:** Submit 1 NL goal → API trả `planId` + structured plan (≥1 task, mỗi task có agent ∈ catalog, input, dependency hợp lệ); plan persist + đọc lại được.
- **SC-2:** Plan chạy được qua **SK planner thật** (LLM gọi qua adapter per-tenant), **không** còn nhánh `IndexOf` keyword-match.
- **SC-3:** State machine vận hành đúng: edit chỉ khi `Draft/PendingApproval`; pause/resume/cancel chỉ khi `Running/Paused`; transition sai → 409/validation error.
- **SC-4:** Autonomy toggle hoạt động: chế độ auto → plan tự chuyển `Running` sau khi lập; chế độ gated → dừng ở `PendingApproval` chờ approve.
- **SC-5:** Guardrails: gọi không đủ RBAC perm → 403 (perm seeded trong RbacSeeder); task vượt tenant scope bị chặn; planner LLM cost ghi vào ledger dưới agentCode `orchestrator`; chạm cap → dừng.
- **SC-6:** Agent fail giữa chừng → orchestrator re-plan/skip theo policy (**bounded** re-plan count), plan không vỡ; trace ghi `failed` + `re-planned/skipped`.
- **SC-9 (parallel + cost guard):** task độc lập chạy song song theo DAG, tôn trọng `dependency` + giới hạn concurrency; **cost-surprise guard** chứng minh bằng test: nhiều task song song không vượt cap (atomic check), chạm cap → dừng `Failed(cost_cap)`, không có race "cùng pass rồi cùng tiêu".
- **SC-7 (catalog seam):** thêm 1 agent vào catalog bằng dữ liệu (test fixture) → planner thấy được agent đó **không sửa code orchestrator**.
- **SC-8 (NFR):** lập plan p95 < 5s (1 LLM call); không phá NFR chat (planner tách scope riêng). Build **0 warning / 0 error** (NuGetAudit + CA gates [[clawbot-build-gates]]); unit test ≥80% nhánh logic mới; test hiện có vẫn xanh.

## Constraints & Assumptions

### Technical
- **Constitution Article 1:** SK là orchestration layer (aligned). **Thêm `Microsoft.SemanticKernel` = dependency mới → bắt buộc RFC** (confirm/supersede RFC-001) + **NuGetAudit phải clean** [[clawbot-build-gates]]. Cần chốt **version SK audit-clean**.
- **Không có connector Anthropic first-party trong SK** (RFC-001) → dùng **adapter `IChatCompletionService` tự viết bọc `ScopedLlmChatClient`** (tránh preview connector; giữ ADR-010).
- **ADR-008:** agent là gRPC service; orchestrator gọi qua stub `Clawbot.Agents.Contracts`. Giữ nguyên.
- **ADR-010 / Constitution:** LLM resolve runtime từ `llm_configs`; **không hardcode prompt** trong agent code; **chỉ gọi LLM ở `AgentService`**.
- **Migrations:** DDL-as-source, **không `GO`**; index trên cột ALTER-added phải file riêng ([[clawbot-migration-no-go]]). Plan persistence cần bảng mới → DDL `000X_*.sql` + EF Fluent config.
- **Multi-tenancy:** bảng plan mới `ITenantOwned`; service singleton-safe qua `IServiceScopeFactory`.
- **PII:** text dẫn xuất (goal, plan task description) persist phải redact; raw purge 30 ngày [[pii-redact-derived-content]].
- **Cost:** planner + bất kỳ LLM re-plan đếm vào `claude_cost_ledger`, cap $200/tháng.
- **Graceful degradation** ở tầng business orchestrator (Constitution §9), không chỉ retry hạ tầng.

### Business
- Single-org, admin-provisioned (no self-register).
- Autonomy "đầy đủ nhưng người vẫn kiểm soát" — cần chốt **default** (auto-run vs gated) ở cấp tenant/plan (Q4).

### Assumptions
- 8 agent gRPC hiện tại đủ cho luồng iteration 1; mô tả `KernelFunction` đủ để LLM chọn đúng.
- `ScopedLlmChatClient` + `LlmConfigGrpcInterceptor` đủ để planner chạy per-tenant (orchestrator có 1 `llm_config` binding như 1 agent).
- FE iteration 1 chỉ cần API; UI editor có thể tối giản.

## Questions & Open Items

**Resolved in review 2026-06-20** (see Locked decisions banner): Q1 scope split → iteration 2 · Q2 persistence → reuse `AgentSession` · Q3 execution → parallel DAG · Q4 autonomy → auto-run default + per-tenant toggle · Q5 SK chat → custom adapter · Q9 planner → hybrid single-shot + re-plan.

**Still open → resolve in `/review-design`:**
1. **RBAC perms:** chốt mã quyền (`plan.submit / plan.approve / plan.run / plan.cancel / plan.view`) + role gán trong RbacSeeder ([[rbac-perm-seed-required]]).
2. **Re-plan bound:** hybrid re-plan dùng LLM khi agent fail — **giới hạn số lần re-plan/retry** mỗi plan là bao nhiêu (chống vòng lặp + cost)?
3. **Cost-cap timing:** chặn khi **pre-flight estimate** vượt cap (không cho auto-run), hay cho chạy tới khi **atomic check** chạm cap rồi dừng? (định nghĩa chi tiết của Goal 7).
4. **Concurrency limit:** số task LLM chạy song song tối đa cho parallel DAG (mặc định đề xuất: nhỏ, vd 3) — cân NFR latency vs cost-burst.
5. **SK version:** version `Microsoft.SemanticKernel` **audit-clean** (NuGetAudit gate) — cần verify khi viết RFC.
6. **FE iteration 1:** chỉ API + test, hay tích hợp tối thiểu submit/duyệt plan vào [AgentDashboardPage](../../src/frontend/clawbot-web/src/features/agents/AgentDashboardPage.tsx)?

**Pre-requisite (blocking design sign-off):** confirm/supersede [RFC-001](../../.sdd/rfcs/001-semantic-kernel-vs-direct-anthropic.md) để **thêm `Microsoft.SemanticKernel`** (dependency mới = RFC per Article 1).
