-- 0057: AI tu hoc Lop 2 — bang contact_memories (facts AI nho ve khach, immutable/supersede)
-- + watermark conversations.memory_extracted_at (CHI set khi trich thanh cong).
-- Fact da PII-redact truoc khi persist. One SqlCommand, no GO.
IF OBJECT_ID('dbo.contact_memories', 'U') IS NULL
CREATE TABLE dbo.contact_memories (
    id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_contact_memories PRIMARY KEY,
    tenant_id UNIQUEIDENTIFIER NOT NULL,
    contact_id UNIQUEIDENTIFIER NOT NULL,
    fact NVARCHAR(1024) NOT NULL,
    category NVARCHAR(32) NOT NULL,
    confidence DECIMAL(3,2) NOT NULL CONSTRAINT DF_contact_memories_confidence DEFAULT 0.5,
    source_conversation_id UNIQUEIDENTIFIER NULL,
    is_active BIT NOT NULL CONSTRAINT DF_contact_memories_is_active DEFAULT 1,
    superseded_by_id UNIQUEIDENTIFIER NULL,
    created_at DATETIMEOFFSET NOT NULL,
    updated_at DATETIMEOFFSET NOT NULL
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_contact_memories_tenant_id_contact_id_is_active' AND object_id = OBJECT_ID('dbo.contact_memories'))
    CREATE INDEX IX_contact_memories_tenant_id_contact_id_is_active ON dbo.contact_memories (tenant_id, contact_id, is_active);
IF COL_LENGTH('dbo.conversations', 'memory_extracted_at') IS NULL
    ALTER TABLE dbo.conversations ADD memory_extracted_at DATETIMEOFFSET NULL;
