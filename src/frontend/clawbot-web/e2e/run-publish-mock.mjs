/**
 * Programmatic Playwright runner for content publish flow E2E (mock API).
 *
 * Covers on /content:
 * 1. Fail-closed: schedule disabled until canSchedule
 * 2. Approve (human_required) → golden schedule created (BE AutoScheduler)
 * 3. Manual schedule dialog for approved item (optional reschedule)
 * 4. Calendar retry publish re-queues Hangfire (no provider call from browser)
 *
 * Usage: node e2e/run-publish-mock.mjs
 * Requires Vite: http://127.0.0.1:15876
 */
import { chromium, expect } from "@playwright/test";

const baseURL = process.env.E2E_BASE_URL ?? "http://127.0.0.1:15876";

const ADMIN = {
  email: process.env.E2E_ADMIN_EMAIL ?? "admin@clawbot.local",
  password: process.env.E2E_ADMIN_PASSWORD ?? "Admin@12345",
};

const ADMIN_PERMS = [
  "system:config",
  "content:read",
  "content:write",
  "content:approve",
  "content:publish",
  "agents:read",
  "agents:manage",
];

const ITEM_AWAITING = "11111111-1111-1111-1111-111111111111";
const ITEM_APPROVED = "22222222-2222-2222-2222-222222222222";
const ITEM_BLOCKED = "33333333-3333-3333-3333-333333333333";
const SCHED_FAILED = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const PAGE_ID = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

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

function nowIso() {
  return new Date().toISOString();
}

function goldenAt() {
  const d = new Date();
  d.setHours(d.getHours() + 3);
  d.setMinutes(0, 0, 0);
  return d.toISOString();
}

function emptyList() {
  return { items: [], total: 0, page: 1, pageSize: 50, nextCursor: null };
}

function makeItem(overrides = {}) {
  const id = overrides.id ?? crypto.randomUUID();
  const revision = overrides.contentRevision ?? 1;
  const base = {
    id,
    briefId: null,
    platform: "facebook",
    status: "draft",
    body: "Bài E2E mock — nội dung chờ phát hành.",
    assetsJson: "[]",
    createdBy: "e2e",
    approvedBy: null,
    approvedAt: null,
    createdAt: nowIso(),
    updatedAt: nowIso(),
    contentRevision: revision,
    agentReview: {
      status: "passed",
      reviewedRevision: revision,
      reviewedByAgentId: "agent-content",
      reviewedAt: nowIso(),
      reason: null,
      imageReviewStatus: "skipped",
      reviewedImageCount: 0,
    },
    publishingApproval: {
      status: "pending",
      policyApplied: "human_required",
      policyVersionApplied: 1,
      approvedRevision: null,
      mode: "human",
      approvedBy: null,
      approvedAt: null,
      reason: null,
      requirementReason: "Cần người duyệt sau agent review.",
    },
    workflowState: "awaiting_human_approval",
    canApprove: true,
    canReject: true,
    canRetryReview: false,
    canSchedule: false,
    canPublish: false,
  };
  return { ...base, ...overrides };
}

function makeSchedule(overrides = {}) {
  return {
    id: overrides.id ?? crypto.randomUUID(),
    contentItemId: overrides.contentItemId,
    platform: overrides.platform ?? "facebook",
    scheduledAt: overrides.scheduledAt ?? goldenAt(),
    postedAt: overrides.postedAt ?? null,
    status: overrides.status ?? "pending",
    postUrl: overrides.postUrl ?? null,
    createdAt: nowIso(),
    updatedAt: nowIso(),
    metaAssetId: overrides.metaAssetId ?? PAGE_ID,
    likeCount: null,
    commentCount: null,
    engagementSyncedAt: null,
    retryCount: overrides.retryCount ?? 0,
    lastError: overrides.lastError ?? null,
  };
}

function toCalendarRow(schedule, item) {
  return {
    scheduleId: schedule.id,
    contentItemId: schedule.contentItemId,
    platform: schedule.platform,
    status: schedule.status,
    body: item?.body ?? "",
    scheduledAt: schedule.scheduledAt,
    postedAt: schedule.postedAt,
    postUrl: schedule.postUrl,
    metaAssetId: schedule.metaAssetId,
    likeCount: schedule.likeCount,
    commentCount: schedule.commentCount,
    retryCount: schedule.retryCount,
    lastError: schedule.lastError,
  };
}

function seedStore(scenario) {
  const items = new Map();
  const schedules = new Map();

  if (scenario === "approve-flow") {
    items.set(
      ITEM_AWAITING,
      makeItem({
        id: ITEM_AWAITING,
        body: "E2E AWAITING — chờ duyệt phát hành sau agent pass.",
        status: "draft",
        workflowState: "awaiting_human_approval",
        canApprove: true,
        canSchedule: false,
      }),
    );
  }

  if (scenario === "schedule-flow") {
    items.set(
      ITEM_APPROVED,
      makeItem({
        id: ITEM_APPROVED,
        body: "E2E APPROVED — đã duyệt, có thể đổi lịch giờ vàng.",
        status: "approved",
        workflowState: "approved_for_publish",
        canApprove: false,
        canReject: false,
        canSchedule: true,
        approvedBy: "e2e-admin",
        approvedAt: nowIso(),
        publishingApproval: {
          status: "approved",
          policyApplied: "human_required",
          policyVersionApplied: 1,
          approvedRevision: 1,
          mode: "human",
          approvedBy: "e2e-admin",
          approvedAt: nowIso(),
          reason: null,
          requirementReason: null,
        },
      }),
    );
  }

  if (scenario === "fail-closed") {
    items.set(
      ITEM_BLOCKED,
      makeItem({
        id: ITEM_BLOCKED,
        body: "E2E BLOCKED — agent chưa review xong, không được lên lịch.",
        status: "draft",
        workflowState: "awaiting_agent_review",
        canApprove: false,
        canReject: false,
        canRetryReview: false,
        canSchedule: false,
        agentReview: {
          status: "pending",
          reviewedRevision: null,
          reviewedByAgentId: null,
          reviewedAt: null,
          reason: null,
          imageReviewStatus: "pending",
          reviewedImageCount: 0,
        },
        publishingApproval: {
          status: "not_ready",
          policyApplied: "human_required",
          policyVersionApplied: 1,
          approvedRevision: null,
          mode: null,
          approvedBy: null,
          approvedAt: null,
          reason: null,
          requirementReason: "Chưa có agent review pass cho revision hiện tại.",
        },
      }),
    );
  }

  if (scenario === "retry-flow") {
    const item = makeItem({
      id: ITEM_APPROVED,
      body: "E2E RETRY — lịch failed, xếp Hangfire thử đăng lại.",
      status: "scheduled",
      workflowState: "scheduled",
      canApprove: false,
      canReject: false,
      canSchedule: true,
      publishingApproval: {
        status: "approved",
        policyApplied: "human_required",
        policyVersionApplied: 1,
        approvedRevision: 1,
        mode: "human",
        approvedBy: "e2e-admin",
        approvedAt: nowIso(),
        reason: null,
        requirementReason: null,
      },
    });
    items.set(item.id, item);
    const past = new Date();
    past.setHours(past.getHours() - 2);
    schedules.set(
      SCHED_FAILED,
      makeSchedule({
        id: SCHED_FAILED,
        contentItemId: item.id,
        status: "failed",
        scheduledAt: past.toISOString(),
        lastError: "publisher_http_500",
        retryCount: 1,
      }),
    );
  }

  return { items, schedules };
}

async function installMockApi(page, { scenario }) {
  const policy = {
    publishingApprovalPolicy: "human_required",
    policyVersion: 1,
    reviewerVisionCapability: "unknown",
    agentReviewRequired: true,
    agentReviewMode: "mandatory",
    updatedAt: nowIso(),
  };
  const store = seedStore(scenario);
  let sessionActive = false;
  const accessToken = "e2e-mock-access-token";
  const expiresAt = () => new Date(Date.now() + 3600_000).toISOString();
  const audit = {
    approveCalls: 0,
    scheduleCalls: 0,
    retryCalls: 0,
    lastScheduleBody: null,
  };

  const json = async (route, status, body) => {
    await route.fulfill({
      status,
      contentType: "application/json",
      body: body == null ? "" : JSON.stringify(body),
    });
  };

  const listItems = () => Array.from(store.items.values());
  const listSchedules = () => Array.from(store.schedules.values());

  await page.route(
    (url) => {
      try {
        return new URL(url).pathname.startsWith("/auth");
      } catch {
        return String(url).includes("/auth");
      }
    },
    async (route) => {
      const request = route.request();
      const method = request.method();
      const pathName = new URL(request.url()).pathname;

      if (method === "POST" && pathName.endsWith("/auth/refresh")) {
        if (!sessionActive) return json(route, 401, { error: "no_session" });
        return json(route, 200, { accessToken, expiresAt: expiresAt() });
      }
      if (method === "POST" && pathName.endsWith("/auth/login")) {
        const body = request.postDataJSON();
        if (body.email === ADMIN.email && body.password === ADMIN.password) {
          sessionActive = true;
          return json(route, 200, { accessToken, expiresAt: expiresAt() });
        }
        return json(route, 401, { error: "invalid_credentials" });
      }
      if (method === "GET" && pathName.endsWith("/auth/me")) {
        if (!sessionActive) return json(route, 401, { error: "unauthorized" });
        return json(route, 200, {
          id: "00000000-0000-0000-0000-000000000002",
          email: ADMIN.email,
          displayName: "E2E Admin",
          permissions: ADMIN_PERMS,
        });
      }
      if (method === "POST" && pathName.endsWith("/auth/logout")) {
        sessionActive = false;
        return json(route, 204, null);
      }
      return json(route, 404, { error: `unmocked auth ${method} ${pathName}` });
    },
  );

  await page.route(
    (url) => {
      try {
        return new URL(url).pathname.startsWith("/api");
      } catch {
        return String(url).includes("/api");
      }
    },
    async (route) => {
      const request = route.request();
      const method = request.method();
      const pathName = new URL(request.url()).pathname;

      if (method === "GET" && pathName.endsWith("/api/content/settings/publishing-policy")) {
        return json(route, 200, { ...policy });
      }

      if (method === "GET" && pathName.endsWith("/api/content/briefs")) {
        return json(route, 200, emptyList());
      }

      if (method === "GET" && pathName.endsWith("/api/content/trends")) {
        return json(route, 200, { trends: [] });
      }

      if (method === "GET" && pathName.endsWith("/api/content/publish-targets")) {
        return json(route, 200, [
          {
            id: PAGE_ID,
            platform: "facebook",
            externalId: "page-123",
            name: "Học Bá E2E Page",
            isDefault: true,
          },
        ]);
      }

      if (method === "GET" && pathName.endsWith("/api/content/queue")) {
        const items = listItems();
        return json(route, 200, {
          items,
          total: items.length,
          page: 1,
          pageSize: 50,
          nextCursor: null,
        });
      }

      if (method === "GET" && pathName.endsWith("/api/content/calendar")) {
        const rows = listSchedules().map((s) => toCalendarRow(s, store.items.get(s.contentItemId)));
        return json(route, 200, { items: rows });
      }

      if (method === "GET" && /^\/api\/content\/items\/[^/]+$/.test(pathName)) {
        const id = pathName.split("/").pop();
        const item = store.items.get(id);
        if (!item) return json(route, 404, { error: "not_found" });
        return json(route, 200, item);
      }

      if (method === "POST" && /\/api\/content\/items\/[^/]+\/approve$/.test(pathName)) {
        const id = pathName.split("/")[4];
        const item = store.items.get(id);
        if (!item) return json(route, 404, { error: "not_found" });
        if (!item.canApprove) {
          return json(route, 409, {
            errorCode: "content.cannot_approve",
            message: "Không thể duyệt phát hành ở trạng thái hiện tại.",
          });
        }
        audit.approveCalls += 1;
        const body = request.postDataJSON() ?? {};
        const expected = body.expectedRevision ?? item.contentRevision;
        if (expected !== item.contentRevision) {
          return json(route, 409, {
            errorCode: "content.revision_conflict",
            message: "Revision đã thay đổi.",
          });
        }

        // Mirror BE: Approve + ContentAutoScheduler.CreateIntentAsync in same txn.
        const at = goldenAt();
        const next = {
          ...item,
          status: "scheduled",
          workflowState: "scheduled",
          canApprove: false,
          canReject: false,
          canSchedule: true,
          approvedBy: "e2e-admin",
          approvedAt: nowIso(),
          updatedAt: nowIso(),
          publishingApproval: {
            status: "approved",
            policyApplied: "human_required",
            policyVersionApplied: policy.policyVersion,
            approvedRevision: item.contentRevision,
            mode: "human",
            approvedBy: "e2e-admin",
            approvedAt: nowIso(),
            reason: body.overrideReason ?? null,
            requirementReason: null,
          },
        };
        store.items.set(id, next);

        const existing = listSchedules().find((s) => s.contentItemId === id && s.status !== "cancelled");
        if (existing) {
          store.schedules.set(existing.id, {
            ...existing,
            status: "pending",
            scheduledAt: at,
            lastError: null,
            updatedAt: nowIso(),
          });
        } else {
          const schedule = makeSchedule({
            contentItemId: id,
            scheduledAt: at,
            status: "pending",
            metaAssetId: PAGE_ID,
          });
          store.schedules.set(schedule.id, schedule);
        }
        return json(route, 200, next);
      }

      if (method === "POST" && /\/api\/content\/items\/[^/]+\/schedule$/.test(pathName)) {
        const id = pathName.split("/")[4];
        const item = store.items.get(id);
        if (!item) return json(route, 404, { error: "not_found" });
        if (!item.canSchedule && item.status !== "approved") {
          return json(route, 409, {
            errorCode: "content.cannot_schedule",
            message: "Bài chưa đủ điều kiện lên lịch (agent review / duyệt phát hành).",
          });
        }
        audit.scheduleCalls += 1;
        const body = request.postDataJSON() ?? {};
        audit.lastScheduleBody = body;
        const at = body.scheduledAt || goldenAt();
        const metaAssetId = body.metaAssetId ?? PAGE_ID;

        let schedule = listSchedules().find((s) => s.contentItemId === id && s.status !== "cancelled");
        if (schedule) {
          schedule = {
            ...schedule,
            scheduledAt: at,
            status: "pending",
            metaAssetId,
            lastError: null,
            updatedAt: nowIso(),
          };
        } else {
          schedule = makeSchedule({
            contentItemId: id,
            scheduledAt: at,
            status: "pending",
            metaAssetId,
          });
        }
        store.schedules.set(schedule.id, schedule);

        const next = {
          ...item,
          status: "scheduled",
          workflowState: "scheduled",
          canSchedule: true,
          updatedAt: nowIso(),
        };
        store.items.set(id, next);
        return json(route, 200, schedule);
      }

      if (method === "POST" && /\/api\/content\/schedules\/[^/]+\/publish\/retry$/.test(pathName)) {
        const id = pathName.split("/")[4];
        const schedule = store.schedules.get(id);
        if (!schedule) return json(route, 404, { error: "not_found" });
        audit.retryCalls += 1;
        // Durable re-queue only — never call social publisher from this endpoint.
        const next = {
          ...schedule,
          status: "pending",
          lastError: null,
          retryCount: (schedule.retryCount ?? 0) + 1,
          updatedAt: nowIso(),
        };
        store.schedules.set(id, next);
        return json(route, 200, next);
      }

      if (method === "DELETE" && /\/api\/content\/schedule\/[^/]+$/.test(pathName)) {
        const id = pathName.split("/").pop();
        const schedule = store.schedules.get(id);
        if (!schedule) return json(route, 404, { error: "not_found" });
        store.schedules.set(id, {
          ...schedule,
          status: "cancelled",
          updatedAt: nowIso(),
        });
        return json(route, 204, null);
      }

      if (method === "GET" && pathName.endsWith("/api/jobs")) {
        return json(route, 200, { items: [], total: 0 });
      }
      if (method === "GET" && pathName.includes("/api/notifications")) {
        return json(route, 200, { items: [], total: 0, nextCursor: null, unreadCount: 0 });
      }
      if (method === "GET" && pathName.endsWith("/api/notifications/unread-count")) {
        return json(route, 200, { count: 0 });
      }

      if (method === "GET") return json(route, 200, emptyList());
      return json(route, 200, { ok: true });
    },
  );

  return {
    audit,
    getItems: () => listItems(),
    getSchedules: () => listSchedules(),
  };
}

async function loginViaUi(page) {
  await page.goto(`${baseURL}/login`, { waitUntil: "domcontentloaded" });
  await page.locator("#email").waitFor({ state: "visible", timeout: 30_000 });
  await page.locator("#email").fill(ADMIN.email);
  await page.locator("#password").fill(ADMIN.password);
  await page.getByRole("button", { name: /đăng nhập|login/i }).click();
  await page.waitForURL((url) => !url.pathname.includes("/login"), { timeout: 30_000 });
}

async function openContent(page) {
  await page.goto(`${baseURL}/content`, { waitUntil: "domcontentloaded" });
  await page.getByRole("heading", { name: "Quản lý bài viết & nội dung" }).waitFor({
    state: "visible",
    timeout: 30_000,
  });
}

async function withPage(fn) {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ locale: "vi-VN" });
  const page = await context.newPage();
  try {
    await fn(page);
  } finally {
    await context.close();
    await browser.close();
  }
}

async function testFailClosedScheduleDisabled() {
  await withPage(async (page) => {
    const mock = await installMockApi(page, { scenario: "fail-closed" });
    await loginViaUi(page);
    await openContent(page);

    await expect(page.getByRole("textbox", { name: /Nội dung bài viết/i })).toHaveValue(
      /E2E BLOCKED/,
      { timeout: 15_000 },
    );
    await expect(page.getByText("Chờ agent review").first()).toBeVisible();

    const scheduleBtn = page.getByRole("button", { name: /Đổi lịch \(tuỳ chọn\)/i });
    await expect(scheduleBtn).toBeDisabled();
    const approveBtn = page.getByRole("button", { name: /Duyệt phát hành/i });
    await expect(approveBtn).toBeDisabled();

    if (mock.audit.scheduleCalls !== 0) {
      throw new Error(`expected 0 schedule calls, got ${mock.audit.scheduleCalls}`);
    }
  });
}

async function testApproveCreatesGoldenSchedule() {
  await withPage(async (page) => {
    const mock = await installMockApi(page, { scenario: "approve-flow" });
    await loginViaUi(page);
    await openContent(page);

    await expect(page.getByRole("textbox", { name: /Nội dung bài viết/i })).toHaveValue(
      /E2E AWAITING/,
      { timeout: 15_000 },
    );
    await expect(page.getByText("Chờ duyệt phát hành").first()).toBeVisible();
    await expect(page.getByRole("button", { name: /Đổi lịch \(tuỳ chọn\)/i })).toBeDisabled();

    await page.getByRole("button", { name: "Duyệt phát hành", exact: true }).click();
    await expect(page.getByText(/Đã duyệt phát hành\. Hệ thống/i)).toBeVisible({ timeout: 15_000 });
    await expect(page.getByText(/lịch giờ vàng/i).first()).toBeVisible({ timeout: 15_000 });

    if (mock.audit.approveCalls !== 1) {
      throw new Error(`expected 1 approve call, got ${mock.audit.approveCalls}`);
    }
    if (mock.getSchedules().filter((s) => s.status === "pending").length < 1) {
      throw new Error("expected pending golden schedule after approve");
    }

    await page.getByRole("tab", { name: /Lịch xuất bản/i }).click();
    await expect(page.getByRole("button", { name: /Hủy lịch/i })).toBeVisible({ timeout: 15_000 });
    await expect(page.getByText(/Chờ đăng/i).first()).toBeVisible();
  });
}

async function testManualScheduleDialog() {
  await withPage(async (page) => {
    const mock = await installMockApi(page, { scenario: "schedule-flow" });
    await loginViaUi(page);
    await openContent(page);

    await expect(page.getByRole("textbox", { name: /Nội dung bài viết/i })).toHaveValue(
      /E2E APPROVED/,
      { timeout: 15_000 },
    );
    const scheduleBtn = page.getByRole("button", { name: /Đổi lịch \(tuỳ chọn\)/i });
    await expect(scheduleBtn).toBeEnabled();
    await scheduleBtn.click();

    await expect(page.getByRole("heading", { name: /Lên lịch xuất bản nội dung/i })).toBeVisible({
      timeout: 10_000,
    });
    await expect(page.getByLabel(/Facebook Page/i)).toContainText(/Học Bá E2E Page/);
    await page.getByRole("button", { name: /Chọn giờ vàng/i }).click();
    await page.getByRole("button", { name: /Xác nhận lên lịch/i }).click();

    await expect(page.getByText(/Đã tạo\/cập nhật lịch giờ vàng|Đã đổi lịch xuất bản/i)).toBeVisible({
      timeout: 15_000,
    });
    if (mock.audit.scheduleCalls !== 1) {
      throw new Error(`expected 1 schedule call, got ${mock.audit.scheduleCalls}`);
    }
    // golden mode posts scheduledAt: null
    if (mock.audit.lastScheduleBody?.scheduledAt != null) {
      throw new Error(
        `golden mode should post scheduledAt null, got ${JSON.stringify(mock.audit.lastScheduleBody)}`,
      );
    }
    if (mock.getSchedules().length < 1) {
      throw new Error("expected schedule row after manual confirm");
    }
  });
}

async function testCalendarRetryPublish() {
  await withPage(async (page) => {
    const mock = await installMockApi(page, { scenario: "retry-flow" });
    await loginViaUi(page);
    await openContent(page);

    await page.getByRole("tab", { name: /Lịch xuất bản/i }).click();
    await expect(page.getByRole("button", { name: /Xếp thử đăng lại/i })).toBeVisible({
      timeout: 15_000,
    });
    await expect(page.getByText(/E2E RETRY/).first()).toBeVisible();

    await page.getByRole("button", { name: /Xếp thử đăng lại/i }).click();

    await expect(page.getByText(/Da xep lai lich de Hangfire/i)).toBeVisible({
      timeout: 15_000,
    });

    if (mock.audit.retryCalls !== 1) {
      throw new Error(`expected 1 retry call, got ${mock.audit.retryCalls}`);
    }
    const schedule = mock.getSchedules().find((s) => s.id === SCHED_FAILED);
    if (!schedule || schedule.status !== "pending") {
      throw new Error(`expected failed schedule re-queued to pending, got ${JSON.stringify(schedule)}`);
    }
  });
}

async function main() {
  try {
    const res = await fetch(`${baseURL}/login`);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
  } catch (error) {
    console.error(`FE not reachable at ${baseURL}/login — start with: npm run dev`);
    console.error(error);
    process.exit(2);
  }

  console.log(`Running content publish-flow E2E against ${baseURL}`);
  const cases = [
    ["fail-closed: schedule/approve disabled without review+approval", testFailClosedScheduleDisabled],
    ["approve creates golden-hour schedule (AutoScheduler path)", testApproveCreatesGoldenSchedule],
    ["manual schedule dialog (golden hour) for approved item", testManualScheduleDialog],
    ["calendar retry re-queues Hangfire without browser provider call", testCalendarRetryPublish],
  ];

  for (const [name, fn] of cases) {
    try {
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
