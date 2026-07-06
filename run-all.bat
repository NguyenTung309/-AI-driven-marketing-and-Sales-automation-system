@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
set "ENV_FILE=%ROOT%deploy\.env"
set "ENV_EXAMPLE=%ROOT%deploy\.env.example"
set "COMPOSE_FILE=%ROOT%deploy\docker-compose.yml"
set "FRONTEND_DIR=%ROOT%src\frontend\clawbot-web"
set "MIGRATIONS_DIR=%ROOT%deploy\migrations"
set "MSSQL_SA_PASSWORD=Clawbot!2026"
set "JWT_SIGNING_KEY=dev-only-jwt-signing-key-change-before-staging-0123456789"
set "ENCRYPTION_BASE64_KEY=Y2xhd2JvdC1sb2NhbC1kZXYtYWVzLWtleS0zMmJ5dGU="
set "DRY_RUN=0"
set "RUN_SEEDS=0"
set "SEED_TENANT_SLUG=demo"

:parse_args
if "%~1"=="" goto args_done
if /i "%~1"=="--dry-run" set "DRY_RUN=1"
if /i "%~1"=="--seed" set "RUN_SEEDS=1"
if /i "%~1"=="--tenant" goto parse_tenant
shift
goto parse_args

:parse_tenant
shift
if "%~1"=="" (
    echo [ERROR] --tenant requires a tenant slug.
    exit /b 1
)
set "SEED_TENANT_SLUG=%~1"
shift
goto parse_args

:args_done

if "%DRY_RUN%"=="1" (
    echo [DRY-RUN] ClawBot one-click runner
    echo Root: %ROOT%
    echo Would copy deploy\.env.example to deploy\.env if missing.
    echo Would run: docker compose --env-file deploy\.env -f deploy\docker-compose.yml up -d sqlserver redis rabbitmq qdrant minio postgres metabase
    echo Would stop old app processes listening on ports 15873, 15874, 15875, 15876
    echo Would apply deploy\seed\*.sql for tenant %SEED_TENANT_SLUG% when --seed is passed.
    echo Would apply one-shot data patches from deploy\fix_contact_overwrite.sql, guarded by dbo.data_patches.
    echo Would run: dotnet restore Clawbot.sln
    echo Would run: dotnet build Clawbot.sln --no-restore
    echo Would run: npm ci in src\frontend\clawbot-web when node_modules is missing
    echo Would start AgentService with ASPNETCORE_URLS=http://localhost:15875 and shared Encryption__Base64Key
    echo Would start API with ASPNETCORE_URLS=http://localhost:15874, AgentService__Url=http://localhost:15875, shared Jwt__SigningKey, and shared Encryption__Base64Key
    echo Would start Gateway with ASPNETCORE_URLS=http://localhost:15873 and shared Jwt__SigningKey
    echo Would start frontend with npm run dev at http://localhost:15876
    exit /b 0
)

echo.
echo === ClawBot local one-click runner ===
echo Root: %ROOT%
echo.

call :setup_dotnet
if errorlevel 1 exit /b 1
call :require_command node "Node.js 20"
if errorlevel 1 exit /b 1
call :require_command npm "npm"
if errorlevel 1 exit /b 1
call :require_command docker "Docker Desktop"
if errorlevel 1 exit /b 1

if not exist "%ENV_FILE%" (
    if not exist "%ENV_EXAMPLE%" (
        echo [ERROR] Missing deploy\.env.example.
        exit /b 1
    )
    echo [INFO] Creating deploy\.env from deploy\.env.example
    copy /Y "%ENV_EXAMPLE%" "%ENV_FILE%" >nul
)

call :read_env_value MSSQL_SA_PASSWORD

echo [INFO] Checking Docker daemon...
docker info >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Docker is installed but the daemon is not reachable.
    echo Open Docker Desktop, wait until it is running, then run this file again.
    exit /b 1
)

call :stop_app_ports
if errorlevel 1 exit /b 1

echo [INFO] Starting infrastructure containers...
docker compose --env-file "%ENV_FILE%" -f "%COMPOSE_FILE%" up -d sqlserver redis rabbitmq qdrant minio postgres metabase
if errorlevel 1 exit /b 1

call :detect_sqlcmd
if errorlevel 1 exit /b 1

call :wait_for_sqlserver
if errorlevel 1 exit /b 1

call :ensure_database
if errorlevel 1 exit /b 1

call :apply_migrations_if_needed
if errorlevel 1 exit /b 1

call :apply_data_patches
if errorlevel 1 exit /b 1

call :ensure_seed_tenant
if errorlevel 1 exit /b 1

call :apply_seeds_if_requested
if errorlevel 1 exit /b 1

echo [INFO] Restoring .NET packages...
dotnet restore "%ROOT%Clawbot.sln"
if errorlevel 1 exit /b 1

echo [INFO] Building solution...
dotnet build "%ROOT%Clawbot.sln" --no-restore
if errorlevel 1 exit /b 1

if not exist "%FRONTEND_DIR%\node_modules" (
    echo [INFO] Installing frontend dependencies with npm ci...
    pushd "%FRONTEND_DIR%" >nul
    npm ci
    if errorlevel 1 (
        popd >nul
        exit /b 1
    )
    popd >nul
)

echo [INFO] Opening service windows...
start "ClawBot AgentService :15875" cmd /k "cd /d ""%ROOT%"" && set ASPNETCORE_ENVIRONMENT=Development&& set ASPNETCORE_URLS=http://localhost:15875&& set Encryption__Base64Key=%ENCRYPTION_BASE64_KEY%&& dotnet run --project ""%ROOT%src\agents\Clawbot.AgentService\Clawbot.AgentService.csproj"" --no-launch-profile"
timeout /t 2 /nobreak >nul

start "ClawBot API :15874" cmd /k "cd /d ""%ROOT%"" && set ASPNETCORE_ENVIRONMENT=Development&& set ASPNETCORE_URLS=http://localhost:15874&& set AgentService__Url=http://localhost:15875&& set Jwt__SigningKey=%JWT_SIGNING_KEY%&& set Encryption__Base64Key=%ENCRYPTION_BASE64_KEY%&& dotnet run --project ""%ROOT%src\api\Clawbot.Api\Clawbot.Api.csproj"" --no-launch-profile"
timeout /t 2 /nobreak >nul

start "ClawBot Gateway :15873" cmd /k "cd /d ""%ROOT%"" && set ASPNETCORE_ENVIRONMENT=Development&& set ASPNETCORE_URLS=http://localhost:15873&& set Jwt__SigningKey=%JWT_SIGNING_KEY%&& dotnet run --project ""%ROOT%src\gateway\Clawbot.Gateway\Clawbot.Gateway.csproj"" --no-launch-profile"
timeout /t 2 /nobreak >nul

start "ClawBot Web :15876" cmd /k "cd /d ""%FRONTEND_DIR%"" && npm run dev -- --host 0.0.0.0 --port 15876"

echo.
echo [OK] ClawBot is starting.
echo Frontend: http://localhost:15876
echo Gateway:  http://localhost:15873
echo API:      http://localhost:15874
echo Swagger:  http://localhost:15874/swagger
echo.
echo Keep the opened terminal windows running. Close them to stop app services.
exit /b 0

:require_command
where %~1 >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Missing %~2. Install it, open a new terminal, then run run-all.bat again.
    exit /b 1
)
exit /b 0

:stop_app_ports
echo [INFO] Releasing old app ports if they are already in use...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ports = @(15873, 15874, 15875, 15876); $listeners = @(Get-NetTCPConnection -LocalPort $ports -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique); foreach ($listenerPid in $listeners) { if (-not $listenerPid -or $listenerPid -eq $PID) { continue }; $proc = Get-Process -Id $listenerPid -ErrorAction SilentlyContinue; if ($null -eq $proc) { continue }; Write-Host ('[INFO] Stopping PID {0} ({1}) listening on an app port.' -f $listenerPid, $proc.ProcessName); Stop-Process -Id $listenerPid -Force -ErrorAction SilentlyContinue }; $remaining = @(); for ($i = 0; $i -lt 20; $i++) { Start-Sleep -Milliseconds 500; $remaining = @(Get-NetTCPConnection -LocalPort $ports -State Listen -ErrorAction SilentlyContinue); if (-not $remaining) { exit 0 } }; $remaining | ForEach-Object { Write-Host ('[ERROR] Port {0} is still used by PID {1}.' -f $_.LocalPort, $_.OwningProcess) }; exit 1"
if errorlevel 1 (
    echo [ERROR] Could not release one or more app ports. Close the listed process and run again.
    exit /b 1
)
exit /b 0

:setup_dotnet
if exist "%ROOT%.dotnet\dotnet.exe" (
    set "DOTNET_ROOT=%ROOT%.dotnet"
    set "PATH=%ROOT%.dotnet;%PATH%"
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Missing .NET SDK 8. Install it, open a new terminal, then run run-all.bat again.
    exit /b 1
)

dotnet --list-sdks | findstr /b "8." >nul 2>nul
if errorlevel 1 (
    echo [ERROR] .NET SDK 8 was not found. The dotnet runtime alone is not enough.
    echo Install SDK 8.0.418, or run:
    echo powershell -NoProfile -ExecutionPolicy Bypass -File dotnet-install.ps1 -Version 8.0.418 -InstallDir .\.dotnet
    exit /b 1
)
exit /b 0

:read_env_value
for /f "usebackq tokens=1,* delims==" %%A in ("%ENV_FILE%") do (
    if /i "%%A"=="%~1" set "%~1=%%B"
)
exit /b 0

:detect_sqlcmd
docker exec clawbot-sqlserver test -x /opt/mssql-tools18/bin/sqlcmd >nul 2>nul
if not errorlevel 1 (
    set "SQLCMD=/opt/mssql-tools18/bin/sqlcmd"
    exit /b 0
)

docker exec clawbot-sqlserver test -x /opt/mssql-tools/bin/sqlcmd >nul 2>nul
if not errorlevel 1 (
    set "SQLCMD=/opt/mssql-tools/bin/sqlcmd"
    exit /b 0
)

echo [ERROR] sqlcmd was not found inside the SQL Server container.
exit /b 1

:wait_for_sqlserver
echo [INFO] Waiting for SQL Server...
for /l %%I in (1,1,60) do (
    docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -Q "SELECT 1" >nul 2>nul
    if not errorlevel 1 exit /b 0
    timeout /t 2 /nobreak >nul
)
echo [ERROR] SQL Server did not become ready in time.
exit /b 1

:ensure_database
echo [INFO] Ensuring database clawbot exists...
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -Q "IF DB_ID(N'clawbot') IS NULL CREATE DATABASE clawbot;" -b
exit /b %errorlevel%

:apply_migrations_if_needed
set "SCHEMA_CHECK=%TEMP%\clawbot_schema_check.txt"
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -h -1 -W -Q "SET NOCOUNT ON; SELECT CONCAT(CASE WHEN OBJECT_ID(N'dbo.tenants', N'U') IS NULL THEN 0 ELSE 1 END, '|', CASE WHEN OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NULL THEN 0 ELSE 1 END, '|', CASE WHEN COL_LENGTH(N'dbo.tenants', N'widget_greeting') IS NULL THEN 0 ELSE 1 END, '|', CASE WHEN OBJECT_ID(N'dbo.messages', N'U') IS NULL THEN 0 ELSE 1 END, '|', CASE WHEN OBJECT_ID(N'dbo.experiment_events', N'U') IS NULL OR OBJECT_ID(N'dbo.competitor_posts', N'U') IS NULL OR COL_LENGTH(N'dbo.generated_documents', N'expires_at') IS NULL OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_messages_external_id' AND object_id = OBJECT_ID(N'dbo.messages')) THEN 0 ELSE 1 END, '|', CASE WHEN COL_LENGTH(N'dbo.users', N'phone_number') IS NULL THEN 0 ELSE 1 END, '|', CASE WHEN COL_LENGTH(N'dbo.conversations', N'last_message_at') IS NULL THEN 0 ELSE 1 END, '|', CASE WHEN COL_LENGTH(N'dbo.agents', N'llm_config_id') IS NULL THEN 0 ELSE 1 END, '|', CASE WHEN COL_LENGTH(N'dbo.agent_sessions', N'requires_approval') IS NULL OR COL_LENGTH(N'dbo.agent_sessions', N'replan_count') IS NULL OR COL_LENGTH(N'dbo.agent_sessions', N'row_version') IS NULL THEN 0 ELSE 1 END, '|', CASE WHEN COL_LENGTH(N'dbo.tenants', N'require_orchestration_approval') IS NULL THEN 0 ELSE 1 END, '|', CASE WHEN NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_sessions_tenant_status_started_at' AND object_id = OBJECT_ID(N'dbo.agent_sessions')) THEN 0 ELSE 1 END, '|', CASE WHEN COL_LENGTH(N'dbo.pancake_configs', N'auth_mode') IS NULL OR COL_LENGTH(N'dbo.pancake_configs', N'base_url') IS NULL OR COL_LENGTH(N'dbo.pancake_configs', N'send_path_template') IS NULL OR COL_LENGTH(N'dbo.pancake_configs', N'signature_algo') IS NULL OR COL_LENGTH(N'dbo.pancake_configs', N'signature_encoding') IS NULL OR COL_LENGTH(N'dbo.pancake_configs', N'signature_header') IS NULL THEN 0 ELSE 1 END)" > "%SCHEMA_CHECK%" 2>nul
if errorlevel 1 (
    echo [ERROR] Could not inspect clawbot schema.
    exit /b 1
)

set "HAS_SCHEMA=0"
set "HAS_IDENTITY_SCHEMA=0"
set "HAS_LATEST_SCHEMA=0"
set "HAS_CORE_TABLES=0"
set "HAS_RECENT_MIGRATIONS=0"
set "HAS_IDENTITY_RUNTIME_COLUMNS=0"
set "HAS_CONVERSATION_RUNTIME_COLUMNS=0"
set "HAS_LLM_CONFIG_BINDING=0"
set "HAS_ORCHESTRATION_COLUMNS=0"
set "HAS_ORCHESTRATION_TENANT=0"
set "HAS_ORCHESTRATION_INDEX=0"
set "HAS_PANCAKE_CONFIG_RUNTIME_COLUMNS=0"
set /p HAS_SCHEMA=<"%SCHEMA_CHECK%"
del "%SCHEMA_CHECK%" >nul 2>nul
for /f "tokens=1,2,3,4,5,6,7,8,9,10,11,12 delims=|" %%A in ("%HAS_SCHEMA%") do (
    set "HAS_SCHEMA=%%A"
    set "HAS_IDENTITY_SCHEMA=%%B"
    set "HAS_LATEST_SCHEMA=%%C"
    set "HAS_CORE_TABLES=%%D"
    set "HAS_RECENT_MIGRATIONS=%%E"
    set "HAS_IDENTITY_RUNTIME_COLUMNS=%%F"
    set "HAS_CONVERSATION_RUNTIME_COLUMNS=%%G"
    set "HAS_LLM_CONFIG_BINDING=%%H"
    set "HAS_ORCHESTRATION_COLUMNS=%%I"
    set "HAS_ORCHESTRATION_TENANT=%%J"
    set "HAS_ORCHESTRATION_INDEX=%%K"
    set "HAS_PANCAKE_CONFIG_RUNTIME_COLUMNS=%%L"
)

if "%HAS_SCHEMA%"=="1" (
    if not "%HAS_IDENTITY_SCHEMA%"=="1" goto incomplete_schema
    if not "%HAS_LATEST_SCHEMA%"=="1" goto incomplete_schema
    if not "%HAS_CORE_TABLES%"=="1" goto incomplete_schema
    if not "%HAS_RECENT_MIGRATIONS%"=="1" goto incomplete_schema
    call :repair_runtime_columns
    if errorlevel 1 exit /b 1
    echo [INFO] Existing schema detected; skipping SQL migration replay.
    echo [INFO] For a clean local DB, run: docker compose --env-file deploy\.env -f deploy\docker-compose.yml down -v
    exit /b 0
)

goto replay_migrations

:repair_runtime_columns
echo [INFO] Repairing runtime columns on existing schema...
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF OBJECT_ID(N'dbo.inboxes', N'U') IS NULL AND OBJECT_ID(N'dbo.tenants', N'U') IS NOT NULL CREATE TABLE dbo.inboxes (id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_inboxes PRIMARY KEY DEFAULT NEWID(), tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id), name NVARCHAR(256) NOT NULL, platform NVARCHAR(32) NOT NULL, external_page_id NVARCHAR(128) NOT NULL, avatar_url NVARCHAR(512) NULL, encrypted_access_token NVARCHAR(1024) NULL, is_active BIT NOT NULL DEFAULT 1, created_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(), updated_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(), deleted_at DATETIMEOFFSET NULL); IF OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.inboxes', N'encrypted_access_token') IS NULL ALTER TABLE dbo.inboxes ADD encrypted_access_token NVARCHAR(1024) NULL; IF OBJECT_ID(N'dbo.channel_tokens', N'U') IS NULL AND OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL CREATE TABLE dbo.channel_tokens (inbox_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_channel_tokens PRIMARY KEY REFERENCES dbo.inboxes(id), access_token_encrypted NVARCHAR(MAX) NOT NULL, refresh_token_encrypted NVARCHAR(MAX) NULL, webhook_secret_encrypted NVARCHAR(MAX) NOT NULL, token_expires_at DATETIMEOFFSET NULL, is_active BIT NOT NULL DEFAULT 1, created_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(), updated_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME()); IF OBJECT_ID(N'dbo.inbox_members', N'U') IS NULL AND OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.users', N'U') IS NOT NULL CREATE TABLE dbo.inbox_members (inbox_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.inboxes(id), agent_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.users(id), tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id), CONSTRAINT PK_inbox_members PRIMARY KEY (inbox_id, agent_id)); IF OBJECT_ID(N'dbo.conversation_read_state', N'U') IS NULL AND OBJECT_ID(N'dbo.users', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL CREATE TABLE dbo.conversation_read_state (user_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.users(id), conversation_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.conversations(id), last_read_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(), CONSTRAINT PK_conversation_read_state PRIMARY KEY (user_id, conversation_id)); IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.conversations', N'inbox_id') IS NULL ALTER TABLE dbo.conversations ADD inbox_id UNIQUEIDENTIFIER NULL REFERENCES dbo.inboxes(id); IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.conversations', N'row_version') IS NULL ALTER TABLE dbo.conversations ADD row_version ROWVERSION; IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.conversations', N'snoozed_until') IS NULL ALTER TABLE dbo.conversations ADD snoozed_until DATETIMEOFFSET NULL; IF OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_inboxes_external' AND object_id = OBJECT_ID(N'dbo.inboxes')) CREATE INDEX ix_inboxes_external ON dbo.inboxes (tenant_id, platform, external_page_id) WHERE is_active = 1; IF OBJECT_ID(N'dbo.conversation_read_state', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_convread_conv' AND object_id = OBJECT_ID(N'dbo.conversation_read_state')) CREATE INDEX ix_convread_conv ON dbo.conversation_read_state (conversation_id); IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.conversations', N'ai_auto_reply_enabled') IS NULL ALTER TABLE dbo.conversations ADD ai_auto_reply_enabled BIT NOT NULL CONSTRAINT DF_conversations_ai_auto_reply_enabled DEFAULT 1;"
if errorlevel 1 exit /b 1
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF COL_LENGTH(N'dbo.llm_configs', N'timeout_seconds') IS NULL ALTER TABLE dbo.llm_configs ADD timeout_seconds INT NULL; IF COL_LENGTH(N'dbo.llm_configs', N'max_output_tokens') IS NULL ALTER TABLE dbo.llm_configs ADD max_output_tokens INT NULL; IF COL_LENGTH(N'dbo.agents', N'llm_config_id') IS NULL ALTER TABLE dbo.agents ADD llm_config_id UNIQUEIDENTIFIER NULL; IF COL_LENGTH(N'dbo.agents', N'llm_config_id') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_agents_llm_config_id' AND object_id = OBJECT_ID(N'dbo.agents')) EXEC(N'CREATE INDEX ix_agents_llm_config_id ON agents (llm_config_id);'); IF COL_LENGTH(N'dbo.agents', N'llm_config_id') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_agents_llm_configs_llm_config_id') EXEC(N'ALTER TABLE agents ADD CONSTRAINT fk_agents_llm_configs_llm_config_id FOREIGN KEY (llm_config_id) REFERENCES llm_configs (id) ON DELETE NO ACTION;'); IF COL_LENGTH(N'dbo.agent_sessions', N'requires_approval') IS NULL ALTER TABLE dbo.agent_sessions ADD requires_approval BIT NOT NULL CONSTRAINT DF_agent_sessions_requires_approval DEFAULT 0; IF COL_LENGTH(N'dbo.agent_sessions', N'replan_count') IS NULL ALTER TABLE dbo.agent_sessions ADD replan_count INT NOT NULL CONSTRAINT DF_agent_sessions_replan_count DEFAULT 0; IF COL_LENGTH(N'dbo.agent_sessions', N'row_version') IS NULL ALTER TABLE dbo.agent_sessions ADD row_version ROWVERSION; IF COL_LENGTH(N'dbo.agent_sessions', N'archived_at') IS NULL ALTER TABLE dbo.agent_sessions ADD archived_at DATETIMEOFFSET NULL; IF COL_LENGTH(N'dbo.tenants', N'require_orchestration_approval') IS NULL ALTER TABLE dbo.tenants ADD require_orchestration_approval BIT NOT NULL CONSTRAINT DF_tenants_require_orchestration_approval DEFAULT 0; IF COL_LENGTH(N'dbo.pancake_configs', N'base_url') IS NULL ALTER TABLE dbo.pancake_configs ADD base_url NVARCHAR(256) NOT NULL CONSTRAINT DF_pancake_configs_base_url DEFAULT N'https://pancake.vn/api/v1'; IF COL_LENGTH(N'dbo.pancake_configs', N'signature_header') IS NULL ALTER TABLE dbo.pancake_configs ADD signature_header NVARCHAR(64) NOT NULL CONSTRAINT DF_pancake_configs_signature_header DEFAULT N'x-pancake-signature'; IF COL_LENGTH(N'dbo.pancake_configs', N'signature_algo') IS NULL ALTER TABLE dbo.pancake_configs ADD signature_algo NVARCHAR(32) NOT NULL CONSTRAINT DF_pancake_configs_signature_algo DEFAULT N'hmac-sha256'; IF COL_LENGTH(N'dbo.pancake_configs', N'signature_encoding') IS NULL ALTER TABLE dbo.pancake_configs ADD signature_encoding NVARCHAR(16) NOT NULL CONSTRAINT DF_pancake_configs_signature_encoding DEFAULT N'hex'; IF COL_LENGTH(N'dbo.pancake_configs', N'send_path_template') IS NULL ALTER TABLE dbo.pancake_configs ADD send_path_template NVARCHAR(512) NOT NULL CONSTRAINT DF_pancake_configs_send_path_template DEFAULT N'/pages/{page_id}/conversations/{thread_id}/messages'; IF COL_LENGTH(N'dbo.pancake_configs', N'auth_mode') IS NULL ALTER TABLE dbo.pancake_configs ADD auth_mode NVARCHAR(16) NOT NULL CONSTRAINT DF_pancake_configs_auth_mode DEFAULT N'query'; IF COL_LENGTH(N'dbo.agent_definitions', N'kb_module_code') IS NULL ALTER TABLE dbo.agent_definitions ADD kb_module_code NVARCHAR(64) NULL; IF OBJECT_ID(N'dbo.embedding_configs', N'U') IS NULL CREATE TABLE dbo.embedding_configs (id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id) ON DELETE CASCADE, provider NVARCHAR(32) NOT NULL, model_id NVARCHAR(128) NOT NULL, display_name NVARCHAR(128) NULL, api_key_encrypted NVARCHAR(MAX) NOT NULL, base_url NVARCHAR(512) NULL, dimension INT NOT NULL CONSTRAINT df_embedding_configs_dimension DEFAULT 1536, is_active BIT NOT NULL CONSTRAINT df_embedding_configs_is_active DEFAULT 1, created_at DATETIMEOFFSET NOT NULL, updated_at DATETIMEOFFSET NOT NULL); IF OBJECT_ID(N'dbo.embedding_configs', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_embedding_configs_tenant_id_is_active' AND object_id = OBJECT_ID(N'dbo.embedding_configs')) CREATE INDEX IX_embedding_configs_tenant_id_is_active ON dbo.embedding_configs (tenant_id, is_active); IF COL_LENGTH(N'dbo.users', N'pancake_access_token_encrypted') IS NULL ALTER TABLE dbo.users ADD pancake_access_token_encrypted NVARCHAR(2048) NULL; IF COL_LENGTH(N'dbo.users', N'pancake_access_token_updated_at') IS NULL ALTER TABLE dbo.users ADD pancake_access_token_updated_at DATETIMEOFFSET NULL; IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_sessions_tenant_status_started_at' AND object_id = OBJECT_ID(N'dbo.agent_sessions')) EXEC(N'CREATE INDEX IX_agent_sessions_tenant_status_started_at ON agent_sessions (tenant_id, status, started_at);'); IF COL_LENGTH(N'dbo.agent_sessions', N'archived_at') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_sessions_tenant_archived_started_at' AND object_id = OBJECT_ID(N'dbo.agent_sessions')) EXEC(N'CREATE INDEX IX_agent_sessions_tenant_archived_started_at ON agent_sessions (tenant_id, archived_at, started_at);');"
exit /b %errorlevel%

:incomplete_schema
        echo [ERROR] Existing clawbot database is missing required tables or columns.
        echo This usually means an older or partial local schema was detected.
        echo For a clean local DB, run:
        echo docker compose --env-file deploy\.env -f deploy\docker-compose.yml down -v
        echo Then run run-all.bat again.
        exit /b 1

:apply_data_patches
echo [INFO] Applying one-shot data patches, guarded by dbo.data_patches...
type "%ROOT%deploy\fix_contact_overwrite.sql" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b
if errorlevel 1 (
    echo [ERROR] Data patch failed: deploy\fix_contact_overwrite.sql
    exit /b 1
)
exit /b 0

:ensure_seed_tenant
if not "%RUN_SEEDS%"=="1" exit /b 0
echo [INFO] Ensuring seed tenant %SEED_TENANT_SLUG% exists...
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -v TenantSlug="%SEED_TENANT_SLUG%" -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF NOT EXISTS (SELECT 1 FROM tenants WHERE slug = N'$(TenantSlug)') INSERT INTO tenants (id, slug, display_name, plan_name, is_active, settings_json, created_at, updated_at) VALUES (NEWID(), N'$(TenantSlug)', N'$(TenantSlug) Tenant', N'free', 1, N'{}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());"
exit /b %errorlevel%

:apply_seeds_if_requested
if not "%RUN_SEEDS%"=="1" exit /b 0
echo [INFO] Applying SQL seeds from deploy\seed...
pushd "%ROOT%deploy\seed" >nul
for %%F in (*.sql) do (
    echo [SEED] %%F
    (echo SET QUOTED_IDENTIFIER ON;& echo SET ARITHABORT ON;& type "%%F") | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -v TenantSlug="%SEED_TENANT_SLUG%"
    if errorlevel 1 (
        popd >nul
        echo [ERROR] Seed failed: %%F
        exit /b 1
    )
)
popd >nul
exit /b 0

:replay_migrations
echo [INFO] Applying SQL migrations from deploy\migrations...
pushd "%MIGRATIONS_DIR%" >nul
for %%F in (*.sql) do (
    echo [SQL] %%F
    (echo SET QUOTED_IDENTIFIER ON;& echo SET ARITHABORT ON;& type "%%F") | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b
    if errorlevel 1 (
        popd >nul
        echo [ERROR] Migration failed: %%F
        exit /b 1
    )
)
popd >nul
exit /b 0
