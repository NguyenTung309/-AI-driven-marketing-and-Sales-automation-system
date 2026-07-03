-- 0048: gắn session_id vào ledger để tính chi phí thực per-run điều phối. NULL cho gọi LLM ngoài run.
IF COL_LENGTH('dbo.claude_cost_ledger', 'session_id') IS NULL
BEGIN
    ALTER TABLE dbo.claude_cost_ledger ADD session_id UNIQUEIDENTIFIER NULL;
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_claude_cost_ledger_session_id' AND object_id = OBJECT_ID(N'dbo.claude_cost_ledger'))
    CREATE INDEX IX_claude_cost_ledger_session_id ON dbo.claude_cost_ledger (session_id);
