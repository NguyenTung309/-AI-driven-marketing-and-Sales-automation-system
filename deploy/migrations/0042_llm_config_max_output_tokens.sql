-- 0042: LLM provider config — optional per-config max output tokens.
-- Adds max_output_tokens to llm_configs; NULL → provider default (3000).
-- Column-add only (no new index) so the file stays a single safe batch.

IF COL_LENGTH(N'dbo.llm_configs', N'max_output_tokens') IS NULL
    EXEC(N'ALTER TABLE llm_configs ADD max_output_tokens INT NULL;');
