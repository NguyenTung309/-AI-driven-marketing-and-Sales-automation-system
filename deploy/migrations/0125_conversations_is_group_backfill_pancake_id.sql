-- Vet is_group cho hoi thoai Zalo da ton tai truoc migration 0124 (Contact.IsGroup mac dinh false).
-- Pancake tu sinh external_thread_id theo dang "pzl_<pageId>:pzl_g_<pageId>_<id>" cho nhom
-- (":pzl_u_..." cho ca nhan) -- day chinh la nguon Pancake dung de tra conv.From.is_group, nen doc lai
-- tu dinh dang id la an toan tuyet doi (khong phai heuristic doan ten). One SqlCommand, no GO, idempotent.

IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.conversations', N'is_group') IS NOT NULL
BEGIN
    UPDATE dbo.conversations
    SET is_group = 1
    WHERE is_group = 0
      AND external_thread_id LIKE N'%:pzl[_]g[_]%';
END
