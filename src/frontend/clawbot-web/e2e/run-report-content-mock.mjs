// E2E mock: báo cáo marketing do report-agent chốt lại phải mở được ở /reports/{id} với đúng
// bảng số liệu nội dung. Hồi quy cần chặn: trang mở ra nhưng hiển thị cột KPI sale (Lead, Chuyển đổi)
// hoặc nhãn loại báo cáo rơi về chuỗi thô "content_snapshot" vì FE chưa biết loại mới.
import { chromium, expect } from "@playwright/test";

const baseURL = process.env.E2E_BASE_URL ?? "http://127.0.0.1:15876";
const timestamp = "2026-08-20T03:00:00.000Z";
const snapshotId = "5115a11c-0000-4000-8000-000000000001";
const funnelId = "5115a11c-0000-4000-8000-000000000002";

const marketer = {
  email: process.env.E2E_ADMIN_EMAIL ?? "admin@clawbot.local",
  password: process.env.E2E_ADMIN_PASSWORD ?? "Admin@12345",
};
const permissions = ["analytics:read", "content:read", "content:write"];

const contentSnapshot = {
  id: snapshotId,
  kind: "content_snapshot",
  title: "Hiệu suất nội dung 2026-08-14 - 2026-08-20",
  platform: "all",
  metric: null,
  fromDate: "2026-08-14",
  toDate: "2026-08-20",
  createdAt: timestamp,
  data: {
    kind: "content_snapshot",
    columns: [
      { key: "platform", label: "Nền tảng", type: "text" },
      { key: "postsPublished", label: "Bài đã đăng", type: "number" },
      { key: "likes", label: "Lượt thích", type: "number" },
      { key: "comments", label: "Bình luận", type: "number" },
      { key: "reactionsTotal", label: "Tổng cảm xúc", type: "number" },
    ],
    rows: [
      { platform: "facebook", postsPublished: 12, likes: 1234, comments: 87, reactionsTotal: 1450 },
      { platform: "instagram", postsPublished: 5, likes: 310, comments: 22, reactionsTotal: 310 },
    ],
    chart: { x: "platform", series: ["postsPublished", "likes", "comments"] },
  },
};

const contentFunnel = {
  id: funnelId,
  kind: "content_funnel",
  title: "Phễu duyệt nội dung tính đến 2026-08-20",
  platform: "all",
  metric: null,
  fromDate: "2026-08-20",
  toDate: "2026-08-20",
  createdAt: timestamp,
  data: {
    kind: "content_funnel",
    columns: [
      { key: "platform", label: "Nền tảng", type: "text" },
      { key: "awaitingAgentReview", label: "Chờ agent review", type: "number" },
      { key: "awaitingHumanApproval", label: "Chờ người duyệt", type: "number" },
      { key: "scheduled", label: "Đã lên lịch", type: "number" },
      { key: "published", label: "Đã đăng", type: "number" },
      { key: "total", label: "Tổng bài", type: "number" },
    ],
    rows: [
      { platform: "facebook", awaitingAgentReview: 3, awaitingHumanApproval: 4, scheduled: 2, published: 12, total: 21 },
    ],
    chart: { x: "platform", series: ["awaitingHumanApproval", "scheduled", "published"] },
  },
};

function emptyList() {
  return { items: [], total: 0, page: 1, pageSize: 50, nextCursor: null };
}

async function fulfillJson(route, status, body) {
  await route.fulfill({
    status,
    contentType: "application/json",
    body: body == null ? "" : JSON.stringify(body),
  });
}

async function installMocks(page) {
  let sessionActive = false;
  const accessToken = "report-content-mock-token";

  await page.route(
    (url) => new URL(url).pathname.startsWith("/auth"),
    async (route) => {
      const request = route.request();
      const path = new URL(request.url()).pathname;
      if (request.method() === "POST" && path.endsWith("/auth/login")) {
        const body = request.postDataJSON();
        sessionActive = body.email === marketer.email && body.password === marketer.password;
        return fulfillJson(route, sessionActive ? 200 : 401, sessionActive
          ? { accessToken, expiresAt: new Date(Date.now() + 3_600_000).toISOString() }
          : { error: "invalid_credentials" });
      }
      if (request.method() === "POST" && path.endsWith("/auth/refresh")) {
        return fulfillJson(route, sessionActive ? 200 : 401, sessionActive
          ? { accessToken, expiresAt: new Date(Date.now() + 3_600_000).toISOString() }
          : { error: "no_session" });
      }
      if (request.method() === "GET" && path.endsWith("/auth/me")) {
        return fulfillJson(route, sessionActive ? 200 : 401, sessionActive
          ? { id: "00000000-0000-0000-0000-000000000003", email: marketer.email, displayName: "E2E Marketer", permissions }
          : { error: "unauthorized" });
      }
      return fulfillJson(route, 204, null);
    },
  );

  const exportRequests = [];
  await page.route(
    (url) => new URL(url).pathname.startsWith("/api"),
    async (route) => {
      const request = route.request();
      const url = new URL(request.url());
      const path = url.pathname;
      if (request.method() === "GET" && path === `/api/reports/${snapshotId}`) {
        return fulfillJson(route, 200, contentSnapshot);
      }
      if (request.method() === "GET" && path === `/api/reports/${funnelId}`) {
        return fulfillJson(route, 200, contentFunnel);
      }
      if (request.method() === "GET" && path.endsWith("/export")) {
        exportRequests.push({ path, format: url.searchParams.get("format") });
        return route.fulfill({
          status: 200,
          contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
          body: Buffer.from("mock-xlsx"),
        });
      }
      if (request.method() === "GET") return fulfillJson(route, 200, emptyList());
      return fulfillJson(route, 200, { ok: true });
    },
  );

  return exportRequests;
}

async function login(page) {
  await page.goto(`${baseURL}/login`, { waitUntil: "domcontentloaded" });
  await page.locator("#email").fill(marketer.email);
  await page.locator("#password").fill(marketer.password);
  await page.getByRole("button", { name: /đăng nhập|login/i }).click();
  await page.waitForURL((url) => !url.pathname.includes("/login"));
}

async function main() {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ locale: "vi-VN" });
  const page = await context.newPage();

  try {
    const exportRequests = await installMocks(page);
    await login(page);

    // 1. Báo cáo hiệu suất nội dung mở đúng nhãn loại + đúng bộ cột marketing.
    await page.goto(`${baseURL}/reports/${snapshotId}`, { waitUntil: "domcontentloaded" });
    await expect(page.getByRole("heading", { name: /Hiệu suất nội dung 2026-08-14/ })).toBeVisible();
    await expect(page.getByText("Hiệu suất nội dung", { exact: true })).toBeVisible();
    // Nhãn thô lọt ra màn hình nghĩa là KIND_LABEL chưa biết loại báo cáo mới.
    await expect(page.getByText("content_snapshot", { exact: true })).toHaveCount(0);

    // Đối chiếu thẳng text của <th>: header bị CSS uppercase nên so theo accessible name rất giòn.
    const table = page.locator("table").first();
    await expect(table.locator("thead th")).toHaveText([
      "Nền tảng",
      "Bài đã đăng",
      "Lượt thích",
      "Bình luận",
      "Tổng cảm xúc",
    ]);

    // Chính là lỗi gốc: báo cáo cho marketing mà trả về cột KPI sale.
    const headers = await table.locator("thead th").allTextContents();
    const saleColumn = headers.find((h) => /lead|chuyển đổi|phản hồi tb/i.test(h));
    if (saleColumn) {
      throw new Error(`Báo cáo nội dung vẫn còn cột KPI sale: ${saleColumn}`);
    }

    // 2. Số liệu render đúng định dạng vi-VN (1.234 chứ không phải 1,234).
    const facebookCells = await table
      .locator("tbody tr")
      .filter({ hasText: "facebook" })
      .first()
      .locator("td")
      .allTextContents();
    if (facebookCells.join("|") !== "facebook|12|1.234|87|1.450") {
      throw new Error(`Dòng facebook sai: ${facebookCells.join("|")}`);
    }

    // Biểu đồ phải nhận chuỗi số liệu nội dung, không rơi về bảng trần.
    await expect(page.getByText("Biểu đồ", { exact: true })).toBeVisible();

    // 3. Xuất file bật được và gọi đúng endpoint export của chính báo cáo này.
    const excelButton = page.getByRole("button", { name: "Tải Excel" });
    await expect(excelButton).toBeEnabled();
    await excelButton.click();
    await expect.poll(() => exportRequests.length).toBeGreaterThan(0);
    if (exportRequests[0].path !== `/api/reports/${snapshotId}/export`) {
      throw new Error(`Export gọi sai báo cáo: ${exportRequests[0].path}`);
    }
    if (exportRequests[0].format !== "xlsx") {
      throw new Error(`Export sai định dạng: ${exportRequests[0].format}`);
    }

    // 4. Phễu duyệt nội dung: nhãn riêng, cột trạng thái quy trình.
    await page.goto(`${baseURL}/reports/${funnelId}`, { waitUntil: "domcontentloaded" });
    await expect(page.getByText("Phễu duyệt nội dung", { exact: true })).toBeVisible();
    await expect(page.getByText("content_funnel", { exact: true })).toHaveCount(0);
    const funnelTable = page.locator("table").first();
    await expect(funnelTable.locator("thead th")).toHaveText([
      "Nền tảng",
      "Chờ agent review",
      "Chờ người duyệt",
      "Đã lên lịch",
      "Đã đăng",
      "Tổng bài",
    ]);

    console.log("Report content E2E passed: kind labels, marketing columns, no sale columns, vi-VN numbers, export wiring, funnel view.");
  } finally {
    await context.close();
    await browser.close();
  }
}

await main();
