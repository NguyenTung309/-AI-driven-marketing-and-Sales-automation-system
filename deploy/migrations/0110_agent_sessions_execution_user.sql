-- 0110: Persist the principal whose current permissions authorize an orchestration execution.
-- user_id remains immutable creator/audit attribution; legacy rows without a creator fail closed at execution time.
IF OBJECT_ID(N'dbo.agent_sessions', N'U') IS NULL
    THROW 51000, 'dbo.agent_sessions is missing; cannot add execution_user_id.', 1;

IF COL_LENGTH(N'dbo.agent_sessions', N'execution_user_id') IS NULL
    ALTER TABLE dbo.agent_sessions ADD execution_user_id UNIQUEIDENTIFIER NULL;

DECLARE @executionUserSql nvarchar(max);
SET @executionUserSql = N'UPDATE dbo.agent_sessions SET execution_user_id = user_id WHERE execution_user_id IS NULL AND user_id IS NOT NULL;';
EXEC(@executionUserSql);
