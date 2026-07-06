SET QUOTED_IDENTIFIER ON;
USE clawbot;

-- Repair contacts polluted by the sender-overwrite bug: conversation contacts took the
-- name/avatar of whoever sent the last message (admin/AI/group member) instead of the
-- conversation counterpart. Run once after deploying the conversation_name metadata fix.

-- Fix A: 1-1 Zalo conversation contacts — restore name/avatar from the first inbound
-- (customer) message of the conversation.
UPDATE c
SET display_name = m.sender_display_name,
    avatar_url = COALESCE(m.sender_avatar_url, c.avatar_url)
FROM clawbot.dbo.contacts c
INNER JOIN clawbot.dbo.conversations cv ON cv.contact_id = c.id
INNER JOIN clawbot.dbo.contact_external_ids e ON e.contact_id = c.id AND e.platform = 'zalo'
CROSS APPLY (
    SELECT TOP 1 m2.sender_display_name, m2.sender_avatar_url
    FROM clawbot.dbo.messages m2
    WHERE m2.conversation_id = cv.id
      AND m2.direction = 'in'
      AND m2.sender_display_name IS NOT NULL
      AND m2.sender_display_name != ''
    ORDER BY m2.sent_at ASC
) m
WHERE e.external_id NOT LIKE 'pzl_g_%'
  AND c.display_name != m.sender_display_name;

SELECT 'Fixed 1-1 contacts: ' + CAST(@@ROWCOUNT AS NVARCHAR) AS step1;

-- Fix B: group contacts — the single shared name/avatar was overwritten by the last sender.
-- The group's own name is not stored anywhere in the DB, so reset to the placeholder id;
-- polling self-heals it with conversation_name/conversation_avatar_url on the next activity.
UPDATE c
SET display_name = e.external_id,
    avatar_url = NULL
FROM clawbot.dbo.contacts c
INNER JOIN clawbot.dbo.contact_external_ids e ON e.contact_id = c.id AND e.platform = 'zalo'
WHERE e.external_id LIKE 'pzl_g_%'
  AND EXISTS (SELECT 1 FROM clawbot.dbo.conversations cv WHERE cv.contact_id = c.id);

SELECT 'Reset group contacts (self-heal on next poll): ' + CAST(@@ROWCOUNT AS NVARCHAR) AS step2;
