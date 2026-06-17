@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
set "ENV_FILE=%ROOT%deploy\.env"
set "ENV_EXAMPLE=%ROOT%deploy\.env.example"
set "COMPOSE_FILE=%ROOT%deploy\docker-compose.yml"
set "FRONTEND_DIR=%ROOT%src\frontend\clawbot-web"
set "MIGRATIONS_DIR=%ROOT%deploy\migrations"
set "MSSQL_SA_PASSWORD=Clawbot!2026"
set "DRY_RUN=0"

if /i "%~1"=="--dry-run" set "DRY_RUN=1"

if "%DRY_RUN%"=="1" (
    echo [DRY-RUN] ClawBot one-click runner
    echo Root: %ROOT%
    echo Would copy deploy\.env.example to deploy\.env if missing.
    echo Would run: docker compose --env-file deploy\.env -f deploy\docker-compose.yml up -d sqlserver redis rabbitmq qdrant minio postgres metabase
    echo Would run: dotnet restore Clawbot.sln
    echo Would run: dotnet build Clawbot.sln --no-restore
    echo Would run: npm ci in src\frontend\clawbot-web when node_modules is missing
    echo Would start AgentService with ASPNETCORE_URLS=http://localhost:15875
    echo Would start API with ASPNETCORE_URLS=http://localhost:15874 and AgentService__Url=http://localhost:15875
    echo Would start Gateway with ASPNETCORE_URLS=http://localhost:15873
    echo Would start frontend with npm run dev at http://localhost:15876
    exit /b 0
)

echo.
echo === ClawBot local one-click runner ===
echo Root: %ROOT%
echo.

call :require_command dotnet ".NET SDK 8"
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

start "ClawBot API :15874" cmd /k "cd /d ""%ROOT%"" && set ASPNETCORE_ENVIRONMENT=Development&& set ASPNETCORE_URLS=http://localhost:15874&& set AgentService__Url=http://localhost:15875&& dotnet run --project ""%ROOT%src\api\Clawbot.Api\Clawbot.Api.csproj"" --no-launch-profile"
timeout /t 2 /nobreak >nul

start "ClawBot Gateway :15873" cmd /k "cd /d ""%ROOT%"" && set ASPNETCORE_ENVIRONMENT=Development&& set ASPNETCORE_URLS=http://localhost:15873&& dotnet run --project ""%ROOT%src\gateway\Clawbot.Gateway\Clawbot.Gateway.csproj"" --no-launch-profile"
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
docker exec clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -h -1 -W -Q "SET NOCOUNT ON; IF OBJECT_ID(N'dbo.tenants', N'U') IS NULL SELECT 0 ELSE SELECT 1" > "%SCHEMA_CHECK%" 2>nul
if errorlevel 1 (
    echo [ERROR] Could not inspect clawbot schema.
    exit /b 1
)

set "HAS_SCHEMA=0"
set /p HAS_SCHEMA=<"%SCHEMA_CHECK%"
del "%SCHEMA_CHECK%" >nul 2>nul

if "%HAS_SCHEMA%"=="1" (
    echo [INFO] Existing schema detected; skipping SQL migration replay.
    echo [INFO] For a clean local DB, run: docker compose --env-file deploy\.env -f deploy\docker-compose.yml down -v
    exit /b 0
)

echo [INFO] Applying SQL migrations from deploy\migrations...
pushd "%MIGRATIONS_DIR%" >nul
for %%F in (*.sql) do (
    echo [SQL] %%F
    type "%%F" | docker exec -i clawbot-sqlserver %SQLCMD% -S localhost -U sa -P "%MSSQL_SA_PASSWORD%" -C -d clawbot -b
    if errorlevel 1 (
        popd >nul
        echo [ERROR] Migration failed: %%F
        exit /b 1
    )
)
popd >nul
exit /b 0
