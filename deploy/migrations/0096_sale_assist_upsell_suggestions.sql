-- Cache goi y upsell theo hoi thoai: sinh 1 lan qua background job, doc lai cho den khi
-- hoi thoai co tin nhan moi hon source_last_message_at. One SqlCommand, no GO.
IF OBJECT_ID(N'dbo.sale_assist_upsell_suggestions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.sale_assist_upsell_suggestions (
        id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_sale_assist_upsell_suggestions PRIMARY KEY,
        tenant_id UNIQUEIDENTIFIER NOT NULL,
        conversation_id UNIQUEIDENTIFIER NOT NULL,
        eligible BIT NOT NULL,
        suggestion NVARCHAR(1000) NOT NULL,
        reason NVARCHAR(400) NOT NULL,
        lead_score INT NOT NULL,
        generated_at DATETIMEOFFSET NOT NULL,
        source_last_message_at DATETIMEOFFSET NOT NULL
    );

    CREATE UNIQUE INDEX IX_sale_assist_upsell_suggestions_tenant_conversation
        ON dbo.sale_assist_upsell_suggestions (tenant_id, conversation_id);
END
