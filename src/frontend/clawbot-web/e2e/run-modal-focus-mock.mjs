import { chromium, expect } from "@playwright/test";

const baseURL = process.env.E2E_BASE_URL ?? "http://127.0.0.1:15876";
const itemId = "46464646-4646-4646-4646-464646464646";
const timestamp = "2026-07-22T08:00:00.000Z";
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
const instagramItem = {
  id: itemId,
  briefId: null,
  platform: "instagram",
  status: "approved",
  body: "Instagram E2E approved item",
  assetsJson: "[]",
  createdBy: "e2e",
  approvedBy: "e2e-admin",
  approvedAt: timestamp,
  createdAt: timestamp,
  updatedAt: timestamp,
  contentRevision: 1,
  agentReview: {
    status: "passed",
    reviewedRevision: 1,
    reviewedByAgentId: "content-reviewer",
    reviewedAt: timestamp,
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
    approvedAt: timestamp,
    reason: null,
    requirementReason: null,
  },
  workflowState: "approved_for_publish",
  canApprove: false,
  canReject: false,
  canRetryReview: false,
  canSchedule: true,
  canPublish: false,
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
  const accessToken = "modal-focus-mock-token";

  await page.route(
    (url) => new URL(url).pathname.startsWith("/auth"),
    async (route) => {
      const request = route.request();
      const path = new URL(request.url()).pathname;
      if (request.method() === "POST" && path.endsWith("/auth/login")) {
        const body = request.postDataJSON();
        sessionActive = body.email === admin.email && body.password === admin.password;
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
          ? { id: "00000000-0000-0000-0000-000000000002", email: admin.email, displayName: "E2E Admin", permissions }
          : { error: "unauthorized" });
      }
      return fulfillJson(route, 204, null);
    },
  );

  await page.route(
    (url) => new URL(url).pathname.startsWith("/api"),
    async (route) => {
      const request = route.request();
      const path = new URL(request.url()).pathname;
      if (request.method() === "GET" && path === "/api/content/queue") {
        return fulfillJson(route, 200, { items: [instagramItem], total: 1, page: 1, pageSize: 40, nextCursor: null });
      }
      if (request.method() === "GET" && path === `/api/content/items/${itemId}`) {
        return fulfillJson(route, 200, instagramItem);
      }
      if (request.method() === "GET" && path === "/api/content/publish-targets") {
        return fulfillJson(route, 200, { mode: "standalone", items: [] });
      }
      if (request.method() === "GET" && path === "/api/content/calendar") {
        return fulfillJson(route, 200, { items: [] });
      }
      if (request.method() === "GET" && path === "/api/content/settings/publishing-policy") {
        return fulfillJson(route, 200, {
          publishingApprovalPolicy: "human_required",
          policyVersion: 1,
          reviewerVisionCapability: "unknown",
          agentReviewRequired: true,
          agentReviewMode: "mandatory",
          updatedAt: timestamp,
        });
      }
      if (request.method() === "GET" && path === "/api/content/trends") {
        return fulfillJson(route, 200, { trends: [] });
      }
      if (request.method() === "GET") return fulfillJson(route, 200, emptyList());
      return fulfillJson(route, 200, { ok: true });
    },
  );
}

async function login(page) {
  await page.goto(`${baseURL}/login`, { waitUntil: "domcontentloaded" });
  await page.locator("#email").fill(admin.email);
  await page.locator("#password").fill(admin.password);
  await page.getByRole("button", { name: /đăng nhập|login/i }).click();
  await page.waitForURL((url) => !url.pathname.includes("/login"));
}

async function openScheduleDialog(page) {
  await page.goto(`${baseURL}/content`, { waitUntil: "domcontentloaded" });
  await expect(page.getByRole("textbox", { name: /Nội dung bài viết/i })).toHaveValue(/Instagram E2E approved item/);
  const opener = page.getByRole("button", { name: /Đổi lịch \(tuỳ chọn\)/i });
  await opener.click();
  const dialog = page.getByRole("dialog", { name: "Lên lịch xuất bản nội dung" });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByText(/tài khoản Instagram độc lập/i)).toBeVisible();
  return { dialog, opener };
}

async function main() {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ locale: "vi-VN" });
  const page = await context.newPage();

  try {
    await installMocks(page);
    await login(page);

    let { dialog, opener } = await openScheduleDialog(page);
    const closeButton = dialog.getByRole("button", { name: "Đóng" });
    const confirmButton = dialog.getByRole("button", { name: "Xác nhận lên lịch" });
    await expect(closeButton).toBeFocused();
    await confirmButton.focus();
    await page.keyboard.press("Tab");
    await expect(closeButton).toBeFocused();
    await closeButton.focus();
    await page.keyboard.press("Shift+Tab");
    await expect(confirmButton).toBeFocused();

    await page.keyboard.press("Escape");
    await expect(dialog).toBeHidden();
    await expect(opener).toBeFocused();

    ({ dialog, opener } = await openScheduleDialog(page));
    await dialog.getByRole("button", { name: "Chọn thời điểm riêng" }).click();
    await expect(dialog).toBeVisible();
    await page.locator('div.fixed.inset-0[role="presentation"]').click({ position: { x: 2, y: 2 } });
    await expect(dialog).toBeHidden();
    await expect(opener).toBeFocused();

    console.log("Modal focus E2E passed: initial focus, Tab containment, Escape, overlay, restoration, Instagram schedule dialog.");
  } finally {
    await context.close();
    await browser.close();
  }
}

await main();
