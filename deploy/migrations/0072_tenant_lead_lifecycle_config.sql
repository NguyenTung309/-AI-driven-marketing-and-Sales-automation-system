-- Lead lifecycle tenant settings. One SqlCommand, no GO.
IF COL_LENGTH(N'dbo.tenants', N'lead_lost_after_days') IS NULL
    ALTER TABLE dbo.tenants ADD lead_lost_after_days INT NOT NULL
        CONSTRAINT DF_tenants_lead_lost_after_days DEFAULT 60;

IF COL_LENGTH(N'dbo.tenants', N'auto_approve_lead_revenue') IS NULL
    ALTER TABLE dbo.tenants ADD auto_approve_lead_revenue BIT NOT NULL
        CONSTRAINT DF_tenants_auto_approve_lead_revenue DEFAULT 0;
