-- 0027: Index + FK for agents.llm_config_id (column added in 0026).
-- ON DELETE NO ACTION avoids SQL Server multiple-cascade-path failures.
-- Rebind agents before deleting a provider config.

IF COL_LENGTH(N'dbo.agents', N'llm_config_id') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_agents_llm_config_id' AND object_id = OBJECT_ID(N'dbo.agents'))
    EXEC(N'CREATE INDEX ix_agents_llm_config_id ON agents (llm_config_id);');

IF COL_LENGTH(N'dbo.agents', N'llm_config_id') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_agents_llm_configs_llm_config_id')
    EXEC(N'ALTER TABLE agents ADD CONSTRAINT fk_agents_llm_configs_llm_config_id FOREIGN KEY (llm_config_id) REFERENCES llm_configs (id) ON DELETE NO ACTION;');
