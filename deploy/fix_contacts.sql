SET QUOTED_IDENTIFIER ON;
USE clawbot;

-- Update contact display_name and avatar_url from the first inbound message
-- Only for contacts whose display_name is a GUID (no 'pzl_' prefix and exactly 36 chars)
UPDATE c
SET display_name = m.sender_display_name,
    avatar_url = ISNULL(m.sender_avatar_url, c.avatar_url)
FROM clawbot.dbo.contacts c
INNER JOIN clawbot.dbo.conversations cv ON cv.contact_id = c.id
INNER JOIN clawbot.dbo.contact_external_ids e ON e.contact_id = c.id
CROSS APPLY (
    SELECT TOP 1 sender_display_name, sender_avatar_url
    FROM clawbot.dbo.messages m2
    WHERE m2.conversation_id = cv.id
      AND m2.direction = 'in'
      AND m2.sender_display_name IS NOT NULL
      AND m2.sender_display_name != ''
    ORDER BY m2.sent_at ASC
) m
WHERE e.external_id NOT LIKE 'pzl_%'
  AND LEN(e.external_id) = 36  -- GUID length with dashes

-- For any remaining corrupted contacts without inbound messages (use outbound or fallback)
UPDATE c
SET display_name = ISNULL(m.sender_display_name, 'Customer ' + LEFT(c.id, 8))
FROM clawbot.dbo.contacts c
INNER JOIN clawbot.dbo.conversations cv ON cv.contact_id = c.id
INNER JOIN clawbot.dbo.contact_external_ids e ON e.contact_id = c.id
CROSS APPLY (
    SELECT TOP 1 sender_display_name
    FROM clawbot.dbo.messages m2
    WHERE m2.conversation_id = cv.id
      AND m2.sender_display_name IS NOT NULL
      AND m2.sender_display_name != ''
    ORDER BY m2.sent_at ASC
) m
WHERE e.external_id NOT LIKE 'pzl_%'
  AND LEN(e.external_id) = 36
  AND c.display_name LIKE '________-____-____-____-____________'

SELECT 'Updated ' + CAST(@@ROWCOUNT AS NVARCHAR) + ' contacts' AS status;