---
phase: requirements
title: Requirements & Problem Understanding
description: Clarify the problem space, gather requirements, and define success criteria
---

# Requirements & Problem Understanding

## Problem Statement

Audit database local `clawbot` ngày 2026-07-29 xác nhận bốn bảng legacy không còn được runtime đọc/ghi, trong khi ba bảng collaboration đang được EF/API sử dụng lại bị thiếu khỏi database dù migration ledger đã đánh dấu lịch sử schema tương ứng.

Các bảng legacy đã xác nhận obsolete:

- `dbo.user_roles`: đường gán role canonical dùng ASP.NET Identity `dbo.AspNetUserRoles`.
- `dbo.channel_tokens`: token runtime canonical lưu trong `dbo.inboxes`.
- `dbo.conversation_read_state`: không có runtime consumer; không được xóa nếu còn row chưa được xử lý.
- `dbo.pancake_pages`: `PancakePageTokenResolver` và `PancakePageTokenService` đã dùng `dbo.inboxes`.

Các bảng collaboration active cần khôi phục:

- `dbo.labels`
- `dbo.conversation_labels`
- `dbo.conversation_notes`

Người bị ảnh hưởng là developer/operator chạy `run-all.bat` hoặc `migrate-local.ps1`, admin dùng RBAC/Pancake/inbox, và mọi môi trường nâng cấp từ schema cũ còn dữ liệu trong các bảng legacy.

## Goals & Objectives

### Mục tiêu chính

- Di chuyển hoặc đối soát dữ liệu legacy với nguồn canonical trước khi xóa bảng.
- Xóa đúng bốn bảng `user_roles`, `channel_tokens`, `conversation_read_state`, `pancake_pages`.
- Khôi phục ba bảng collaboration active bằng repair script transactional, idempotent.
- Ngăn `run-all.bat`, model EF và seeder tái tạo hoặc truy vấn bảng legacy.
- Chạy tự động qua cơ chế `deploy/migrations/*.sql` của `run-all.bat` và `migrate-local.ps1`.
- Fail closed nếu Identity role assignment không bằng chính xác desired legacy set, kể cả trường hợp desired set rỗng cần thu hồi toàn bộ role.
- Không tạo hoặc kích hoạt lại inbox khi canonical inbox phù hợp đang disconnected; trạng thái canonical là authoritative.

### Mục tiêu phụ

- Chuẩn hóa `inboxes` thành nơi lưu credential theo channel duy nhất.
- Bổ sung verification gate kiểm tra đầy đủ contract bảng/cột/index/FK trước khi service khởi động.
- Đồng bộ `JWT_SIGNING_KEY` và `ENCRYPTION_BASE64_KEY` giữa local runner, `.env` và Docker Compose.
- Ghi lại phân loại bảng để không nhầm bảng rỗng hoặc biến động table count với bảng thừa.

### Ngoài phạm vi

- Không xóa bảng chỉ vì đang có 0 row.
- Không xóa bảng HangFire, MassTransit, ASP.NET Identity, ledger migration/data patch.
- Không xóa các module optional hoặc mới triển khai như Ads, Experiments, Documents, Notifications, lead revenue, content render/workflow.
- Không thay đổi API hoặc giao diện người dùng.
- Không sửa lịch sử migration cũ.
- Không coi một table count cố định là correctness gate; count chỉ mang tính thông tin.
- Không bao gồm commit, push hoặc production deployment trong lần cập nhật tài liệu này.

## User Stories & Use Cases

- Là operator, tôi muốn chạy `run-all.bat` hoặc `migrate-local.ps1` và database tự về schema canonical mà ledger chỉ được ghi cùng transaction thành công.
- Là admin, tôi muốn role assignment cũ được đối soát chính xác với `AspNetUserRoles`, không tự khôi phục role đã bị thu hồi.
- Là admin kết nối Pancake, tôi muốn token cũ tiếp tục dùng được sau khi gộp `pancake_pages` và `channel_tokens` vào `inboxes`, nhưng inbox đã disconnected không bị re-activate.
- Là developer, tôi muốn schema repair phục hồi các bảng labels/notes active nếu database local bị drift.
- Là người vận hành, tôi muốn migration dừng và rollback nếu role reconciliation không an toàn hoặc `conversation_read_state` có writer/row đồng thời.

## Approaches Considered

1. **Chỉ xóa các bảng đang rỗng**: đơn giản nhưng sai vì nhiều bảng rỗng vẫn có endpoint/job đang sử dụng và read-state có thể xuất hiện giữa check/drop.
2. **Đổi tên toàn bộ bảng legacy thành archive**: giữ dữ liệu nhưng không thực sự gom schema và tiếp tục tăng số bảng.
3. **Migrate/reconcile, verify, rồi drop trong một transaction bắt buộc**: giữ dữ liệu cần thiết, fail closed khi không thể ánh xạ, khóa read-state qua check/drop, và đưa schema về trạng thái canonical.

Chọn phương án 3.

## Success Criteria

- Migration `0094_database_table_consolidation.sql` chỉ chạy khi có transaction và ledger được commit atomically bởi cả `apply-migrations.ps1` lẫn `migrate-local.ps1`.
- Sau migration, bốn bảng legacy không còn tồn tại.
- Ba bảng collaboration tồn tại với đúng column, PK, FK và index contract.
- Identity role reconciliation yêu cầu exact equality; unmappable, ambiguous, extra hoặc missing assignment đều rollback, kể cả zero-role revocation.
- Canonical inbox disconnected giữ nguyên inactive/deleted state, không sinh active duplicate và vẫn bảo toàn ciphertext.
- `conversation_read_state` được kiểm tra dưới `TABLOCKX, HOLDLOCK`; có row hoặc concurrent writer làm migration rollback trước drop.
- Verification live trả `111111111111111|91|102`: 15 contract flags đều pass; `91` dbo tables và `102` user-defined tables chỉ là số liệu thông tin.
- Happy fixture trả `legacy_tables=0 identity_admin=1 disconnected_rows=1 disconnected_active=0 disconnected_token=1 channel_inactive=1 ledger=1`.
- Revoked-role fixture rollback với `legacy_tables=4 ledger=0 canonical_roles=0 legacy_roles=1`.
- Concurrent read-state fixture rollback với `read_state_rows=1 legacy_tables=4 ledger=0`.
- Migration và repair chạy lặp lại không tạo object/data trùng; verification vẫn pass.
- `dotnet build Clawbot.sln --no-restore` hoàn tất với 0 warnings và 0 errors; full solution test run có đủ 154 tests passed khi cấu hình kết nối SQL Server integration.
- `run-all.bat --dry-run` và `docker compose config` đều pass.

## Constraints & Assumptions

- SQL Server hỗ trợ transactional DDL, `OBJECT_ID` checks và table locking hints.
- Migration tự `THROW` nếu `@@TRANCOUNT = 0`; mọi supported runner phải bọc migration, DML và ledger insert trong cùng transaction với `XACT_ABORT ON`.
- Credential luôn được sao chép dưới dạng ciphertext; migration không decrypt và không log giá trị secret.
- `roles.name` chỉ map tới đúng một fixed system `AspNetRoles.normalized_name`, cùng tenant với user.
- Historical migration files vẫn giữ nguyên để fresh database có thể replay đầy đủ trước migration consolidation.
- `JWT_SIGNING_KEY` phải nhất quán giữa API và Gateway; `ENCRYPTION_BASE64_KEY` phải nhất quán giữa API và AgentService. Docker Compose nhận các giá trị qua environment forwarding.
- Thay đổi schema có thể cần `Sch-M`; production application vẫn yêu cầu backup và maintenance plan riêng.

## Questions & Open Items

Không còn quyết định implementation cần hỏi thêm. Local implementation và verification đã hoàn tất. Commit, push và production deployment chưa được tuyên bố trong tài liệu này; môi trường production vẫn phải chạy backup, migration, verification và smoke-test gates theo deployment guide.
