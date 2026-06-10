-- Drip sequence tables for automated follow-up campaigns.
-- drip_sequences: template for a multi-step drip campaign.
-- drip_enrollments: tracks which lead is at which step, when to send next.

CREATE TABLE drip_sequences (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id       UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    name            NVARCHAR(256) NOT NULL,
    description     NVARCHAR(MAX),
    trigger_event   NVARCHAR(64) NOT NULL,   -- e.g. 'new_lead', 'demo_no_show', 'stale_30d'
    is_active       BIT NOT NULL DEFAULT 1,
    created_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
CREATE INDEX ix_drip_sequences_tenant ON drip_sequences (tenant_id, is_active);

CREATE TABLE drip_sequence_steps (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    sequence_id     UNIQUEIDENTIFIER NOT NULL REFERENCES drip_sequences(id) ON DELETE CASCADE,
    step_order      INT NOT NULL,
    delay_hours     INT NOT NULL,             -- hours after previous step (or enrollment for step 1)
    channel         NVARCHAR(32) NOT NULL,    -- pancake|email|sms
    template_body   NVARCHAR(MAX) NOT NULL,
    created_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    UNIQUE (sequence_id, step_order)
);

CREATE TABLE drip_enrollments (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id       UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    sequence_id     UNIQUEIDENTIFIER NOT NULL REFERENCES drip_sequences(id) ON DELETE CASCADE,
    lead_id         UNIQUEIDENTIFIER NOT NULL REFERENCES leads(id) ON DELETE NO ACTION,
    current_step    INT NOT NULL DEFAULT 0,
    next_send_at    DATETIMEOFFSET NOT NULL,
    status          NVARCHAR(32) NOT NULL DEFAULT 'active',  -- active|completed|cancelled
    enrolled_at     DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    completed_at    DATETIMEOFFSET,
    UNIQUE (sequence_id, lead_id)
);
CREATE INDEX ix_drip_enrollments_due ON drip_enrollments (status, next_send_at) WHERE status = 'active';
CREATE INDEX ix_drip_enrollments_tenant ON drip_enrollments (tenant_id, status);
