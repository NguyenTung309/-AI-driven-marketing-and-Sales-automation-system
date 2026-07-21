-- Phase 2.6: nullable supports_vision override on llm_configs.
-- NULL = auto (registry/unknown); 1/0 = explicit operator override for custom gateways.
IF COL_LENGTH(N'dbo.llm_configs', N'supports_vision') IS NULL
    EXEC(N'ALTER TABLE llm_configs ADD supports_vision BIT NULL;');
