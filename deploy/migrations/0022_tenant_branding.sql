IF COL_LENGTH('dbo.tenants', 'brand_name') IS NULL
BEGIN
    ALTER TABLE dbo.tenants ADD
        brand_name NVARCHAR(256) NULL,
        logo_url NVARCHAR(512) NULL,
        primary_color NVARCHAR(16) NULL,
        accent_color NVARCHAR(16) NULL,
        support_name NVARCHAR(256) NULL,
        widget_greeting NVARCHAR(1024) NULL;
END
