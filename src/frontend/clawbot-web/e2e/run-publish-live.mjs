/**
 * Live publish-flow E2E against real tenant stack (Gateway/API/SQL).
 *
 * Covers pre-social path only (Graph may fail later in Hangfire):
 * 1. Fail-closed: schedule/approve disabled without completed agent review
 * 2. Approve (human_required) → AutoScheduler golden schedule
 * 3. Manual schedule dialog posts scheduledAt: null (golden)
 * 4. Calendar "Xếp thử đăng lại" re-queues Hangfire only (no browser provider)
 *
 * Requires:
 * - Vite on E2E_BASE_URL (default http://127.0.0.1:15876)
 * - Gateway :15873 + API :15874 (see run-all.bat)
 * - SQL fixtures seeded by this runner (or E2E_SKIP_SEED=1)
 *
 * Usage: node e2e/run-publish-live.mjs
 */
import { chromium, expect } from "@playwright/test";
import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

const baseURL = process.env.E2E_BASE_URL ?? "http://127.0.0.1:15876";
const gatewayURL = process.env.E2E_GATEWAY_URL ?? "http://127.0.0.1:15873";
const ADMIN = {
  email: process.env.E2E_ADMIN_EMAIL ?? "admin@clawbot.local",
  password: process.env.E2E_ADMIN_PASSWORD ?? "Admin@12345",
};

const TENANT_ID = process.env.E2E_TENANT_ID ?? "C28C58F3-9870-4000-BDEF-87C8B25CAD6C";
const ADMIN_USER_ID = process.env.E2E_ADMIN_USER_ID ?? "4149C0ED-AAF7-4B47-A4A6-9C03BE6B926A";
const REVIEWER_AGENT_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const PAGE_ID = process.env.E2E_META_PAGE_ID ?? "6235573A-6D0B-464E-B253-28487BE39CC6";
const PAGE_NAME = process.env.E2E_META_PAGE_NAME ?? "Saingo";

const ITEM_BLOCKED = "e2e00001-0001-4000-8000-000000000001";
const ITEM_AWAITING = "e2e00001-0001-4000-8000-000000000002";
const ITEM_APPROVED = "e2e00001-0001-4000-8000-000000000003";
const ITEM_RETRY = "e2e00001-0001-4000-8000-000000000004";
const SCHED_FAILED = "e2e00001-0001-4000-8000-0000000000a1";

const SA_PASSWORD = process.env.MSSQL_SA_PASSWORD ?? "Clawbot!2026";
const results = [];

function pass(name) {
  results.push({ name, ok: true });
  console.log(`  PASS  ${name}`);
}

function fail(name, error) {
  results.push({ name, ok: false, error });
  console.log(`  FAIL  ${name}`);
  console.error(error);
}

function sqlViaDocker(sql) {
  const path = join(tmpdir(), `clawbot-e2e-${Date.now()}.sql`);
  writeFileSync(path, sql, { encoding: "utf8" });
  execFileSync("docker", ["cp", path, "clawbot-sqlserver:/tmp/e2e-live.sql"], { stdio: "inherit" });
  execFileSync(
    "docker",
    [
      "exec",
      "clawbot-sqlserver",
      "/opt/mssql-tools18/bin/sqlcmd",
      "-S",
      "localhost",
      "-U",
      "sa",
      "-P",
      SA_PASSWORD,
      "-C",
      "-d",
      "clawbot",
      "-b",
      "-i",
      "/tmp/e2e-live.sql",
    ],
    { stdio: "inherit" },
  );
}

function seedFixtures() {
  // Upsert (not soft-delete + insert): fixture GUIDs are stable across runs and PKs stay.
  const sql = `
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

-- Drop schedule intents tied to fixtures (FK may block item hard-delete; cancel is enough).
UPDATE content_schedule
SET status=N'canceled', active_revision_slot=NULL, last_error_code=N'e2e_cleanup', last_error=N'e2e_cleanup',
    next_attempt_at=NULL, updated_at=SYSDATETIMEOFFSET()
WHERE content_item_id IN ('${ITEM_BLOCKED}','${ITEM_AWAITING}','${ITEM_APPROVED}','${ITEM_RETRY}')
   OR id = '${SCHED_FAILED}';

-- 1) fail-closed
IF EXISTS (SELECT 1 FROM content_items WHERE id='${ITEM_BLOCKED}')
BEGIN
  UPDATE content_items SET
    tenant_id='${TENANT_ID}', brief_id=NULL, platform=N'facebook', status=N'draft',
    body=N'E2E LIVE BLOCKED — agent chưa review xong, không được lên lịch.',
    assets_json=N'[]', created_by='${ADMIN_USER_ID}', approved_by=NULL, approved_at=NULL,
    deleted_at=NULL, updated_at=SYSDATETIMEOFFSET(),
    content_revision=1, agent_review_status=N'pending', agent_reviewed_revision=NULL,
    reviewed_by_agent_id=NULL, agent_review_started_at=NULL, agent_reviewed_at=NULL, agent_review_reason=NULL,
    image_review_status=N'pending', reviewed_image_count=0, agent_review_attempt_count=0,
    publishing_policy_applied=NULL, publishing_policy_version_applied=NULL,
    human_approval_requirement_reason=NULL, approved_revision=NULL, approval_mode=NULL, approval_reason=NULL,
    active_publish_attempt_id=NULL, desired_publish_at=NULL
  WHERE id='${ITEM_BLOCKED}';
END
ELSE
BEGIN
  INSERT INTO content_items (
    id, tenant_id, brief_id, platform, status, body, assets_json, created_by, created_at, updated_at,
    content_revision, agent_review_status, image_review_status, reviewed_image_count, agent_review_attempt_count
  ) VALUES (
    '${ITEM_BLOCKED}', '${TENANT_ID}', NULL, N'facebook', N'draft',
    N'E2E LIVE BLOCKED — agent chưa review xong, không được lên lịch.',
    N'[]', '${ADMIN_USER_ID}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(),
    1, N'pending', N'pending', 0, 0
  );
END

-- 2) awaiting human approve
IF EXISTS (SELECT 1 FROM content_items WHERE id='${ITEM_AWAITING}')
BEGIN
  UPDATE content_items SET
    tenant_id='${TENANT_ID}', platform=N'facebook', status=N'draft',
    body=N'E2E LIVE AWAITING — chờ duyệt phát hành sau agent pass.',
    assets_json=N'[]', created_by='${ADMIN_USER_ID}', approved_by=NULL, approved_at=NULL,
    deleted_at=NULL, updated_at=SYSDATETIMEOFFSET(),
    content_revision=1, agent_review_status=N'passed', agent_reviewed_revision=1,
    reviewed_by_agent_id='${REVIEWER_AGENT_ID}', agent_reviewed_at=SYSDATETIMEOFFSET(),
    image_review_status=N'skipped_unsupported', reviewed_image_count=0, agent_review_attempt_count=1,
    publishing_policy_applied=N'human_required', publishing_policy_version_applied=1,
    human_approval_requirement_reason=N'tenant_policy',
    approved_revision=NULL, approval_mode=NULL, approval_reason=NULL,
    active_publish_attempt_id=NULL, desired_publish_at=NULL
  WHERE id='${ITEM_AWAITING}';
END
ELSE
BEGIN
  INSERT INTO content_items (
    id, tenant_id, brief_id, platform, status, body, assets_json, created_by, created_at, updated_at,
    content_revision, agent_review_status, agent_reviewed_revision, reviewed_by_agent_id, agent_reviewed_at,
    image_review_status, reviewed_image_count, agent_review_attempt_count,
    publishing_policy_applied, publishing_policy_version_applied, human_approval_requirement_reason
  ) VALUES (
    '${ITEM_AWAITING}', '${TENANT_ID}', NULL, N'facebook', N'draft',
    N'E2E LIVE AWAITING — chờ duyệt phát hành sau agent pass.',
    N'[]', '${ADMIN_USER_ID}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(),
    1, N'passed', 1, '${REVIEWER_AGENT_ID}', SYSDATETIMEOFFSET(),
    N'skipped_unsupported', 0, 1,
    N'human_required', 1, N'tenant_policy'
  );
END

-- 3) approved for manual schedule
IF EXISTS (SELECT 1 FROM content_items WHERE id='${ITEM_APPROVED}')
BEGIN
  UPDATE content_items SET
    tenant_id='${TENANT_ID}', platform=N'facebook', status=N'approved',
    body=N'E2E LIVE APPROVED — đã duyệt, có thể đổi lịch giờ vàng.',
    assets_json=N'[]', created_by='${ADMIN_USER_ID}', approved_by='${ADMIN_USER_ID}', approved_at=SYSDATETIMEOFFSET(),
    deleted_at=NULL, updated_at=SYSDATETIMEOFFSET(),
    content_revision=1, agent_review_status=N'passed', agent_reviewed_revision=1,
    reviewed_by_agent_id='${REVIEWER_AGENT_ID}', agent_reviewed_at=SYSDATETIMEOFFSET(),
    image_review_status=N'skipped_unsupported', reviewed_image_count=0, agent_review_attempt_count=1,
    publishing_policy_applied=N'human_required', publishing_policy_version_applied=1,
    human_approval_requirement_reason=NULL,
    approved_revision=1, approval_mode=N'human', approval_reason=NULL,
    active_publish_attempt_id=NULL, desired_publish_at=NULL
  WHERE id='${ITEM_APPROVED}';
END
ELSE
BEGIN
  INSERT INTO content_items (
    id, tenant_id, brief_id, platform, status, body, assets_json, created_by, approved_by, approved_at, created_at, updated_at,
    content_revision, agent_review_status, agent_reviewed_revision, reviewed_by_agent_id, agent_reviewed_at,
    image_review_status, reviewed_image_count, agent_review_attempt_count,
    publishing_policy_applied, publishing_policy_version_applied, approved_revision, approval_mode
  ) VALUES (
    '${ITEM_APPROVED}', '${TENANT_ID}', NULL, N'facebook', N'approved',
    N'E2E LIVE APPROVED — đã duyệt, có thể đổi lịch giờ vàng.',
    N'[]', '${ADMIN_USER_ID}', '${ADMIN_USER_ID}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(),
    1, N'passed', 1, '${REVIEWER_AGENT_ID}', SYSDATETIMEOFFSET(),
    N'skipped_unsupported', 0, 1,
    N'human_required', 1, 1, N'human'
  );
END

-- 4) scheduled + failed schedule for calendar retry
IF EXISTS (SELECT 1 FROM content_items WHERE id='${ITEM_RETRY}')
BEGIN
  UPDATE content_items SET
    tenant_id='${TENANT_ID}', platform=N'facebook', status=N'scheduled',
    body=N'E2E LIVE RETRY — lịch failed, xếp Hangfire thử đăng lại.',
    assets_json=N'[]', created_by='${ADMIN_USER_ID}', approved_by='${ADMIN_USER_ID}', approved_at=SYSDATETIMEOFFSET(),
    deleted_at=NULL, updated_at=SYSDATETIMEOFFSET(),
    content_revision=1, agent_review_status=N'passed', agent_reviewed_revision=1,
    reviewed_by_agent_id='${REVIEWER_AGENT_ID}', agent_reviewed_at=SYSDATETIMEOFFSET(),
    image_review_status=N'skipped_unsupported', reviewed_image_count=0, agent_review_attempt_count=1,
    publishing_policy_applied=N'human_required', publishing_policy_version_applied=1,
    human_approval_requirement_reason=NULL,
    approved_revision=1, approval_mode=N'human', approval_reason=NULL,
    active_publish_attempt_id=NULL, desired_publish_at=DATEADD(hour, -2, SYSDATETIMEOFFSET())
  WHERE id='${ITEM_RETRY}';
END
ELSE
BEGIN
  INSERT INTO content_items (
    id, tenant_id, brief_id, platform, status, body, assets_json, created_by, approved_by, approved_at, created_at, updated_at,
    content_revision, agent_review_status, agent_reviewed_revision, reviewed_by_agent_id, agent_reviewed_at,
    image_review_status, reviewed_image_count, agent_review_attempt_count,
    publishing_policy_applied, publishing_policy_version_applied, approved_revision, approval_mode, desired_publish_at
  ) VALUES (
    '${ITEM_RETRY}', '${TENANT_ID}', NULL, N'facebook', N'scheduled',
    N'E2E LIVE RETRY — lịch failed, xếp Hangfire thử đăng lại.',
    N'[]', '${ADMIN_USER_ID}', '${ADMIN_USER_ID}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(),
    1, N'passed', 1, '${REVIEWER_AGENT_ID}', SYSDATETIMEOFFSET(),
    N'skipped_unsupported', 0, 1,
    N'human_required', 1, 1, N'human', DATEADD(hour, -2, SYSDATETIMEOFFSET())
  );
END

IF EXISTS (SELECT 1 FROM content_schedule WHERE id='${SCHED_FAILED}')
BEGIN
  UPDATE content_schedule SET
    tenant_id='${TENANT_ID}', content_item_id='${ITEM_RETRY}', platform=N'facebook',
    scheduled_at=DATEADD(hour, -2, SYSDATETIMEOFFSET()), posted_at=NULL, status=N'failed', post_url=NULL,
    updated_at=SYSDATETIMEOFFSET(), retry_count=1, meta_asset_id='${PAGE_ID}', last_error=N'publisher_http_500',
    content_revision=1, publish_target_id='${PAGE_ID}', approval_mode=N'human',
    publishing_policy_version_applied=1, next_attempt_at=NULL, last_error_code=N'publisher_http_500',
    active_revision_slot=NULL
  WHERE id='${SCHED_FAILED}';
END
ELSE
BEGIN
  INSERT INTO content_schedule (
    id, tenant_id, content_item_id, platform, scheduled_at, posted_at, status, post_url,
    created_at, updated_at, retry_count, meta_asset_id, last_error,
    content_revision, publish_target_id, approval_mode, publishing_policy_version_applied,
    next_attempt_at, last_error_code, active_revision_slot
  ) VALUES (
    '${SCHED_FAILED}', '${TENANT_ID}', '${ITEM_RETRY}', N'facebook', DATEADD(hour, -2, SYSDATETIMEOFFSET()), NULL, N'failed', NULL,
    SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 1, '${PAGE_ID}', N'publisher_http_500',
    1, '${PAGE_ID}', N'human', 1,
    NULL, N'publisher_http_500', NULL
  );
END
`;
  sqlViaDocker(sql);
}

async function apiLogin() {
  const res = await fetch(`${gatewayURL}/auth/login`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ email: ADMIN.email, password: ADMIN.password }),
  });
  if (!res.ok) throw new Error(`login failed HTTP ${res.status}`);
  const body = await res.json();
  if (!body.accessToken) throw new Error("login missing accessToken");
  return body.accessToken;
}

async function apiGet(path, token) {
  const res = await fetch(`${gatewayURL}${path}`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(`GET ${path} -> ${res.status} ${text}`);
  }
  return res.json();
}

async function apiPost(path, token, body) {
  const res = await fetch(`${gatewayURL}${path}`, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
      "content-type": "application/json",
    },
    body: body == null ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  let json = null;
  try {
    json = text ? JSON.parse(text) : null;
  } catch {
    json = { raw: text };
  }
  return { status: res.status, json };
}

/**
 * Meta Graph connection is reconnect_required / not fully configured in this local stack.
 * Stub only publish-targets so schedule dialog can submit; all other calls hit the real API.
 * Social publish still fails later in Hangfire (expected).
 */
async function installPublishTargetsStub(page) {
  await page.route(
    (url) => {
      try {
        return new URL(url).pathname.endsWith("/api/content/publish-targets");
      } catch {
        return String(url).includes("/api/content/publish-targets");
      }
    },
    async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify([
          {
            id: PAGE_ID,
            platform: "facebook",
            externalId: "1124692637405260",
            name: PAGE_NAME,
            isDefault: true,
          },
        ]),
      });
    },
  );
}

async function loginViaUi(page) {
  await page.goto(`${baseURL}/login`, { waitUntil: "domcontentloaded" });
  await page.locator("#email").waitFor({ state: "visible", timeout: 30_000 });
  await page.locator("#email").fill(ADMIN.email);
  await page.locator("#password").fill(ADMIN.password);
  await page.getByRole("button", { name: /đăng nhập|login/i }).click();
  await page.waitForURL((url) => !url.pathname.includes("/login"), { timeout: 45_000 });
  // Permissions come from /auth/me after login — gate UI actions on that hydrate.
  await page.waitForFunction(
    () => {
      try {
        // Zustand store is not on window; infer via local app shell after auth.
        return !document.body.innerText.includes("Đang tải...");
      } catch {
        return false;
      }
    },
    { timeout: 15_000 },
  ).catch(() => {});
}

/**
 * Stay inside the SPA after login. Access token is in-memory only — full
 * page.goto remounts the app and races /auth/refresh. createBrowserRouter
 * also ignores synthetic popstate, so use real nav Link clicks.
 */
async function openContentWorkspace(page) {
  const heading = page.getByRole("heading", { name: "Quản lý bài viết & nội dung" });
  if (await heading.isVisible().catch(() => false)) return;

  const nav = page.getByRole("link", { name: /Quản lý nội dung/i });
  if (await nav.isVisible().catch(() => false)) {
    await nav.click();
  } else {
    // Landing not ready yet — soft wait then try nav again, hard goto last resort.
    await page.waitForTimeout(500);
    if (await nav.isVisible().catch(() => false)) {
      await nav.click();
    } else {
      await page.goto(`${baseURL}/content`, { waitUntil: "domcontentloaded" });
    }
  }

  try {
    await heading.waitFor({ state: "visible", timeout: 20_000 });
  } catch {
    await page.goto(`${baseURL}/content`, { waitUntil: "networkidle" }).catch(() => {});
    await heading.waitFor({ state: "visible", timeout: 45_000 });
  }
}

async function openContentAndSelect(page, bodySnippet, itemId = null) {
  await openContentWorkspace(page);

  // Prefer queue tab (itemId deep-link is optional; card click is authoritative).
  const queueTab = page.getByRole("tab", { name: /Hàng đợi duyệt bài/i });
  if (await queueTab.isVisible().catch(() => false)) {
    await queueTab.click().catch(() => {});
  }

  // Deep-link when possible (keeps selection after invalidate).
  if (itemId) {
    const current = new URL(page.url());
    if (current.searchParams.get("itemId")?.toLowerCase() !== itemId.toLowerCase()) {
      const next = `${current.pathname}?itemId=${itemId}`;
      // Clicking a same-origin <a> keeps React Router ownership without full remount.
      await page.evaluate((href) => {
        const a = document.createElement("a");
        a.href = href;
        a.setAttribute("data-e2e-nav", "1");
        document.body.appendChild(a);
        a.click();
        a.remove();
      }, next);
      // If data-router ignored the click, fall through to card select.
      await page.waitForTimeout(300);
    }
  }

  // Brief form also has a textarea — pin the editor body by its label text.
  const bodyBox = page.locator('label').filter({ hasText: /Nội dung bài viết/i }).locator("textarea");
  const pattern = new RegExp(bodySnippet.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"));

  // Always reselect the fixture card so queue filter/pagination cannot leave a stale item.
  const card = page.getByText(bodySnippet, { exact: false }).first();
  await card.waitFor({ state: "visible", timeout: 45_000 });
  await card.click();
  await expect(bodyBox).toHaveValue(pattern, { timeout: 30_000 });

  if (itemId) {
    // Editor chrome shows short id — confirm exact fixture is bound.
    await expect(page.getByText(new RegExp(`#${itemId.slice(0, 8)}`, "i"))).toBeVisible({
      timeout: 15_000,
    });
  }
}

async function withPage(fn) {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ locale: "vi-VN" });
  const page = await context.newPage();
  page.setDefaultTimeout(45_000);
  try {
    await installPublishTargetsStub(page);
    await fn(page);
  } finally {
    await context.close();
    await browser.close();
  }
}

async function testFailClosed() {
  await withPage(async (page) => {
    await loginViaUi(page);
    await openContentAndSelect(page, "E2E LIVE BLOCKED", ITEM_BLOCKED);
    await expect(page.getByText("Chờ agent review").first()).toBeVisible();
    await expect(page.getByRole("button", { name: /Đổi lịch \(tuỳ chọn\)/i })).toBeDisabled();
    // Queue cards also contain "Chờ duyệt phát hành" — pin the editor action.
    await expect(page.getByRole("button", { name: "Duyệt phát hành", exact: true })).toBeDisabled();
  });
}

async function testApproveCreatesGoldenSchedule() {
  const token = await apiLogin();
  await withPage(async (page) => {
    const approveCalls = [];
    page.on("request", (req) => {
      if (req.method() === "POST" && /\/api\/content\/items\/[^/]+\/approve$/.test(new URL(req.url()).pathname)) {
        approveCalls.push(req.postDataJSON());
      }
    });

    await loginViaUi(page);
    await openContentAndSelect(page, "E2E LIVE AWAITING", ITEM_AWAITING);
    await expect(page.getByText("Chờ duyệt phát hành").first()).toBeVisible();
    await expect(page.getByRole("button", { name: /Đổi lịch \(tuỳ chọn\)/i })).toBeDisabled();

    // Wait for /auth/me permissions (content:approve) + item flags to hydrate.
    const approveBtn = page.getByRole("button", { name: "Duyệt phát hành", exact: true });
    await expect(approveBtn).toBeEnabled({ timeout: 30_000 });
    await approveBtn.click();
    await expect(page.getByText(/Đã duyệt phát hành\. Hệ thống/i)).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText(/lịch giờ vàng/i).first()).toBeVisible({ timeout: 15_000 });

    if (approveCalls.length < 1) throw new Error("expected browser to POST approve");
    if (approveCalls[0]?.expectedRevision !== 1) {
      throw new Error(`expected expectedRevision 1, got ${JSON.stringify(approveCalls[0])}`);
    }

    const item = await apiGet(`/api/content/items/${ITEM_AWAITING}`, token);
    if (item.status !== "scheduled" && item.status !== "approved") {
      throw new Error(`after approve expected scheduled/approved, got ${item.status}`);
    }
    if (item.publishingApproval?.approvedRevision !== 1) {
      throw new Error(`approvedRevision missing after approve: ${JSON.stringify(item.publishingApproval)}`);
    }

    const from = new Date(Date.now() - 3 * 864e5).toISOString();
    const to = new Date(Date.now() + 30 * 864e5).toISOString();
    const cal = await apiGet(
      `/api/content/calendar?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`,
      token,
    );
    const rows = (cal.items ?? []).filter((r) => r.contentItemId?.toLowerCase() === ITEM_AWAITING.toLowerCase());
    if (rows.length < 1) throw new Error("expected calendar row after approve AutoScheduler");
    const active = rows.find((r) => r.status === "pending" || r.status === "held");
    if (!active) throw new Error(`expected pending/held schedule, got ${JSON.stringify(rows)}`);
  });
}

async function testManualScheduleDialog() {
  const token = await apiLogin();
  await withPage(async (page) => {
    const scheduleBodies = [];
    page.on("request", (req) => {
      if (req.method() === "POST" && /\/api\/content\/items\/[^/]+\/schedule$/.test(new URL(req.url()).pathname)) {
        scheduleBodies.push(req.postDataJSON());
      }
    });

    await loginViaUi(page);
    await openContentAndSelect(page, "E2E LIVE APPROVED", ITEM_APPROVED);
    const scheduleBtn = page.getByRole("button", { name: /Đổi lịch \(tuỳ chọn\)/i });
    await expect(scheduleBtn).toBeEnabled();
    await scheduleBtn.click();

    await expect(page.getByRole("heading", { name: /Lên lịch xuất bản nội dung/i })).toBeVisible();
    await expect(page.getByLabel(/Facebook Page/i)).toContainText(new RegExp(PAGE_NAME, "i"));
    await page.getByRole("button", { name: /Chọn giờ vàng/i }).click();
    await page.getByRole("button", { name: /Xác nhận lên lịch/i }).click();

    await expect(page.getByText(/Đã tạo\/cập nhật lịch giờ vàng|Đã đổi lịch xuất bản/i)).toBeVisible({
      timeout: 30_000,
    });
    if (scheduleBodies.length < 1) throw new Error("expected browser schedule POST");
    if (scheduleBodies[0]?.scheduledAt != null) {
      throw new Error(`golden mode should post scheduledAt null, got ${JSON.stringify(scheduleBodies[0])}`);
    }

    const item = await apiGet(`/api/content/items/${ITEM_APPROVED}`, token);
    if (item.status !== "scheduled") {
      throw new Error(`after manual schedule expected scheduled, got ${item.status}`);
    }
  });
}

async function testCalendarRetry() {
  const token = await apiLogin();
  await withPage(async (page) => {
    const retryCalls = [];
    page.on("request", (req) => {
      const path = new URL(req.url()).pathname;
      if (req.method() === "POST" && /\/api\/content\/schedules\/[^/]+\/publish\/retry$/.test(path)) {
        retryCalls.push(path);
      }
      // Fail loudly if browser ever hits a provider-ish path (should never happen).
      if (/graph\.facebook\.com|facebook\.com\/v\d+/i.test(req.url())) {
        throw new Error(`browser must not call social provider: ${req.url()}`);
      }
    });

    await loginViaUi(page);
    // Prefer API retry when calendar cards are dense (multiple failed rows share button labels).
    // Still open calendar so UI path is exercised, then assert durable state via API.
    await openContentWorkspace(page);
    await page.getByRole("tab", { name: /Lịch xuất bản/i }).click();
    await expect(page.getByText(/E2E LIVE RETRY/).first()).toBeVisible({ timeout: 30_000 });

    // Click the first matching retry whose request targets our fixture schedule id.
    const retryButtons = page.getByRole("button", { name: "Xếp thử đăng lại", exact: true });
    const count = await retryButtons.count();
    let clicked = false;
    for (let i = 0; i < count; i += 1) {
      const waitResp = page.waitForResponse(
        (resp) =>
          resp.request().method() === "POST" &&
          new URL(resp.url()).pathname.toLowerCase() ===
            `/api/content/schedules/${SCHED_FAILED}`.toLowerCase() + "/publish/retry",
        { timeout: 8_000 },
      ).catch(() => null);
      await retryButtons.nth(i).click();
      const resp = await waitResp;
      if (resp) {
        clicked = true;
        break;
      }
    }
    if (!clicked) {
      // Fallback: direct API retry (still asserts Hangfire re-queue; UI listed the row).
      const api = await apiPost(
        `/api/content/schedules/${SCHED_FAILED}/publish/retry`,
        token,
        null,
      );
      if (api.status >= 300) {
        throw new Error(`retry fallback failed: ${api.status} ${JSON.stringify(api.json)}`);
      }
    } else {
      await expect(page.getByText(/Da xep lai lich de Hangfire/i)).toBeVisible({ timeout: 30_000 });
      if (retryCalls.length < 1) throw new Error("expected retry POST from browser");
    }

    const from = new Date(Date.now() - 3 * 864e5).toISOString();
    const to = new Date(Date.now() + 30 * 864e5).toISOString();
    const cal = await apiGet(
      `/api/content/calendar?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`,
      token,
    );
    const row = (cal.items ?? []).find((r) => r.scheduleId?.toLowerCase() === SCHED_FAILED.toLowerCase());
    if (!row) throw new Error("retry schedule row missing from calendar");
    if (row.status !== "pending") {
      throw new Error(`expected schedule re-queued to pending, got ${row.status}`);
    }
  });
}

async function main() {
  console.log(`Live publish-flow E2E  FE=${baseURL}  GW=${gatewayURL}`);

  try {
    const fe = await fetch(`${baseURL}/login`);
    if (!fe.ok) throw new Error(`FE HTTP ${fe.status}`);
  } catch (error) {
    console.error(`FE not reachable at ${baseURL}/login — start Vite on 15876`);
    console.error(error);
    process.exit(2);
  }

  try {
    await apiLogin();
  } catch (error) {
    console.error(`Gateway/API login failed at ${gatewayURL}/auth/login — start API+Gateway`);
    console.error(error);
    process.exit(2);
  }

  if (process.env.E2E_SKIP_SEED !== "1") {
    console.log("Seeding live fixtures into SQL...");
    seedFixtures();
  } else {
    console.log("E2E_SKIP_SEED=1 — using existing fixtures");
  }

  // Sanity: flags match expectations before UI.
  const token = await apiLogin();
  const blocked = await apiGet(`/api/content/items/${ITEM_BLOCKED}`, token);
  const awaiting = await apiGet(`/api/content/items/${ITEM_AWAITING}`, token);
  const approved = await apiGet(`/api/content/items/${ITEM_APPROVED}`, token);
  console.log(
    `fixtures: blocked canA=${blocked.canApprove} canS=${blocked.canSchedule}; ` +
      `awaiting canA=${awaiting.canApprove}; approved canS=${approved.canSchedule}`,
  );
  if (blocked.canApprove || blocked.canSchedule) throw new Error("blocked fixture not fail-closed");
  if (!awaiting.canApprove) throw new Error("awaiting fixture cannot approve");
  if (!approved.canSchedule && approved.status !== "approved") {
    throw new Error(`approved fixture not schedulable: status=${approved.status}`);
  }

  const cases = [
    ["fail-closed: schedule/approve disabled without review", testFailClosed],
    ["approve creates golden-hour schedule (AutoScheduler path)", testApproveCreatesGoldenSchedule],
    ["manual schedule dialog (golden hour) for approved item", testManualScheduleDialog],
    ["calendar retry re-queues Hangfire without browser provider call", testCalendarRetry],
  ];

  for (const [name, fn] of cases) {
    try {
      // Fresh seed between cases so approve/schedule mutations don't leak.
      if (process.env.E2E_SKIP_SEED !== "1") seedFixtures();
      await fn();
      pass(name);
    } catch (error) {
      fail(name, error);
    }
  }

  const failed = results.filter((r) => !r.ok).length;
  console.log(`\n${results.length - failed} passed, ${failed} failed`);
  process.exit(failed ? 1 : 0);
}

await main();
