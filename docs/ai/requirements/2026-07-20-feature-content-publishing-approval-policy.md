---
phase: requirements
title: Requirements & Problem Understanding
description: Clarify the problem space, gather requirements, and define success criteria
---

# Requirements & Problem Understanding — `content-publishing-approval-policy`

Tách hai khái niệm hiện đang bị trộn: **Agent review nội dung chữ** là bước kiểm soát chất lượng bắt buộc cho revision cuối cùng của mọi bài trước khi đăng; **review hình ảnh** chạy best-effort khi reviewer/model hỗ trợ vision; **quyền phát hành** là chính sách tenant chọn tự động hoàn toàn hoặc chờ người duyệt.

## Problem Statement
**What problem are we solving?**

- Luồng `/content` hiện thiên về duyệt tay: bài do hệ thống sinh ra vào hàng đợi, người dùng duyệt rồi mới lên lịch. Chưa có cấu hình tenant để chạy trọn vòng tự động.
- Cấu hình duyệt ở `/agents` đang diễn đạt Agent review như một lựa chọn có thể bật/tắt. Ngữ nghĩa này sai với yêu cầu sản phẩm mới: Agent review phải luôn chạy cho 100% nội dung do hệ thống sinh hoặc chuyển thể; tenant chỉ được chọn có thêm checkpoint con người trước khi phát hành hay không.
- Nếu dùng cùng một cờ cho “có Agent review” và “có cần người duyệt”, hệ thống dễ rơi vào hai lỗi đối nghịch: bỏ qua quality review khi bật tự động, hoặc bắt người duyệt mọi bài dù Agent đã chấm đạt.
- Nội dung/ảnh có thể bị sửa sau lần review. Nếu vẫn giữ chữ ký review cũ thì bản thực tế được đăng chưa chắc là bản Agent đã kiểm tra.
- Người bị ảnh hưởng:
  - Quản lý tenant cần chọn mức tự động hóa phù hợp với độ tin cậy vận hành.
  - Nhân sự content cần biết bài đang chờ Agent, chờ người, đã tự lên lịch hay bị trả về.
  - Người vận hành `/agents` cần thấy đúng chính sách phát hành, không hiểu nhầm rằng có thể tắt quality review.

## Goals & Objectives
**What do we want to achieve?**

Primary goals:
1. **Agent review text bắt buộc, vision best-effort**: mọi bài hiện có trước khi đăng — AI sinh, chuyển thể hoặc draft được người sửa — phải có kết quả review body gắn với đúng revision cuối cùng. Feature này không thêm luồng tạo draft thủ công mới. Nếu reviewer/model hỗ trợ vision thì review thêm toàn bộ ảnh; nếu không hỗ trợ thì ghi rõ ảnh bị bỏ qua và vẫn cho phép quyết định dựa trên text.
2. **Hai chế độ phát hành tenant-level**:
   - `automatic`: Agent review đạt → hệ thống tự duyệt phát hành → tự chọn khung giờ vàng gần nhất → tạo lịch → publish khi tới giờ.
   - `human_required`: Agent review hoàn tất → chờ người duyệt → người duyệt đồng ý → hệ thống tự chọn khung giờ vàng gần nhất → tạo lịch → publish khi tới giờ.
3. **Fail-closed**: ở chế độ tự động, nếu reviewer trả `reject`, `needs_human`, lỗi, timeout hoặc không có reviewer hợp lệ thì bài chuyển sang hàng đợi người duyệt; không tự đăng và không mất bài.
4. **Review theo revision**: sửa body hoặc assets sau review làm chữ ký cũ mất hiệu lực và tự kích hoạt review lại.
5. **Một nguồn cấu hình, hai điểm truy cập**: `/content` và khu cấu hình duyệt của `/agents` đọc/ghi cùng một tenant policy, cập nhật ở một nơi phải phản ánh ở nơi còn lại.
6. **Ngôn ngữ UI đúng bản chất**: bỏ cách gọi “bật/tắt Agent review”; hiển thị rõ “Chính sách phát hành nội dung” và khẳng định Agent review luôn bật.

Secondary goals:
- Hiển thị trạng thái review/phê duyệt/lịch đăng đủ rõ để biết hệ thống đang chờ bước nào và vì sao bị fallback.
- Giữ audit attribution tách biệt: Agent reviewer, người duyệt, policy áp dụng và thời điểm quyết định.
- Tái sử dụng quy tắc giờ vàng, notification và social publisher hiện có, nhưng mọi đường publish trực tiếp phải được hợp nhất vào một pipeline claim/idempotency duy nhất; không giữ lối tắt Agent tool hoặc HTTP retry gọi provider ngay.

Non-goals:
- Không cho phép tắt Agent review đối với content output của hệ thống.
- Không thay mới toàn bộ rubric/prompt của `ContentReviewer`; v1 giữ text review là invariant và thêm nhánh vision tùy capability, trả kết quả có cấu trúc và minh bạch ảnh đã review hay bị bỏ qua.
- Không tự viết lại nhiều vòng khi review không đạt; v1 chuyển người duyệt theo quyết định đã chốt.
- Không đăng ngay sau generation; cả hai chế độ đều đi qua lịch giờ vàng.
- Không xây engine workflow hoặc service mới; dùng stack .NET/EF/Hangfire/React hiện có.
- Không thay đổi chính sách duyệt chat, KB hoặc các approval domain khác.

## User Stories & Use Cases
**How will users interact with the solution?**

- Là **quản lý tenant**, tôi muốn chọn “Tự động phát hành” để bài đạt Agent review tự lên lịch giờ vàng mà không cần người bấm duyệt.
- Là **quản lý tenant thận trọng**, tôi muốn chọn “Cần người duyệt” để mọi bài vẫn được Agent review trước, sau đó chờ người quyết định trước khi tự lên lịch.
- Là **nhân sự content**, tôi muốn thấy kết quả và ghi chú Agent review trên bài để biết cần sửa gì hoặc vì sao bài chuyển sang duyệt tay.
- Là **người duyệt**, tôi muốn bấm Duyệt một lần; sau đó hệ thống tự chọn giờ vàng và lên lịch, không bắt tôi làm thêm bước kỹ thuật.
- Là **người chỉnh sửa bài**, khi tôi đổi body hoặc ảnh sau review, tôi muốn hệ thống tự nhận ra bản review cũ không còn hợp lệ và chạy review lại revision; body luôn được review, ảnh được review khi reviewer hỗ trợ vision.
- Là **người duyệt có quyền override**, khi Agent trả non-pass nhưng tôi đã xác minh nội dung, tôi muốn được duyệt phát hành với lý do bắt buộc và audit đầy đủ.
- Là **admin trên `/agents`**, tôi muốn cấu hình đúng chính sách phát hành nội dung và thấy thông báo “Agent review luôn bắt buộc” thay vì một toggle dễ hiểu sai.
- Là **admin trên `/content`**, tôi muốn đổi cùng chính sách ngay trong ngữ cảnh vận hành nội dung mà không cần chuyển trang.

Key workflows:
1. **Automatic happy path**: generate/repurpose → Agent review `approve` → auto approval → chọn giờ vàng → schedule → publish.
2. **Automatic fallback**: generate/repurpose → Agent review `reject|needs_human|error|timeout` → giữ bài → hiển thị lý do → human review queue.
3. **Human-required happy path**: generate/repurpose → Agent review hoàn tất → chờ người → người duyệt → chọn giờ vàng → schedule → publish.
4. **Edit after review**: body/assets thay đổi → vô hiệu review và mọi quyết định phát hành cũ chưa thực thi → review lại → áp dụng policy hiện hành.
5. **Concurrent config views**: tenant admin đổi policy ở `/content` → invalidate/refetch → `/agents` hiển thị cùng giá trị và ngược lại; người không có quyền quản trị chỉ được xem.
6. **Policy changes with queued items**: policy được snapshot khi review revision hiện tại hoàn tất; đổi policy không tự đánh giá lại hoặc mass-schedule bài đang chờ. Policy mới chỉ áp dụng ở lần review completion tiếp theo sau khi bài được sửa/review lại.
7. **Capability-aware image review**: body luôn được chấm. Nếu reviewer/model có vision, reviewer nhận thêm assets; GIF được lấy mẫu nhiều frame có giới hạn. Nếu binding không hỗ trợ vision, hệ thống bỏ qua ảnh, ghi `imageReviewStatus=skipped_unsupported` và vẫn áp dụng verdict text. Nếu đã chọn nhánh vision nhưng tải/giải mã asset lỗi thì chuyển người duyệt thay vì giả vờ đã review ảnh.

Edge cases:
- Không có reviewer agent hợp lệ hoặc reviewer trùng generator agent.
- Reviewer RPC/LLM lỗi, timeout hoặc output không parse được.
- Hai worker cùng xử lý một item/revision.
- Người sửa bài khi review đang chạy; kết quả review của revision cũ phải bị bỏ.
- Người duyệt đồng thời với lần sửa mới; optimistic concurrency phải ngăn duyệt nhầm revision.
- Không tìm được khung giờ vàng hợp lệ; giữ trạng thái approved/pending scheduling, retry có giới hạn và báo lỗi rõ.
- Asset URL/storage lỗi, file không hợp lệ hoặc quá lớn trong khi nhánh vision đang chạy; fail-closed sang người duyệt. Model không hỗ trợ vision là trường hợp hợp lệ: bỏ qua ảnh có ghi trạng thái minh bạch, không coi là lỗi text review.
- Đổi policy trong khi item đang chờ người: không âm thầm auto-publish bài cũ nếu chưa có revision/review mới có audit.

## Success Criteria
**How will we know when we're done?**

- Không có đường publish nào — Hangfire, HTTP retry hay Agent tool — có thể đăng bài khi thiếu Agent review text và publishing approval hợp lệ cho revision hiện tại.
- Mỗi external publish dùng durable claim/attempt và stable idempotency key; transmitted timeout chuyển `outcome_unknown` và không đăng lại mù.
- Agent text verdict chỉ hợp lệ khi provider adapter quan sát terminal completion thành công, không refusal/filter/truncation và toàn bộ response là đúng một JSON object closed-schema; prose-wrapped/embedded approve bị loại.
- `automatic` chỉ tự lên lịch khi reviewer verdict là `approve`; mọi verdict/lỗi khác chuyển `human_required` cho chính item đó và ghi lý do.
- DTO/audit cho biết rõ `imageReviewStatus`: `reviewed`, `not_applicable`, `skipped_unsupported` hoặc `failed`; không được hiển thị như thể ảnh đã được chấm khi model không có vision.
- Khi vision khả dụng, requested/sent/reviewed asset-frame IDs phải là ba tập canonical không trùng và bằng nhau cả phần tử lẫn cardinality, response không refusal/content-filter/truncation. APIs không chứng minh semantic attention, nên mọi mismatch/thiếu/mơ hồ chuyển người duyệt và không được mô tả là “provider xác nhận đã nhìn ảnh”.
- `human_required` không tạo lịch trước quyết định người duyệt; sau khi duyệt thành công, hệ thống tự tạo một lịch giờ vàng duy nhất.
- Sửa body hoặc assets làm review cũ mất hiệu lực; kết quả review đến trễ của revision cũ không thể approve/schedule bài mới.
- Cùng một policy được hiển thị và cập nhật từ `/content` và `/agents`, không có hai nguồn dữ liệu hoặc hai cờ drift nhau.
- Tenant hiện hữu và tenant mới mặc định `human_required` để giữ hành vi an toàn; chỉ tenant admin có quyền cấu hình hệ thống mới bật `automatic`.
- Human override một kết quả Agent non-pass chỉ hợp lệ khi có lý do bắt buộc; audit phân biệt được generated-by agent, reviewed-by agent, human approver/overrider, policy applied, revision, verdict, timestamps và fallback reason.
- Trạng thái UI phân biệt tối thiểu: đang Agent review, chờ người duyệt, review không đạt/chuyển người, đã lên lịch tự động, đăng thành công, lỗi lên lịch/đăng.
- Unit/integration/E2E tests bao phủ cả hai policy, stale revision, concurrency, reviewer failure và scheduler failure; coverage phần code mới/đổi đạt tối thiểu 80%.
- Không làm hỏng manual retry, cancel schedule, notification, SLA review và publisher hiện có.

## Constraints & Assumptions
**What limitations do we need to work within?**

Technical constraints:
- Stack hiện tại: .NET 8/10 theo project, EF Core, SQL Server, Hangfire, React + TanStack Query.
- DDL migration là một `SqlCommand` mỗi file, không dùng `GO`; cột/bảng mới phải có đường repair cho schema hiện hữu trong `run-all.bat`.
- Background/Hangfire scope không có HTTP tenant context; mọi job phải nhận `tenantId`, query có chủ đích và không dựa vào `ITenantAccessor.Require()` trong job scope.
- Mọi publish path phải đi qua cùng claim/backstop; không chỉ gate ở frontend/API caller. `content.publish`, `content.schedule` và HTTP retry không được gọi social provider trực tiếp.
- Publish claim là ranh giới không đảo ngược: snapshot revision/body/assets được cố định trước external call; edit bị chặn khi claim active.
- Trong cutover, SQL writer gate chưa đủ vì binary cũ có thể gọi provider trước DB write; phải fence outbound provider/credentials và tắt manual job triggers cho tới khi binary cũ bị drain.
- Assets dùng bản ghi server-owned có tenant/item ownership và storage key namespace; không tin `storageKey` hoặc URL do client gửi trong JSON.
- Policy có version/rowversion; review completion phải snapshot policy value + version trong cùng transaction hoặc retry khi policy đổi đồng thời.
- Agent review phải giữ separation of duties: reviewer khác generator khi generator attribution tồn tại.
- Không tạo hai cache shape khác nhau dưới cùng React Query key; cập nhật policy phải invalidate key dùng chung.

Business constraints:
- Mặc định `human_required` cho cả tenant cũ và tenant mới.
- Auto mode không được biến reviewer failure thành approve.
- Cả hai chế độ chọn khung giờ vàng; không publish ngay.
- Sau sửa nội dung/ảnh phải review lại.
- Chỉ tenant admin có quyền đổi policy; content editor chỉ xem.
- Human được override kết quả Agent non-pass nhưng phải nhập lý do và để lại audit.
- Policy mới không hồi tố tự động lên bài đang chờ; chỉ áp dụng từ review completion tiếp theo.
- Khi cutover, tạm dừng publishing, drain binary cũ và coi toàn bộ unpublished/scheduled legacy là chưa review/chưa approval theo revision; không kế thừa blanket `ApprovedByAgentId`. Chỉ bài đã published được `legacy_exempt` cho lịch sử.
- V1 có image review theo capability: model có vision thì review ảnh thật (GIF lấy mẫu nhiều frame); model không có vision thì bỏ qua ảnh có trạng thái/audit minh bạch và không chặn auto-publish.

Assumptions:
- Thuật toán/chức năng chọn “giờ vàng” hiện có có thể được đưa về service backend dùng chung hoặc được hiện thực deterministically từ cùng quy tắc hiện tại.
- Tenant có reviewer agent và LLM binding hợp lệ; khi thiếu thì fallback người duyệt.
- Phân quyền cố định: policy GET = `content:read`; policy PUT = `system:config`; human approve/reject/override = `content:approve`; edit/review retry = `content:write`; publish retry/reconciliation = `content:publish`. Built-in roles: Admin có cả bốn quyền; Marketer có `content:read|write|approve` nhưng không có `system:config|content:publish`; các role khác không được suy diễn grant. `content:publish` admin-only mặc định và không bao giờ kế thừa từ `content:write` hay legacy `content.approve`.

## Questions & Open Items
**What do we still need to clarify?**

Các quyết định sản phẩm đã chốt ngày 2026-07-20:

- (CHỐT) Feature name: `content-publishing-approval-policy`.
- (CHỐT) Agent review text chạy cho revision cuối cùng của mọi bài hiện có trước khi đăng, gồm bài AI sinh/chuyển thể và draft được người sửa; đây không phải toggle tenant. Feature không thêm entry point tạo draft thủ công mới.
- (CHỐT) Policy có hai chế độ: tự động hoàn toàn hoặc cần người duyệt.
- (CHỐT) Auto mode chọn giờ vàng, không đăng ngay.
- (CHỐT) Human mode sau khi người duyệt đồng ý cũng tự chọn giờ vàng và lên lịch.
- (CHỐT) Reviewer reject/needs_human/lỗi/timeout ở auto mode → chuyển người duyệt, fail-closed.
- (CHỐT) Người có quyền duyệt được override non-pass mà không buộc sửa/review lại, nhưng phải nhập lý do và ghi audit.
- (CHỐT) Tenant hiện hữu và mới mặc định cần người duyệt.
- (CHỐT) Sửa body/assets sau review → bắt buộc review lại.
- (CHỐT) Đổi policy không tự xử lý lại bài đang chờ; chỉ áp dụng từ review completion tiếp theo.
- (CHỐT) Chỉ tenant admin được đổi policy; content editor chỉ xem.
- (CHỐT) V1 review ảnh theo capability: có vision thì review ảnh thật; không có vision thì bỏ qua ảnh, ghi trạng thái minh bạch và vẫn cho phép auto-publish theo text.
- (CHỐT) GIF khi vision khả dụng được lấy mẫu nhiều frame có giới hạn, không chỉ frame đầu.

Design review đã chốt các implementation boundary chính:
- Revision dùng `content_revision` + rowversion; review/approval/schedule bind revision, publish claim giữ immutable snapshot/hash.
- Policy dùng cột string closed-set + monotonic version + updated timestamp; cờ `RequireContentReview` chỉ còn compatibility read-only rồi xóa.
- `/api/content/settings/publishing-policy` là API canonical và sole writer cho cả `/content` lẫn `/agents`.
- Cross-host review dùng `content_review_tasks`; assets dùng bảng server-owned; external publish dùng durable attempt/idempotency/reconciliation.
- Cutover tạm dừng publish và không có mixed-version writer window.
