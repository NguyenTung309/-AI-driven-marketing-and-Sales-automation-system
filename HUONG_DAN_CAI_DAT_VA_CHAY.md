# Huong dan cai dat va chay ClawBot

File nay danh cho may Windows dev/local. Cach nhanh nhat la double-click `run-all.bat` o thu muc goc repo.

## 1. Can cai truoc

- **.NET SDK 8**: kiem tra bang `dotnet --version`.
- **Node.js 20** hoac moi hon: kiem tra bang `node --version` va `npm --version`.
- **Docker Desktop**: mo Docker Desktop truoc khi chay project, sau do kiem tra bang `docker info`.
- **Git**: can cho clone/pull source.

Neu dung Visual Studio, chon Visual Studio 2022 va workload ASP.NET/.NET.

## 2. Chay nhanh bang one-click

Tai thu muc goc `d:\Clawbot`, double-click:

```bat
run-all.bat
```

Script se lam cac viec sau:

1. Tao `deploy/.env` tu `deploy/.env.example` neu file `.env` chua ton tai.
2. Bat Docker services: SQL Server, Redis, RabbitMQ, Qdrant, MinIO, Postgres/Metabase.
3. Tao database `clawbot` neu chua co.
4. Apply cac SQL file trong `deploy/migrations` neu database chua co schema.
5. Chay `dotnet restore` va `dotnet build`.
6. Cai frontend dependencies bang `npm ci` neu `node_modules` chua ton tai.
7. Mo 4 cua so terminal rieng:
   - AgentService: `http://localhost:15875`
   - API backend: `http://localhost:15874`
   - Gateway/proxy: `http://localhost:15873`
   - Frontend: `http://localhost:15876`

Kiem tra script ma khong chay service:

```bat
run-all.bat --dry-run
```

## 3. Dia chi sau khi chay

- Frontend: `http://localhost:15876`
- Gateway/API proxy cho frontend: `http://localhost:15873`
- API backend truc tiep: `http://localhost:15874`
- AgentService gRPC: `http://localhost:15875`
- API Swagger: `http://localhost:15874/swagger`
- API health: `http://localhost:15874/health/ready`
- RabbitMQ dashboard: `http://localhost:15672` (`guest` / `guest`)
- MinIO console: `http://localhost:9001` (`minio` / `minio12345`)
- Metabase: `http://localhost:3000`

Luu y: repo hien chua co default tenant/user seed. Neu frontend hien man login nhung chua dang nhap duoc, stack van da chay; can seed tenant + admin user rieng cho moi truong dev.

## 4. Chay thu cong neu khong dung bat

Tu root repo:

```powershell
copy deploy\.env.example deploy\.env
docker compose --env-file deploy\.env -f deploy\docker-compose.yml up -d sqlserver redis rabbitmq qdrant minio postgres metabase
dotnet restore Clawbot.sln
dotnet build Clawbot.sln --no-restore
```

Mo 4 terminal rieng:

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
$env:ASPNETCORE_URLS="http://localhost:15875"
dotnet run --project src\agents\Clawbot.AgentService\Clawbot.AgentService.csproj --no-launch-profile
```

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
$env:ASPNETCORE_URLS="http://localhost:15874"
$env:AgentService__Url="http://localhost:15875"
dotnet run --project src\api\Clawbot.Api\Clawbot.Api.csproj --no-launch-profile
```

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
$env:ASPNETCORE_URLS="http://localhost:15873"
dotnet run --project src\gateway\Clawbot.Gateway\Clawbot.Gateway.csproj --no-launch-profile
```

```powershell
cd src\frontend\clawbot-web
npm ci
npm run dev -- --port 15876
```

## 5. Cau hinh can biet

- Config local nam trong `deploy/.env`; file mau la `deploy/.env.example`.
- SQL Server local dung password mac dinh `Clawbot!2026` neu ban khong doi `MSSQL_SA_PASSWORD`.
- Frontend Vite proxy `/api`, `/auth`, `/hubs` sang `http://localhost:15873`.
- API backend goi AgentService qua `AgentService__Url=http://localhost:15875`.
- Cac API that nhu Pancake, Anthropic, embedder, Meta/TikTok va publisher co placeholder trong `.env.example`; local dev van co the chay khi chua cau hinh chung, nhung go-live readiness se bao thieu.

## 6. Kiem tra va troubleshooting

Kiem tra build/test nhanh:

```powershell
dotnet test Clawbot.sln --no-restore --filter "FullyQualifiedName!~Integration"
```

Kiem tra go-live readiness:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File deploy\ci\verify-go-live-readiness.ps1 -ReportOnly -SkipDockerProbe
```

`Docker/Testcontainers` can Docker CLI/daemon. Neu Docker chua chay, integration tests va SQL Server container se fail.

Neu database local cu bi lech schema:

```powershell
docker compose --env-file deploy\.env -f deploy\docker-compose.yml down
docker volume rm deploy_sqlserver_data
run-all.bat
```

Neu port bi trung:

- `15876`: frontend Vite.
- `15873`: Gateway cho frontend proxy.
- `15874`: API backend.
- `15875`: AgentService gRPC.
- `1433`, `6379`, `5672`, `6333`, `9000`, `9001`, `15672`, `3000`: Docker services.

Dong project:

```powershell
docker compose --env-file deploy\.env -f deploy\docker-compose.yml down
```

Dong cac cua so terminal `.NET` va `npm` da duoc `run-all.bat` mo.
