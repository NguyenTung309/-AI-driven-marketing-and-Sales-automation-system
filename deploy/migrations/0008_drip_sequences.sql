-- Drip sequence tables for automated follow-up campaigns.
-- drip_sequences: template for a multi-step drip campaign.
-- drip_enrollments: tracks which lead is at which step, when to send next.

IF OBJECT_ID(N'dbo.drip_sequences', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.drip_sequences (
        id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        tenant_id       UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id) ON DELETE CASCADE,
        name            NVARCHAR(256) NOT NULL,
        description     NVARCHAR(MAX),
        trigger_event   NVARCHAR(64) NOT NULL,   -- e.g. 'new_lead', 'demo_no_show', 'stale_30d'
        is_active       BIT NOT NULL DEFAULT 1,
        created_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        updated_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_drip_sequences_tenant' AND object_id = OBJECT_ID(N'dbo.drip_sequences'))
    CREATE INDEX ix_drip_sequences_tenant ON dbo.drip_sequences (tenant_id, is_active);

IF OBJECT_ID(N'dbo.drip_sequence_steps', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.drip_sequence_steps (
        id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        sequence_id     UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.drip_sequences(id) ON DELETE CASCADE,
        step_order      INT NOT NULL,
        delay_hours     INT NOT NULL,             -- hours after previous step (or enrollment for step 1)
        channel         NVARCHAR(32) NOT NULL,    -- pancake|email|sms
        template_body   NVARCHAR(MAX) NOT NULL,
        created_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        UNIQUE (sequence_id, step_order)
    );
END

IF OBJECT_ID(N'dbo.drip_enrollments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.drip_enrollments (
        id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        tenant_id       UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id) ON DELETE CASCADE,
        sequence_id     UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.drip_sequences(id) ON DELETE NO ACTION,
        lead_id         UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.leads(id) ON DELETE NO ACTION,
        current_step    INT NOT NULL DEFAULT 0,
        next_send_at    DATETIMEOFFSET NOT NULL,
        status          NVARCHAR(32) NOT NULL DEFAULT 'active',  -- active|completed|cancelled
        enrolled_at     DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        completed_at    DATETIMEOFFSET,
        UNIQUE (sequence_id, lead_id)
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_drip_enrollments_due' AND object_id = OBJECT_ID(N'dbo.drip_enrollments'))
    CREATE INDEX ix_drip_enrollments_due ON dbo.drip_enrollments (status, next_send_at) WHERE status = 'active';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_drip_enrollments_tenant' AND object_id = OBJECT_ID(N'dbo.drip_enrollments'))
    CREATE INDEX ix_drip_enrollments_tenant ON dbo.drip_enrollments (tenant_id, status);
