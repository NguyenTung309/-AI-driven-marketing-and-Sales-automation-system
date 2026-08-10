@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "ROOT=%~dp0"
set "ENV_FILE=%ROOT%deploy\.env"
set "ENV_EXAMPLE=%ROOT%deploy\.env.example"
set "COMPOSE_FILE=%ROOT%deploy\docker-compose.yml"
set "FRONTEND_DIR=%ROOT%src\frontend\clawbot-web"
set "MIGRATIONS_DIR=%ROOT%deploy\migrations"
set "MIGRATION_BASELINE_NUMBER=67"
set "API_APPSETTINGS=%ROOT%src\api\Clawbot.Api\appsettings.json"
REM Khong nhung secret vao file nay. Moi gia tri duoi day chi den tu deploy\.env, do
REM deploy\initialize-local-env.ps1 sinh ra khi con trong. Khai bao rong o day de bien moi truong
REM cua may khong am tham thay cho gia tri trong .env.
set "MSSQL_SA_PASSWORD="
set "JWT_SIGNING_KEY="
set "AGENT_SERVICE_AUTH_SIGNING_KEY="
REM ENCRYPTION_BASE64_KEY phai khop Encryption:Base64Key trong appsettings.json (API + AgentService):
REM service chay ngoai run-all.bat fallback ve appsettings, va du lieu ma hoa bang khoa nay khong
REM doc duoc bang khoa kia (llm/embedding api key, inbox/pancake token).
set "ENCRYPTION_BASE64_KEY="
set "PANCAKE_PAGE_ACCESS_TOKEN="
set "PANCAKE_USER_ACCESS_TOKEN="
set "PANCAKE_PAGE_ID="
set "PANCAKE_TENANT_SLUG="
set "PANCAKE_PLATFORM="
REM Cap VAPID cho Web Push (thong bao khi dong tab). Thieu key = web push tu tat, feed + chuong
REM + email van chay binh thuong. Sinh cap moi bang `npx web-push generate-vapid-keys`.
set "WEBPUSH_PUBLIC_KEY="
set "WEBPUSH_PRIVATE_KEY="
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

echo [INFO] Filling missing local secrets in deploy\.env...
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%deploy\initialize-local-env.ps1" -Path "%ENV_FILE%" -AppSettingsFile "%API_APPSETTINGS%"
if errorlevel 1 (
    echo [ERROR] Could not initialize deploy\.env with local secrets.
    exit /b 1
)

call :read_env_value MSSQL_SA_PASSWORD
call :read_env_value JWT_SIGNING_KEY
call :read_env_value AGENT_SERVICE_AUTH_SIGNING_KEY
call :read_env_value ENCRYPTION_BASE64_KEY
call :read_env_value WEBPUSH_PUBLIC_KEY
call :read_env_value WEBPUSH_PRIVATE_KEY
REM Doc theo quy uoc cua PancakeBootstrapSeeder.NormalizeCredential: gia tri mau "replace-with-..."
REM trong .env.example khong phai token that, seeder bo qua nen guard cung phai bo qua.
call :read_credential_env_value PANCAKE_PAGE_ACCESS_TOKEN
call :read_credential_env_value PANCAKE_USER_ACCESS_TOKEN
call :read_credential_env_value PANCAKE_PAGE_ID
call :read_env_value PANCAKE_TENANT_SLUG
call :read_env_value PANCAKE_PLATFORM
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

call :require_env_value MSSQL_SA_PASSWORD
if errorlevel 1 exit /b 1
call :require_env_value JWT_SIGNING_KEY
if errorlevel 1 exit /b 1
call :require_env_value AGENT_SERVICE_AUTH_SIGNING_KEY
if errorlevel 1 exit /b 1
call :require_env_value ENCRYPTION_BASE64_KEY
if errorlevel 1 exit /b 1

REM PancabeBootstrapSeeder chi chay khi co token Pancake, va luc do no bat buoc page id + tenant slug.
REM Thieu bien = AgentService throw ngay luc khoi dong, nen chan som voi thong bao ro rang.
set "PANCAKE_BOOTSTRAP=0"
if defined PANCAKE_PAGE_ACCESS_TOKEN set "PANCAKE_BOOTSTRAP=1"
if defined PANCAKE_USER_ACCESS_TOKEN set "PANCAKE_BOOTSTRAP=1"
if "%PANCAKE_BOOTSTRAP%"=="1" (
    call :require_env_value PANCAKE_PAGE_ID
    if errorlevel 1 exit /b 1
    call :require_env_value PANCAKE_TENANT_SLUG
    if errorlevel 1 exit /b 1
)
REM Chi duong page token truc tiep moi can platform; duong user token tu suy ra khi liet ke page.
if defined PANCAKE_PAGE_ACCESS_TOKEN if not defined PANCAKE_USER_ACCESS_TOKEN (
    call :require_env_value PANCAKE_PLATFORM
    if errorlevel 1 exit /b 1
)

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

rem Final gate before services: repair active tables and verify every schema contract EF maps at boot.
call :repair_tenant_runtime_columns
if errorlevel 1 exit /b 1
call :repair_inbox_collaboration_tables
if errorlevel 1 exit /b 1
call :verify_tenant_runtime_columns
if errorlevel 1 exit /b 1
call :verify_database_table_consolidation
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
REM Xoa launcher cu: ban truoc tung ghi secret thang vao cac file .cmd nay.
del /q "%LAUNCH_DIR%\*.cmd" >nul 2>nul

REM Secret dat trong tien trinh nay va di theo `start` sang cua so con qua environment block,
REM nen khong con dong nao chua secret bi ghi xuong dia.
REM Dung 127.0.0.1 chu khong phai localhost: docker publish cong o 127.0.0.1:11433 (chi IPv4), con
REM Windows phan giai localhost ra ::1 truoc. SqlClient thu IPv6, goi tin bi nuot chu khong bi tu choi
REM nen no cho het Connect Timeout roi moi bo cuoc, keo theo pool het cho va host tu tat.
set "ConnectionStrings__SqlServer=Server=127.0.0.1,11433;Database=clawbot;User Id=sa;Password=%MSSQL_SA_PASSWORD%;TrustServerCertificate=True;MultipleActiveResultSets=true"
set "Jwt__SigningKey=%JWT_SIGNING_KEY%"
set "AgentServiceAuthentication__SigningKey=%AGENT_SERVICE_AUTH_SIGNING_KEY%"
set "Encryption__Base64Key=%ENCRYPTION_BASE64_KEY%"
set "WebPush__PublicKey=%WEBPUSH_PUBLIC_KEY%"
set "WebPush__PrivateKey=%WEBPUSH_PRIVATE_KEY%"

> "%LAUNCH_DIR%\agent.cmd" (
    echo @echo off
    echo setlocal EnableExtensions DisableDelayedExpansion
    echo cd /d "%ROOT%"
    echo set "ASPNETCORE_ENVIRONMENT=Development"
    echo set "ASPNETCORE_URLS=http://localhost:15875"
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
    echo title ClawBot API :15874
    echo echo [API] starting against 127.0.0.1,11433...
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

REM Frontend khong can secret backend: bo khoi environment block truoc khi start Vite/npm.
set "ConnectionStrings__SqlServer="
set "Jwt__SigningKey="
set "Encryption__Base64Key="
set "WebPush__PublicKey="
set "WebPush__PrivateKey="
set "MSSQL_SA_PASSWORD="
set "JWT_SIGNING_KEY="
set "AGENT_SERVICE_AUTH_SIGNING_KEY="
set "ENCRYPTION_BASE64_KEY="
set "WEBPUSH_PRIVATE_KEY="
set "PANCAKE_PAGE_ACCESS_TOKEN="
set "PANCAKE_USER_ACCESS_TOKEN="
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
echo Would run deploy\initialize-local-env.ps1 to fill blank local secrets in deploy\.env, reusing Encryption:Base64Key from src\api\Clawbot.Api\appsettings.json.
echo Would abort when MSSQL_SA_PASSWORD, JWT_SIGNING_KEY, AGENT_SERVICE_AUTH_SIGNING_KEY, or ENCRYPTION_BASE64_KEY is still empty.
echo Would run: docker compose --env-file deploy\.env -f deploy\docker-compose.yml up -d sqlserver redis rabbitmq qdrant minio postgres metabase searxng
echo Would stop old app processes listening on ports 15873, 15874, 15875, 15876
echo Would apply deploy\seed\*.sql for tenant %SEED_TENANT_SLUG% when --seed is passed.
echo Would create dbo.schema_migrations, baseline repaired migrations through %MIGRATION_BASELINE_NUMBER%, and apply every pending deploy\migrations\*.sql file.
echo Would repair inbox collaboration tables and verify database-table consolidation before starting services.
echo Would apply one-shot data patches from deploy\fix_contact_overwrite.sql, guarded by dbo.data_patches.
echo Would run: dotnet restore Clawbot.sln
echo Would run: dotnet build Clawbot.sln --no-restore
echo Would run: npm ci in src\frontend\clawbot-web when node_modules is missing
echo Would start AgentService with ASPNETCORE_URLS=http://localhost:15875, AgentServiceAuthentication__SigningKey, shared Encryption__Base64Key, and optional PANCAKE_* bootstrap variables from deploy\.env.
echo Would pass every secret to the service windows through the inherited environment block, not by writing them into %%TEMP%%\clawbot-run-all\*.cmd.
echo Would start API with ASPNETCORE_URLS=http://localhost:15874, AgentService__Url=http://localhost:15875, and shared Jwt__SigningKey/AgentServiceAuthentication__SigningKey/Encryption__Base64Key.
echo Would start Gateway with ASPNETCORE_URLS=http://localhost:15873 and shared Jwt__SigningKey.
echo Would start frontend with npm run dev at http://localhost:15876
exit /b 0

:read_env_value
for /f "usebackq tokens=1,* delims==" %%A in ("%ENV_FILE%") do (
    if /i "%%A"=="%~1" set "%~1=%%B"
)
exit /b 0

REM Nhu :read_env_value nhung coi gia tri mau la rong. Bat buoc phai giong
REM PancakeBootstrapSeeder.NormalizeCredential: neu batch coi placeholder la token that thi guard
REM se doi PANCAKE_PLATFORM cho mot lan bootstrap ma seeder khong bao gio chay.
:read_credential_env_value
for /f "usebackq tokens=1,* delims==" %%A in ("%ENV_FILE%") do (
    if /i "%%A"=="%~1" (
        set "%~1=%%B"
        call :is_placeholder_value "%%B"
        if errorlevel 1 set "%~1="
    )
)
exit /b 0

REM Tra ve errorlevel 1 khi gia tri chi la chu giu cho trong .env.example. Khong in gia tri ra man hinh.
:is_placeholder_value
set "CANDIDATE=%~1"
if not defined CANDIDATE exit /b 0
if /i "%CANDIDATE:~0,13%"=="replace-with-" exit /b 1
if /i "%CANDIDATE%"=="changeme" exit /b 1
if /i "%CANDIDATE%"=="change-me" exit /b 1
if /i "%CANDIDATE%"=="replace-me" exit /b 1
if /i "%CANDIDATE%"=="your-token" exit /b 1
if /i "%CANDIDATE%"=="your-access-token" exit /b 1
exit /b 0

REM Fail closed: khong khoi dong service voi cau hinh bat buoc con trong. Chi in ten khoa, khong in gia tri.
:require_env_value
if not defined %~1 (
    echo [ERROR] %~1 is empty. Set it in deploy\.env, then run run-all.bat again.
    exit /b 1
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
type "%ROOT%deploy\migrations\0081_content_schedule_provider_target.sql" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b
if errorlevel 1 (
    echo [ERROR] Content schedule provider target repair failed.
    exit /b 1
)
type "%ROOT%deploy\migrations\0082_content_render_tasks.sql" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b
if errorlevel 1 (
    echo [ERROR] Content render task persistence repair failed.
    exit /b 1
)
type "%ROOT%deploy\migrations\0083_content_generation_traces.sql" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b
if errorlevel 1 (
    echo [ERROR] Content generation traces repair failed.
    exit /b 1
)
type "%ROOT%deploy\migrations\0084_content_items_chain_snapshot.sql" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b
if errorlevel 1 (
    echo [ERROR] Content items chain snapshot repair failed.
    exit /b 1
)
type "%ROOT%deploy\migrations\0085_content_review_tasks_refine_attempt.sql" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b
if errorlevel 1 (
    echo [ERROR] Content review tasks refine attempt repair failed.
    exit /b 1
)
exit /b 0

:repair_inbox_collaboration_tables
if not exist "%ROOT%deploy\repair_inbox_collaboration_tables.sql" (
    echo [ERROR] Missing deploy\repair_inbox_collaboration_tables.sql
    exit /b 1
)
type "%ROOT%deploy\repair_inbox_collaboration_tables.sql" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b
if errorlevel 1 (
    echo [ERROR] Inbox collaboration table repair failed.
    exit /b 1
)
exit /b 0

:verify_database_table_consolidation
if not exist "%ROOT%deploy\verify_database_table_consolidation.sql" (
    echo [ERROR] Missing deploy\verify_database_table_consolidation.sql
    exit /b 1
)
set "DATABASE_CONSOLIDATION_CHECK=%TEMP%\clawbot-database-consolidation-%RANDOM%.txt"
type "%ROOT%deploy\verify_database_table_consolidation.sql" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -h -1 -W -b > "%DATABASE_CONSOLIDATION_CHECK%" 2>nul
if errorlevel 1 (
    del /q "%DATABASE_CONSOLIDATION_CHECK%" >nul 2>nul
    echo [ERROR] Could not verify database table consolidation.
    exit /b 1
)
set "DATABASE_CONSOLIDATION_FLAGS="
set "DATABASE_DBO_TABLE_COUNT="
set "DATABASE_TOTAL_TABLE_COUNT="
for /f "usebackq tokens=1-3 delims=|" %%A in ("%DATABASE_CONSOLIDATION_CHECK%") do (
    if not defined DATABASE_CONSOLIDATION_FLAGS set "DATABASE_CONSOLIDATION_FLAGS=%%A"
    if not defined DATABASE_DBO_TABLE_COUNT set "DATABASE_DBO_TABLE_COUNT=%%B"
    if not defined DATABASE_TOTAL_TABLE_COUNT set "DATABASE_TOTAL_TABLE_COUNT=%%C"
)
del /q "%DATABASE_CONSOLIDATION_CHECK%" >nul 2>nul
if not "%DATABASE_CONSOLIDATION_FLAGS%"=="111111111111111" (
    echo [ERROR] Database consolidation objects or indexes are incomplete: %DATABASE_CONSOLIDATION_FLAGS%
    exit /b 1
)
rem Counts are reported for audit visibility only; explicit required/forbidden object contracts are the startup gate.
echo [INFO] Database table consolidation verified: dbo=%DATABASE_DBO_TABLE_COUNT%, total=%DATABASE_TOTAL_TABLE_COUNT%.
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
rem Content render task gate (0082): exact rowversion, ordered FK/index columns, trusted CHECK definitions, and payload defaults.
rem verify_content_render_tasks.sql checks sys.index_columns/key_ordinal, sys.foreign_key_columns/constraint_column_id/referenced_object_id,
rem delete_referential_action_desc, is_not_trusted, is_disabled, system_type_id, and normalized constraint definition values.
rem Payload checks cover canonical_slots_json, slots_hash, and sys.default_constraints for every immutable payload column.
echo [INFO] Verifying content_render_tasks schema...
if not exist "%ROOT%deploy\verify_content_render_tasks.sql" (
    echo [ERROR] Missing deploy\verify_content_render_tasks.sql
    exit /b 1
)
set "CONTENT_RENDER_TASK_CHECK=%TEMP%\clawbot_content_render_tasks_%RANDOM%.txt"
type "%ROOT%deploy\verify_content_render_tasks.sql" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -h -1 -W -b > "%CONTENT_RENDER_TASK_CHECK%" 2>nul
if errorlevel 1 (
    del "%CONTENT_RENDER_TASK_CHECK%" >nul 2>nul
    echo [ERROR] Could not verify content_render_tasks schema.
    exit /b 1
)
set "CONTENT_RENDER_TASK_FLAGS="
for /f "usebackq delims= " %%A in ("%CONTENT_RENDER_TASK_CHECK%") do (
    if not defined CONTENT_RENDER_TASK_FLAGS set "CONTENT_RENDER_TASK_FLAGS=%%A"
)
del "%CONTENT_RENDER_TASK_CHECK%" >nul 2>nul
if /i not "%CONTENT_RENDER_TASK_FLAGS%"=="1111111111111" (
    echo [ERROR] content_render_tasks schema definitions are incomplete or malformed after repair.
    echo Expected exact rowversion, tenant-safe FK, ordered worker indexes, trusted CHECK definitions, canonical payload columns, full column contract, non-null required values, and clustered primary key.
    echo Verify flags were "%CONTENT_RENDER_TASK_FLAGS%" - want 1111111111111.
    exit /b 1
)
exit /b 0

:repair_runtime_columns
echo [INFO] Repairing runtime columns on existing schema...
call :repair_tenant_runtime_columns
if errorlevel 1 exit /b 1
if not exist "%ROOT%deploy\repair_inbox_runtime_columns.sql" (
    echo [ERROR] Missing deploy\repair_inbox_runtime_columns.sql
    exit /b 1
)
type "%ROOT%deploy\repair_inbox_runtime_columns.sql" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b
if errorlevel 1 exit /b 1
if not exist "%ROOT%deploy\repair_agent_runtime_columns.sql" (
    echo [ERROR] Missing deploy\repair_agent_runtime_columns.sql
    exit /b 1
)
type "%ROOT%deploy\repair_agent_runtime_columns.sql" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b
if errorlevel 1 exit /b 1
rem Nhan dong ledger uoc luong (0086) — lenh rieng, giu moi lenh -Q duoi tran 8191 ky tu cua cmd.exe
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF COL_LENGTH(N'dbo.claude_cost_ledger', N'is_estimated') IS NULL ALTER TABLE dbo.claude_cost_ledger ADD is_estimated BIT NOT NULL CONSTRAINT DF_claude_cost_ledger_is_estimated DEFAULT 0;"
if errorlevel 1 exit /b 1
rem Review-gate (P1-P4) columns — lenh rieng vi dong tren da sat tran 8191 ky tu cua cmd.exe
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF COL_LENGTH(N'dbo.tenants', N'require_content_review') IS NULL ALTER TABLE dbo.tenants ADD require_content_review BIT NOT NULL CONSTRAINT DF_tenants_require_content_review DEFAULT 0; IF COL_LENGTH(N'dbo.content_items', N'created_by_agent_id') IS NULL ALTER TABLE dbo.content_items ADD created_by_agent_id UNIQUEIDENTIFIER NULL; IF COL_LENGTH(N'dbo.content_items', N'rejected_reason') IS NULL ALTER TABLE dbo.content_items ADD rejected_reason NVARCHAR(1024) NULL; IF COL_LENGTH(N'dbo.messages', N'status') IS NULL ALTER TABLE dbo.messages ADD status NVARCHAR(32) NOT NULL CONSTRAINT DF_messages_status DEFAULT N'sent'; IF COL_LENGTH(N'dbo.tenants', N'require_chat_reply_approval') IS NULL ALTER TABLE dbo.tenants ADD require_chat_reply_approval BIT NOT NULL CONSTRAINT DF_tenants_require_chat_reply_approval DEFAULT 0; IF COL_LENGTH(N'dbo.content_items', N'desired_publish_at') IS NULL ALTER TABLE dbo.content_items ADD desired_publish_at DATETIMEOFFSET NULL; IF COL_LENGTH(N'dbo.content_items', N'last_review_alert_at') IS NULL ALTER TABLE dbo.content_items ADD last_review_alert_at DATETIMEOFFSET NULL; IF COL_LENGTH(N'dbo.inboxes', N'sender_id') IS NULL ALTER TABLE dbo.inboxes ADD sender_id NVARCHAR(128) NULL;"
if errorlevel 1 exit /b 1
rem Engagement counts (0059_content_schedule_engagement) — lenh rieng, giu duoi tran 8191 ky tu cua cmd.exe
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF COL_LENGTH(N'dbo.content_schedule', N'like_count') IS NULL ALTER TABLE dbo.content_schedule ADD like_count INT NULL; IF COL_LENGTH(N'dbo.content_schedule', N'comment_count') IS NULL ALTER TABLE dbo.content_schedule ADD comment_count INT NULL; IF COL_LENGTH(N'dbo.content_schedule', N'engagement_synced_at') IS NULL ALTER TABLE dbo.content_schedule ADD engagement_synced_at DATETIMEOFFSET NULL;"
if errorlevel 1 exit /b 1
rem Provider object id (0087_content_schedule_external_post_id) — lenh rieng
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF COL_LENGTH(N'dbo.content_schedule', N'external_post_id') IS NULL ALTER TABLE dbo.content_schedule ADD external_post_id NVARCHAR(256) NULL;"
if errorlevel 1 exit /b 1
rem Meta inbox identity (0088_meta_inbox_unique_identity) — lenh rieng
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF EXISTS (SELECT 1 FROM dbo.inboxes WHERE is_active = 1 AND deleted_at IS NULL GROUP BY tenant_id, platform, external_page_id HAVING COUNT(*) > 1) THROW 51088, 'meta_inbox_duplicate_identity', 1; IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_inboxes_tenant_platform_external_active' AND object_id = OBJECT_ID(N'dbo.inboxes')) CREATE UNIQUE INDEX UX_inboxes_tenant_platform_external_active ON dbo.inboxes (tenant_id, platform, external_page_id) WHERE is_active = 1 AND deleted_at IS NULL;"
if errorlevel 1 exit /b 1
rem Meta comment reconciliation watermark (0089_content_schedule_meta_comments_synced_at) — lenh rieng
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF COL_LENGTH(N'dbo.content_schedule', N'meta_comments_synced_at') IS NULL ALTER TABLE dbo.content_schedule ADD meta_comments_synced_at DATETIMEOFFSET NULL;"
if errorlevel 1 exit /b 1
rem Parent comment id (0090_messages_parent_comment_id) — lenh rieng
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF COL_LENGTH(N'dbo.messages', N'parent_comment_id') IS NULL ALTER TABLE dbo.messages ADD parent_comment_id NVARCHAR(256) NULL;"
if errorlevel 1 exit /b 1
rem Parent comment id index (0091_messages_parent_comment_id_index) — lenh rieng
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_messages_tenant_parent_comment_id' AND object_id = OBJECT_ID(N'dbo.messages')) CREATE INDEX IX_messages_tenant_parent_comment_id ON dbo.messages (tenant_id, parent_comment_id) WHERE parent_comment_id IS NOT NULL;"
if errorlevel 1 exit /b 1
rem Bot comment claim uniqueness (0092_messages_parent_comment_unique_claim) — lenh rieng
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_messages_bot_parent_comment_type' AND object_id = OBJECT_ID(N'dbo.messages') AND (filter_definition IS NULL OR CHARINDEX(N'send_failed', filter_definition) = 0)) DROP INDEX UX_messages_bot_parent_comment_type ON dbo.messages; IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_messages_bot_parent_comment_type' AND object_id = OBJECT_ID(N'dbo.messages')) CREATE UNIQUE INDEX UX_messages_bot_parent_comment_type ON dbo.messages (tenant_id, parent_comment_id, message_type) WHERE parent_comment_id IS NOT NULL AND direction = N'out' AND sender_type = N'bot' AND status != N'send_failed';"
if errorlevel 1 exit /b 1
rem Page feed webhook subscription marker (0093_meta_assets_feed_subscribed_at) — lenh rieng
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; IF COL_LENGTH(N'dbo.meta_assets', N'feed_subscribed_at') IS NULL ALTER TABLE dbo.meta_assets ADD feed_subscribed_at DATETIMEOFFSET NULL;"
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
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%deploy\apply-migrations.ps1" -ContainerName "clawbot-sqlserver" -Database "clawbot" -SaPassword "%MSSQL_SA_PASSWORD%" -MigrationsDir "%MIGRATIONS_DIR%" -SqlCmdPath "%SQLCMD%" -BaselineNumber %MIGRATION_BASELINE_NUMBER% -BaselineExisting -RepairFilesCsv "0041_social_credentials.sql"
exit /b %errorlevel%

:apply_pending_migrations
call :repair_inbox_credential_columns
if errorlevel 1 exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%deploy\apply-migrations.ps1" -ContainerName "clawbot-sqlserver" -Database "clawbot" -SaPassword "%MSSQL_SA_PASSWORD%" -MigrationsDir "%MIGRATIONS_DIR%" -SqlCmdPath "%SQLCMD%" -BaselineNumber %MIGRATION_BASELINE_NUMBER%
exit /b %errorlevel%

:repair_inbox_credential_columns
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b -Q "SET QUOTED_IDENTIFIER ON; SET ARITHABORT ON; SET XACT_ABORT ON; IF OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.schema_migrations', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.schema_migrations WHERE filename = N'0030_add_inbox_encrypted_token.sql' OR (LEN(filename) = 17 AND filename COLLATE Latin1_General_100_BIN2 LIKE N'[_][_]baseline[_][0-9][0-9][0-9][0-9][_][_]' COLLATE Latin1_General_100_BIN2 AND TRY_CONVERT(INT, SUBSTRING(filename, 12, 4)) >= 30)) BEGIN IF COL_LENGTH(N'dbo.inboxes', N'encrypted_access_token') IS NULL ALTER TABLE dbo.inboxes ADD encrypted_access_token NVARCHAR(MAX) NULL; IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.inboxes') AND name = N'encrypted_access_token' AND (max_length <> -1 OR is_nullable = 0)) ALTER TABLE dbo.inboxes ALTER COLUMN encrypted_access_token NVARCHAR(MAX) NULL; END;"
exit /b %errorlevel%

:replay_migrations
echo [INFO] Applying SQL migrations from deploy\migrations...
call :apply_pending_migrations
exit /b %errorlevel%
