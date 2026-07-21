-- Content workflow runtime gate: singleton pause + minimum writer version + SQL write fencing.
-- One SqlCommand, no GO. Safe to re-run. Bridge binaries set SESSION_CONTEXT('clawbot_content_writer_version').

SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.content_workflow_runtime_gate', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.content_workflow_runtime_gate (
        id TINYINT NOT NULL,
        publication_paused BIT NOT NULL CONSTRAINT DF_content_workflow_runtime_gate_paused DEFAULT (0),
        minimum_writer_version INT NOT NULL CONSTRAINT DF_content_workflow_runtime_gate_min_writer DEFAULT (0),
        updated_at DATETIMEOFFSET NOT NULL CONSTRAINT DF_content_workflow_runtime_gate_updated DEFAULT (SYSDATETIMEOFFSET()),
        updated_by NVARCHAR(128) NULL,
        notes NVARCHAR(1024) NULL,
        CONSTRAINT PK_content_workflow_runtime_gate PRIMARY KEY (id),
        CONSTRAINT CK_content_workflow_runtime_gate_singleton CHECK (id = 1),
        CONSTRAINT CK_content_workflow_runtime_gate_min_writer CHECK (minimum_writer_version >= 0)
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.content_workflow_runtime_gate WHERE id = 1)
BEGIN
    INSERT INTO dbo.content_workflow_runtime_gate (
        id, publication_paused, minimum_writer_version, updated_at, updated_by, notes)
    VALUES (
        1, 0, 0, SYSDATETIMEOFFSET(), N'system',
        N'Permissive default: bridge writers may report any version; publication not paused.');
END;

-- Fence claim/attempt creation while paused or writer below minimum.
IF OBJECT_ID(N'dbo.TR_content_publish_attempts_writer_gate', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_content_publish_attempts_writer_gate;

IF OBJECT_ID(N'dbo.content_publish_attempts', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE TRIGGER dbo.TR_content_publish_attempts_writer_gate
ON dbo.content_publish_attempts
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) RETURN;

    DECLARE @paused BIT = 0;
    DECLARE @min_writer INT = 0;
    DECLARE @writer INT = TRY_CONVERT(INT, SESSION_CONTEXT(N''clawbot_content_writer_version''));

    SELECT
        @paused = publication_paused,
        @min_writer = minimum_writer_version
    FROM dbo.content_workflow_runtime_gate WITH (UPDLOCK, HOLDLOCK)
    WHERE id = 1;

    IF @paused = 1
    BEGIN
        THROW 53001, ''content_publication_paused'', 1;
    END;

    IF @min_writer > 0 AND (@writer IS NULL OR @writer < @min_writer)
    BEGIN
        THROW 53002, ''content_writer_version_too_low'', 1;
    END;
END');
END;

IF OBJECT_ID(N'dbo.TR_content_schedule_writer_gate', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_content_schedule_writer_gate;

IF OBJECT_ID(N'dbo.content_schedule', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE TRIGGER dbo.TR_content_schedule_writer_gate
ON dbo.content_schedule
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) RETURN;

    -- Only fence claim/publishing transitions and active intents that can reach the provider.
    IF NOT EXISTS (
        SELECT 1
        FROM inserted i
        WHERE i.status IN (N''publishing'', N''outcome_unknown'', N''pending'', N''held'', N''failed''))
    BEGIN
        RETURN;
    END;

    DECLARE @paused BIT = 0;
    DECLARE @min_writer INT = 0;
    DECLARE @writer INT = TRY_CONVERT(INT, SESSION_CONTEXT(N''clawbot_content_writer_version''));

    SELECT
        @paused = publication_paused,
        @min_writer = minimum_writer_version
    FROM dbo.content_workflow_runtime_gate WITH (UPDLOCK, HOLDLOCK)
    WHERE id = 1;

    -- Pause blocks transitions into publishing / new pending intent creation; allow cancel/posted history.
    IF @paused = 1
       AND EXISTS (
           SELECT 1
           FROM inserted i
           LEFT JOIN deleted d ON d.id = i.id
           WHERE i.status IN (N''publishing'', N''pending'', N''held'', N''failed'', N''outcome_unknown'')
             AND (d.id IS NULL OR d.status <> i.status OR ISNULL(d.next_attempt_at, ''1900-01-01'') <> ISNULL(i.next_attempt_at, ''1900-01-01'')))
    BEGIN
        THROW 53001, ''content_publication_paused'', 1;
    END;

    IF @min_writer > 0 AND (@writer IS NULL OR @writer < @min_writer)
    BEGIN
        THROW 53002, ''content_writer_version_too_low'', 1;
    END;
END');
END;
