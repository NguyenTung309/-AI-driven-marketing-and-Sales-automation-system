-- 0122: Separate compact planner capability descriptions from full runtime system prompts.
-- Keep every statement in this migration GO-free: the deployment runner sends one command per file.

IF COL_LENGTH(N'dbo.agent_definitions', N'system_prompt') IS NULL
    ALTER TABLE dbo.agent_definitions ADD system_prompt NVARCHAR(MAX) NULL;

IF COL_LENGTH(N'dbo.agent_definitions', N'system_prompt_version') IS NULL
    ALTER TABLE dbo.agent_definitions ADD system_prompt_version INT NULL;
