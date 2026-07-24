-- 0081: Snapshot the provider-side target identity for fail-closed social publishing retries.
-- sqlcmd mac dinh QUOTED_IDENTIFIER OFF; content_schedule co filtered index nen UPDATE se loi 1934.
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.content_schedule', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.content_schedule', N'provider_target_id') IS NULL
BEGIN
    ALTER TABLE dbo.content_schedule ADD provider_target_id NVARCHAR(128) NULL;
END;

-- Legacy Instagram schedules did not snapshot the provider-side Instagram user ID.
-- Hold idle active rows until an administrator explicitly reselects the target. Publishing and
-- outcome_unknown rows must retain their in-flight/reconciliation state and are not rewritten here.
IF OBJECT_ID(N'dbo.content_schedule', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.content_schedule', N'provider_target_id') IS NOT NULL
BEGIN
    DECLARE @writerGateWasEnabled BIT = 0;
    IF OBJECT_ID(N'dbo.TR_content_schedule_writer_gate', N'TR') IS NOT NULL
       AND OBJECTPROPERTYEX(OBJECT_ID(N'dbo.TR_content_schedule_writer_gate'), N'ExecIsTriggerDisabled') = 0
    BEGIN
        SET @writerGateWasEnabled = 1;
        DISABLE TRIGGER dbo.TR_content_schedule_writer_gate ON dbo.content_schedule;
    END;

    BEGIN TRY
        EXEC(N'
            UPDATE dbo.content_schedule
            SET status = N''held'',
                next_attempt_at = NULL,
                last_error_code = N''instagram_target_reselection_required'',
                last_error = N''Instagram target must be reselected after the provider target snapshot migration.'',
                updated_at = SYSDATETIMEOFFSET()
            WHERE LOWER(LTRIM(RTRIM(platform))) = N''instagram''
              AND status IN (N''pending'', N''held'')
              AND NULLIF(LTRIM(RTRIM(provider_target_id)), N'''') IS NULL
              AND (status <> N''held''
                   OR ISNULL(last_error_code, N'''') <> N''instagram_target_reselection_required''
                   OR next_attempt_at IS NOT NULL);');
    END TRY
    BEGIN CATCH
        IF @writerGateWasEnabled = 1
            ENABLE TRIGGER dbo.TR_content_schedule_writer_gate ON dbo.content_schedule;
        THROW;
    END CATCH;

    IF @writerGateWasEnabled = 1
        ENABLE TRIGGER dbo.TR_content_schedule_writer_gate ON dbo.content_schedule;
END;
