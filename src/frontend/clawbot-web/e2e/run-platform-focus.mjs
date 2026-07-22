import { chromium, expect } from "@playwright/test";

const baseURL = process.env.E2E_BASE_URL ?? "http://127.0.0.1:15876";
const timestamp = "2026-07-22T08:00:00.000Z";
const legacyBriefId = "11111111-1111-1111-1111-111111111111";
const legacyItemId = "22222222-2222-2222-2222-222222222222";
const scheduledItemId = "33333333-3333-3333-3333-333333333333";
const scheduledId = "44444444-4444-4444-4444-444444444444";
const originalMetaAssetId = "55555555-5555-5555-5555-555555555555";
const currentDefaultMetaAssetId = "66666666-6666-6666-6666-666666666666";
const admin = {
  email: process.env.E2E_ADMIN_EMAIL ?? "admin@clawbot.local",
  password: process.env.E2E_ADMIN_PASSWORD ?? "Admin@12345",
};
const adminPermissions = [
  "system:config",
  "content:read",
  "content:write",
  "content:approve",
  "content:publish",
  "agents:read",
  "agents:manage",
];

function analyticsRows() {
  return [
    { platform: "all", leads: 1000, dms: 1000, replies: 900, conversions: 800, avgResponseTimeSec: 1, adSpend: 1000, cpl: 1, revenue: 2000 },
    { platform: "facebook", leads: 10, dms: 20, replies: 15, conversions: 5, avgResponseTimeSec: 2, adSpend: 100, cpl: 10, revenue: 200 },
    { platform: "zalo", leads: 20, dms: 40, replies: 30, conversions: 10, avgResponseTimeSec: 3, adSpend: 200, cpl: 10, revenue: 400 },
    { platform: "instagram", leads: 30, dms: 60, replies: 45, conversions: 15, avgResponseTimeSec: 4, adSpend: 300, cpl: 10, revenue: 600 },
    { platform: "tiktok", leads: 900, dms: 900, replies: 800, conversions: 700, avgResponseTimeSec: 5, adSpend: 900, cpl: 1, revenue: 1800 },
    { platform: "youtube", leads: 800, dms: 800, replies: 700, conversions: 600, avgResponseTimeSec: 6, adSpend: 800, cpl: 1, revenue: 1600 },
  ];
}

function emptyList() {
  return { items: [], total: 0, page: 1, pageSize: 50 };
}

async function json(route, status, body) {
  await route.fulfill({
    status,
    contentType: "application/json",
    body: body == null ? "" : JSON.stringify(body),
  });
}

async function installMockApi(page, options = {}) {
  let sessionActive = false;
  let lastScheduleRequest = null;
  let legacyBrief = {
    id: legacyBriefId,
    platform: "tiktok",
    brief: "Legacy TikTok brief",
    status: "draft",
    createdBy: "e2e",
    createdAt: timestamp,
    updatedAt: timestamp,
  };
  const legacyItem = {
    id: legacyItemId,
    briefId: legacyBriefId,
    platform: "tiktok",
    status: "draft",
    body: "Legacy TikTok queue item",
    assetsJson: "[]",
    createdBy: "e2e",
    approvedBy: null,
    approvedAt: null,
    createdAt: timestamp,
    updatedAt: timestamp,
    contentRevision: 1,
    agentReview: null,
    publishingApproval: null,
    workflowState: "awaiting_agent_review",
    canApprove: false,
    canReject: false,
    canRetryReview: false,
    canSchedule: false,
    canPublish: false,
  };
  const scheduledItem = {
    ...legacyItem,
    id: scheduledItemId,
    briefId: null,
    platform: "facebook",
    status: "scheduled",
    body: "Facebook schedule with frozen non-default target",
    workflowState: "scheduled",
    canSchedule: true,
  };
  let lastBriefUpdate = null;
  const accessToken = "platform-focus-access-token";
  const expiresAt = () => new Date(Date.now() + 3_600_000).toISOString();

  await page.route(
    (url) => new URL(url).pathname.startsWith("/auth"),
    async (route) => {
      const request = route.request();
      const path = new URL(request.url()).pathname;
      if (request.method() === "POST" && path.endsWith("/auth/refresh")) {
        return sessionActive
          ? json(route, 200, { accessToken, expiresAt: expiresAt() })
          : json(route, 401, { error: "no_session" });
      }
      if (request.method() === "POST" && path.endsWith("/auth/login")) {
        const payload = request.postDataJSON();
        if (payload.email === admin.email && payload.password === admin.password) {
          sessionActive = true;
          return json(route, 200, { accessToken, expiresAt: expiresAt() });
        }
        return json(route, 401, { error: "invalid_credentials" });
      }
      if (request.method() === "GET" && path.endsWith("/auth/me")) {
        return sessionActive
          ? json(route, 200, {
              id: "00000000-0000-0000-0000-000000000002",
              email: admin.email,
              displayName: "Platform Focus Admin",
              permissions: adminPermissions,
            })
          : json(route, 401, { error: "unauthorized" });
      }
      return json(route, 200, {});
    },
  );

  await page.route(
    (url) => new URL(url).pathname.startsWith("/api"),
    async (route) => {
      const request = route.request();
      const path = new URL(request.url()).pathname;
      const method = request.method();
      const from = "2026-07-16";
      const to = "2026-07-22";

      if (method === "GET" && path === "/api/content/settings/publishing-policy") {
        return json(route, 200, {
          publishingApprovalPolicy: "human_required",
          policyVersion: 1,
          reviewerVisionCapability: "unknown",
          agentReviewRequired: true,
          agentReviewMode: "mandatory",
          updatedAt: timestamp,
        });
      }
      if (method === "GET" && path === "/api/content/briefs") {
        return json(route, 200, { items: [legacyBrief], total: 1, page: 1, pageSize: 50 });
      }
      if (method === "PUT" && path === `/api/content/briefs/${legacyBriefId}`) {
        const payload = request.postDataJSON();
        lastBriefUpdate = { platform: payload.platform, brief: payload.brief };
        legacyBrief = { ...legacyBrief, ...lastBriefUpdate, updatedAt: "2026-07-22T09:00:00.000Z" };
        return json(route, 200, legacyBrief);
      }
      if (method === "GET" && path === "/api/content/queue") {
        return json(route, 200, { items: [scheduledItem, legacyItem], total: 2, page: 1, pageSize: 40, nextCursor: null });
      }
      if (method === "GET" && path === `/api/content/items/${legacyItemId}`) {
        return json(route, 200, legacyItem);
      }
      if (method === "GET" && path === `/api/content/items/${scheduledItemId}`) {
        return json(route, 200, scheduledItem);
      }
      if (method === "GET" && path === "/api/content/calendar") {
        return json(route, 200, {
          items: [{
            scheduleId: scheduledId,
            contentItemId: scheduledItemId,
            platform: "facebook",
            status: "pending",
            body: scheduledItem.body,
            scheduledAt: "2026-07-25T02:00:00.000Z",
            postedAt: null,
            postUrl: null,
            metaAssetId: originalMetaAssetId,
            likeCount: null,
            commentCount: null,
            retryCount: 0,
            lastError: null,
          }],
        });
      }
      if (method === "GET" && path === "/api/content/trends") {
        return json(route, 200, { trends: [] });
      }
      if (method === "GET" && path === "/api/content/trends/settings") {
        return json(route, 200, {
          geo: "VN",
          google: { enabled: true, hasApiKey: false, url: null },
          youTube: { enabled: true, hasApiKey: true, url: null },
          tikTok: { enabled: true, hasApiKey: false, url: "https://example.com/trends" },
          schedule: { cadence: "off", nextRunAt: null, lastRunAt: null },
        });
      }
      if (method === "GET" && path === "/api/content/publish-targets") {
        const originalTarget = {
          id: originalMetaAssetId,
          platform: "facebook",
          externalId: "page-original",
          name: "Page gốc đã khóa",
          isDefault: false,
        };
        return json(route, 200, {
          mode: "linked_meta",
          items: [
            {
              id: currentDefaultMetaAssetId,
              platform: "facebook",
              externalId: "page-current-default",
              name: "Page mặc định mới",
              isDefault: true,
            },
            ...(options.includeOriginalTarget === false ? [] : [originalTarget]),
          ],
        });
      }
      if (method === "POST" && path === `/api/content/items/${scheduledItemId}/schedule`) {
        lastScheduleRequest = request.postDataJSON();
        return json(route, 201, {
          id: scheduledId,
          contentItemId: scheduledItemId,
          platform: "facebook",
          scheduledAt: lastScheduleRequest.scheduledAt,
          postedAt: null,
          status: "pending",
          postUrl: null,
          createdAt: timestamp,
          updatedAt: timestamp,
          metaAssetId: originalMetaAssetId,
          likeCount: null,
          commentCount: null,
          engagementSyncedAt: null,
          retryCount: 0,
          lastError: null,
        });
      }
      if (method === "GET" && path.endsWith("/api/analytics/omnichannel")) {
        return json(route, 200, { from, to, stale: false, rows: analyticsRows() });
      }
      if (method === "GET" && path.endsWith("/api/analytics/omnichannel-delta")) {
        return json(route, 200, {
          from,
          to,
          compare: "wow",
          prevFrom: "2026-07-09",
          prevTo: "2026-07-15",
          metrics: [],
        });
      }
      if (method === "GET" && path.endsWith("/api/analytics/funnel")) {
        return json(route, 200, {
          platform: "all",
          leads: 0,
          dms: 0,
          replies: 0,
          conversions: 0,
          dmRate: 0,
          replyRate: 0,
          conversionRate: 0,
        });
      }
      if (method === "GET" && path.endsWith("/api/analytics/agent-cost")) {
        return json(route, 200, { from, to, items: [] });
      }
      if (method === "GET" && path.startsWith("/api/analytics/")) {
        return json(route, 200, []);
      }
      if (method === "GET" && path.includes("/api/notifications")) {
        return json(route, 200, { items: [], total: 0, nextCursor: null });
      }
      if (method === "GET" && path === "/api/jobs") {
        return json(route, 200, { items: [], total: 0 });
      }
      if (method === "GET") {
        return json(route, 200, emptyList());
      }
      return json(route, 200, { ok: true });
    },
  );

  return {
    getLastBriefUpdate: () => lastBriefUpdate,
    getLastScheduleRequest: () => lastScheduleRequest,
  };
}

async function loginViaUi(page) {
  await page.goto(`${baseURL}/login`, { waitUntil: "domcontentloaded" });
  await page.locator("#email").waitFor({ state: "visible", timeout: 30_000 });
  await page.locator("#email").fill(admin.email);
  await page.locator("#password").fill(admin.password);
  await page.getByRole("button", { name: /đăng nhập|login/i }).click();
  await page.waitForURL((url) => !url.pathname.includes("/login"), { timeout: 30_000 });
}

async function withPage(run, options = {}) {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ locale: "vi-VN" });
  const page = await context.newPage();
  try {
    const mocks = await installMockApi(page, options);
    await loginViaUi(page);
    await run(page, mocks);
  } finally {
    await context.close();
    await browser.close();
  }
}

async function testContentPlatformPicker() {
  await withPage(async (page) => {
    await page.goto(`${baseURL}/content`);
    const platformSelect = page.getByLabel("Kênh").first();
    await expect(platformSelect).toBeVisible();
    await expect(platformSelect.locator("option")).toHaveText(["Facebook", "Zalo", "Instagram"]);
    await expect(platformSelect.locator('option[value="instagram"]')).toHaveCount(1);
    await expect(platformSelect.locator('option[value="tiktok"]')).toHaveCount(0);
    await expect(platformSelect.locator('option[value="youtube"]')).toHaveCount(0);
    await expect(platformSelect.locator('option[value="website"]')).toHaveCount(0);
  });
}

async function testTrendSettingsRemainAvailable() {
  await withPage(async (page) => {
    await page.goto(`${baseURL}/content`);
    await page.getByRole("button", { name: "Xem tất cả", exact: true }).click();
    await page.getByRole("button", { name: "Cấu hình quét xu hướng", exact: true }).click();
    const dialog = page.getByRole("dialog");
    await expect(dialog.getByText("Google Trends", { exact: true })).toBeVisible();
    await expect(dialog.getByText("YouTube", { exact: true })).toHaveCount(0);
    await expect(dialog.getByText("TikTok (thử nghiệm)", { exact: true })).toHaveCount(0);
  });
}

async function testAnalyticsThreeChannelFocus() {
  await withPage(async (page) => {
    await page.goto(`${baseURL}/analytics`);
    const channelSelect = page.locator("select").filter({ has: page.locator('option[value="all"]') });
    await expect(channelSelect.locator("option")).toHaveText(["Tất cả kênh", "Facebook", "Zalo", "Instagram"]);

    const channelBars = page.getByRole("heading", { name: "Xu hướng kênh" }).locator("xpath=../../..");
    await expect(channelBars.getByText("Facebook", { exact: true })).toHaveCount(1);
    await expect(channelBars.getByText("Zalo", { exact: true })).toHaveCount(1);
    await expect(channelBars.getByText("Instagram", { exact: true })).toHaveCount(1);
    await expect(channelBars.getByText("TikTok", { exact: true })).toHaveCount(0);
    await expect(channelBars.getByText("YouTube", { exact: true })).toHaveCount(0);
    await expect(channelBars.getByText("Tất cả kênh", { exact: true })).toHaveCount(0);
    const instagramBars = channelBars.locator(".grid").filter({ hasText: "Instagram" });
    await expect(instagramBars.locator(".h-2 > div").nth(1)).toHaveAttribute("style", "width: 100%;");

    const channelKpis = page.getByRole("heading", { name: "Hiệu suất 3 kênh" }).locator("xpath=../../..");
    await expect(channelKpis.getByText("3 dòng dữ liệu", { exact: true })).toBeVisible();
    await expect(channelKpis.locator("article")).toHaveCount(3);
    await expect(channelKpis.getByText("TikTok", { exact: true })).toHaveCount(0);
    await expect(channelKpis.getByText("YouTube", { exact: true })).toHaveCount(0);
    const instagramKpi = channelKpis.locator("article").filter({ hasText: "Instagram" });
    await expect(instagramKpi.locator(".h-2 > div")).toHaveAttribute("style", "width: 100%;");
  });
}

async function openExistingFacebookSchedule(page) {
  await page.goto(`${baseURL}/content`);
  const scheduledItem = page.getByRole("button", { name: /Facebook.*Facebook schedule with frozen non-default target/ });
  await expect(scheduledItem).toBeVisible();
  await scheduledItem.click();
  await page.getByRole("button", { name: "Đổi lịch (tuỳ chọn)", exact: true }).click();
  return page.getByRole("dialog", { name: "Lên lịch xuất bản nội dung" });
}

async function testExistingScheduleKeepsFrozenTarget() {
  await withPage(async (page, mocks) => {
    const dialog = await openExistingFacebookSchedule(page);
    const targetSelect = dialog.getByLabel("Facebook Page");
    await expect(targetSelect).toHaveValue(originalMetaAssetId);
    await expect(targetSelect.locator(`option[value="${currentDefaultMetaAssetId}"]`)).toHaveText("Page mặc định mới (mặc định)");

    await dialog.getByRole("button", { name: /Chọn thời điểm riêng/i }).click();
    await dialog.getByLabel("Giờ").fill("10:30");
    await dialog.getByRole("button", { name: "Xác nhận lên lịch", exact: true }).click();

    await expect.poll(() => mocks.getLastScheduleRequest()).toMatchObject({ metaAssetId: null });
  });
}

async function testMissingFrozenTargetDoesNotUseCurrentDefault() {
  await withPage(async (page, mocks) => {
    const dialog = await openExistingFacebookSchedule(page);
    const targetSelect = dialog.getByLabel("Facebook Page");
    await expect(targetSelect).toHaveValue("");
    await expect(dialog.getByText(/giữ nguyên đích đăng đã khóa/i)).toBeVisible();
    await expect(dialog.getByRole("button", { name: "Xác nhận lên lịch", exact: true })).toBeEnabled();

    await dialog.getByRole("button", { name: /Chọn thời điểm riêng/i }).click();
    await dialog.getByLabel("Giờ").fill("11:15");
    await dialog.getByRole("button", { name: "Xác nhận lên lịch", exact: true }).click();

    await expect.poll(() => mocks.getLastScheduleRequest()).toMatchObject({ metaAssetId: null });
  }, { includeOriginalTarget: false });
}

async function testLegacyReadOnlyContract() {
  await withPage(async (page, mocks) => {
    await page.goto(`${baseURL}/content`);

    const legacyQueueItem = page.getByRole("button", { name: /TikTok.*Legacy TikTok queue item/ });
    await expect(legacyQueueItem).toBeVisible();
    const legacyBrief = page.getByRole("button", { name: /TikTok.*Legacy TikTok brief/ });
    await expect(legacyBrief).toBeVisible();
    await legacyBrief.click();

    const platformSelect = page.getByLabel("Kênh").first();
    await expect(platformSelect).toHaveValue("tiktok");
    await expect(platformSelect.locator("option")).toHaveText([
      "TikTok (lịch sử — chọn kênh mới để sinh bài)",
      "Facebook",
      "Zalo",
      "Instagram",
    ]);
    await expect(page.getByRole("button", { name: "Sinh bài nháp", exact: true })).toBeDisabled();
    await expect(page.getByRole("button", { name: "Tạo bài viết mới", exact: true })).toBeDisabled();

    await page.getByLabel("Nội dung yêu cầu").fill("Legacy TikTok brief updated");
    await page.getByRole("button", { name: "Cập nhật yêu cầu", exact: true }).click();
    await expect(page.getByText("Đã lưu yêu cầu nội dung.", { exact: true })).toBeVisible();
    await expect.poll(() => mocks.getLastBriefUpdate()).toEqual({
      platform: "tiktok",
      brief: "Legacy TikTok brief updated",
    });
    await expect(platformSelect).toHaveValue("tiktok");

    await platformSelect.selectOption("instagram");
    await expect(page.getByRole("button", { name: "Sinh bài nháp", exact: true })).toBeEnabled();
    await expect(page.getByRole("button", { name: "Tạo bài viết mới", exact: true })).toBeEnabled();
    await page.getByRole("button", { name: "Yêu cầu mới", exact: true }).click();
    await expect(platformSelect.locator("option")).toHaveText(["Facebook", "Zalo", "Instagram"]);
  });
}

const results = [];

async function main() {
  try {
    const response = await fetch(`${baseURL}/login`);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
  } catch (error) {
    console.error(`Frontend not reachable at ${baseURL}/login. Start Vite on port 15876 first.`);
    console.error(error);
    process.exit(2);
  }

  const cases = [
    ["new content picker exposes only Facebook, Zalo, and Instagram", testContentPlatformPicker],
    ["trend settings keep Google and omit legacy content targets", testTrendSettingsRemainAvailable],
    ["analytics charts render and scale only the three primary channels", testAnalyticsThreeChannelFocus],
    ["time-only reschedule keeps the frozen non-default Meta target", testExistingScheduleKeepsFrozenTarget],
    ["missing frozen target never falls back to the current default", testMissingFrozenTargetDoesNotUseCurrentDefault],
    ["legacy TikTok content remains readable and text-editable", testLegacyReadOnlyContract],
  ];

  for (const [name, run] of cases) {
    try {
      await run();
      results.push({ name, ok: true });
      console.log(`  PASS  ${name}`);
    } catch (error) {
      results.push({ name, ok: false });
      console.error(`  FAIL  ${name}`);
      console.error(error);
    }
  }

  const failed = results.filter((result) => !result.ok).length;
  console.log(`\n${results.length - failed} passed, ${failed} failed`);
  process.exit(failed ? 1 : 0);
}

await main();
