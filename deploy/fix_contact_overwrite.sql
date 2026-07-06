SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;

-- Repair contacts polluted by the sender-overwrite bug: conversation contacts took the
-- name/avatar of whoever sent the last message (admin/AI/group member) instead of the
-- conversation counterpart. Auto-run by run-all.bat; the data_patches marker makes it
-- one-shot (Fix B resets group contacts to placeholder, so re-running after self-heal
-- would wipe healed names - must not repeat).

IF OBJECT_ID(N'dbo.data_patches', N'U') IS NULL
    CREATE TABLE dbo.data_patches (
        patch_id NVARCHAR(64) NOT NULL CONSTRAINT PK_data_patches PRIMARY KEY,
        applied_at DATETIMEOFFSET NOT NULL CONSTRAINT DF_data_patches_applied_at DEFAULT SYSUTCDATETIME()
    );

IF NOT EXISTS (SELECT 1 FROM dbo.data_patches WHERE patch_id = N'2026-07-06-fix-contact-overwrite')
BEGIN
    DECLARE @fixed INT = 0, @reset INT = 0;

    -- Fix A: 1-1 Zalo conversation contacts - restore name/avatar from the first inbound
    -- (customer) message of the conversation.
    UPDATE c
    SET display_name = m.sender_display_name,
        avatar_url = COALESCE(m.sender_avatar_url, c.avatar_url)
    FROM dbo.contacts c
    INNER JOIN dbo.conversations cv ON cv.contact_id = c.id
    INNER JOIN dbo.contact_external_ids e ON e.contact_id = c.id AND e.platform = 'zalo'
    CROSS APPLY (
        SELECT TOP 1 m2.sender_display_name, m2.sender_avatar_url
        FROM dbo.messages m2
        WHERE m2.conversation_id = cv.id
          AND m2.direction = 'in'
          AND m2.sender_display_name IS NOT NULL
          AND m2.sender_display_name != ''
        ORDER BY m2.sent_at ASC
    ) m
    WHERE e.external_id NOT LIKE 'pzl_g_%'
      AND c.display_name != m.sender_display_name;
    SET @fixed = @@ROWCOUNT;

    -- Fix B: group contacts - the single shared name/avatar was overwritten by the last
    -- sender. The group's own name is not stored anywhere in the DB, so reset to the
    -- placeholder id; polling self-heals it with conversation_name/conversation_avatar_url
    -- on the next activity.
    UPDATE c
    SET display_name = e.external_id,
        avatar_url = NULL
    FROM dbo.contacts c
    INNER JOIN dbo.contact_external_ids e ON e.contact_id = c.id AND e.platform = 'zalo'
    WHERE e.external_id LIKE 'pzl_g_%'
      AND EXISTS (SELECT 1 FROM dbo.conversations cv WHERE cv.contact_id = c.id);
    SET @reset = @@ROWCOUNT;

    INSERT INTO dbo.data_patches (patch_id) VALUES (N'2026-07-06-fix-contact-overwrite');

    PRINT CONCAT('Fixed 1-1 contacts: ', @fixed);
    PRINT CONCAT('Reset group contacts, self-heal on next poll: ', @reset);
END
ELSE
    PRINT 'Patch 2026-07-06-fix-contact-overwrite already applied - skipped.';
