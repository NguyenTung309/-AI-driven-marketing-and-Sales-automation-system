-- 0123: Facebook reaction breakdown per published post.
-- like_count giữ nguyên nghĩa cũ (chỉ reaction LIKE) để không phá dữ liệu đã đồng bộ;
-- tổng và từng loại nằm ở cột riêng.
-- Mọi câu trong file này phải GO-free: bộ chạy migration gửi cả file như một SqlCommand.

IF COL_LENGTH(N'dbo.content_schedule', N'reactions_total') IS NULL
    ALTER TABLE dbo.content_schedule ADD reactions_total INT NULL;

IF COL_LENGTH(N'dbo.content_schedule', N'reaction_love') IS NULL
    ALTER TABLE dbo.content_schedule ADD reaction_love INT NULL;

IF COL_LENGTH(N'dbo.content_schedule', N'reaction_haha') IS NULL
    ALTER TABLE dbo.content_schedule ADD reaction_haha INT NULL;

IF COL_LENGTH(N'dbo.content_schedule', N'reaction_wow') IS NULL
    ALTER TABLE dbo.content_schedule ADD reaction_wow INT NULL;

IF COL_LENGTH(N'dbo.content_schedule', N'reaction_sad') IS NULL
    ALTER TABLE dbo.content_schedule ADD reaction_sad INT NULL;

IF COL_LENGTH(N'dbo.content_schedule', N'reaction_angry') IS NULL
    ALTER TABLE dbo.content_schedule ADD reaction_angry INT NULL;

IF COL_LENGTH(N'dbo.content_schedule', N'reaction_care') IS NULL
    ALTER TABLE dbo.content_schedule ADD reaction_care INT NULL;
