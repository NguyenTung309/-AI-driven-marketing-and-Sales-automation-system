-- Tệp kỹ năng (.md) tái sử dụng cho agent: store + upload thay vì gõ tên tay.
IF OBJECT_ID(N'dbo.skill_files', N'U') IS NULL
CREATE TABLE dbo.skill_files (
    id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_skill_files PRIMARY KEY DEFAULT NEWID(),
    tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id) ON DELETE CASCADE,
    name NVARCHAR(128) NOT NULL,
    description NVARCHAR(512) NULL,
    content_md NVARCHAR(MAX) NOT NULL,
    created_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    deleted_at DATETIMEOFFSET NULL
);

IF OBJECT_ID(N'dbo.skill_files', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_skill_files_tenant_name' AND object_id = OBJECT_ID(N'dbo.skill_files'))
CREATE UNIQUE INDEX ix_skill_files_tenant_name ON dbo.skill_files (tenant_id, name) WHERE deleted_at IS NULL;
