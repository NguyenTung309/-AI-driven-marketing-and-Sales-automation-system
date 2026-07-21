@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "ROOT=%~dp0"
set "ENV_FILE=%ROOT%deploy\.env"
set "ENV_EXAMPLE=%ROOT%deploy\.env.example"
set "COMPOSE_FILE=%ROOT%deploy\docker-compose.yml"
set "FRONTEND_DIR=%ROOT%src\frontend\clawbot-web"
set "MIGRATIONS_DIR=%ROOT%deploy\migrations"
set "MIGRATION_BASELINE_NUMBER=67"
set "MSSQL_SA_PASSWORD=Clawbot!2026"
set "JWT_SIGNING_KEY=dev-only-jwt-signing-key-change-before-staging-0123456789"
REM Must match Encryption:Base64Key in appsettings.json (API + AgentService) — services started
REM outside run-all.bat fall back to appsettings, and secrets written under one key are
REM unreadable under the other (llm/embedding api keys, inbox/pancake tokens).
set "ENCRYPTION_BASE64_KEY=5o1CS1PahuiUsgwkAJgRSAz3TyEeUfhbp08UDakwNRE="
REM VAPID key DEV cho Web Push (thong bao khi dong tab). Production PHAI thay cap khac va
REM dua private key qua secret/env — key nay nam trong repo nen coi nhu da lo.
REM Thieu key = web push tu tat, feed + chuong + email van chay binh thuong.
set "WEBPUSH_PUBLIC_KEY=BF1bUp5ttGzYgykFyN0pkgzFcIxpgpKE2LNuxxrluVtTogFqMFPKR7wRX19iZArVxKOIUR_cgBAa7Tdpflg3hEI"
set "WEBPUSH_PRIVATE_KEY=6VgsNFEgaIp2GNC0sJLXNovnxYGy7DnOpF7azIUBNL8"
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
powershell -NoProfile -Command "if ($env:SEED_TENANT_SLUG -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,62}$') { exit 1 }"
if errorlevel 1 (
    echo [ERROR] --tenant only accepts 1-63 letters, numbers, dot, underscore, or hyphen.
    exit /b 1
)
shift
goto parse_args

:args_done

if "%DRY_RUN%"=="1" goto dry_run

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
call :read_env_value Meta__Graph__AppId
call :read_env_value Meta__Graph__AppSecret
call :read_env_value Meta__Graph__ConfigurationId
call :read_env_value Meta__Graph__AuthorizationMode
call :read_env_value Meta__Graph__WebhookVerifyToken
call :read_env_value Meta__Graph__RedirectUri
call :read_env_value Meta__Graph__FrontendReturnUrl
call :read_env_value Meta__Graph__ApiVersion
call :read_env_value Ads__Meta__Enabled
call :read_env_value Ads__Meta__WebhookSecret

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
docker compose --env-file "%ENV_FILE%" -f "%COMPOSE_FILE%" up -d sqlserver redis rabbitmq qdrant minio postgres metabase searxng
if errorlevel 1 exit /b 1

call :detect_sqlcmd
if errorlevel 1 exit /b 1

call :wait_for_sqlserver
if errorlevel 1 exit /b 1

call :ensure_database
if errorlevel 1 exit /b 1

call :wait_for_clawbot_db
if errorlevel 1 exit /b 1

call :apply_migrations_if_needed
if errorlevel 1 exit /b 1
call :apply_meta_migration
if errorlevel 1 exit /b 1

rem Final gate before services: tenant columns EF maps on every API/AgentService boot.
call :repair_tenant_runtime_columns
if errorlevel 1 exit /b 1
call :verify_tenant_runtime_columns
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
set "LAUNCH_DIR=%TEMP%\clawbot-run-all"
if not exist "%LAUNCH_DIR%" mkdir "%LAUNCH_DIR%" >nul 2>nul

> "%LAUNCH_DIR%\agent.cmd" (
    echo @echo off
    echo setlocal EnableExtensions DisableDelayedExpansion
    echo cd /d "%ROOT%"
    echo set "ASPNETCORE_ENVIRONMENT=Development"
    echo set "ASPNETCORE_URLS=http://localhost:15875"
    echo set "ConnectionStrings__SqlServer=Server=localhost,11433;Database=clawbot;User Id=sa;Password=%MSSQL_SA_PASSWORD%;TrustServerCertificate=True;MultipleActiveResultSets=true"
    echo set "Encryption__Base64Key=%ENCRYPTION_BASE64_KEY%"
    echo title ClawBot AgentService :15875
    echo echo [AgentService] starting...
    echo dotnet run --project "%ROOT%src\agents\Clawbot.AgentService\Clawbot.AgentService.csproj" --no-launch-profile
    echo echo [AgentService] exited with %%ERRORLEVEL%%
    echo pause
)
> "%LAUNCH_DIR%\api.cmd" (
    echo @echo off
    echo setlocal EnableExtensions DisableDelayedExpansion
    echo cd /d "%ROOT%"
    echo set "ASPNETCORE_ENVIRONMENT=Development"
    echo set "ASPNETCORE_URLS=http://localhost:15874"
    echo set "AgentService__Url=http://localhost:15875"
    echo set "ConnectionStrings__SqlServer=Server=localhost,11433;Database=clawbot;User Id=sa;Password=%MSSQL_SA_PASSWORD%;TrustServerCertificate=True;MultipleActiveResultSets=true"
    echo set "Jwt__SigningKey=%JWT_SIGNING_KEY%"
    echo set "Encryption__Base64Key=%ENCRYPTION_BASE64_KEY%"
    echo set "WebPush__PublicKey=%WEBPUSH_PUBLIC_KEY%"
    echo set "WebPush__PrivateKey=%WEBPUSH_PRIVATE_KEY%"
    echo title ClawBot API :15874
    echo echo [API] starting against localhost,11433...
    echo dotnet run --project "%ROOT%src\api\Clawbot.Api\Clawbot.Api.csproj" --no-launch-profile
    echo echo [API] exited with %%ERRORLEVEL%%
    echo pause
)
> "%LAUNCH_DIR%\gateway.cmd" (
    echo @echo off
    echo setlocal EnableExtensions DisableDelayedExpansion
    echo cd /d "%ROOT%"
    echo set "ASPNETCORE_ENVIRONMENT=Development"
    echo set "ASPNETCORE_URLS=http://localhost:15873"
    echo set "Jwt__SigningKey=%JWT_SIGNING_KEY%"
    echo title ClawBot Gateway :15873
    echo echo [Gateway] starting...
    echo dotnet run --project "%ROOT%src\gateway\Clawbot.Gateway\Clawbot.Gateway.csproj" --no-launch-profile
    echo echo [Gateway] exited with %%ERRORLEVEL%%
    echo pause
)
> "%LAUNCH_DIR%\web.cmd" (
    echo @echo off
    echo setlocal EnableExtensions DisableDelayedExpansion
    echo cd /d "%FRONTEND_DIR%"
    echo title ClawBot Web :15876
    echo echo [Web] starting...
    echo npm run dev -- --host 0.0.0.0 --port 15876
    echo echo [Web] exited with %%ERRORLEVEL%%
    echo pause
)

start "ClawBot AgentService :15875" cmd /k call "%LAUNCH_DIR%\agent.cmd"
ping -n 3 127.0.0.1 >nul
start "ClawBot API :15874" cmd /k call "%LAUNCH_DIR%\api.cmd"
ping -n 3 127.0.0.1 >nul
start "ClawBot Gateway :15873" cmd /k call "%LAUNCH_DIR%\gateway.cmd"
ping -n 3 127.0.0.1 >nul
start "ClawBot Web :15876" cmd /k call "%LAUNCH_DIR%\web.cmd"

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

:dry_run
echo [DRY-RUN] ClawBot one-click runner
echo Root: "%ROOT%"
echo Would copy deploy\.env.example to deploy\.env if missing.
echo Would run: docker compose --env-file deploy\.env -f deploy\docker-compose.yml up -d sqlserver redis rabbitmq qdrant minio postgres metabase searxng
echo Would stop old app processes listening on ports 15873, 15874, 15875, 15876
echo Would apply deploy\seed\*.sql for tenant %SEED_TENANT_SLUG% when --seed is passed.
echo Would create dbo.schema_migrations, baseline repaired migrations through %MIGRATION_BASELINE_NUMBER%, and apply every pending deploy\migrations\*.sql file.
echo Would apply one-shot data patches from deploy\fix_contact_overwrite.sql, guarded by dbo.data_patches.
echo Would run: dotnet restore Clawbot.sln
echo Would run: dotnet build Clawbot.sln --no-restore
echo Would run: npm ci in src\frontend\clawbot-web when node_modules is missing
echo Would start AgentService with ASPNETCORE_URLS=http://localhost:15875 and shared Encryption__Base64Key.
echo Would start API with ASPNETCORE_URLS=http://localhost:15874, AgentService__Url=http://localhost:15875, and shared Jwt__SigningKey/Encryption__Base64Key.
echo Would start Gateway with ASPNETCORE_URLS=http://localhost:15873 and shared Jwt__SigningKey.
echo Would start frontend with npm run dev at http://localhost:15876
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
    ping -n 3 127.0.0.1 >nul
)
echo [ERROR] SQL Server did not become ready in time.
exit /b 1

:ensure_database
echo [INFO] Ensuring database clawbot exists...
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -Q "IF DB_ID(N'clawbot') IS NULL CREATE DATABASE clawbot;" -b
exit /b %errorlevel%

:wait_for_clawbot_db
echo [INFO] Waiting for database clawbot to come online...
for /l %%I in (1,1,60) do (
    docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -Q "SELECT 1" -b >nul 2>nul
    if not errorlevel 1 exit /b 0
    ping -n 3 127.0.0.1 >nul
)
echo [ERROR] Database clawbot did not come online in time.
exit /b 1

:apply_migrations_if_needed
call :ensure_migration_ledger
if errorlevel 1 exit /b 1
call :detect_migration_history
if errorlevel 1 exit /b 1
if "%HAS_MIGRATION_HISTORY%"=="1" (
    call :apply_pending_migrations
    if errorlevel 1 exit /b 1
    call :repair_runtime_columns
    if errorlevel 1 exit /b 1
    exit /b 0
)

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
    call :baseline_existing_migrations
    if errorlevel 1 exit /b 1
    echo [INFO] Existing schema repaired and migration history synchronized.
    exit /b 0
)

goto replay_migrations

:repair_tenant_runtime_columns
echo [INFO] Repairing tenant runtime columns required by EF...
if not exist "%ROOT%deploy\repair_tenant_runtime_columns.sql" (
    echo [ERROR] Missing deploy\repair_tenant_runtime_columns.sql
    exit /b 1
)
type "%ROOT%deploy\repair_tenant_runtime_columns.sql" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b
if errorlevel 1 (
    echo [ERROR] Tenant runtime column repair failed.
    exit /b 1
)
type "%ROOT%deploy\migrations\0076_content_publishing_policy_columns.sql" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b
if errorlevel 1 (
    echo [ERROR] Content publishing policy column repair failed.
    exit /b 1
)
type "%ROOT%deploy\migrations\0077_content_publishing_policy_constraints.sql" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b
if errorlevel 1 (
    echo [ERROR] Content publishing policy constraint repair failed.
    exit /b 1
)
type "%ROOT%deploy\migrations\0078_content_review_task_claim.sql" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b
if errorlevel 1 (
    echo [ERROR] Content review task claim repair failed.
    exit /b 1
)
type "%ROOT%deploy\migrations\0079_llm_config_supports_vision.sql" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b
if errorlevel 1 (
    echo [ERROR] LLM supports_vision repair failed.
    exit /b 1
)
type "%ROOT%deploy\migrations\0080_content_workflow_runtime_gate.sql" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b
if errorlevel 1 (
    echo [ERROR] Content workflow runtime gate repair failed.
    exit /b 1
)
exit /b 0

:verify_tenant_runtime_columns
echo [INFO] Verifying tenant runtime columns...
set "TENANT_COL_CHECK=%TEMP%\clawbot_tenant_cols_%RANDOM%.txt"
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -h -1 -W -Q "SET NOCOUNT ON; SELECT CONCAT(CASE WHEN COL_LENGTH(N'dbo.tenants', N'monthly_cost_cap_usd') IS NULL THEN 0 ELSE 1 END, CASE WHEN COL_LENGTH(N'dbo.tenants', N'require_content_review') IS NULL THEN 0 ELSE 1 END, CASE WHEN COL_LENGTH(N'dbo.tenants', N'require_chat_reply_approval') IS NULL THEN 0 ELSE 1 END, CASE WHEN COL_LENGTH(N'dbo.tenants', N'require_kb_human_review') IS NULL THEN 0 ELSE 1 END, CASE WHEN COL_LENGTH(N'dbo.tenants', N'lead_lost_after_days') IS NULL THEN 0 ELSE 1 END, CASE WHEN COL_LENGTH(N'dbo.tenants', N'auto_approve_lead_revenue') IS NULL THEN 0 ELSE 1 END, CASE WHEN COL_LENGTH(N'dbo.tenants', N'content_publishing_approval_policy') IS NULL THEN 0 ELSE 1 END, CASE WHEN COL_LENGTH(N'dbo.tenants', N'content_publishing_policy_version') IS NULL THEN 0 ELSE 1 END, CASE WHEN COL_LENGTH(N'dbo.tenants', N'content_publishing_policy_updated_at') IS NULL THEN 0 ELSE 1 END);" > "%TENANT_COL_CHECK%" 2>nul
if errorlevel 1 (
    del "%TENANT_COL_CHECK%" >nul 2>nul
    echo [ERROR] Could not verify tenant runtime columns.
    exit /b 1
)
set "TENANT_COL_FLAGS="
for /f "usebackq delims= " %%A in ("%TENANT_COL_CHECK%") do (
    if not defined TENANT_COL_FLAGS set "TENANT_COL_FLAGS=%%A"
)
del "%TENANT_COL_CHECK%" >nul 2>nul
if /i not "%TENANT_COL_FLAGS%"=="111111111" (
    echo [ERROR] dbo.tenants is missing one or more runtime columns required by EF.
    echo Expected existing tenant runtime columns plus content publishing policy, version, and updated timestamp.
    echo Verify flags were "%TENANT_COL_FLAGS%" - want 111111111.
    echo Re-run after fixing SQL Server, or reset local DB with:
    echo docker compose --env-file deploy\.env -f deploy\docker-compose.yml down -v
    exit /b 1
)
rem Lead revenue schema gate (0073/0075): table + unique active index must exist after repair.
echo [INFO] Verifying lead_revenues schema...
set "LEAD_REV_CHECK=%TEMP%\clawbot_lead_rev_%RANDOM%.txt"
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -h -1 -W -Q "SET NOCOUNT ON; SELECT CONCAT(CASE WHEN OBJECT_ID(N'dbo.lead_revenues', N'U') IS NULL THEN 0 ELSE 1 END, CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_lead_revenues_one_active' AND object_id = OBJECT_ID(N'dbo.lead_revenues')) THEN 1 ELSE 0 END, CASE WHEN OBJECT_ID(N'dbo.kpi_daily', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.kpi_daily', N'revenue') IS NOT NULL THEN 1 WHEN OBJECT_ID(N'dbo.kpi_daily', N'U') IS NULL THEN 1 ELSE 0 END);" > "%LEAD_REV_CHECK%" 2>nul
if errorlevel 1 (
    del "%LEAD_REV_CHECK%" >nul 2>nul
    echo [ERROR] Could not verify lead_revenues schema.
    exit /b 1
)
set "LEAD_REV_FLAGS="
for /f "usebackq delims= " %%A in ("%LEAD_REV_CHECK%") do (
    if not defined LEAD_REV_FLAGS set "LEAD_REV_FLAGS=%%A"
)
del "%LEAD_REV_CHECK%" >nul 2>nul
if /i not "%LEAD_REV_FLAGS%"=="111" (
    echo [ERROR] lead_revenues schema incomplete after repair.
    echo Expected table lead_revenues + UX_lead_revenues_one_active + kpi_daily.revenue when kpi_daily exists.
    echo Verify flags were "%LEAD_REV_FLAGS%" - want 111.
    exit /b 1
)
exit /b 0

:repair_runtime_columns
echo [INFO] Repairing runtime columns on existing schema...
call :repair_tenant_runtime_columns
if errorlevel 1 exit /b 1
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF OBJECT_ID(N'dbo.inboxes', N'U') IS NULL AND OBJECT_ID(N'dbo.tenants', N'U') IS NOT NULL CREATE TABLE dbo.inboxes (id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_inboxes PRIMARY KEY DEFAULT NEWID(), tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id), name NVARCHAR(256) NOT NULL, platform NVARCHAR(32) NOT NULL, external_page_id NVARCHAR(128) NOT NULL, avatar_url NVARCHAR(512) NULL, encrypted_access_token NVARCHAR(1024) NULL, is_active BIT NOT NULL DEFAULT 1, created_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(), updated_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(), deleted_at DATETIMEOFFSET NULL); IF OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.inboxes', N'encrypted_access_token') IS NULL ALTER TABLE dbo.inboxes ADD encrypted_access_token NVARCHAR(1024) NULL; IF OBJECT_ID(N'dbo.channel_tokens', N'U') IS NULL AND OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL CREATE TABLE dbo.channel_tokens (inbox_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_channel_tokens PRIMARY KEY REFERENCES dbo.inboxes(id), access_token_encrypted NVARCHAR(MAX) NOT NULL, refresh_token_encrypted NVARCHAR(MAX) NULL, webhook_secret_encrypted NVARCHAR(MAX) NOT NULL, token_expires_at DATETIMEOFFSET NULL, is_active BIT NOT NULL DEFAULT 1, created_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(), updated_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME()); IF OBJECT_ID(N'dbo.inbox_members', N'U') IS NULL AND OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.users', N'U') IS NOT NULL CREATE TABLE dbo.inbox_members (inbox_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.inboxes(id), agent_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.users(id), tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id), CONSTRAINT PK_inbox_members PRIMARY KEY (inbox_id, agent_id)); IF OBJECT_ID(N'dbo.conversation_read_state', N'U') IS NULL AND OBJECT_ID(N'dbo.users', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL CREATE TABLE dbo.conversation_read_state (user_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.users(id), conversation_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.conversations(id), last_read_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(), CONSTRAINT PK_conversation_read_state PRIMARY KEY (user_id, conversation_id)); IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.conversations', N'inbox_id') IS NULL ALTER TABLE dbo.conversations ADD inbox_id UNIQUEIDENTIFIER NULL REFERENCES dbo.inboxes(id); IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.conversations', N'row_version') IS NULL ALTER TABLE dbo.conversations ADD row_version ROWVERSION; IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.conversations', N'snoozed_until') IS NULL ALTER TABLE dbo.conversations ADD snoozed_until DATETIMEOFFSET NULL; IF OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_inboxes_external' AND object_id = OBJECT_ID(N'dbo.inboxes')) CREATE INDEX ix_inboxes_external ON dbo.inboxes (tenant_id, platform, external_page_id) WHERE is_active = 1; IF OBJECT_ID(N'dbo.conversation_read_state', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_convread_conv' AND object_id = OBJECT_ID(N'dbo.conversation_read_state')) CREATE INDEX ix_convread_conv ON dbo.conversation_read_state (conversation_id); IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.conversations', N'ai_auto_reply_enabled') IS NULL ALTER TABLE dbo.conversations ADD ai_auto_reply_enabled BIT NOT NULL CONSTRAINT DF_conversations_ai_auto_reply_enabled DEFAULT 1; IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.conversations', N'ai_auto_reply_resume_at') IS NULL ALTER TABLE dbo.conversations ADD ai_auto_reply_resume_at DATETIMEOFFSET NULL; IF OBJECT_ID(N'dbo.messages', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.messages', N'sender_display_name') IS NULL ALTER TABLE dbo.messages ADD sender_display_name NVARCHAR(256) NULL; IF OBJECT_ID(N'dbo.messages', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.messages', N'sender_avatar_url') IS NULL ALTER TABLE dbo.messages ADD sender_avatar_url NVARCHAR(512) NULL; IF OBJECT_ID(N'dbo.messages', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.messages', N'attachment_url') IS NULL ALTER TABLE dbo.messages ADD attachment_url NVARCHAR(2048) NULL; IF OBJECT_ID(N'dbo.contacts', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.contacts', N'avatar_url') IS NULL ALTER TABLE dbo.contacts ADD avatar_url NVARCHAR(512) NULL;"
if errorlevel 1 exit /b 1
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF COL_LENGTH(N'dbo.llm_configs', N'timeout_seconds') IS NULL ALTER TABLE dbo.llm_configs ADD timeout_seconds INT NULL; IF COL_LENGTH(N'dbo.llm_configs', N'max_output_tokens') IS NULL ALTER TABLE dbo.llm_configs ADD max_output_tokens INT NULL; IF COL_LENGTH(N'dbo.llm_configs', N'supports_vision') IS NULL ALTER TABLE dbo.llm_configs ADD supports_vision BIT NULL; IF COL_LENGTH(N'dbo.agents', N'llm_config_id') IS NULL ALTER TABLE dbo.agents ADD llm_config_id UNIQUEIDENTIFIER NULL; IF COL_LENGTH(N'dbo.agents', N'llm_config_id') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_agents_llm_config_id' AND object_id = OBJECT_ID(N'dbo.agents')) EXEC(N'CREATE INDEX ix_agents_llm_config_id ON agents (llm_config_id);'); IF COL_LENGTH(N'dbo.agents', N'llm_config_id') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_agents_llm_configs_llm_config_id') EXEC(N'ALTER TABLE agents ADD CONSTRAINT fk_agents_llm_configs_llm_config_id FOREIGN KEY (llm_config_id) REFERENCES llm_configs (id) ON DELETE NO ACTION;'); IF COL_LENGTH(N'dbo.agent_sessions', N'requires_approval') IS NULL ALTER TABLE dbo.agent_sessions ADD requires_approval BIT NOT NULL CONSTRAINT DF_agent_sessions_requires_approval DEFAULT 0; IF COL_LENGTH(N'dbo.agent_sessions', N'replan_count') IS NULL ALTER TABLE dbo.agent_sessions ADD replan_count INT NOT NULL CONSTRAINT DF_agent_sessions_replan_count DEFAULT 0; IF COL_LENGTH(N'dbo.agent_sessions', N'row_version') IS NULL ALTER TABLE dbo.agent_sessions ADD row_version ROWVERSION; IF COL_LENGTH(N'dbo.agent_sessions', N'archived_at') IS NULL ALTER TABLE dbo.agent_sessions ADD archived_at DATETIMEOFFSET NULL; IF COL_LENGTH(N'dbo.tenants', N'require_orchestration_approval') IS NULL ALTER TABLE dbo.tenants ADD require_orchestration_approval BIT NOT NULL CONSTRAINT DF_tenants_require_orchestration_approval DEFAULT 0; IF COL_LENGTH(N'dbo.processed_messages', N'tenant_id') IS NULL ALTER TABLE dbo.processed_messages ADD tenant_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_processed_messages_tenant_id DEFAULT '00000000-0000-0000-0000-000000000000'; IF COL_LENGTH(N'dbo.pancake_configs', N'channel') IS NOT NULL BEGIN DECLARE @pcuq nvarchar(200); SELECT @pcuq = name FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.pancake_configs') AND type = 'UQ'; IF @pcuq IS NOT NULL EXEC(N'ALTER TABLE pancake_configs DROP CONSTRAINT ' + @pcuq); DECLARE @pcdf nvarchar(200); SELECT @pcdf = dc.name FROM sys.default_constraints dc INNER JOIN sys.columns c ON c.default_object_id = dc.object_id WHERE dc.parent_object_id = OBJECT_ID(N'dbo.pancake_configs') AND c.name = N'channel'; IF @pcdf IS NOT NULL EXEC(N'ALTER TABLE pancake_configs DROP CONSTRAINT ' + @pcdf); EXEC(N'ALTER TABLE dbo.pancake_configs DROP COLUMN channel'); END; IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.pancake_configs') AND type = 'UQ') ALTER TABLE dbo.pancake_configs ADD CONSTRAINT UQ_pancake_configs_tenant_id UNIQUE (tenant_id); IF COL_LENGTH(N'dbo.pancake_configs', N'base_url') IS NULL ALTER TABLE dbo.pancake_configs ADD base_url NVARCHAR(256) NOT NULL CONSTRAINT DF_pancake_configs_base_url DEFAULT N'https://pancake.vn/api/v1'; IF COL_LENGTH(N'dbo.pancake_configs', N'signature_header') IS NULL ALTER TABLE dbo.pancake_configs ADD signature_header NVARCHAR(64) NOT NULL CONSTRAINT DF_pancake_configs_signature_header DEFAULT N'x-pancake-signature'; IF COL_LENGTH(N'dbo.pancake_configs', N'signature_algo') IS NULL ALTER TABLE dbo.pancake_configs ADD signature_algo NVARCHAR(32) NOT NULL CONSTRAINT DF_pancake_configs_signature_algo DEFAULT N'hmac-sha256'; IF COL_LENGTH(N'dbo.pancake_configs', N'signature_encoding') IS NULL ALTER TABLE dbo.pancake_configs ADD signature_encoding NVARCHAR(16) NOT NULL CONSTRAINT DF_pancake_configs_signature_encoding DEFAULT N'hex'; IF COL_LENGTH(N'dbo.pancake_configs', N'send_path_template') IS NULL ALTER TABLE dbo.pancake_configs ADD send_path_template NVARCHAR(512) NOT NULL CONSTRAINT DF_pancake_configs_send_path_template DEFAULT N'/pages/{page_id}/conversations/{thread_id}/messages'; IF COL_LENGTH(N'dbo.pancake_configs', N'auth_mode') IS NULL ALTER TABLE dbo.pancake_configs ADD auth_mode NVARCHAR(16) NOT NULL CONSTRAINT DF_pancake_configs_auth_mode DEFAULT N'query'; IF COL_LENGTH(N'dbo.agent_definitions', N'kb_module_code') IS NULL ALTER TABLE dbo.agent_definitions ADD kb_module_code NVARCHAR(64) NULL; IF OBJECT_ID(N'dbo.embedding_configs', N'U') IS NULL CREATE TABLE dbo.embedding_configs (id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id) ON DELETE CASCADE, provider NVARCHAR(32) NOT NULL, model_id NVARCHAR(128) NOT NULL, display_name NVARCHAR(128) NULL, api_key_encrypted NVARCHAR(MAX) NOT NULL, base_url NVARCHAR(512) NULL, dimension INT NOT NULL CONSTRAINT df_embedding_configs_dimension DEFAULT 1536, is_active BIT NOT NULL CONSTRAINT df_embedding_configs_is_active DEFAULT 1, created_at DATETIMEOFFSET NOT NULL, updated_at DATETIMEOFFSET NOT NULL); IF OBJECT_ID(N'dbo.embedding_configs', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_embedding_configs_tenant_id_is_active' AND object_id = OBJECT_ID(N'dbo.embedding_configs')) CREATE INDEX IX_embedding_configs_tenant_id_is_active ON dbo.embedding_configs (tenant_id, is_active); IF COL_LENGTH(N'dbo.users', N'pancake_access_token_encrypted') IS NULL ALTER TABLE dbo.users ADD pancake_access_token_encrypted NVARCHAR(2048) NULL; IF COL_LENGTH(N'dbo.users', N'pancake_access_token_updated_at') IS NULL ALTER TABLE dbo.users ADD pancake_access_token_updated_at DATETIMEOFFSET NULL; IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_sessions_tenant_status_started_at' AND object_id = OBJECT_ID(N'dbo.agent_sessions')) EXEC(N'CREATE INDEX IX_agent_sessions_tenant_status_started_at ON agent_sessions (tenant_id, status, started_at);'); IF COL_LENGTH(N'dbo.agent_sessions', N'archived_at') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_sessions_tenant_archived_started_at' AND object_id = OBJECT_ID(N'dbo.agent_sessions')) EXEC(N'CREATE INDEX IX_agent_sessions_tenant_archived_started_at ON agent_sessions (tenant_id, archived_at, started_at);'); IF COL_LENGTH(N'dbo.agent_schedules', N'trigger_type') IS NULL ALTER TABLE dbo.agent_schedules ADD trigger_type NVARCHAR(16) NOT NULL CONSTRAINT DF_agent_schedules_trigger_type DEFAULT N'cadence'; IF COL_LENGTH(N'dbo.agent_schedules', N'event_key') IS NULL ALTER TABLE dbo.agent_schedules ADD event_key NVARCHAR(64) NULL; IF COL_LENGTH(N'dbo.tenants', N'monthly_cost_cap_usd') IS NULL ALTER TABLE dbo.tenants ADD monthly_cost_cap_usd DECIMAL(12,2) NULL; IF COL_LENGTH(N'dbo.claude_cost_ledger', N'session_id') IS NULL ALTER TABLE dbo.claude_cost_ledger ADD session_id UNIQUEIDENTIFIER NULL; IF OBJECT_ID(N'dbo.claude_cost_ledger', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_claude_cost_ledger_session_id' AND object_id = OBJECT_ID(N'dbo.claude_cost_ledger')) EXEC(N'CREATE INDEX IX_claude_cost_ledger_session_id ON claude_cost_ledger (session_id);'); IF OBJECT_ID(N'dbo.skill_files', N'U') IS NULL AND OBJECT_ID(N'dbo.tenants', N'U') IS NOT NULL CREATE TABLE dbo.skill_files (id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_skill_files PRIMARY KEY DEFAULT NEWID(), tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id) ON DELETE CASCADE, name NVARCHAR(128) NOT NULL, description NVARCHAR(512) NULL, content_md NVARCHAR(MAX) NOT NULL, created_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(), updated_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(), deleted_at DATETIMEOFFSET NULL); IF OBJECT_ID(N'dbo.skill_files', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_skill_files_tenant_name' AND object_id = OBJECT_ID(N'dbo.skill_files')) EXEC(N'CREATE UNIQUE INDEX ix_skill_files_tenant_name ON dbo.skill_files (tenant_id, name) WHERE deleted_at IS NULL;');"
if errorlevel 1 exit /b 1
rem Review-gate (P1-P4) columns — lenh rieng vi dong tren da sat tran 8191 ky tu cua cmd.exe
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF COL_LENGTH(N'dbo.tenants', N'require_content_review') IS NULL ALTER TABLE dbo.tenants ADD require_content_review BIT NOT NULL CONSTRAINT DF_tenants_require_content_review DEFAULT 0; IF COL_LENGTH(N'dbo.content_items', N'created_by_agent_id') IS NULL ALTER TABLE dbo.content_items ADD created_by_agent_id UNIQUEIDENTIFIER NULL; IF COL_LENGTH(N'dbo.content_items', N'rejected_reason') IS NULL ALTER TABLE dbo.content_items ADD rejected_reason NVARCHAR(1024) NULL; IF COL_LENGTH(N'dbo.messages', N'status') IS NULL ALTER TABLE dbo.messages ADD status NVARCHAR(32) NOT NULL CONSTRAINT DF_messages_status DEFAULT N'sent'; IF COL_LENGTH(N'dbo.tenants', N'require_chat_reply_approval') IS NULL ALTER TABLE dbo.tenants ADD require_chat_reply_approval BIT NOT NULL CONSTRAINT DF_tenants_require_chat_reply_approval DEFAULT 0; IF COL_LENGTH(N'dbo.content_items', N'desired_publish_at') IS NULL ALTER TABLE dbo.content_items ADD desired_publish_at DATETIMEOFFSET NULL; IF COL_LENGTH(N'dbo.content_items', N'last_review_alert_at') IS NULL ALTER TABLE dbo.content_items ADD last_review_alert_at DATETIMEOFFSET NULL; IF COL_LENGTH(N'dbo.inboxes', N'sender_id') IS NULL ALTER TABLE dbo.inboxes ADD sender_id NVARCHAR(128) NULL;"
if errorlevel 1 exit /b 1
rem Engagement counts (0059_content_schedule_engagement) — lenh rieng, giu duoi tran 8191 ky tu cua cmd.exe
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF COL_LENGTH(N'dbo.content_schedule', N'like_count') IS NULL ALTER TABLE dbo.content_schedule ADD like_count INT NULL; IF COL_LENGTH(N'dbo.content_schedule', N'comment_count') IS NULL ALTER TABLE dbo.content_schedule ADD comment_count INT NULL; IF COL_LENGTH(N'dbo.content_schedule', N'engagement_synced_at') IS NULL ALTER TABLE dbo.content_schedule ADD engagement_synced_at DATETIMEOFFSET NULL;"
if errorlevel 1 exit /b 1
rem Last publish error on schedule (0069_content_schedule_last_error) — lenh rieng
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF COL_LENGTH(N'dbo.content_schedule', N'last_error') IS NULL ALTER TABLE dbo.content_schedule ADD last_error NVARCHAR(1024) NULL;"
if errorlevel 1 exit /b 1
rem System error logs (0070_system_logs) — lenh rieng, giu duoi tran 8191 ky tu cua cmd.exe
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF OBJECT_ID(N'dbo.system_logs', N'U') IS NULL BEGIN CREATE TABLE dbo.system_logs (id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_system_logs PRIMARY KEY, occurred_at DATETIMEOFFSET NOT NULL, level NVARCHAR(16) NOT NULL, source NVARCHAR(32) NOT NULL, category NVARCHAR(256) NULL, message NVARCHAR(2048) NOT NULL, exception NVARCHAR(MAX) NULL, status_code INT NULL, method NVARCHAR(10) NULL, path NVARCHAR(512) NULL, elapsed_ms FLOAT NULL, trace_id NVARCHAR(64) NULL, tenant_id UNIQUEIDENTIFIER NULL, user_id UNIQUEIDENTIFIER NULL, properties NVARCHAR(MAX) NULL); CREATE INDEX ix_system_logs_occurred ON dbo.system_logs(occurred_at DESC) INCLUDE (level, tenant_id); CREATE INDEX ix_system_logs_tenant ON dbo.system_logs(tenant_id, occurred_at DESC); CREATE INDEX ix_system_logs_trace ON dbo.system_logs(trace_id) WHERE trace_id IS NOT NULL; END"
if errorlevel 1 exit /b 1
rem Request stats hourly (0071_request_stats_hourly)
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF OBJECT_ID(N'dbo.request_stats_hourly', N'U') IS NULL BEGIN CREATE TABLE dbo.request_stats_hourly (id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_request_stats_hourly PRIMARY KEY, bucket_hour DATETIMEOFFSET NOT NULL, tenant_id UNIQUEIDENTIFIER NOT NULL, status_class NVARCHAR(8) NOT NULL, count BIGINT NOT NULL CONSTRAINT df_request_stats_hourly_count DEFAULT 0); CREATE UNIQUE INDEX ux_request_stats_hourly_bucket_tenant_class ON dbo.request_stats_hourly(bucket_hour, tenant_id, status_class); CREATE INDEX ix_request_stats_hourly_tenant_bucket ON dbo.request_stats_hourly(tenant_id, bucket_hour DESC); END"
if errorlevel 1 exit /b 1
rem Lead lifecycle + revenue KPI (0072/0073/0074)
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF COL_LENGTH(N'dbo.tenants', N'lead_lost_after_days') IS NULL ALTER TABLE dbo.tenants ADD lead_lost_after_days INT NOT NULL CONSTRAINT DF_tenants_lead_lost_after_days DEFAULT 60; IF COL_LENGTH(N'dbo.tenants', N'auto_approve_lead_revenue') IS NULL ALTER TABLE dbo.tenants ADD auto_approve_lead_revenue BIT NOT NULL CONSTRAINT DF_tenants_auto_approve_lead_revenue DEFAULT 0; IF OBJECT_ID(N'dbo.lead_revenues', N'U') IS NULL BEGIN CREATE TABLE dbo.lead_revenues (id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_lead_revenues PRIMARY KEY, tenant_id UNIQUEIDENTIFIER NOT NULL, lead_id UNIQUEIDENTIFIER NOT NULL, amount DECIMAL(18,2) NOT NULL, currency NVARCHAR(8) NOT NULL CONSTRAINT DF_lead_revenues_currency DEFAULT N'VND', source NVARCHAR(16) NOT NULL, status NVARCHAR(16) NOT NULL, evidence NVARCHAR(1000) NULL, proposed_by UNIQUEIDENTIFIER NULL, decided_by UNIQUEIDENTIFIER NULL, created_at DATETIMEOFFSET NOT NULL, decided_at DATETIMEOFFSET NULL); CREATE INDEX IX_lead_revenues_tenant_status ON dbo.lead_revenues (tenant_id, status, created_at DESC); CREATE INDEX IX_lead_revenues_lead ON dbo.lead_revenues (lead_id); END; IF OBJECT_ID(N'dbo.kpi_daily', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.kpi_daily', N'revenue') IS NULL ALTER TABLE dbo.kpi_daily ADD revenue DECIMAL(18,2) NULL;"
if errorlevel 1 exit /b 1
rem Lead revenue invariants (0075): FK + amount CHECK + one active pending/approved per lead
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF OBJECT_ID(N'dbo.lead_revenues', N'U') IS NOT NULL BEGIN IF OBJECT_ID(N'dbo.leads', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_lead_revenues_leads' AND parent_object_id = OBJECT_ID(N'dbo.lead_revenues')) ALTER TABLE dbo.lead_revenues WITH NOCHECK ADD CONSTRAINT FK_lead_revenues_leads FOREIGN KEY (lead_id) REFERENCES dbo.leads(id) ON DELETE CASCADE; IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_lead_revenues_amount' AND parent_object_id = OBJECT_ID(N'dbo.lead_revenues')) ALTER TABLE dbo.lead_revenues WITH NOCHECK ADD CONSTRAINT CK_lead_revenues_amount CHECK (amount > 0 AND amount <= 10000000000 AND currency = N'VND'); IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_lead_revenues_one_active' AND object_id = OBJECT_ID(N'dbo.lead_revenues')) CREATE UNIQUE INDEX UX_lead_revenues_one_active ON dbo.lead_revenues (lead_id) WHERE status IN (N'pending', N'approved'); END"
if errorlevel 1 exit /b 1
rem Notification preferences + web push + email fallback (0063/0064/0065) — lenh rieng
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF OBJECT_ID(N'dbo.notification_preferences', N'U') IS NULL CREATE TABLE dbo.notification_preferences (id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_notification_preferences PRIMARY KEY, tenant_id UNIQUEIDENTIFIER NOT NULL, user_id UNIQUEIDENTIFIER NOT NULL, type NVARCHAR(64) NOT NULL, in_app BIT NOT NULL CONSTRAINT DF_notification_preferences_in_app DEFAULT 1, push BIT NOT NULL CONSTRAINT DF_notification_preferences_push DEFAULT 1, email BIT NOT NULL CONSTRAINT DF_notification_preferences_email DEFAULT 0, updated_at DATETIMEOFFSET NOT NULL); IF OBJECT_ID(N'dbo.notification_preferences', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_notification_preferences' AND object_id = OBJECT_ID(N'dbo.notification_preferences')) CREATE UNIQUE INDEX UX_notification_preferences ON dbo.notification_preferences (tenant_id, user_id, type); IF OBJECT_ID(N'dbo.push_subscriptions', N'U') IS NULL CREATE TABLE dbo.push_subscriptions (id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_push_subscriptions PRIMARY KEY, tenant_id UNIQUEIDENTIFIER NOT NULL, user_id UNIQUEIDENTIFIER NOT NULL, endpoint NVARCHAR(512) NOT NULL, p256dh NVARCHAR(256) NOT NULL, auth NVARCHAR(128) NOT NULL, created_at DATETIMEOFFSET NOT NULL); IF OBJECT_ID(N'dbo.push_subscriptions', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_push_subscriptions_endpoint' AND object_id = OBJECT_ID(N'dbo.push_subscriptions')) CREATE UNIQUE INDEX UX_push_subscriptions_endpoint ON dbo.push_subscriptions (endpoint); IF COL_LENGTH(N'dbo.notifications', N'email_sent_at') IS NULL ALTER TABLE dbo.notifications ADD email_sent_at DATETIMEOFFSET NULL;"
if errorlevel 1 exit /b 1
rem Gom nhom thong bao (0061/0062) — lenh rieng, giu duoi tran 8191 ky tu cua cmd.exe
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF COL_LENGTH(N'dbo.notifications', N'group_key') IS NULL ALTER TABLE dbo.notifications ADD group_key NVARCHAR(128) NULL; IF COL_LENGTH(N'dbo.notifications', N'occurrence_count') IS NULL ALTER TABLE dbo.notifications ADD occurrence_count INT NOT NULL CONSTRAINT DF_notifications_occurrence_count DEFAULT 1; IF COL_LENGTH(N'dbo.notifications', N'last_occurred_at') IS NULL ALTER TABLE dbo.notifications ADD last_occurred_at DATETIMEOFFSET NULL;"
if errorlevel 1 exit /b 1
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF COL_LENGTH(N'dbo.notifications', N'group_key') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_notifications_group' AND object_id = OBJECT_ID(N'dbo.notifications')) EXEC(N'CREATE INDEX IX_notifications_group ON dbo.notifications (tenant_id, user_id, group_key, is_read);');"
if errorlevel 1 exit /b 1
rem Background jobs (0060_background_jobs) — lenh rieng, giu duoi tran 8191 ky tu cua cmd.exe
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF OBJECT_ID(N'dbo.background_jobs', N'U') IS NULL CREATE TABLE dbo.background_jobs (id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_background_jobs PRIMARY KEY, tenant_id UNIQUEIDENTIFIER NOT NULL, user_id UNIQUEIDENTIFIER NULL, type NVARCHAR(64) NOT NULL, title NVARCHAR(200) NOT NULL, status NVARCHAR(20) NOT NULL CONSTRAINT DF_background_jobs_status DEFAULT 'queued', progress INT NOT NULL CONSTRAINT DF_background_jobs_progress DEFAULT 0, progress_note NVARCHAR(200) NULL, payload_json NVARCHAR(MAX) NULL, result_link NVARCHAR(400) NULL, result_summary NVARCHAR(MAX) NULL, error NVARCHAR(1000) NULL, hangfire_job_id NVARCHAR(64) NULL, idempotency_key NVARCHAR(128) NULL, cancel_requested BIT NOT NULL CONSTRAINT DF_background_jobs_cancel_requested DEFAULT 0, created_at DATETIMEOFFSET NOT NULL, started_at DATETIMEOFFSET NULL, finished_at DATETIMEOFFSET NULL); IF OBJECT_ID(N'dbo.background_jobs', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_background_jobs_tenant_created' AND object_id = OBJECT_ID(N'dbo.background_jobs')) CREATE INDEX IX_background_jobs_tenant_created ON dbo.background_jobs (tenant_id, created_at DESC); IF OBJECT_ID(N'dbo.background_jobs', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_background_jobs_tenant_user_created' AND object_id = OBJECT_ID(N'dbo.background_jobs')) CREATE INDEX IX_background_jobs_tenant_user_created ON dbo.background_jobs (tenant_id, user_id, created_at DESC); IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_background_jobs_idempotency' AND object_id = OBJECT_ID(N'dbo.background_jobs')) EXEC(N'DROP INDEX UX_background_jobs_idempotency ON dbo.background_jobs;'); IF OBJECT_ID(N'dbo.background_jobs', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_background_jobs_idempotency' AND object_id = OBJECT_ID(N'dbo.background_jobs')) EXEC(N'CREATE INDEX IX_background_jobs_idempotency ON dbo.background_jobs (tenant_id, idempotency_key, status) WHERE idempotency_key IS NOT NULL;');"
if errorlevel 1 exit /b 1
rem AI tu hoc (0056_kb_suggestions) — lenh rieng, giu duoi tran 8191 ky tu cua cmd.exe
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF OBJECT_ID(N'dbo.kb_suggestions', N'U') IS NULL CREATE TABLE dbo.kb_suggestions (id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_kb_suggestions PRIMARY KEY, tenant_id UNIQUEIDENTIFIER NOT NULL, op NVARCHAR(16) NOT NULL, target_kb_module_id UNIQUEIDENTIFIER NULL, title NVARCHAR(256) NOT NULL, content_md NVARCHAR(MAX) NOT NULL, rationale NVARCHAR(MAX) NULL, evidence_json NVARCHAR(MAX) NULL, dedup_hash NVARCHAR(64) NOT NULL, reviewer_verdict NVARCHAR(16) NULL, reviewer_notes NVARCHAR(MAX) NULL, accuracy_before DECIMAL(5,2) NULL, accuracy_after DECIMAL(5,2) NULL, status NVARCHAR(16) NOT NULL CONSTRAINT DF_kb_suggestions_status DEFAULT 'pending', approval_mode NVARCHAR(8) NULL, rejected_reason NVARCHAR(1024) NULL, decided_by UNIQUEIDENTIFIER NULL, created_at DATETIMEOFFSET NOT NULL, decided_at DATETIMEOFFSET NULL, CONSTRAINT UQ_kb_suggestions_tenant_dedup UNIQUE (tenant_id, dedup_hash)); IF OBJECT_ID(N'dbo.kb_suggestions', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_kb_suggestions_tenant_id_status' AND object_id = OBJECT_ID(N'dbo.kb_suggestions')) CREATE INDEX IX_kb_suggestions_tenant_id_status ON dbo.kb_suggestions (tenant_id, status); IF COL_LENGTH(N'dbo.tenants', N'require_kb_human_review') IS NULL ALTER TABLE dbo.tenants ADD require_kb_human_review BIT NOT NULL CONSTRAINT DF_tenants_require_kb_human_review DEFAULT 0;"
if errorlevel 1 exit /b 1
rem AI tu hoc Lop 2 (0057_contact_memories) — lenh rieng, giu duoi tran 8191 ky tu cua cmd.exe
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF OBJECT_ID(N'dbo.contact_memories', N'U') IS NULL CREATE TABLE dbo.contact_memories (id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_contact_memories PRIMARY KEY, tenant_id UNIQUEIDENTIFIER NOT NULL, contact_id UNIQUEIDENTIFIER NOT NULL, fact NVARCHAR(1024) NOT NULL, category NVARCHAR(32) NOT NULL, confidence DECIMAL(3,2) NOT NULL CONSTRAINT DF_contact_memories_confidence DEFAULT 0.5, source_conversation_id UNIQUEIDENTIFIER NULL, is_active BIT NOT NULL CONSTRAINT DF_contact_memories_is_active DEFAULT 1, superseded_by_id UNIQUEIDENTIFIER NULL, created_at DATETIMEOFFSET NOT NULL, updated_at DATETIMEOFFSET NOT NULL); IF OBJECT_ID(N'dbo.contact_memories', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_contact_memories_tenant_id_contact_id_is_active' AND object_id = OBJECT_ID(N'dbo.contact_memories')) CREATE INDEX IX_contact_memories_tenant_id_contact_id_is_active ON dbo.contact_memories (tenant_id, contact_id, is_active); IF COL_LENGTH(N'dbo.conversations', N'memory_extracted_at') IS NULL ALTER TABLE dbo.conversations ADD memory_extracted_at DATETIMEOFFSET NULL;"
if errorlevel 1 exit /b 1
rem AI tu hoc Lop 3 (0058_agent_memories) — lenh rieng, giu duoi tran 8191 ky tu cua cmd.exe
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF OBJECT_ID(N'dbo.agent_memories', N'U') IS NULL CREATE TABLE dbo.agent_memories (id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_agent_memories PRIMARY KEY, tenant_id UNIQUEIDENTIFIER NOT NULL, agent_code NVARCHAR(64) NOT NULL, fact NVARCHAR(1024) NOT NULL, category NVARCHAR(32) NOT NULL CONSTRAINT DF_agent_memories_category DEFAULT 'mistake', confidence DECIMAL(3,2) NOT NULL CONSTRAINT DF_agent_memories_confidence DEFAULT 0.5, is_active BIT NOT NULL CONSTRAINT DF_agent_memories_is_active DEFAULT 1, superseded_by_id UNIQUEIDENTIFIER NULL, created_at DATETIMEOFFSET NOT NULL, updated_at DATETIMEOFFSET NOT NULL); IF OBJECT_ID(N'dbo.agent_memories', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_memories_tenant_id_agent_code_is_active' AND object_id = OBJECT_ID(N'dbo.agent_memories')) CREATE INDEX IX_agent_memories_tenant_id_agent_code_is_active ON dbo.agent_memories (tenant_id, agent_code, is_active);"
if errorlevel 1 exit /b 1
rem Keyset pagination indexes (0067) — lenh rieng, giu duoi tran 8191 ky tu cua cmd.exe
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_conversations_tenant_last_message_id' AND object_id = OBJECT_ID(N'dbo.conversations')) CREATE INDEX IX_conversations_tenant_last_message_id ON dbo.conversations (tenant_id, last_message_at DESC, id DESC); IF OBJECT_ID(N'dbo.notifications', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_notifications_tenant_created_id' AND object_id = OBJECT_ID(N'dbo.notifications')) CREATE INDEX IX_notifications_tenant_created_id ON dbo.notifications (tenant_id, created_at DESC, id DESC); IF OBJECT_ID(N'dbo.background_jobs', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_background_jobs_tenant_created_id' AND object_id = OBJECT_ID(N'dbo.background_jobs')) CREATE INDEX IX_background_jobs_tenant_created_id ON dbo.background_jobs (tenant_id, created_at DESC, id DESC); IF OBJECT_ID(N'dbo.agent_sessions', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_sessions_tenant_started_id' AND object_id = OBJECT_ID(N'dbo.agent_sessions')) CREATE INDEX IX_agent_sessions_tenant_started_id ON dbo.agent_sessions (tenant_id, started_at DESC, id DESC); IF OBJECT_ID(N'dbo.audit_logs', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_audit_logs_tenant_occurred_id' AND object_id = OBJECT_ID(N'dbo.audit_logs')) CREATE INDEX IX_audit_logs_tenant_occurred_id ON dbo.audit_logs (tenant_id, occurred_at DESC, id DESC);"
if errorlevel 1 exit /b 1
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF OBJECT_ID(N'dbo.generated_documents', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_generated_documents_tenant_created_id' AND object_id = OBJECT_ID(N'dbo.generated_documents')) CREATE INDEX IX_generated_documents_tenant_created_id ON dbo.generated_documents (tenant_id, created_at DESC, id DESC); IF OBJECT_ID(N'dbo.competitor_posts', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_competitor_posts_tenant_detected_id' AND object_id = OBJECT_ID(N'dbo.competitor_posts')) CREATE INDEX IX_competitor_posts_tenant_detected_id ON dbo.competitor_posts (tenant_id, detected_at DESC, id DESC); IF OBJECT_ID(N'dbo.ad_actions', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ad_actions_tenant_executed_id' AND object_id = OBJECT_ID(N'dbo.ad_actions')) CREATE INDEX IX_ad_actions_tenant_executed_id ON dbo.ad_actions (tenant_id, executed_at DESC, id DESC); IF OBJECT_ID(N'dbo.content_items', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_content_items_tenant_updated_id' AND object_id = OBJECT_ID(N'dbo.content_items')) CREATE INDEX IX_content_items_tenant_updated_id ON dbo.content_items (tenant_id, updated_at DESC, id DESC);"
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
type "%ROOT%deploy\backfill_lead_owners.sql" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b
if errorlevel 1 (
    echo [ERROR] Data patch failed: deploy\backfill_lead_owners.sql
    exit /b 1
)
type "%ROOT%deploy\fix_duplicate_outbound_echo.sql" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b
if errorlevel 1 (
    echo [ERROR] Data patch failed: deploy\fix_duplicate_outbound_echo.sql
    exit /b 1
)
rem Unsafe blanket content review backfill retired: unpublished rows require a real revision-bound review.
type "%ROOT%deploy\repair_agent_allowed_tools.sql" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b
if errorlevel 1 (
    echo [ERROR] Data patch failed: deploy\repair_agent_allowed_tools.sql
    exit /b 1
)
exit /b 0

:apply_meta_migration
rem SQL preamble: SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; then 0055_meta_facebook_login_for_business.sql
echo [INFO] Ensuring Meta Facebook Login for Business schema...
set "MIGRATION_FILE=0055_meta_facebook_login_for_business.sql"
set "MIGRATION_SOURCE=%ROOT%deploy\migrations\%MIGRATION_FILE%"
set "MIGRATION_SQL=%TEMP%\clawbot_meta_migration_%RANDOM%.sql"
powershell -NoProfile -Command "$prefix = 'SET QUOTED_IDENTIFIER ON;' + [Environment]::NewLine + 'SET ARITHABORT ON;' + [Environment]::NewLine; [IO.File]::WriteAllText($env:MIGRATION_SQL, $prefix + [IO.File]::ReadAllText($env:MIGRATION_SOURCE), [Text.UTF8Encoding]::new($false))"
if errorlevel 1 exit /b 1
docker cp "%MIGRATION_SQL%" clawbot-sqlserver:/tmp/clawbot_meta_migration.sql >nul
if errorlevel 1 (
    del "%MIGRATION_SQL%" >nul 2>nul
    exit /b 1
)
del "%MIGRATION_SQL%" >nul 2>nul
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -i /tmp/clawbot_meta_migration.sql
exit /b %errorlevel%

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

:ensure_migration_ledger
echo [INFO] Ensuring migration history table...
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF OBJECT_ID(N'dbo.schema_migrations', N'U') IS NULL CREATE TABLE dbo.schema_migrations (filename NVARCHAR(260) NOT NULL CONSTRAINT PK_schema_migrations PRIMARY KEY, applied_at DATETIMEOFFSET NOT NULL);"
exit /b %errorlevel%

:detect_migration_history
set "MIGRATION_HISTORY_CHECK=%TEMP%\clawbot_migration_history_%RANDOM%.txt"
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(1) FROM dbo.schema_migrations;" > "%MIGRATION_HISTORY_CHECK%" 2>nul
if errorlevel 1 (
    del "%MIGRATION_HISTORY_CHECK%" >nul 2>nul
    echo [ERROR] Could not inspect migration history.
    exit /b 1
)
set "MIGRATION_HISTORY_COUNT=0"
set /p MIGRATION_HISTORY_COUNT=<"%MIGRATION_HISTORY_CHECK%"
del "%MIGRATION_HISTORY_CHECK%" >nul 2>nul
set "HAS_MIGRATION_HISTORY=0"
if not "%MIGRATION_HISTORY_COUNT%"=="0" set "HAS_MIGRATION_HISTORY=1"
exit /b 0

:baseline_existing_migrations
echo [INFO] Baselining repaired schema and applying migrations newer than %MIGRATION_BASELINE_NUMBER%...
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%deploy\apply-migrations.ps1" -ContainerName "clawbot-sqlserver" -Database "clawbot" -SaPassword "%MSSQL_SA_PASSWORD%" -MigrationsDir "%MIGRATIONS_DIR%" -SqlCmdPath "%SQLCMD%" -BaselineNumber %MIGRATION_BASELINE_NUMBER% -BaselineExisting -RepairFilesCsv "0037_pancake_pages.sql,0041_social_credentials.sql"
exit /b %errorlevel%

:apply_pending_migrations
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%deploy\apply-migrations.ps1" -ContainerName "clawbot-sqlserver" -Database "clawbot" -SaPassword "%MSSQL_SA_PASSWORD%" -MigrationsDir "%MIGRATIONS_DIR%" -SqlCmdPath "%SQLCMD%" -BaselineNumber %MIGRATION_BASELINE_NUMBER%
exit /b %errorlevel%

:replay_migrations
echo [INFO] Applying SQL migrations from deploy\migrations...
call :apply_pending_migrations
exit /b %errorlevel%
