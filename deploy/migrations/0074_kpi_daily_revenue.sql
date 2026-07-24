-- Revenue approved during the KPI day. One SqlCommand, no GO.
IF COL_LENGTH(N'dbo.kpi_daily', N'revenue') IS NULL
    ALTER TABLE dbo.kpi_daily ADD revenue DECIMAL(18,2) NULL;
