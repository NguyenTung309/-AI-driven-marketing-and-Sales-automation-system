-- 0121: durable claim around external Hangfire enqueue. One SqlCommand, no GO.
-- Keep this additive because 0120 may already have been applied before enqueue claims existed.
IF COL_LENGTH(N'dbo.recurring_job_executions', N'enqueue_claim_token') IS NULL
BEGIN
    ALTER TABLE dbo.recurring_job_executions
        ADD enqueue_claim_token UNIQUEIDENTIFIER NULL,
            enqueue_claimed_at DATETIMEOFFSET NULL;
END
