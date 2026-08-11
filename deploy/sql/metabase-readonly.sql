:SETVAR METABASE_READONLY_PASSWORD "ChangeThisMetabaseReadonlyPassword!"

USE master;
GO

IF NOT EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = N'metabase_readonly')
BEGIN
    CREATE LOGIN metabase_readonly
        WITH PASSWORD = N'$(METABASE_READONLY_PASSWORD)',
        CHECK_POLICY = ON,
        CHECK_EXPIRATION = OFF;
END
GO

USE clawbot;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'metabase_readonly')
BEGIN
    CREATE USER metabase_readonly FOR LOGIN metabase_readonly;
END
GO

GRANT SELECT ON dbo.kpi_daily TO metabase_readonly;
GRANT SELECT ON dbo.kpi_forecast TO metabase_readonly;
GRANT SELECT ON dbo.agent_sessions TO metabase_readonly;
GRANT SELECT ON dbo.agent_traces TO metabase_readonly;
GO

