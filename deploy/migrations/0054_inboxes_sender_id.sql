-- 0054: inboxes.sender_id — id user gửi phía Pancake (PancakePageTokenResolver dùng khi send).
-- Cột đã có trong entity Inbox từ trước nhưng thiếu migration + repair block -> DB local cũ
-- chết mọi query Inboxes (Invalid column name 'sender_id', poll dừng).
-- One SqlCommand, no GO.
IF COL_LENGTH('dbo.inboxes', 'sender_id') IS NULL
    ALTER TABLE dbo.inboxes ADD sender_id NVARCHAR(128) NULL;
