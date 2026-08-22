# Playwright E2E — content publishing

## Default (mock API)

Does **not** need API/SQL/AgentService. Start Vite yourself (Playwright CLI webServer is flaky on this Windows harness):

```bash
cd src/frontend/clawbot-web
npm run dev -- --host 127.0.0.1 --port 15876
# other terminal:
npm run test:e2e            # dual-screen policy
npm run test:e2e:publish    # approve → schedule → retry publish
npm run test:e2e:all        # both suites
```

`/auth/*` and `/api/*` are route-mocked. Access token is in-memory — mocks keep `sessionActive` so `/auth/refresh` rehydrates after full `page.goto`.

### Policy suite (`run-mock.mjs`)

1. Admin changes policy on `/content` → `/agents` (Cấu hình duyệt) shows same value
2. Admin changes policy on `/agents` → `/content` shows same value
3. Marketer (no `system:config`) sees disabled radios on both screens
4. Policy radio group is keyboard operable

### Publish-flow suite (`run-publish-mock.mjs`)

1. Fail-closed: schedule + approve disabled while agent review incomplete
2. Approve (human_required) creates golden-hour schedule (AutoScheduler path)
3. Manual “Đổi lịch” dialog for approved item posts `scheduledAt: null` (golden)
4. Calendar “Xếp thử đăng lại” re-queues Hangfire only (no browser provider call)

### Report-content suite (`run-report-content-mock.mjs`)

`npm run test:e2e:report-content` — báo cáo marketing do report-agent chốt (`/reports/{id}`):

1. `content_snapshot` hiện nhãn "Hiệu suất nội dung", không rơi về chuỗi thô
2. Bảng đúng bộ cột nội dung và **không** còn cột KPI sale (Lead / Chuyển đổi)
3. Số liệu định dạng vi-VN (`1.234`)
4. Nút Tải Excel gọi đúng `/api/reports/{id}/export?format=xlsx`
5. `content_funnel` hiện nhãn "Phễu duyệt nội dung" với cột trạng thái quy trình

Header bảng bị CSS `uppercase` nên assert theo text của `<th>`, không theo accessible name.

## Live stack

Start full stack (or keep Vite + start API/Gateway/AgentService like `run-all.bat`):

- Web `http://127.0.0.1:15876`
- Gateway `http://127.0.0.1:15873`
- API `http://127.0.0.1:15874`
- SQL Server `localhost,11433` (Docker `clawbot-sqlserver`)

Schema for content publishing must be applied on existing DBs (`deploy/repair_tenant_runtime_columns.sql`, `0076`/`0077`). `run-all.bat` does this; if API fails on missing `content_publishing_*` columns, apply those SQL files manually.

```powershell
cd src/frontend/clawbot-web
# dual-screen policy via Playwright CLI (can hang on this Windows harness):
$env:E2E_LIVE = "1"
npm run test:e2e:live

# publish-flow pre-social path on real tenant (programmatic, preferred):
npm run test:e2e:publish:live
```

`test:e2e:publish:live` seeds 4 fixtures into SQL, logs in as `admin@clawbot.local`, and exercises fail-closed / approve→golden schedule / manual schedule / calendar Hangfire retry. Facebook Graph may still fail later in Hangfire — that is out of scope. Only `/api/content/publish-targets` is stubbed when Meta connection is not fully usable, so the schedule dialog can submit; all approve/schedule/retry calls hit the real API.

## Notes

- Prefer programmatic runners (`node e2e/run-*.mjs`); `playwright test` CLI may hang here.
- Controlled radios: click wrapping label after StatusPill hydrate — not Playwright `.check()`.
- Live: after login use SPA nav (sidebar “Quản lý nội dung”), not full `page.goto` — access token is in-memory and remount races `/auth/refresh`.
- Content editor body is the textarea under label “Nội dung bài viết”; the first page textarea is the brief form.
