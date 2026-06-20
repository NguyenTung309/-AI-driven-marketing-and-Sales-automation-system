-- 0026: Bind each agent to an LLM provider config (nullable FK column only).
-- Column-add only; the FK constraint + index land in 0027 (separate file = separate batch,
-- so they reference a column already committed — SQL Server compiles each file as one batch).

IF COL_LENGTH(N'dbo.agents', N'llm_config_id') IS NULL
    EXEC(N'ALTER TABLE agents ADD llm_config_id UNIQUEIDENTIFIER NULL;');
