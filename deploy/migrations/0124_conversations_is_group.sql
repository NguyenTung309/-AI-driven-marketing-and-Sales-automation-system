-- Danh dau hoi thoai la nhom (Zalo/FB nhieu thanh vien) thay vi 1 khach ca nhan.
-- Dung de loai hoi thoai nhom khoi dem/cham Lead o trang /leads. One SqlCommand, no GO. Safe to re-run.

IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.conversations', N'is_group') IS NULL
        ALTER TABLE dbo.conversations ADD is_group BIT NOT NULL
            CONSTRAINT DF_conversations_is_group DEFAULT 0;
END
