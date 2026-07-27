-- Prompt chaining P4: lưu ảnh chụp L1 (plan) + L2 (outline, kèm SelectedHookIndex) trên content_items.
-- CHỈ set khi chuỗi chạy đủ 4 mắt xích thành công; repurpose/đổi hook (§4.5) tái dùng để chạy lại chỉ L3+L4.
-- NULL = bài tạo bằng single-shot (hoặc chain tắt). Một SqlCommand, không GO. An toàn chạy lại (COL_LENGTH guard).
IF COL_LENGTH(N'dbo.content_items', N'chain_plan_json') IS NULL
    ALTER TABLE dbo.content_items ADD chain_plan_json NVARCHAR(MAX) NULL;
IF COL_LENGTH(N'dbo.content_items', N'chain_outline_json') IS NULL
    ALTER TABLE dbo.content_items ADD chain_outline_json NVARCHAR(MAX) NULL;
