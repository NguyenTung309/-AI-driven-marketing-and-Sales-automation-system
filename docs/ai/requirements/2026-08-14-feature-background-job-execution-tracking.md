---
phase: requirements
title: Background Job Execution Tracking Requirements
description: Theo dõi kết quả thực thi thực của Job hệ thống Hangfire, tách biệt với thao tác kích chạy
feature: background-job-execution-tracking
status: approved
created: 2026-08-14
---

# Background Job Execution Tracking Requirements

## Problem Statement

Màn hình **Job hệ thống (Hangfire)** đang hiển thị `Succeeded` theo `LastJobState` của Hangfire hoặc bản ghi `BackgroundJob` được tạo khi quản trị viên bấm **Chạy ngay**. Với recurring job, endpoint hiện tại chỉ tạo một row audit, lập tức đánh dấu `succeeded`, rồi gọi `IRecurringJobManager.Trigger`. Vì vậy trạng thái này chỉ chứng minh rằng hệ thống đã nhận yêu cầu trigger/enqueue; nó không chứng minh tác vụ nghiệp vụ đã bắt đầu, hoàn thành, retry, hay lỗi.

Hệ quả:

- Quản trị viên có thể hiểu nhầm “Đã kích chạy” là “Job đã chạy xong”.
- Không có tracking ID liên kết thao tác **Chạy ngay** với lần thực thi Hangfire thực tế.
- Không có lịch sử attempt, tiến độ, output an toàn hoặc lỗi đã redaction để điều tra vận hành.
- Thông báo cảnh báo khi job gốc lỗi là tín hiệu phụ, không phải lịch sử thực thi đáng tin cậy.
- Nút **Chạy ngay** cho Agent schedule hiện chỉ kéo `NextRunAt` về hiện tại, không đi qua manual-run path tạo `AgentScheduleRun`, nên cũng không trả về run/session tracking ID.

Người dùng chính là quản trị viên có quyền `system:config`, vận hành hệ thống và cần xác minh rõ một lần chạy đã được nhận, đang chạy, retry, thành công hay thất bại.

## Goals & Objectives

### Goals

1. Phân biệt rõ **yêu cầu chạy đã được nhận/xếp hàng** với **kết quả thực thi thực tế** trong API và UI.
2. Tạo một tracking record riêng cho mỗi logical execution của recurring Hangfire job, bao gồm scheduled, manual, và manual retry.
3. Lưu immutable attempt cho mỗi physical Hangfire performance, kể cả automatic retry, thay vì ghi đè lỗi attempt trước.
4. Cho phép quản trị viên theo dõi trạng thái, timestamps, progress, safe output/result, retry timeline và lỗi đã redaction từ màn hình Job hệ thống.
5. Khi bấm **Chạy ngay**, trả `202 Accepted` với tracking ID và status URL của execution thực, không trả một fake `succeeded` record.
6. Sửa Agent schedule **Chạy ngay** để gọi manual-run gRPC path và trả `AgentScheduleRun.Id` (cùng session ID nếu có), không chỉ thay đổi `NextRunAt`.
7. Giữ tương thích với `/api/jobs` và Job Center hiện có cho các `BackgroundJob` tenant/user-oriented; system executions không xuất hiện trong Job Center đó.

### Non-goals

- Không thay thế Hangfire Dashboard hoặc sao chép toàn bộ metadata/console của Hangfire.
- Không biến `BackgroundJob` thành lịch sử execution chung: entity này tiếp tục phục vụ các tác vụ tenant/user hiện có và có retry theo cùng một row.
- Không hợp nhất `AgentScheduleRun` vào model execution của recurring Hangfire; chúng là hai runtime khác nhau.
- Không cam kết mọi legacy recurring job phải có phần trăm tiến độ chi tiết ngay trong lần đầu. Nếu executor không có metric an toàn, UI phải hiển thị “Chưa báo cáo tiến độ”, không bịa progress.
- Không lưu raw exception, stack trace, SQL, file path, credential, payload tenant hoặc output chưa được phê duyệt/redact.
- Không thay đổi cron, queue, retry policy, concurrency/idempotency behavior của job nghiệp vụ khi migrate registration sang đường tracked dispatcher.

## User Stories & Use Cases

### Core stories

- Là quản trị viên, tôi bấm **Chạy ngay** một recurring job và nhận được tracking ID ngay sau khi yêu cầu đã được enqueue, để tôi biết đây mới là trạng thái “đã nhận yêu cầu”, chưa phải kết quả.
- Là quản trị viên, tôi mở execution detail và thấy lần chạy chuyển `queued → running → succeeded`, với thời gian, progress và summary an toàn.
- Là quản trị viên, khi attempt lỗi nhưng Hangfire còn retry, tôi thấy `retrying` và lịch sử attempt; execution chỉ là `failed` khi retry đã cạn.
- Là quản trị viên, tôi thấy lỗi cuối cùng đã redaction và bounded, không cần xem log server để biết nguyên nhân vận hành cơ bản.
- Là quản trị viên, tôi có thể **Chạy lại** một execution terminal được phép; hệ thống tạo tracking record mới liên kết về execution gốc, không sửa lịch sử cũ.
- Là quản trị viên, tôi bấm **Chạy ngay** một Agent schedule và nhận tracking ID của `AgentScheduleRun` thực tế, hoặc nhận conflict rõ ràng khi schedule bị overlap.

### States and edge cases

| State | Semantics |
|---|---|
| `requested` | Manual request đã được lưu nhưng chưa xác nhận enqueue. Không phải kết quả chạy. |
| `queued` | Hangfire đã nhận work; attempt chưa bắt đầu. Không phải kết quả chạy. |
| `running` | Attempt hiện tại đang thực thi. |
| `retrying` | Attempt trước lỗi và Hangfire sẽ retry. |
| `succeeded` | Một attempt đã hoàn thành thành công. Terminal. |
| `failed` | Hangfire đã áp dụng final failure sau khi retry cạn. Terminal. |
| `cancelled` | Workload đã bị huỷ. Terminal. |
| `skipped` | Dispatcher cố ý không chạy theo concurrency/overlap policy. Terminal. |
| `enqueue_failed` | Không xác nhận được enqueue; không có workload execution. Terminal. |

- Double-click, network retry hoặc duplicate delivery không được tạo hai execution nghiệp vụ độc lập cho cùng một manual request.
- Crash giữa lúc lưu request và lúc ghi Hangfire job ID phải để lại record có thể reconciliation, không được “succeeded”.
- Duplicate delivery/retry của Hangfire không được thực thi một completed logical execution lần hai.
- Nếu enqueue hoặc progress persistence lỗi, UI phải hiển thị trạng thái trung thực và server phải có observability để vận hành xử lý.
- History/detail phải phân trang và không xuất hiện dữ liệu global/tenant-sensitive ngoài quyền `system:config`.

## Success Criteria

### Functional acceptance criteria

- [ ] `POST /api/admin/jobs/recurring/{definitionId}/trigger` chỉ trả `202` sau khi có durable execution tracking ID; response chứa `definitionId`, `trackingId`, `status: "queued"` và `statusUrl`.
- [ ] Endpoint trigger không tạo `BackgroundJob` hoàn thành giả và không dùng `IRecurringJobManager.Trigger` cho manual execution đã migrate.
- [ ] Scheduled và manual recurring execution đều tạo logical execution record; automatic retry tạo attempt mới dưới cùng execution.
- [ ] UI hiển thị `queued/accepted`, `running`, `retrying`, terminal outcome và attempt timeline với nghĩa tách biệt, không label `Succeeded` cho acknowledgment.
- [ ] UI chỉ hiển thị progress/output/error được server giới hạn và redaction; status detail có timestamps requested/enqueued/started/finished, source và initiator (nếu manual).
- [ ] Execution chỉ `failed` khi Hangfire final failure; failure notification safe được gửi đúng một lần cho tracked dispatcher job.
- [ ] “Chạy lại” tạo execution mới, có `retryOfExecutionId`; execution/attempt cũ bất biến.
- [ ] Detail/history/retry APIs yêu cầu `system:config`; `/api/jobs` và Job Center không truy cập hoặc hiển thị system executions.
- [ ] Agent schedule run-now gọi gRPC manual-run path, trả tracking ID thực; `skipped_overlap` là `409`, không phải fake queued result.
- [ ] Cron, queue, timezone, retry và concurrency behavior của từng job giữ nguyên sau migration qua dispatcher registry.

### Quality criteria

- [ ] Trạng thái active được UI poll không quá một request/3 giây cho detail đang mở; overview giữ cadence riêng và không làm request waterfall.
- [ ] Attempt/error/result fields có giới hạn kích thước; progress note được redaction trước khi persist/display.
- [ ] Query execution history được cursor-paginated, có index cho definition + newest-first và execution + attempt number.
- [ ] New/changed backend branches đạt coverage tối thiểu 80%; có unit, integration/API, và Playwright coverage cho luồng quản trị chính.

## Constraints & Assumptions

- `BackgroundJob` và `JobRunner` đã có lifecycle/progress/redaction cho background job tenant/user; không đổi semantics của model đó.
- Recurring jobs hiện được đăng ký trực tiếp với concrete `RunAsync`, vì vậy cần registry + dispatcher adapter thay vì chỉ sửa Admin endpoint.
- Với scheduled execution, `PerformContext.BackgroundJob.Id` là correlation key. Lần perform đầu tạo execution theo `(DefinitionId, HangfireBackgroundJobId)`; Hangfire retry dùng lại background-job ID đó và phải tiếp tục cùng logical execution.
- Hangfire persistence và EF persistence không là một distributed transaction. Thiết kế cần idempotency, correlation ID, reconciliation cho `requested` cũ và dispatcher transition idempotent.
- `IApplyStateFilter` chỉ xử lý final failure khi `FailedState` đã thực sự được Hangfire apply sau `AutomaticRetry`; dispatcher luôn persist failed attempt, đánh dấu `retrying` và rethrow, không tự suy đoán lượt retry cuối.
- Moving a direct recurring target to a generic dispatcher loses method/type attributes. Mỗi definition phải có dispatcher wrapper mang lại effective `AutomaticRetry`, `DisableConcurrentExecution` và custom filter tương đương target gốc; registry metadata không tự thay thế Hangfire filters.
- Existing redaction path (`IPiiRedactor` pattern) là mandatory cho persisted/displayed summary và exception message.
- Multi-tenant global recurring jobs không có một tenant owner trung thực, nên execution entity không implement `ITenantOwned`; endpoint admin là authorization boundary.
- Migration SQL tuân theo runner hiện có: unique full filename, ledger-based apply, transaction per file, không dùng `GO`; không giả định numeric prefix tiếp theo trước khi kiểm tra migration ledger/release state.
- Retention được chốt là 180 ngày cho execution và attempt detail. Cleanup phải batch-limited, observable, giữ aggregate metrics và không tự tạo tracking noise.

## Approaches Considered

1. **Chỉ relabel row trigger hiện tại và đọc `LastJobState` từ Hangfire.** Nhanh nhưng không có correlation, attempt history, safe output/progress hay final state đáng tin cậy; loại.
2. **Mở rộng `BackgroundJob` cho recurring execution.** Tận dụng hạ tầng có sẵn nhưng model tenant/user và retry-in-place mâu thuẫn với execution history immutable/global job; loại.
3. **Dedicated logical execution + immutable attempts + tracked dispatcher registry.** Cần migrate registrations/adapters, nhưng correlation, retries, output, security boundary và UI semantics đúng; **được chọn**.

## Resolved Decisions

- Retention: lưu execution/attempt detail 180 ngày; cleanup theo batch, observable và giữ aggregate metrics.
- Vertical slice: `health-check`. Job này chỉ kiểm tra database, không mutation tenant/outbound action, có safe deterministic summary và rethrow lỗi; test retry/final failure dùng test-only executor, không cố ý làm health check production fail.
- Manual idempotency: client tạo UUID cho từng thao tác và gửi qua header `Idempotency-Key`; chỉ transport retry của cùng thao tác mới dùng lại key.
- Release-one output: executor chỉ expose lifecycle phase và safe summary; counter/progress chi tiết được bổ sung từng definition khi đã phê duyệt.
- Release-one history: Admin Jobs cung cấp execution detail và per-definition cursor-paginated history; không thêm cross-definition filtered history endpoint cho đến khi có use case vận hành cụ thể.
- Agent schedule presentation: thêm `GET /api/admin/jobs/schedule-runs/{runId}` scope theo current tenant + `system:config`; đây là detail chính. Nếu run có `SessionId`, UI hiển thị secondary link **Mở phiên điều phối**; trend scan không có session vẫn xem detail được và có secondary link tới `/content`.

## Questions & Open Items

Không còn open question ảnh hưởng schema, API contract hoặc rollout của release đầu.
