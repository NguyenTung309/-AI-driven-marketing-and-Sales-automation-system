SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;

-- Backfill OwnerUserId cho leads cu chua co sale phu trach.
-- Quy tac: khach thuoc kenh nao thi sale (inbox_member dau tien) cua kenh do phu trach.
-- Auto-run boi run-all.bat qua marker dbo.data_patches (one-shot). Lead phat sinh sau
-- duoc LeadAutoScorer tu gan/heal khi khach nhan tin, khong can chay lai patch nay.

IF OBJECT_ID(N'dbo.data_patches', N'U') IS NULL
    CREATE TABLE dbo.data_patches (
        patch_id NVARCHAR(64) NOT NULL CONSTRAINT PK_data_patches PRIMARY KEY,
        applied_at DATETIMEOFFSET NOT NULL CONSTRAINT DF_data_patches_applied_at DEFAULT SYSUTCDATETIME()
    );

IF NOT EXISTS (SELECT 1 FROM dbo.data_patches WHERE patch_id = N'2026-07-08-backfill-lead-owners-v2')
BEGIN
    -- inbox_members den tu migration 0026 / repair block; schema cu chua co thi skip
    -- (khong danh dau applied) de lan chay sau thu lai.
    IF OBJECT_ID(N'dbo.leads', N'U') IS NULL
        OR OBJECT_ID(N'dbo.conversations', N'U') IS NULL
        OR OBJECT_ID(N'dbo.inboxes', N'U') IS NULL
        OR OBJECT_ID(N'dbo.inbox_members', N'U') IS NULL
        OR COL_LENGTH(N'dbo.conversations', N'inbox_id') IS NULL
    BEGIN
        PRINT 'leads/conversations/inboxes chua du schema - patch skipped, retries next run.';
        RETURN;
    END

    DECLARE @convFixed INT = 0, @assigned INT = 0;

    -- Buoc 1: hoi thoai tao truoc khi co bang inboxes -> inbox_id NULL het. Gan lai theo
    -- external_thread_id chua external_page_id (poller dung dinh dang "{pageId}:{convId}").
    -- Dynamic SQL: cot inbox_id co the vua duoc repair block them, static batch se loi compile.
    EXEC sp_executesql N'
    UPDATE c
    SET c.inbox_id = i.id
    FROM dbo.conversations c
    CROSS APPLY (
        SELECT TOP 1 i.id
        FROM dbo.inboxes i
        WHERE i.tenant_id = c.tenant_id
          AND i.platform = c.platform
          AND i.deleted_at IS NULL
          AND CHARINDEX(i.external_page_id, c.external_thread_id) > 0
    ) i
    WHERE c.inbox_id IS NULL
      AND c.deleted_at IS NULL;';
    SET @convFixed = @@ROWCOUNT;

    -- Buoc 2: gan owner cho lead theo sale cua kenh (inbox_member dau tien).
    EXEC sp_executesql N'
    UPDATE l
    SET l.owner_user_id = m.agent_id
    FROM dbo.leads l
    CROSS APPLY (
        SELECT TOP 1 c.inbox_id
        FROM dbo.conversations c
        WHERE c.tenant_id = l.tenant_id
          AND c.contact_id = l.contact_id
          AND c.inbox_id IS NOT NULL
          AND c.deleted_at IS NULL
        ORDER BY COALESCE(c.last_message_at, c.created_at) DESC
    ) conv
    CROSS APPLY (
        SELECT TOP 1 im.agent_id
        FROM dbo.inbox_members im
        WHERE im.inbox_id = conv.inbox_id
        ORDER BY im.agent_id
    ) m
    WHERE l.owner_user_id IS NULL
      AND l.deleted_at IS NULL;';
    SET @assigned = @@ROWCOUNT;

    INSERT INTO dbo.data_patches (patch_id) VALUES (N'2026-07-08-backfill-lead-owners-v2');

    PRINT CONCAT('Conversations gan lai inbox_id: ', @convFixed);
    PRINT CONCAT('Leads auto-assigned owner theo kenh: ', @assigned);
END
ELSE
    PRINT 'Patch 2026-07-08-backfill-lead-owners-v2 already applied - skipped.';
