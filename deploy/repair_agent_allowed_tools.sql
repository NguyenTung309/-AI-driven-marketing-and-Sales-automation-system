-- One-shot data patch: re-grant orchestration tool allow-lists that were seeded as []
-- (text-only). Safe to re-run. Guarded by dbo.data_patches when applied via run-all.
-- Matches DevDataSeeder.OrchestratorAgents + deploy/seed/agent-definitions.sql.

SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;

IF OBJECT_ID(N'dbo.data_patches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.data_patches (
        patch_id NVARCHAR(64) NOT NULL CONSTRAINT PK_data_patches PRIMARY KEY,
        applied_at DATETIMEOFFSET NOT NULL CONSTRAINT DF_data_patches_applied_at DEFAULT SYSUTCDATETIME()
    );
END

IF NOT EXISTS (SELECT 1 FROM dbo.data_patches WHERE patch_id = N'2026-07-20-agent-allowed-tools-grant')
BEGIN
IF OBJECT_ID(N'dbo.agent_definitions', N'U') IS NOT NULL
BEGIN
    UPDATE dbo.agent_definitions SET allowed_tools_json = N'["chat-agent"]', updated_at = SYSDATETIMEOFFSET()
    WHERE code = N'chat-agent' AND (allowed_tools_json IS NULL OR LTRIM(RTRIM(allowed_tools_json)) = N'');

    UPDATE dbo.agent_definitions SET allowed_tools_json = N'["sale-assist"]', updated_at = SYSDATETIMEOFFSET()
    WHERE code = N'sale-assist-agent' AND (allowed_tools_json IS NULL OR LTRIM(RTRIM(allowed_tools_json)) = N'');

    UPDATE dbo.agent_definitions SET allowed_tools_json = N'["lead-agent"]', updated_at = SYSDATETIMEOFFSET()
    WHERE code = N'lead-agent' AND (allowed_tools_json IS NULL OR LTRIM(RTRIM(allowed_tools_json)) = N'');

    UPDATE dbo.agent_definitions SET allowed_tools_json = N'["research-agent","web.search"]', updated_at = SYSDATETIMEOFFSET()
    WHERE code = N'research-agent' AND (allowed_tools_json IS NULL OR LTRIM(RTRIM(allowed_tools_json)) = N'');

    UPDATE dbo.agent_definitions SET allowed_tools_json = N'["docs-agent"]', updated_at = SYSDATETIMEOFFSET()
    WHERE code = N'docs-agent' AND (allowed_tools_json IS NULL OR LTRIM(RTRIM(allowed_tools_json)) = N'');

    UPDATE dbo.agent_definitions SET allowed_tools_json = N'["report-agent"]', updated_at = SYSDATETIMEOFFSET()
    WHERE code = N'report-agent' AND (allowed_tools_json IS NULL OR LTRIM(RTRIM(allowed_tools_json)) = N'');

    -- Only populate an absent legacy persona; an existing tenant-specific prompt is administrator-owned configuration.
    UPDATE dbo.agent_definitions
    SET persona_prompt = N'ALWAYS call lead-agent tool. Use operation=list|find_cold (stage, topN) to query CRM — do not invent lists or ask for lead IDs. Also score/create/batch_score.',
        updated_at = SYSDATETIMEOFFSET()
    WHERE code = N'lead-agent'
      AND (persona_prompt IS NULL OR LTRIM(RTRIM(persona_prompt)) = N'');
END

INSERT INTO dbo.data_patches (patch_id, applied_at)
VALUES (N'2026-07-20-agent-allowed-tools-grant', SYSDATETIMEOFFSET());
END
