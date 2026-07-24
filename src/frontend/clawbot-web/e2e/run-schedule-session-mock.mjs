import { chromium, expect } from "@playwright/test";

const baseURL = process.env.E2E_BASE_URL ?? "http://127.0.0.1:15876";
const ITEM_A_ID = "71717171-7171-7171-7171-717171717171";
const ITEM_B_ID = "72727272-7272-7272-7272-727272727272";
const ITEM_A_BODY = "Schedule session item A";
const ITEM_B_BODY = "Schedule session item B";
const TIMESTAMP = "2026-07-22T08:00:00.000Z";
const CONFIRMATION_LABEL = "Tôi xác nhận dùng tài khoản Instagram độc lập hiện đang cấu hình";
const RESELECTION_GUIDANCE = "Lịch Instagram này cần chọn lại đích đăng. Hãy xác nhận tài khoản Instagram độc lập hiện đang cấu hình hoặc chọn Meta Page liên kết rồi thử lại.";
const admin = {
  email: process.env.E2E_ADMIN_EMAIL ?? "admin@clawbot.local",
  password: process.env.E2E_ADMIN_PASSWORD ?? "Admin@12345",
};
const permissions = [
  "system:config",
  "content:read",
  "content:write",
  "content:approve",
  "content:publish",
  "agents:read",
  "agents:manage",
];

function createDeferred() {
  let resolvePromise;
  const promise = new Promise((resolve) => {
    resolvePromise = resolve;
  });
  return { promise, resolve: (value) => resolvePromise(value) };
}

function contentItem(id, body) {
  return {
    id,
    briefId: null,
    platform: "instagram",
    status: "scheduled",
    body,
    assetsJson: "[]",
    createdBy: "e2e",
    approvedBy: "e2e-admin",
    approvedAt: TIMESTAMP,
    createdAt: TIMESTAMP,
    updatedAt: TIMESTAMP,
    contentRevision: 1,
    agentReview: {
      status: "passed",
      reviewedRevision: 1,
      reviewedByAgentId: "content-reviewer",
      reviewedAt: TIMESTAMP,
      reason: null,
      imageReviewStatus: "reviewed",
      reviewedImageCount: 1,
    },
    publishingApproval: {
      status: "approved",
      policyApplied: "human_required",
      policyVersionApplied: 1,
      approvedRevision: 1,
      mode: "human",
      approvedBy: "e2e-admin",
      approvedAt: TIMESTAMP,
      reason: null,
      requirementReason: null,
    },
    workflowState: "scheduled",
    canApprove: false,
    canReject: false,
    canRetryReview: false,
    canSchedule: true,
    canPublish: false,
  };
}

const itemA = contentItem(ITEM_A_ID, ITEM_A_BODY);
const itemB = contentItem(ITEM_B_ID, ITEM_B_BODY);

function calendarItem(item, scheduleId) {
  return {
    scheduleId,
    contentItemId: item.id,
    platform: item.platform,
    status: "held",
    body: item.body,
    scheduledAt: "2026-07-25T02:00:00.000Z",
    postedAt: null,
    postUrl: null,
    metaAssetId: null,
    likeCount: null,
    commentCount: null,
    retryCount: 0,
    lastError: "Instagram target must be reselected.",
    requiresInstagramAccountConfirmation: true,
  };
}

async function json(route, status, body) {
  await route.fulfill({
    status,
    contentType: "application/json",
    body: body == null ? "" : JSON.stringify(body),
  });
}

async function installMocks(page) {
  let sessionActive = false;
  let pendingRequests = [];
  let requestCounts = { queue: 0, calendar: 0 };
  const accessToken = "schedule-session-mock-token";

  await page.route(
    (url) => new URL(url).pathname.startsWith("/auth"),
    async (route) => {
      const request = route.request();
      const path = new URL(request.url()).pathname;
      if (request.method() === "POST" && path.endsWith("/auth/login")) {
        const body = request.postDataJSON();
        sessionActive = body.email === admin.email && body.password === admin.password;
        return json(route, sessionActive ? 200 : 401, sessionActive
          ? { accessToken, expiresAt: new Date(Date.now() + 3_600_000).toISOString() }
          : { error: "invalid_credentials" });
      }
      if (request.method() === "POST" && path.endsWith("/auth/refresh")) {
        return json(route, sessionActive ? 200 : 401, sessionActive
          ? { accessToken, expiresAt: new Date(Date.now() + 3_600_000).toISOString() }
          : { error: "no_session" });
      }
      if (request.method() === "GET" && path.endsWith("/auth/me")) {
        return json(route, sessionActive ? 200 : 401, sessionActive
          ? { id: "00000000-0000-0000-0000-000000000002", email: admin.email, displayName: "E2E Admin", permissions }
          : { error: "unauthorized" });
      }
      return json(route, 204, null);
    },
  );

  await page.route(
    (url) => new URL(url).pathname.startsWith("/api"),
    async (route) => {
      const request = route.request();
      const path = new URL(request.url()).pathname;
      const method = request.method();

      if (method === "GET" && path === "/api/content/queue") {
        requestCounts = { ...requestCounts, queue: requestCounts.queue + 1 };
        return json(route, 200, { items: [itemA, itemB], total: 2, page: 1, pageSize: 40, nextCursor: null });
      }
      if (method === "GET" && path === `/api/content/items/${ITEM_A_ID}`) return json(route, 200, itemA);
      if (method === "GET" && path === `/api/content/items/${ITEM_B_ID}`) return json(route, 200, itemB);
      if (method === "GET" && path === "/api/content/calendar") {
        requestCounts = { ...requestCounts, calendar: requestCounts.calendar + 1 };
        return json(route, 200, {
          items: [
            calendarItem(itemA, "73737373-7373-7373-7373-737373737373"),
            calendarItem(itemB, "74747474-7474-7474-7474-747474747474"),
          ],
        });
      }
      if (method === "GET" && path === "/api/content/publish-targets") {
        return json(route, 200, { mode: "standalone", items: [] });
      }
      if (method === "GET" && path === "/api/content/settings/publishing-policy") {
        return json(route, 200, {
          publishingApprovalPolicy: "human_required",
          policyVersion: 1,
          reviewerVisionCapability: "unknown",
          agentReviewRequired: true,
          agentReviewMode: "mandatory",
          updatedAt: TIMESTAMP,
        });
      }
      if (method === "GET" && path === "/api/content/trends") return json(route, 200, { trends: [] });
      if (method === "POST" && path.endsWith("/schedule")) {
        const itemId = path.split("/").at(-2);
        const payload = request.postDataJSON();
        const outcome = createDeferred();
        pendingRequests = [...pendingRequests, { itemId, payload: { ...payload }, outcome }];
        const response = await outcome.promise;
        if (response === "conflict") {
          return json(route, 409, {
            errorCode: "content.instagram_target_reselection_required",
            message: "Instagram target must be explicitly reselected before this schedule can be changed.",
          });
        }
        return json(route, 201, {
          id: itemId === ITEM_A_ID
            ? "75757575-7575-7575-7575-757575757575"
            : "76767676-7676-7676-7676-767676767676",
          contentItemId: itemId,
          platform: "instagram",
          scheduledAt: payload.scheduledAt ?? "2026-07-25T02:00:00.000Z",
          postedAt: null,
          status: "pending",
          postUrl: null,
          createdAt: TIMESTAMP,
          updatedAt: TIMESTAMP,
          metaAssetId: payload.metaAssetId,
          likeCount: null,
          commentCount: null,
          engagementSyncedAt: null,
          retryCount: 0,
          lastError: null,
        });
      }
      if (method === "GET" && path.includes("/api/notifications")) {
        return json(route, 200, { items: [], total: 0, nextCursor: null });
      }
      if (method === "GET") return json(route, 200, { items: [], total: 0, page: 1, pageSize: 50 });
      return json(route, 200, { ok: true });
    },
  );

  return {
    getRequests: () => pendingRequests.map(({ itemId, payload }) => ({ itemId, payload: { ...payload } })),
    getRequestCounts: () => ({ ...requestCounts }),
    releaseRequest(index, outcome) {
      const pendingRequest = pendingRequests[index];
      if (!pendingRequest) throw new Error(`Schedule request ${index} has not arrived.`);
      pendingRequest.outcome.resolve(outcome);
    },
  };
}

async function login(page) {
  await page.goto(`${baseURL}/login`, { waitUntil: "domcontentloaded" });
  await page.locator("#email").fill(admin.email);
  await page.locator("#password").fill(admin.password);
  await page.getByRole("button", { name: /đăng nhập|login/i }).click();
  await page.waitForURL((url) => !url.pathname.includes("/login"));
}

function dialog(page) {
  return page.getByRole("dialog", { name: "Lên lịch xuất bản nội dung" });
}

async function openInitialDialog(page) {
  await page.goto(`${baseURL}/content`);
  await expect(page.getByRole("textbox", { name: /Nội dung bài viết/i })).toHaveValue(ITEM_A_BODY);
  await page.getByRole("button", { name: "Đổi lịch (tuỳ chọn)", exact: true }).click();
  const currentDialog = dialog(page);
  await expect(currentDialog).toBeVisible();
  return currentDialog;
}

async function setSpecificSchedule(currentDialog, date, time) {
  await currentDialog.getByRole("button", { name: /Chọn thời điểm riêng/ }).click();
  await currentDialog.getByLabel("Ngày").fill(date);
  await currentDialog.getByLabel("Giờ").fill(time);
  await currentDialog.getByRole("checkbox", { name: CONFIRMATION_LABEL, exact: true }).check();
}

async function submit(currentDialog, mocks, count) {
  await currentDialog.getByRole("button", { name: "Xác nhận lên lịch", exact: true }).click();
  await expect.poll(() => mocks.getRequests().length).toBe(count);
}

async function replaceWithItemB(page) {
  await page.getByRole("button", { name: new RegExp(ITEM_B_BODY) }).dispatchEvent("click");
  await expect(page.getByRole("textbox", { name: /Nội dung bài viết/i })).toHaveValue(ITEM_B_BODY);
  await page.getByRole("button", { name: "Đổi lịch (tuỳ chọn)", exact: true }).dispatchEvent("click");
  const currentDialog = dialog(page);
  await expect(currentDialog).toBeVisible();
  await expect(currentDialog.getByRole("checkbox", { name: CONFIRMATION_LABEL, exact: true })).not.toBeChecked();
  return currentDialog;
}

function expectedScheduledAt(date, time) {
  return new Date(`${date}T${time}:00`).toISOString();
}

async function withPage(run) {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ locale: "vi-VN" });
  const page = await context.newPage();
  try {
    const mocks = await installMocks(page);
    await login(page);
    await run(page, mocks);
  } finally {
    await context.close();
    await browser.close();
  }
}

async function testPendingDismissalIsBlocked() {
  await withPage(async (page, mocks) => {
    const currentDialog = await openInitialDialog(page);
    await setSpecificSchedule(currentDialog, "2026-08-10", "10:15");
    await submit(currentDialog, mocks, 1);
    try {
      await page.keyboard.press("Escape");
      await expect(currentDialog).toBeVisible();
      await page.locator('div.fixed.inset-0[role="presentation"]').click({ position: { x: 2, y: 2 } });
      await expect(currentDialog).toBeVisible();
      const closeButton = currentDialog.getByRole("button", { name: "Đóng", exact: true });
      await expect(closeButton).toBeDisabled();
      await closeButton.click({ force: true });
      await expect(currentDialog).toBeVisible();
    } finally {
      mocks.releaseRequest(0, "success");
    }
  });
}

async function testDelayedSuccessIsFenced() {
  await withPage(async (page, mocks) => {
    const itemADialog = await openInitialDialog(page);
    await setSpecificSchedule(itemADialog, "2026-08-11", "10:30");
    await submit(itemADialog, mocks, 1);
    expect(mocks.getRequests()[0]).toEqual({
      itemId: ITEM_A_ID,
      payload: {
        scheduledAt: expectedScheduledAt("2026-08-11", "10:30"),
        metaAssetId: null,
        confirmInstagramAccount: true,
      },
    });

    const itemBDialog = await replaceWithItemB(page);
    await setSpecificSchedule(itemBDialog, "2026-08-21", "17:45");
    const confirmation = itemBDialog.getByRole("checkbox", { name: CONFIRMATION_LABEL, exact: true });
    const countsBefore = mocks.getRequestCounts();
    mocks.releaseRequest(0, "success");

    await expect(itemBDialog).toBeVisible();
    await expect(itemBDialog.getByLabel("Ngày")).toHaveValue("2026-08-21");
    await expect(itemBDialog.getByLabel("Giờ")).toHaveValue("17:45");
    await expect(confirmation).toBeChecked();
    await expect(page.getByText("Đã đổi lịch xuất bản theo thời điểm bạn chọn.", { exact: true })).toHaveCount(0);
    await expect.poll(() => mocks.getRequestCounts().queue).toBeGreaterThan(countsBefore.queue);
    await expect.poll(() => mocks.getRequestCounts().calendar).toBeGreaterThan(countsBefore.calendar);
  });
}

async function testDelayedConflictIsFenced() {
  await withPage(async (page, mocks) => {
    const itemADialog = await openInitialDialog(page);
    await setSpecificSchedule(itemADialog, "2026-08-12", "11:00");
    await submit(itemADialog, mocks, 1);

    const itemBDialog = await replaceWithItemB(page);
    await setSpecificSchedule(itemBDialog, "2026-08-22", "18:20");
    const confirmation = itemBDialog.getByRole("checkbox", { name: CONFIRMATION_LABEL, exact: true });
    mocks.releaseRequest(0, "conflict");

    await expect(itemBDialog).toBeVisible();
    await expect(confirmation).toBeChecked();
    await expect(itemBDialog.getByText(RESELECTION_GUIDANCE, { exact: true })).toHaveCount(0);

    await submit(itemBDialog, mocks, 2);
    mocks.releaseRequest(1, "conflict");
    await expect(itemBDialog.getByText(RESELECTION_GUIDANCE, { exact: true })).toBeVisible();
    await expect(confirmation).not.toBeChecked();
    await expect(itemBDialog.getByLabel("Ngày")).toHaveValue("2026-08-22");
    await expect(itemBDialog.getByLabel("Giờ")).toHaveValue("18:20");
  });
}

async function main() {
  const response = await fetch(`${baseURL}/login`);
  if (!response.ok) throw new Error(`Frontend unavailable: HTTP ${response.status}`);

  const cases = [
    ["pending schedule blocks every modal dismissal path", testPendingDismissalIsBlocked],
    ["delayed success cannot mutate a replacement dialog session", testDelayedSuccessIsFenced],
    ["delayed 409 cannot mutate a replacement dialog session", testDelayedConflictIsFenced],
  ];
  let failures = 0;
  for (const [name, run] of cases) {
    try {
      await run();
      console.log(`  PASS  ${name}`);
    } catch (error) {
      failures += 1;
      console.error(`  FAIL  ${name}`);
      console.error(error);
    }
  }
  console.log(`\n${cases.length - failures} passed, ${failures} failed`);
  process.exit(failures ? 1 : 0);
}

await main();
