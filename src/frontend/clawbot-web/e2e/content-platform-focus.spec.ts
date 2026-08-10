import { expect, test, type Page, type Route } from "@playwright/test";
import { DEFAULT_ADMIN, loginViaUi } from "./fixtures/auth";
import { installMockApi } from "./fixtures/mockApi";

async function fulfillJson(route: Route, body: unknown): Promise<void> {
  await route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify(body),
  });
}

interface PlatformSurfaceMocks {
  readonly getLastBriefUpdate: () => { readonly platform: string; readonly brief: string } | null;
}

async function installPlatformSurfaceMocks(page: Page): Promise<PlatformSurfaceMocks> {
  const legacyBriefId = "11111111-1111-1111-1111-111111111111";
  const legacyItemId = "22222222-2222-2222-2222-222222222222";
  const timestamp = "2026-07-22T08:00:00.000Z";
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
  let lastBriefUpdate: { readonly platform: string; readonly brief: string } | null = null;

  await page.route("**/api/content/trends/settings", (route) =>
    fulfillJson(route, {
      geo: "VN",
      google: { enabled: true, hasApiKey: false, url: null },
      youTube: { enabled: true, hasApiKey: true, url: null },
      tikTok: { enabled: true, hasApiKey: false, url: "https://example.com/trends" },
      schedule: { cadence: "off", nextRunAt: null, lastRunAt: null },
    }),
  );

  await page.route(
    (url) => {
      const path = new URL(url).pathname;
      return path === "/api/content/briefs"
        || path === `/api/content/briefs/${legacyBriefId}`
        || path === "/api/content/queue"
        || path === `/api/content/items/${legacyItemId}`;
    },
    async (route) => {
      const request = route.request();
      const path = new URL(request.url()).pathname;

      if (request.method() === "GET" && path === "/api/content/briefs") {
        return fulfillJson(route, { items: [legacyBrief], total: 1, page: 1, pageSize: 50 });
      }
      if (request.method() === "PUT" && path === `/api/content/briefs/${legacyBriefId}`) {
        const payload = request.postDataJSON() as { readonly platform: string; readonly brief: string };
        lastBriefUpdate = { ...payload };
        legacyBrief = { ...legacyBrief, ...payload, updatedAt: "2026-07-22T09:00:00.000Z" };
        return fulfillJson(route, legacyBrief);
      }
      if (request.method() === "GET" && path === "/api/content/queue") {
        return fulfillJson(route, { items: [legacyItem], total: 1, page: 1, pageSize: 40, nextCursor: null });
      }
      if (request.method() === "GET" && path === `/api/content/items/${legacyItemId}`) {
        return fulfillJson(route, legacyItem);
      }

      return fulfillJson(route, { code: "unmocked_platform_surface" });
    },
  );

  await page.route(
    (url) => new URL(url).pathname.startsWith("/api/analytics/"),
    async (route) => {
      const path = new URL(route.request().url()).pathname;
      const from = "2026-07-16";
      const to = "2026-07-22";

      if (path.endsWith("/omnichannel")) {
        return fulfillJson(route, {
          from,
          to,
          stale: false,
          rows: [
            { platform: "all", leads: 1000, dms: 1000, replies: 900, conversions: 800, avgResponseTimeSec: 1 },
            { platform: "facebook", leads: 10, dms: 20, replies: 15, conversions: 5, avgResponseTimeSec: 2 },
            { platform: "zalo", leads: 20, dms: 40, replies: 30, conversions: 10, avgResponseTimeSec: 3 },
            { platform: "instagram", leads: 30, dms: 60, replies: 45, conversions: 15, avgResponseTimeSec: 4 },
            { platform: "tiktok", leads: 900, dms: 900, replies: 800, conversions: 700, avgResponseTimeSec: 5 },
            { platform: "youtube", leads: 800, dms: 800, replies: 700, conversions: 600, avgResponseTimeSec: 6 },
          ],
        });
      }
      if (path.endsWith("/omnichannel-delta")) {
        return fulfillJson(route, { from, to, compare: "wow", prevFrom: "2026-07-09", prevTo: "2026-07-15", metrics: [] });
      }
      if (path.endsWith("/funnel")) {
        return fulfillJson(route, {
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
      if (path.endsWith("/agent-cost")) {
        return fulfillJson(route, { from, to, items: [] });
      }
      if (path.endsWith("/agent-performance") || path.endsWith("/forecast") || path.endsWith("/anomalies")) {
        return fulfillJson(route, []);
      }

      return fulfillJson(route, {});
    },
  );

  return { getLastBriefUpdate: () => lastBriefUpdate };
}

test.describe("content platform focus", () => {
  test("content platform picker exposes exactly Facebook, Zalo, and Instagram", async ({ page }) => {
    await installMockApi(page);
    await installPlatformSurfaceMocks(page);
    await loginViaUi(page, DEFAULT_ADMIN);

    await page.goto("/content");

    const platformSelect = page.getByLabel("Kênh").first();
    await expect(platformSelect).toBeVisible();
    await expect(platformSelect.locator("option")).toHaveText(["Facebook", "Zalo", "Instagram"]);
  });

  test("trend settings keep Google configurable and omit YouTube and TikTok", async ({ page }) => {
    await installMockApi(page);
    await installPlatformSurfaceMocks(page);
    await loginViaUi(page, DEFAULT_ADMIN);
    await page.goto("/content");

    await page.getByRole("button", { name: "Xem tất cả", exact: true }).click();
    await page.getByRole("button", { name: "Cấu hình quét xu hướng", exact: true }).click();

    const dialog = page.getByRole("dialog");
    await expect(dialog.getByText("Google Trends", { exact: true })).toBeVisible();
    await expect(dialog.getByText("YouTube", { exact: true })).toHaveCount(0);
    await expect(dialog.getByText("TikTok (thử nghiệm)", { exact: true })).toHaveCount(0);
  });

  test("analytics charts render and scale only the three primary channels", async ({ page }) => {
    await installMockApi(page);
    await installPlatformSurfaceMocks(page);
    await loginViaUi(page, DEFAULT_ADMIN);

    await page.goto("/analytics");

    const channelSelect = page.locator("select").filter({ has: page.locator('option[value="all"]') });
    await expect(channelSelect).toBeVisible();
    await expect(channelSelect.locator("option")).toHaveText([
      "Tất cả kênh",
      "Facebook",
      "Zalo",
      "Instagram",
    ]);

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

  test("legacy TikTok content stays readable and text-editable without becoming a new target", async ({ page }) => {
    await installMockApi(page);
    const mocks = await installPlatformSurfaceMocks(page);
    await loginViaUi(page, DEFAULT_ADMIN);

    await page.goto("/content");

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
});
