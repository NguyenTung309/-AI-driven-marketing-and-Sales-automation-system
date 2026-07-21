-- Approved manual revenue and AI revenue proposals for leads. One SqlCommand, no GO.
IF OBJECT_ID(N'dbo.lead_revenues', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.lead_revenues (
        id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_lead_revenues PRIMARY KEY,
        tenant_id UNIQUEIDENTIFIER NOT NULL,
        lead_id UNIQUEIDENTIFIER NOT NULL,
        amount DECIMAL(18,2) NOT NULL,
        currency NVARCHAR(8) NOT NULL CONSTRAINT DF_lead_revenues_currency DEFAULT N'VND',
        source NVARCHAR(16) NOT NULL,
        status NVARCHAR(16) NOT NULL,
        evidence NVARCHAR(1000) NULL,
        proposed_by UNIQUEIDENTIFIER NULL,
        decided_by UNIQUEIDENTIFIER NULL,
        created_at DATETIMEOFFSET NOT NULL,
        decided_at DATETIMEOFFSET NULL
    );

    CREATE INDEX IX_lead_revenues_tenant_status
        ON dbo.lead_revenues (tenant_id, status, created_at DESC);
    CREATE INDEX IX_lead_revenues_lead
        ON dbo.lead_revenues (lead_id);
END
