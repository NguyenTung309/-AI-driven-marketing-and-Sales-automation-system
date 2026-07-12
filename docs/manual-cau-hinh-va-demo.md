# Manual cấu hình hệ thống và chạy demo ClawBot

Tài liệu này dành cho người cần tự cấu hình và chạy demo ClawBot ở môi trường dev/local. Nếu chỉ muốn chạy nhanh, dùng file `run-all.bat` ở thư mục gốc repo. Nếu muốn hiểu từng service, từng biến cấu hình và cách demo các luồng, đọc theo thứ tự bên dưới.

## 1. Tổng quan nhanh

ClawBot local chạy bằng 4 service chính:

| Thành phần | URL local | Vai trò |
| --- | --- | --- |
| Frontend React | `http://localhost:15876` | Giao diện quản trị và demo |
| Gateway | `http://localhost:15873` | Proxy cho frontend gọi `/auth`, `/api`, `/hubs` |
| API backend | `http://localhost:15874` | API chính, Swagger, webhook, seed dev admin |
| AgentService | `http://localhost:15875` | Luồng agent, gợi ý trả lời, tài liệu, AI |

Các port này cố ý dùng port 5 chữ số và không dùng `5000` hoặc `5001`.

Các service hạ tầng chạy bằng Docker:

| Thành phần | URL/port | Ghi chú |
| --- | --- | --- |
| SQL Server | `localhost:1433` | Database chính `clawbot` |
| Redis | `localhost:6379` | Cache / realtime support |
| RabbitMQ | `http://localhost:15672` | User mặc định `guest` / `guest` |
| Qdrant | `localhost:6333` | Vector store |
| MinIO | `http://localhost:9001` | User mặc định `minio` / `minio12345` |
| Metabase | `http://localhost:3000` | Dashboard BI local |

## 2. Cài trước

Cài các công cụ sau:

- .NET SDK 8.
- Node.js 20 hoặc mới hơn.
- Docker Desktop.
- Git.
- PowerShell hoặc Windows Terminal.

Kiểm tra nhanh:

```powershell
dotnet --version
node --version
npm --version
docker info
```

Nếu `docker info` lỗi, mở Docker Desktop và đợi Docker chạy xong.

## 3. Tài khoản demo local

Khi API chạy ở `Development`, hệ thống tự seed tenant và admin mặc định:

- Tenant slug: `default`
- Email: `admin@clawbot.local`
- Password: `Admin@12345`

Tài khoản này chỉ dùng cho dev/local. Không dùng password này cho staging hoặc production.

Lưu ý quan trọng: repo hiện chỉ seed tenant/user mặc định cho môi trường `Development`. Nếu chạy staging, production hoặc môi trường riêng mà frontend hiện màn login nhưng chưa đăng nhập được, stack vẫn có thể đã chạy đúng. Khi đó cần tạo tenant và admin user riêng cho môi trường đó trước khi login.

## 4. File cấu hình cần biết

File mẫu nằm ở:

```text
deploy\.env.example
```

Tạo file cấu hình local:

```powershell
copy deploy\.env.example deploy\.env
```

Các nhóm biến quan trọng:

| Biến | Dùng cho | Ghi chú |
| --- | --- | --- |
| `MSSQL_SA_PASSWORD` | SQL Server Docker | Mặc định local là `Clawbot!2026` |
| `RABBITMQ_USER`, `RABBITMQ_PASSWORD` | RabbitMQ | Mặc định `guest` / `guest` |
| `MINIO_USER`, `MINIO_PASSWORD` | MinIO | Mặc định `minio` / `minio12345` |
| `DEMO_MODE` | Script/env mẫu | Dùng cho script demo/readiness; khi chạy `dotnet run` thủ công cần đặt thêm `Demo__Mode=true` cho API |
| `PANCAKE_BASE_URL` | Pancake API | Mặc định `https://pancake.vn/api/v1` |
| `PANCAKE_ACCESS_TOKEN` | Pancake đọc dữ liệu | Cần token thật nếu demo live Pancake |
| `PANCAKE_PAGE_ACCESS_TOKEN` | Pancake gửi tin | Cần token page thật nếu muốn auto-reply gửi ra Pancake |
| `PANCAKE_PAGE_ID` | Pancake page | Page ID thật |
| `PANCAKE_WEBHOOK_SECRET` | Ký webhook Pancake | Phải trùng giữa Pancake và ClawBot |
| `PANCAKE_TENANT_SLUG` | Tenant nhận webhook | Local dev dùng `default` |
| `CLAWBOT_PUBLIC_BASE_URL` | URL public nhận webhook | Dùng URL tunnel hoặc URL deploy thật |
| `ANTHROPIC_API_KEY`, `EMBEDDING_API_KEY`, `CONTENT_LLM_API_KEY` | AI/LLM | Không có key thì các luồng AI thật có thể không chạy đầy đủ |
| `Meta__Graph__AppId`, `Meta__Graph__AppSecret`, `Meta__Graph__ConfigurationId`, `Meta__Graph__AuthorizationMode` | Meta Graph/Marketing API | Fallback tùy chọn; local dùng `development_user`, production dùng `business_system_user`; cấu hình chính được tenant lưu mã hóa tại `/system` > Kênh đăng bài |
| `TIKTOK_ACCESS_TOKEN` | TikTok Ads | Chỉ cần khi demo TikTok Ads live |

Điểm dễ nhầm: `deploy\.env` được Docker Compose đọc nhưng nhóm `Meta__Graph__*` chỉ là bootstrap fallback. Quản trị viên tenant có thể nhập/cập nhật Meta App trực tiếp tại `/system`; cấu hình DB mã hóa luôn được ưu tiên. Với các credential khác, hãy đặt biến môi trường trong terminal đang chạy service hoặc dùng màn cấu hình tương ứng.

Riêng tích hợp Meta Graph API v25.0, xem hướng dẫn đầy đủ tại `docs/meta-facebook-graph-v25.md`. Không dán Page Access Token của Facebook vào màn quản trị; token được nhận qua Facebook Login for Business và mã hóa trong database. Chế độ `development_user` cho phép test localhost bằng User access token của tài khoản có vai trò trong App; production dùng System-user access token.

## 5. Chạy nhanh bằng one-click

Từ thư mục gốc repo:

```bat
run-all.bat
```

Script này sẽ:

1. Tạo `deploy\.env` nếu chưa có.
2. Chạy các container hạ tầng.
3. Tạo database `clawbot` nếu chưa có.
4. Replay migration SQL nếu database chưa có schema.
5. Restore/build .NET.
6. Cài frontend dependency nếu thiếu `node_modules`.
7. Mở 4 cửa sổ terminal cho AgentService, API, Gateway và frontend.

Kiểm tra script mà chưa chạy thật:

```bat
run-all.bat --dry-run
```

Sau khi chạy xong, mở:

```text
http://localhost:15876
```

Login bằng tài khoản dev ở mục 3.

## 6. Chạy thủ công từng phần

Phần này dùng khi muốn debug từng service hoặc không muốn dùng `run-all.bat`.

### 6.1. Chạy hạ tầng Docker

```powershell
docker compose --env-file deploy\.env -f deploy\docker-compose.yml up -d sqlserver redis rabbitmq qdrant minio postgres metabase
```

### 6.2. Tạo database nếu thiếu

Nếu database `clawbot` chưa có, tạo bằng SQL Server container:

```powershell
docker exec clawbot-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Clawbot!2026" -C -Q "IF DB_ID(N'clawbot') IS NULL CREATE DATABASE clawbot;"
```

Nếu container không có `/opt/mssql-tools18/bin/sqlcmd`, thử đường dẫn cũ:

```powershell
docker exec clawbot-sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "Clawbot!2026" -C -Q "IF DB_ID(N'clawbot') IS NULL CREATE DATABASE clawbot;"
```

Với database mới hoàn toàn, cách ít lỗi nhất là để `run-all.bat` bootstrap schema lần đầu. Sau đó có thể dừng các cửa sổ app và chạy thủ công từng service. Nếu muốn tự replay migration, chạy các file `.sql` trong `deploy\migrations` theo thứ tự tên file.

### 6.3. Restore và build

```powershell
dotnet restore Clawbot.sln
dotnet build Clawbot.sln --no-restore
```

### 6.4. Chạy AgentService

Mở terminal 1:

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
$env:ASPNETCORE_URLS="http://localhost:15875"
dotnet run --project src\agents\Clawbot.AgentService\Clawbot.AgentService.csproj --no-launch-profile
```

### 6.5. Chạy API backend

Mở terminal 2:

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
$env:ASPNETCORE_URLS="http://localhost:15874"
$env:AgentService__Url="http://localhost:15875"
$env:Jwt__SigningKey="dev-only-jwt-signing-key-change-before-staging-0123456789"
$env:Demo__Mode="true"
$env:Demo__SkipHmac="true"
dotnet run --project src\api\Clawbot.Api\Clawbot.Api.csproj --no-launch-profile
```

Khi API khởi động thành công ở `Development`, seed `default` tenant và admin local sẽ được tạo hoặc sửa lại nếu thiếu.

### 6.6. Chạy Gateway

Mở terminal 3:

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
$env:ASPNETCORE_URLS="http://localhost:15873"
$env:Jwt__SigningKey="dev-only-jwt-signing-key-change-before-staging-0123456789"
dotnet run --project src\gateway\Clawbot.Gateway\Clawbot.Gateway.csproj --no-launch-profile
```

`Jwt__SigningKey` của API và Gateway phải giống nhau. Nếu khác, login có thể xong nhưng các request `/api` bị `401`.

### 6.7. Chạy frontend

Mở terminal 4:

```powershell
cd src\frontend\clawbot-web
npm ci
npm run dev -- --host 0.0.0.0 --port 15876
```

Frontend dùng Vite proxy:

- `/auth` sang Gateway `http://localhost:15873`.
- `/api` sang Gateway `http://localhost:15873`.
- `/hubs` sang Gateway `http://localhost:15873`.

## 7. Kiểm tra hệ thống đã sống

Mở các URL sau:

```text
http://localhost:15874/health/ready
http://localhost:15874/swagger
http://localhost:15876/login
```

Kết quả mong muốn:

- Health trả `Healthy` hoặc response sẵn sàng.
- Swagger mở được.
- Frontend login được bằng `admin@clawbot.local` / `Admin@12345`.

Nếu frontend mở được nhưng login lỗi, kiểm tra terminal API trước. Seed chỉ chạy sau khi API kết nối được SQL Server và khởi động thành công.

## 8. Cấu hình Pancake để demo live

Nếu chỉ demo UI local, có thể bỏ qua mục này. Nếu muốn nhận message/comment thật từ Pancake và auto-reply, cần cấu hình live.

### 8.1. Cấu hình trong env/script

Trong `deploy\.env`, đặt các biến sau:

```text
PANCAKE_BASE_URL=https://pancake.vn/api/v1
PANCAKE_ACCESS_TOKEN=<token-doc-du-lieu>
PANCAKE_PAGE_ACCESS_TOKEN=<token-gui-tin-cua-page>
PANCAKE_PAGE_ID=<page-id>
PANCAKE_TENANT_SLUG=default
PANCAKE_WEBHOOK_SECRET=<secret-ban-tu-dat>
CLAWBOT_PUBLIC_BASE_URL=<url-public-tro-ve-api-hoac-proxy-co-forward-webhooks>
```

Với local, `CLAWBOT_PUBLIC_BASE_URL` phải là URL public từ tunnel như ngrok/cloudflared. URL đó cần forward được tới endpoint webhook thật:

```text
POST /webhooks/pancake/{tenantSlug}
```

Ví dụ nếu tunnel trỏ trực tiếp vào API backend `http://localhost:15874`, callback sẽ là:

```text
https://<tunnel-domain>/webhooks/pancake/default
```

Lưu ý: endpoint webhook ingest thật nằm trên API backend ở `/webhooks/pancake/{tenantSlug}`. Nếu đi qua Gateway, hãy chắc chắn proxy đang forward đúng path `/webhooks/**`.

### 8.2. Cấu hình trong màn System

Trong frontend:

1. Login admin.
2. Vào `/system`.
3. Mở phần tích hợp Pancake.
4. Nhập `Base URL`, `Access token`, `Webhook secret`, signature header/algo/encoding nếu Pancake account yêu cầu khác mặc định.
5. Bật kết nối.
6. Lưu lại.
7. Copy webhook URL hiển thị trên màn hình và cấu hình vào Pancake dashboard.

Nếu màn `/system` lưu Pancake bị `404`, mở Swagger ở `http://localhost:15874/swagger` và kiểm tra endpoint channel của branch đang chạy. Backend hiện tại map group `/api/channels`, còn một số tài liệu/UI cũ có thể dùng prefix `/api/channels/pancake`.

### 8.3. Đăng ký webhook bằng script

Sau khi set env, chạy dry-run trước:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File deploy\pancake-webhook-subscribe.ps1 -DryRun
```

Nếu body và callback URL đúng, chạy thật:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File deploy\pancake-webhook-subscribe.ps1
```

Script mặc định đăng ký callback:

```text
{CLAWBOT_PUBLIC_BASE_URL}/webhooks/pancake/{PANCAKE_TENANT_SLUG}
```

### 8.4. Replay payload đã capture

Khi đã có payload Pancake thật, đặt:

```text
PANCAKE_WEBHOOK_PAYLOAD=deploy\samples\pancake-comment-webhook.json
```

Hoặc trỏ tới file payload bạn capture được. Chạy:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File deploy\pancake-webhook-replay.ps1 -DryRun
powershell -NoProfile -ExecutionPolicy Bypass -File deploy\pancake-webhook-replay.ps1
```

Kết quả mong muốn:

- API trả `202 Accepted`.
- Inbox có conversation/message mới.
- Nếu payload là comment và cấu hình gửi ra đủ, job auto-reply được enqueue.

## 9. Chạy demo các luồng chính

Các luồng dưới đây ưu tiên demo bằng UI để người xem dễ hiểu. Có thể dùng Swagger ở `http://localhost:15874/swagger` để kiểm tra API tương ứng.

### 9.1. Luồng login và phân quyền

1. Mở `http://localhost:15876/login`.
2. Login bằng `admin@clawbot.local` / `Admin@12345`.
3. Sau khi vào app, mở `/profile` để xem thông tin user.
4. Thử đổi mật khẩu hoặc bật/tắt 2FA nếu cần demo bảo mật.

Kết quả mong muốn:

- Login thành công.
- UI hiển thị menu theo quyền của admin.
- `/auth/me` trả tenant `default`, roles và permissions.

### 9.2. Luồng public widget tạo lead

Luồng này không cần Pancake live.

1. Mở `http://localhost:15876/chat-widget/default`.
2. Nhập tên, số điện thoại, email nếu form yêu cầu.
3. Gửi tin nhắn tư vấn.
4. Quay lại app admin, mở `/conversations`.
5. Mở `/leads`.

Kết quả mong muốn:

- Widget tạo contact.
- Widget tạo lead nguồn `web-widget`.
- Inbox có conversation mới.
- Lead xuất hiện trong danh sách lead.

Nếu không truyền tenant slug, frontend dùng mặc định `default`:

```text
http://localhost:15876/chat-widget
```

### 9.3. Luồng FAQ / support page

1. Mở `http://localhost:15876/support/default`.
2. Kiểm tra branding, tên hỗ trợ và danh sách câu hỏi.
3. Nếu FAQ trống, vào `/kb` để tạo module, version và test case.
4. Deploy version KB.
5. Reload support page.

Kết quả mong muốn:

- Support page lấy FAQ từ KB active.
- Câu hỏi/đáp án đã publish xuất hiện ngoài trang public.

Nếu màn `/kb` bị lỗi route API trong branch hiện tại, kiểm tra Swagger để dùng endpoint KB thật của backend trước khi demo.

### 9.4. Luồng Inbox và chăm sóc hội thoại

Có 2 cách tạo dữ liệu:

- Cách dễ: dùng public widget ở mục 9.2.
- Cách live: nhận webhook Pancake ở mục 8.

Sau khi có conversation:

1. Mở `/conversations`.
2. Chọn conversation mới.
3. Gửi tin nhắn outbound.
4. Assign, resolve hoặc escalate conversation.
5. Export CSV nếu cần trình diễn dữ liệu hội thoại.

Kết quả mong muốn:

- Tin nhắn vào/ra hiển thị trong thread.
- Realtime cập nhật nếu SignalR hoạt động.
- Trạng thái conversation thay đổi đúng.

### 9.5. Luồng Sale Assist

Luồng này cần AgentService chạy. Nếu cần câu trả lời AI thật, cần cấu hình LLM key phù hợp.

1. Mở `/conversations`.
2. Chọn một conversation có nội dung.
3. Mở panel Sale Assist.
4. Tạo draft trả lời.
5. Gửi feedback cho draft hoặc dùng quick reply.
6. Xem daily summary hoặc upsell suggestions nếu có dữ liệu.

Kết quả mong muốn:

- Sale Assist đọc được conversation.
- Draft/summary hiển thị.
- Nếu thiếu LLM key, hệ thống có thể fallback hoặc báo lỗi cấu hình thay vì gửi được draft thật.

### 9.6. Luồng Lead CRM

1. Mở `/leads`.
2. Tạo lead mới hoặc dùng lead sinh từ widget.
3. Ghi activity cho lead.
4. Assign owner.
5. Lọc theo stage/source/owner.
6. Export CSV.
7. Mở forecast.

Kết quả mong muốn:

- Lead lưu vào DB.
- Activity hiển thị ở detail/context.
- Forecast có kết quả nếu có đủ dữ liệu lịch sử. Nếu dữ liệu ít, API có thể trả note kiểu `need_at_least_7_days_of_data`.

### 9.7. Luồng Documents

1. Mở `/documents`.
2. Kiểm tra danh sách template.
3. Tạo hoặc chỉnh template nếu cần.
4. Generate document hoặc generate kit.
5. Download tài liệu đã tạo.
6. Nếu demo gửi tài liệu, cấu hình kênh gửi trước.

Kết quả mong muốn:

- Tài liệu được generate và lưu.
- Download hoạt động.
- Trạng thái gửi/open tracking cập nhật nếu kênh gửi và tracking được cấu hình.

### 9.8. Luồng Content

1. Mở `/content`.
2. Tạo content brief.
3. Scan trends nếu có cấu hình nguồn trend.
4. Generate content item.
5. Approve/reject.
6. Schedule.
7. Mở calendar để xem lịch.

Kết quả mong muốn:

- Brief và item được lưu.
- Queue/calendar cập nhật.
- Publish live cần credential publisher/Meta/TikTok thật. Nếu chưa có credential, chỉ demo được phần chuẩn bị nội dung trong app.

### 9.9. Luồng Analytics

1. Mở `/analytics`.
2. Chọn khoảng ngày.
3. Xem omnichannel, funnel, agent performance, forecast, anomaly.
4. Export báo cáo nếu cần.

Kết quả mong muốn:

- Dashboard load được dữ liệu hiện có.
- Nếu môi trường mới chưa có event/lead/conversation, số liệu có thể thấp hoặc trống. Tạo dữ liệu bằng widget, lead và inbox trước khi demo analytics.

### 9.10. Luồng Admin/System

1. Mở `/system`.
2. Tạo user mới.
3. Tạo role hoặc chỉnh permission.
4. Tạo API key.
5. Cấu hình branding tenant.
6. Cấu hình Pancake nếu demo live.
7. Xem audit logs.

Kết quả mong muốn:

- Thay đổi user/role/API key/branding lưu được.
- Public widget và support page đổi branding theo tenant.
- Audit log ghi lại thao tác quản trị quan trọng.

### 9.11. Luồng demo trace kỹ thuật `/api/demo`

Luồng này dùng để demo pipeline webhook theo kiểu trace từng bước. Cần API runtime có `Demo__Mode=true`. Nếu chỉ sửa `DEMO_MODE=true` trong `deploy\.env` nhưng không import vào process API, `/api/demo` vẫn có thể không bật.

Kiểm tra demo mode:

```text
GET http://localhost:15874/api/demo/status
```

Gửi webhook demo:

```text
POST http://localhost:15874/api/demo/webhook/pancake
```

Xem trace:

```text
GET http://localhost:15874/api/demo/traces
GET http://localhost:15874/api/demo/traces/{traceId}
```

Kết quả mong muốn:

- Trace cho thấy các bước gateway, ingest, agent, outbound.
- Nếu thiếu token/page ID, outbound thật có thể bị skip hoặc fail có lý do rõ ràng.

## 10. Kiểm tra readiness trước demo lớn

Chạy report readiness:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File deploy\ci\verify-go-live-readiness.ps1 -ReportOnly -SkipDockerProbe
```

Khi chuẩn bị go-live thật, bỏ `-SkipDockerProbe` và chạy strict:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File deploy\ci\verify-go-live-readiness.ps1 -Strict
```

Report có thể báo thiếu credential Pancake, LLM, Meta/TikTok, payload webhook thật hoặc KB authoring. Đó là việc cấu hình môi trường, không nhất thiết là lỗi code.

## 11. Troubleshooting nhanh

| Hiện tượng | Cách kiểm tra / xử lý |
| --- | --- |
| Frontend mở được nhưng login lỗi | Kiểm tra terminal API `:15874`; API phải chạy `Development`, kết nối được SQL Server và seed admin xong |
| Login được nhưng request `/api` trả `401` | Kiểm tra `Jwt__SigningKey` của API và Gateway phải giống nhau |
| Swagger không mở | API backend chưa chạy hoặc port `15874` bị chiếm |
| Frontend gọi API lỗi proxy | Gateway `:15873` chưa chạy hoặc Vite proxy không tới được Gateway |
| DB lỗi schema | Dùng `run-all.bat` để bootstrap lại, hoặc reset volume local nếu không cần dữ liệu cũ |
| Muốn reset DB local | Chạy `docker compose --env-file deploy\.env -f deploy\docker-compose.yml down -v`, sau đó chạy lại `run-all.bat` |
| Widget báo tenant not found | Dùng tenant slug `default` ở local dev, ví dụ `/chat-widget/default` |
| Pancake webhook không vào Inbox | Kiểm tra callback public trỏ đúng `/webhooks/pancake/default`, secret/signature đúng, API nhận được request |
| Auto-reply không gửi ra Pancake | Kiểm tra `PANCAKE_PAGE_ACCESS_TOKEN`, `PANCAKE_PAGE_ID`, send path/base URL và log API |
| Sale Assist không tạo draft | Kiểm tra AgentService `:15875` và LLM key |
| `/system` lưu Pancake bị 404 | Kiểm tra Swagger của backend branch đang chạy; một số file cũ dùng `/api/channels/pancake/config`, backend hiện có thể map `/api/channels/config` |
| Analytics trống | Tạo dữ liệu bằng widget, leads, conversations trước rồi reload dashboard |

## 12. Nguyên tắc an toàn khi cấu hình

- Không commit `deploy\.env` có secret thật.
- Không dùng `admin@clawbot.local` / `Admin@12345` ngoài dev/local.
- Không bật credential live trên máy demo không kiểm soát.
- Khi dùng webhook live, dùng secret riêng cho từng môi trường.
- Khi reset DB bằng `down -v`, toàn bộ dữ liệu local trong volume sẽ mất.
