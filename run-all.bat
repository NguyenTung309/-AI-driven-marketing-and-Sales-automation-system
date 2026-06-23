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
    echo Would run: dotnet restore Clawbot.sln
    echo Would run: dotnet build Clawbot.sln --no-restore
    echo Would run: npm ci in src\frontend\clawbot-web when node_modules is missing
    echo Would start AgentService with ASPNETCORE_URLS=http://localhost:15875
    echo Would start API with ASPNETCORE_URLS=http://localhost:15874, AgentService__Url=http://localhost:15875, and shared Jwt__SigningKey
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
start "ClawBot AgentService :15875" cmd /k "cd /d ""%ROOT%"" && set ASPNETCORE_ENVIRONMENT=Development&& set ASPNETCORE_URLS=http://localhost:15875&& dotnet run --project ""%ROOT%src\agents\Clawbot.AgentService\Clawbot.AgentService.csproj"" --no-launch-profile"
timeout /t 2 /nobreak >nul

start "ClawBot API :15874" cmd /k "cd /d ""%ROOT%"" && set ASPNETCORE_ENVIRONMENT=Development&& set ASPNETCORE_URLS=http://localhost:15874&& set AgentService__Url=http://localhost:15875&& set Jwt__SigningKey=%JWT_SIGNING_KEY%&& dotnet run --project ""%ROOT%src\api\Clawbot.Api\Clawbot.Api.csproj"" --no-launch-profile"
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
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -h -1 -W -Q "SET NOCOUNT ON; SELECT CONCAT(CASE WHEN OBJECT_ID(N'dbo.tenants', N'U') IS NULL THEN 0 ELSE 1 END, '|', CASE WHEN OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NULL THEN 0 ELSE 1 END, '|', CASE WHEN COL_LENGTH(N'dbo.tenants', N'widget_greeting') IS NULL THEN 0 ELSE 1 END, '|', CASE WHEN OBJECT_ID(N'dbo.messages', N'U') IS NULL THEN 0 ELSE 1 END, '|', CASE WHEN OBJECT_ID(N'dbo.experiment_events', N'U') IS NULL OR OBJECT_ID(N'dbo.competitor_posts', N'U') IS NULL OR COL_LENGTH(N'dbo.generated_documents', N'expires_at') IS NULL OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_messages_external_id' AND object_id = OBJECT_ID(N'dbo.messages')) THEN 0 ELSE 1 END, '|', CASE WHEN COL_LENGTH(N'dbo.users', N'phone_number') IS NULL THEN 0 ELSE 1 END, '|', CASE WHEN COL_LENGTH(N'dbo.conversations', N'last_message_at') IS NULL THEN 0 ELSE 1 END, '|', CASE WHEN COL_LENGTH(N'dbo.agents', N'llm_config_id') IS NULL THEN 0 ELSE 1 END, '|', CASE WHEN COL_LENGTH(N'dbo.agent_sessions', N'requires_approval') IS NULL OR COL_LENGTH(N'dbo.agent_sessions', N'replan_count') IS NULL OR COL_LENGTH(N'dbo.agent_sessions', N'row_version') IS NULL THEN 0 ELSE 1 END, '|', CASE WHEN COL_LENGTH(N'dbo.tenants', N'require_orchestration_approval') IS NULL THEN 0 ELSE 1 END, '|', CASE WHEN NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_sessions_tenant_status_started_at' AND object_id = OBJECT_ID(N'dbo.agent_sessions')) THEN 0 ELSE 1 END)" > "%SCHEMA_CHECK%" 2>nul
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
set /p HAS_SCHEMA=<"%SCHEMA_CHECK%"
del "%SCHEMA_CHECK%" >nul 2>nul
for /f "tokens=1,2,3,4,5,6,7,8,9,10,11 delims=|" %%A in ("%HAS_SCHEMA%") do (
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
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF COL_LENGTH(N'dbo.users', N'phone_number') IS NULL ALTER TABLE dbo.users ADD phone_number NVARCHAR(MAX); IF COL_LENGTH(N'dbo.conversations', N'last_message_at') IS NULL ALTER TABLE dbo.conversations ADD last_message_at DATETIMEOFFSET; IF COL_LENGTH(N'dbo.conversations', N'last_msg_at') IS NOT NULL AND COL_LENGTH(N'dbo.conversations', N'last_message_at') IS NOT NULL EXEC(N'UPDATE conversations SET last_message_at = last_msg_at WHERE last_message_at IS NULL AND last_msg_at IS NOT NULL;'); IF COL_LENGTH(N'dbo.conversations', N'last_message_at') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_conversations_tenant_id_status_last_message_at' AND object_id = OBJECT_ID(N'dbo.conversations')) EXEC(N'CREATE INDEX ix_conversations_tenant_id_status_last_message_at ON conversations (tenant_id, status, last_message_at DESC);'); IF COL_LENGTH(N'dbo.agents', N'llm_config_id') IS NULL ALTER TABLE dbo.agents ADD llm_config_id UNIQUEIDENTIFIER NULL; IF COL_LENGTH(N'dbo.agents', N'llm_config_id') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_agents_llm_config_id' AND object_id = OBJECT_ID(N'dbo.agents')) EXEC(N'CREATE INDEX ix_agents_llm_config_id ON agents (llm_config_id);'); IF COL_LENGTH(N'dbo.agents', N'llm_config_id') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_agents_llm_configs_llm_config_id') EXEC(N'ALTER TABLE agents ADD CONSTRAINT fk_agents_llm_configs_llm_config_id FOREIGN KEY (llm_config_id) REFERENCES llm_configs (id) ON DELETE NO ACTION;'); IF COL_LENGTH(N'dbo.agent_sessions', N'requires_approval') IS NULL ALTER TABLE dbo.agent_sessions ADD requires_approval BIT NOT NULL CONSTRAINT DF_agent_sessions_requires_approval DEFAULT 0; IF COL_LENGTH(N'dbo.agent_sessions', N'replan_count') IS NULL ALTER TABLE dbo.agent_sessions ADD replan_count INT NOT NULL CONSTRAINT DF_agent_sessions_replan_count DEFAULT 0; IF COL_LENGTH(N'dbo.agent_sessions', N'row_version') IS NULL ALTER TABLE dbo.agent_sessions ADD row_version ROWVERSION; IF COL_LENGTH(N'dbo.tenants', N'require_orchestration_approval') IS NULL ALTER TABLE dbo.tenants ADD require_orchestration_approval BIT NOT NULL CONSTRAINT DF_tenants_require_orchestration_approval DEFAULT 0; IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_sessions_tenant_status_started_at' AND object_id = OBJECT_ID(N'dbo.agent_sessions')) EXEC(N'CREATE INDEX IX_agent_sessions_tenant_status_started_at ON agent_sessions (tenant_id, status, started_at);');"
exit /b %errorlevel%

:incomplete_schema
        echo [ERROR] Existing clawbot database is missing required tables or columns.
        echo This usually means an older or partial local schema was detected.
        echo For a clean local DB, run:
        echo docker compose --env-file deploy\.env -f deploy\docker-compose.yml down -v
        echo Then run run-all.bat again.
        exit /b 1

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
