-- 0025: LLM provider config — admin label + per-config cost rates.
-- Adds display_name (picker label) and input/output USD-per-1M-token rates to llm_configs.
-- Column-adds only (no new index) so the file stays a single safe batch.

IF COL_LENGTH(N'dbo.llm_configs', N'display_name') IS NULL
    EXEC(N'ALTER TABLE llm_configs ADD display_name NVARCHAR(128) NULL;');

IF COL_LENGTH(N'dbo.llm_configs', N'input_usd_per_1m') IS NULL
    EXEC(N'ALTER TABLE llm_configs ADD input_usd_per_1m DECIMAL(10,4) NULL;');

IF COL_LENGTH(N'dbo.llm_configs', N'output_usd_per_1m') IS NULL
    EXEC(N'ALTER TABLE llm_configs ADD output_usd_per_1m DECIMAL(10,4) NULL;');
