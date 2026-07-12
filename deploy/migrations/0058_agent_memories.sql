-- 0058: AI tu hoc Lop 3 — bang agent_memories (bai hoc nghiep vu theo agent_code, immutable/supersede).
-- Dung dau tien cho reviewer-agent: loi content hay gap, nap vao persona khi cham.
-- One SqlCommand, no GO.
IF OBJECT_ID('dbo.agent_memories', 'U') IS NULL
CREATE TABLE dbo.agent_memories (
    id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_agent_memories PRIMARY KEY,
    tenant_id UNIQUEIDENTIFIER NOT NULL,
    agent_code NVARCHAR(64) NOT NULL,
    fact NVARCHAR(1024) NOT NULL,
    category NVARCHAR(32) NOT NULL CONSTRAINT DF_agent_memories_category DEFAULT 'mistake',
    confidence DECIMAL(3,2) NOT NULL CONSTRAINT DF_agent_memories_confidence DEFAULT 0.5,
    is_active BIT NOT NULL CONSTRAINT DF_agent_memories_is_active DEFAULT 1,
    superseded_by_id UNIQUEIDENTIFIER NULL,
    created_at DATETIMEOFFSET NOT NULL,
    updated_at DATETIMEOFFSET NOT NULL
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_agent_memories_tenant_id_agent_code_is_active' AND object_id = OBJECT_ID('dbo.agent_memories'))
    CREATE INDEX IX_agent_memories_tenant_id_agent_code_is_active ON dbo.agent_memories (tenant_id, agent_code, is_active);
