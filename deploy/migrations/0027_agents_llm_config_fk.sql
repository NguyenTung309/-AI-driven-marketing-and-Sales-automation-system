-- 0027: Index + FK for agents.llm_config_id (column added in 0026).
-- ON DELETE SET NULL: deleting a provider config orphans bound agents (they hard-error at
-- runtime until rebound, D1) rather than cascading deletes onto agents.

IF COL_LENGTH(N'dbo.agents', N'llm_config_id') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_agents_llm_config_id' AND object_id = OBJECT_ID(N'dbo.agents'))
    EXEC(N'CREATE INDEX ix_agents_llm_config_id ON agents (llm_config_id);');

IF COL_LENGTH(N'dbo.agents', N'llm_config_id') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_agents_llm_configs_llm_config_id')
    EXEC(N'ALTER TABLE agents ADD CONSTRAINT fk_agents_llm_configs_llm_config_id FOREIGN KEY (llm_config_id) REFERENCES llm_configs (id) ON DELETE SET NULL;');
