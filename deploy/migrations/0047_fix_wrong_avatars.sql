-- Required for filtered indexes on contacts table
SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;

BEGIN TRANSACTION;

-- 1. Xóa avatar của các Contact bị lưu nhầm thành avatar của OA Page
-- (Lấy các avatar_url trong bảng inboxes làm danh sách avatar của Page)
UPDATE c
SET c.avatar_url = NULL
FROM contacts c
INNER JOIN contact_external_ids ce ON ce.contact_id = c.id
INNER JOIN inboxes i ON i.external_page_id = ce.external_id OR c.display_name = i.name
WHERE c.avatar_url = i.avatar_url;

-- 2. Cập nhật lại avatar đúng cho các Contact từ tin nhắn inbound (direction = 'in') có chứa avatar hợp lệ (không trùng với avatar của Page)
WITH CorrectAvatars AS (
    SELECT 
        c.id AS contact_id,
        m.sender_avatar_url AS real_avatar_url,
        ROW_NUMBER() OVER (PARTITION BY c.id ORDER BY m.sent_at DESC) as rn
    FROM contacts c
    INNER JOIN conversations conv ON conv.contact_id = c.id
    INNER JOIN messages m ON m.conversation_id = conv.id
    LEFT JOIN inboxes i ON i.avatar_url = m.sender_avatar_url
    WHERE m.direction = 'in' 
      AND m.sender_avatar_url IS NOT NULL 
      AND m.sender_avatar_url <> ''
      AND i.id IS NULL -- Không lấy avatar của bất kỳ OA Page nào
)
UPDATE c
SET c.avatar_url = ca.real_avatar_url
FROM contacts c
INNER JOIN CorrectAvatars ca ON ca.contact_id = c.id
WHERE ca.rn = 1 AND (c.avatar_url IS NULL OR c.avatar_url = '');

-- 3. Xóa các avatar_url sai ở cấp độ tin nhắn trong bảng messages đối với các tin nhắn gửi đến (inbound)
UPDATE m
SET m.sender_avatar_url = NULL
FROM messages m
INNER JOIN conversations conv ON conv.id = m.conversation_id
INNER JOIN inboxes i ON i.avatar_url = m.sender_avatar_url
WHERE m.direction = 'in';

COMMIT;
