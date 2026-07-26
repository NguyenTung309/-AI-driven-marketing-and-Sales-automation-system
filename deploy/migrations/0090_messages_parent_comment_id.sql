-- 0090: link outbound comment/DM rows to the exact inbound comment they handled.
-- One SqlCommand, no GO.
IF COL_LENGTH('dbo.messages', 'parent_comment_id') IS NULL
    ALTER TABLE dbo.messages ADD parent_comment_id NVARCHAR(256) NULL;
