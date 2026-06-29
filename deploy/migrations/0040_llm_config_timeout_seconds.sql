-- 0040: LLM provider config — optional per-config request timeout.
-- Adds timeout_seconds to llm_configs; NULL → fall back to global Llm:HttpTimeoutSeconds (120).
-- Column-add only (no new index) so the file stays a single safe batch.

IF COL_LENGTH(N'dbo.llm_configs', N'timeout_seconds') IS NULL
    EXEC(N'ALTER TABLE llm_configs ADD timeout_seconds INT NULL;');
