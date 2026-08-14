-- 0120: admin-only tracking for recurring Hangfire executions. One SqlCommand, no GO.
IF OBJECT_ID(N'dbo.recurring_job_executions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.recurring_job_executions (
        id                          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_recurring_job_executions PRIMARY KEY,
        definition_id               NVARCHAR(128) NOT NULL,
        source                      NVARCHAR(16) NOT NULL,
        status                      NVARCHAR(20) NOT NULL,
        version                     INT NOT NULL CONSTRAINT DF_recurring_job_executions_version DEFAULT 0,
        requested_by_user_id        UNIQUEIDENTIFIER NULL,
        requested_tenant_id         UNIQUEIDENTIFIER NULL,
        retry_of_execution_id       UNIQUEIDENTIFIER NULL,
        request_key                 NVARCHAR(64) NULL,
        hangfire_background_job_id  NVARCHAR(64) NULL,
        enqueue_claim_token         UNIQUEIDENTIFIER NULL,
        enqueue_claimed_at          DATETIMEOFFSET NULL,
        progress_percent            INT NULL,
        progress_note               NVARCHAR(200) NULL,
        result_summary              NVARCHAR(1000) NULL,
        result_link                 NVARCHAR(400) NULL,
        error                       NVARCHAR(1000) NULL,
        requested_at                DATETIMEOFFSET NOT NULL,
        enqueued_at                 DATETIMEOFFSET NULL,
        started_at                  DATETIMEOFFSET NULL,
        finished_at                 DATETIMEOFFSET NULL,
        CONSTRAINT FK_recurring_job_executions_retry_of_execution
            FOREIGN KEY (retry_of_execution_id)
            REFERENCES dbo.recurring_job_executions(id)
    );
END

IF OBJECT_ID(N'dbo.recurring_job_execution_attempts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.recurring_job_execution_attempts (
        id                          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_recurring_job_execution_attempts PRIMARY KEY,
        execution_id                UNIQUEIDENTIFIER NOT NULL,
        hangfire_background_job_id  NVARCHAR(64) NOT NULL,
        retry_count                 INT NOT NULL,
        attempt_number              INT NOT NULL,
        status                      NVARCHAR(20) NOT NULL,
        started_at                  DATETIMEOFFSET NOT NULL,
        finished_at                 DATETIMEOFFSET NULL,
        error                       NVARCHAR(1000) NULL,
        worker_id                   NVARCHAR(128) NULL,
        version                     INT NOT NULL CONSTRAINT DF_recurring_job_execution_attempts_version DEFAULT 0,
        CONSTRAINT FK_recurring_job_execution_attempts_execution
            FOREIGN KEY (execution_id)
            REFERENCES dbo.recurring_job_executions(id)
            ON DELETE CASCADE
    );
END

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_recurring_job_executions_definition_requested_at'
        AND object_id = OBJECT_ID(N'dbo.recurring_job_executions'))
BEGIN
    CREATE INDEX IX_recurring_job_executions_definition_requested_at
        ON dbo.recurring_job_executions (definition_id, requested_at DESC);
END

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_recurring_job_executions_status_requested_at'
        AND object_id = OBJECT_ID(N'dbo.recurring_job_executions'))
BEGIN
    CREATE INDEX IX_recurring_job_executions_status_requested_at
        ON dbo.recurring_job_executions (status, requested_at DESC);
END

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_recurring_job_executions_retry_of_execution_id'
        AND object_id = OBJECT_ID(N'dbo.recurring_job_executions'))
BEGIN
    CREATE INDEX IX_recurring_job_executions_retry_of_execution_id
        ON dbo.recurring_job_executions (retry_of_execution_id);
END

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_recurring_job_executions_definition_hangfire_id'
        AND object_id = OBJECT_ID(N'dbo.recurring_job_executions'))
BEGIN
    CREATE UNIQUE INDEX UX_recurring_job_executions_definition_hangfire_id
        ON dbo.recurring_job_executions (definition_id, hangfire_background_job_id)
        WHERE hangfire_background_job_id IS NOT NULL;
END

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_recurring_job_executions_request_key'
        AND object_id = OBJECT_ID(N'dbo.recurring_job_executions'))
BEGIN
    CREATE UNIQUE INDEX UX_recurring_job_executions_request_key
        ON dbo.recurring_job_executions (requested_tenant_id, requested_by_user_id, request_key)
        WHERE request_key IS NOT NULL;
END

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_recurring_job_execution_attempts_execution_attempt_number'
        AND object_id = OBJECT_ID(N'dbo.recurring_job_execution_attempts'))
BEGIN
    CREATE UNIQUE INDEX UX_recurring_job_execution_attempts_execution_attempt_number
        ON dbo.recurring_job_execution_attempts (execution_id, attempt_number);
END

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_recurring_job_execution_attempts_hangfire_id'
        AND object_id = OBJECT_ID(N'dbo.recurring_job_execution_attempts'))
BEGIN
    CREATE INDEX IX_recurring_job_execution_attempts_hangfire_id
        ON dbo.recurring_job_execution_attempts (hangfire_background_job_id);
END

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_recurring_job_execution_attempts_running_execution'
        AND object_id = OBJECT_ID(N'dbo.recurring_job_execution_attempts'))
BEGIN
    CREATE UNIQUE INDEX UX_recurring_job_execution_attempts_running_execution
        ON dbo.recurring_job_execution_attempts (execution_id)
        WHERE status = N'running';
END
