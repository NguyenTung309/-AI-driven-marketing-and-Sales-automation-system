-- Cột đánh dấu dòng ledger có token/cost do hệ thống ước lượng cục bộ (provider không trả usage).
-- Dòng cũ đều là số provider trả về -> DEFAULT 0. Báo cáo tách riêng số ước lượng, không trộn làm số thật.
IF COL_LENGTH(N'dbo.claude_cost_ledger', N'is_estimated') IS NULL
    EXEC(N'ALTER TABLE claude_cost_ledger ADD is_estimated BIT NOT NULL CONSTRAINT DF_claude_cost_ledger_is_estimated DEFAULT 0;');
