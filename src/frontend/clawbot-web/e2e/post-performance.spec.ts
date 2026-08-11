import { expect, test, type Page, type Route } from "@playwright/test";
import { DEFAULT_ADMIN, loginViaUi } from "./fixtures/auth";
import { installMockApi } from "./fixtures/mockApi";

type PerformancePlatform = "facebook" | "instagram";

interface PerformancePostFixture {
  readonly scheduleId: string;
  readonly contentItemId: string;
  readonly isContentAvailable: boolean;
  readonly platform: PerformancePlatform;
  readonly excerpt: string;
  readonly postUrl: string | null;
  readonly postedAt: string;
  readonly likes: number | null;
  readonly comments: number | null;
}

const PERFORMANCE_POSTS: readonly PerformancePostFixture[] = [
  {
    scheduleId: "11111111-1111-1111-1111-111111111111",
    contentItemId: "22222222-2222-2222-2222-222222222222",
    isContentAvailable: true,
    platform: "facebook",
    excerpt: "Facebook đã có số liệu",
    postUrl: "https://www.facebook.com/example/posts/1",
    postedAt: "2026-08-03T08:00:00Z",
    likes: 2,
    comments: 3,
  },
  {
    scheduleId: "33333333-3333-3333-3333-333333333333",
    contentItemId: "44444444-4444-4444-4444-444444444444",
    isContentAvailable: false,
    platform: "facebook",
    excerpt: "Facebook chưa có số liệu",
    postUrl: "https://attacker.example/login",
    postedAt: "2026-08-04T08:00:00Z",
    likes: null,
    comments: null,
  },
  {
    scheduleId: "55555555-5555-5555-5555-555555555555",
    contentItemId: "66666666-6666-6666-6666-666666666666",
    isContentAvailable: true,
    platform: "instagram",
    excerpt: "Instagram có số liệu bằng không",
    postUrl: "https://www.instagram.com/p/example/",
    postedAt: "2026-08-04T09:00:00Z",
    likes: 0,
    comments: 0,
  },
];

function isMeasured(post: PerformancePostFixture): boolean {
  return post.likes !== null && post.comments !== null;
}

function aggregate(posts: readonly PerformancePostFixture[]) {
  const measuredPosts = posts.filter(isMeasured);
  const likes = measuredPosts.length
    ? measuredPosts.reduce((total, post) => total + (post.likes ?? 0), 0)
    : null;
  const comments = measuredPosts.length
    ? measuredPosts.reduce((total, post) => total + (post.comments ?? 0), 0)
    : null;

  return {
    posts: posts.length,
    syncedPosts: measuredPosts.length,
    likes,
    comments,
    avgEngagementPerPost: measuredPosts.length
      ? ((likes ?? 0) + (comments ?? 0)) / measuredPosts.length
      : null,
  };
}

function performanceResponse(days: number, platform: PerformancePlatform | null, isEmpty: boolean) {
  const posts = isEmpty
    ? []
    : platform
      ? PERFORMANCE_POSTS.filter((post) => post.platform === platform)
      : PERFORMANCE_POSTS;
  const byPlatform = (["facebook", "instagram"] as const)
    .map((candidate) => {
      const groupedPosts = posts.filter((post) => post.platform === candidate);
      return groupedPosts.length ? { platform: candidate, ...aggregate(groupedPosts) } : null;
    })
    .filter((row): row is { platform: PerformancePlatform } & ReturnType<typeof aggregate> => row !== null);
  const daily = [...new Set(posts.map((post) => post.postedAt.slice(0, 10)))].sort().map((date) => {
    const dayPosts = posts.filter((post) => post.postedAt.startsWith(date));
    return { date, ...aggregate(dayPosts) };
  });

  return {
    windowDays: days,
    from: "2026-07-06T00:00:00Z",
    to: "2026-08-05T00:00:00Z",
    totals: aggregate(posts),
    freshness: {
      syncedPosts: posts.filter(isMeasured).length,
      unsyncedPosts: posts.filter((post) => !isMeasured(post)).length,
      oldestEngagementAttemptAt: posts.length ? "2026-08-04T09:15:00Z" : null,
    },
    byPlatform,
    byTarget: posts.length ? [{ metaAssetId: null, targetName: "Không xác định Page", ...aggregate(posts) }] : [],
    daily,
    topPosts: [...posts]
      .sort((left, right) => ((right.likes ?? -1) + (right.comments ?? -1)) - ((left.likes ?? -1) + (left.comments ?? -1)))
      .map((post) => ({
        ...post,
        total: isMeasured(post) ? (post.likes ?? 0) + (post.comments ?? 0) : null,
      })),
  };
}

async function installPerformanceMocks(page: Page, isEmpty = false): Promise<readonly URL[]> {
  const requests: URL[] = [];
  await installMockApi(page);
  await page.route("**/api/content/post-performance?*", async (route: Route) => {
    const requestUrl = new URL(route.request().url());
    requests.push(requestUrl);
    const days = Number(requestUrl.searchParams.get("days") ?? "30");
    const platform = requestUrl.searchParams.get("platform") as PerformancePlatform | null;
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(performanceResponse(days, platform, isEmpty)),
    });
  });
  return requests;
}

test.describe("post performance dashboard", () => {
  test("keeps the selected tab, scopes every dashboard block, and distinguishes unknown from zero metrics", async ({ page }) => {
    const requests = await installPerformanceMocks(page);
    await loginViaUi(page, DEFAULT_ADMIN);

    await page.goto("/content?tab=performance");

    const panel = page.locator("#content-performance-panel");
    await expect(page.getByRole("tab", { name: "Hiệu quả bài đăng", exact: true })).toHaveAttribute("aria-selected", "true");
    await expect(panel.getByRole("heading", { name: "Hiệu quả bài đăng", exact: true })).toBeVisible();
    await expect.poll(() => requests.length).toBe(1);
    expect(requests[0].searchParams.get("days")).toBe("30");
    expect(requests[0].searchParams.get("platform")).toBeNull();
    await expect(panel.getByText("Lần thử đồng bộ cũ nhất:")).toBeVisible();
    await expect(panel.getByText("Facebook chưa có số liệu", { exact: true })).toBeVisible();
    await expect(panel.getByRole("button", { name: "Facebook chưa có số liệu", exact: true })).toHaveCount(0);
    const unknownPostRow = panel.getByText("Facebook chưa có số liệu", { exact: true }).locator("xpath=ancestor::tr");
    await expect(unknownPostRow.getByRole("cell", { name: "—", exact: true })).toHaveCount(4);
    await expect(panel.getByRole("link", { name: "Mở bài", exact: true })).toHaveCount(2);
    await expect(panel.getByRole("link", { name: "Mở bài", exact: true }).first()).toHaveAttribute("rel", "noreferrer noopener");
    await expect(panel.getByRole("link", { name: "Mở bài", exact: true }).first()).toHaveAttribute("href", /facebook\.com/);
    await expect(panel.locator("a[href*='attacker.example']")).toHaveCount(0);
    await expect(panel.getByRole("heading", { name: "Xu hướng theo ngày đăng bài", exact: true })).toBeVisible();

    const platformSelect = panel.getByLabel("Kênh");
    await expect(platformSelect.locator("option")).toHaveText(["Facebook và Instagram", "Facebook", "Instagram"]);
    await platformSelect.selectOption("facebook");
    await expect.poll(() => requests.length).toBe(2);
    expect(requests[1].searchParams.get("platform")).toBe("facebook");
    await expect(panel.getByText("Facebook đã có số liệu", { exact: true })).toBeVisible();
    await expect(panel.getByText("Instagram có số liệu bằng không", { exact: true })).toHaveCount(0);

    await platformSelect.selectOption("instagram");
    await expect.poll(() => requests.length).toBe(3);
    expect(requests[2].searchParams.get("platform")).toBe("instagram");
    await expect(panel.getByText("Instagram có số liệu bằng không", { exact: true })).toBeVisible();
    await expect(panel.getByText("Facebook đã có số liệu", { exact: true })).toHaveCount(0);
    await expect(panel.getByText("0", { exact: true }).first()).toBeVisible();

    await page.reload();
    await expect(page.getByRole("tab", { name: "Hiệu quả bài đăng", exact: true })).toHaveAttribute("aria-selected", "true");
  });

  test("guides an empty period back to the publishing calendar", async ({ page }) => {
    await installPerformanceMocks(page, true);
    await loginViaUi(page, DEFAULT_ADMIN);

    await page.goto("/content?tab=performance");

    const panel = page.locator("#content-performance-panel");
    await expect(panel.getByRole("heading", { name: "Chưa có bài Facebook hoặc Instagram trong kỳ này", exact: true })).toBeVisible();
    await expect(panel.getByRole("button", { name: "Mở lịch xuất bản", exact: true })).toBeVisible();
    await panel.getByRole("button", { name: "Mở lịch xuất bản", exact: true }).click();
    await expect(page.getByRole("tab", { name: "Lịch xuất bản", exact: true })).toHaveAttribute("aria-selected", "true");
  });
});
