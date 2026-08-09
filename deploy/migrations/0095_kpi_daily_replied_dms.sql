-- 0095: số hội thoại (dms) trong ngày có ít nhất 1 phản hồi outbound của AI.
-- "replies" đếm theo tin nhắn nên 1 hội thoại nhiều lượt qua lại trong ngày làm replies > dms,
-- khiến tỉ lệ tự động hóa replies/dms vượt 100%. replied_dms đếm theo hội thoại nên luôn <= dms.
IF COL_LENGTH('dbo.kpi_daily', 'replied_dms') IS NULL
    ALTER TABLE dbo.kpi_daily ADD replied_dms INT NOT NULL DEFAULT 0;
