-- 0035: Bind data-defined sub-agents to a KB module for dynamic RAG knowledge.
IF COL_LENGTH(N'dbo.agent_definitions', N'kb_module_code') IS NULL
BEGIN
    ALTER TABLE dbo.agent_definitions ADD kb_module_code NVARCHAR(64) NULL;
END
