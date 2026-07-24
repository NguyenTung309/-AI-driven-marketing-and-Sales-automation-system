/**
 * Programmatic Playwright runner for dual-screen policy E2E (mock API).
 * Avoids hanging `playwright test` CLI in some Windows harness shells.
 *
 * Usage: node e2e/run-mock.mjs
 * Requires Vite already on http://127.0.0.1:15876 (npm run dev).
 */
import { chromium, expect } from "@playwright/test";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
// Load TS fixtures via dynamic import of compiled-equivalent logic inline.
// Fixtures are duplicated here as plain JS to keep the runner dependency-free of ts-node.

const baseURL = process.env.E2E_BASE_URL ?? "http://127.0.0.1:15876";
const __dirname = path.dirname(fileURLToPath(import.meta.url));

const ADMIN = {
  email: process.env.E2E_ADMIN_EMAIL ?? "admin@clawbot.local",
  password: process.env.E2E_ADMIN_PASSWORD ?? "Admin@12345",
};
const MARKETER = {
  email: process.env.E2E_MARKETER_EMAIL ?? "marketer@clawbot.local",
  password: process.env.E2E_MARKETER_PASSWORD ?? "Marketer@12345",
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
const MARKETER_PERMS = ["content:read", "content:write", "content:approve", "agents:read"];

function emptyList() {
  return { items: [], total: 0, page: 1, pageSize: 50 };
}

function orchestrationDefaults() {
  return {
    // FE AgentDashboard reads requireApproval; API may also expose requireOrchestrationApproval.
    requireApproval: false,
    requireOrchestrationApproval: false,
    monthlyCostCapUsd: null,
    requireContentReview: true,
    requireChatReplyApproval: false,
    requireKbHumanReview: false,
    aiAutoReplyResumeMinutes: 5,
    skipChatReplyReview: false,
    idleAlertMinutes: 30,
    leadLostAfterDays: 30,
    autoApproveLeadRevenue: false,
  };
}

async function installMockApi(page, { user, initialPolicy = "human_required" }) {
  const policy = {
    publishingApprovalPolicy: initialPolicy,
    policyVersion: 1,
    reviewerVisionCapability: "unknown",
    agentReviewRequired: true,
    agentReviewMode: "mandatory",
    updatedAt: new Date().toISOString(),
  };
  // Access token is in-memory only; full page.goto remounts AuthProvider and re-calls
  // /auth/refresh. After login we keep a mock session so refresh rehydrates.
  let sessionActive = false;
  const accessToken = "e2e-mock-access-token";
  const expiresAt = () => new Date(Date.now() + 3600_000).toISOString();

  const json = async (route, status, body) => {
    await route.fulfill({
      status,
      contentType: "application/json",
      body: body == null ? "" : JSON.stringify(body),
    });
  };

  // Predicate form is more reliable than glob when Vite proxies /auth and /api.
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
        if (!sessionActive) {
          return json(route, 401, { error: "no_session" });
        }
        return json(route, 200, { accessToken, expiresAt: expiresAt() });
      }
      if (method === "POST" && pathName.endsWith("/auth/login")) {
        const body = request.postDataJSON();
        if (body.email === user.email && body.password === user.password) {
          sessionActive = true;
          return json(route, 200, { accessToken, expiresAt: expiresAt() });
        }
        return json(route, 401, { error: "invalid_credentials" });
      }
      if (method === "GET" && pathName.endsWith("/auth/me")) {
        if (!sessionActive) {
          return json(route, 401, { error: "unauthorized" });
        }
        return json(route, 200, {
          id: "00000000-0000-0000-0000-000000000002",
          email: user.email,
          displayName: user.displayName,
          permissions: user.permissions,
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
    if (method === "PUT" && pathName.endsWith("/api/content/settings/publishing-policy")) {
      if (!user.permissions.includes("system:config")) {
        return json(route, 403, { code: "forbidden", message: "system:config required" });
      }
      const body = request.postDataJSON();
      const next = body.publishingApprovalPolicy;
      if (next !== "automatic" && next !== "human_required") {
        return json(route, 400, { code: "content.publishing_policy_invalid", message: "invalid" });
      }
      if (policy.publishingApprovalPolicy !== next) {
        policy.publishingApprovalPolicy = next;
        policy.policyVersion += 1;
        policy.updatedAt = new Date().toISOString();
      }
      return json(route, 200, { ...policy });
    }
    if (method === "GET" && pathName.endsWith("/api/admin/tenant/orchestration")) {
      return json(route, 200, orchestrationDefaults());
    }
    if (method === "PUT" && pathName.endsWith("/api/admin/tenant/orchestration")) {
      return json(route, 200, orchestrationDefaults());
    }
    if (method === "GET" && pathName.endsWith("/api/agents")) {
      return json(route, 200, { items: [] });
    }
    if (method === "GET" && /\/api\/agents\/[^/]+\/traces$/.test(pathName)) {
      return json(route, 200, { items: [], total: 0, page: 1, pageSize: 50 });
    }
    if (method === "GET" && /\/api\/agents\/[^/]+\/settings$/.test(pathName)) {
      return json(route, 200, {
        code: "content-agent",
        systemPrompt: "",
        temperature: 0.2,
        maxTokens: 1024,
        tools: [],
      });
    }
    if (method === "GET" && pathName.endsWith("/api/analytics/agent-cost")) {
      return json(route, 200, {
        from: new Date(0).toISOString(),
        to: new Date().toISOString(),
        items: [],
      });
    }
    if (method === "GET" && pathName.includes("/api/orchestration")) {
      return json(route, 200, []);
    }
    if (method === "GET" && pathName.endsWith("/api/jobs")) {
      return json(route, 200, { items: [], total: 0 });
    }
    if (method === "GET" && pathName.endsWith("/api/llm-configs")) {
      return json(route, 200, { items: [] });
    }
    if (method === "GET" && pathName.endsWith("/api/content/briefs")) {
      return json(route, 200, emptyList());
    }
    if (
      method === "GET" &&
      (pathName.endsWith("/api/content/queue") || pathName.endsWith("/api/content/items"))
    ) {
      return json(route, 200, { items: [], total: 0, page: 1, pageSize: 50, nextCursor: null });
    }
    if (method === "GET" && pathName.endsWith("/api/content/calendar")) {
      return json(route, 200, { items: [] });
    }
    if (method === "GET" && pathName.endsWith("/api/content/trends")) {
      return json(route, 200, { items: [] });
    }
    if (method === "GET" && pathName.endsWith("/api/content/publish-targets")) {
      return json(route, 200, { items: [] });
    }
    if (method === "GET" && pathName.includes("/api/notifications")) {
      return json(route, 200, { items: [], total: 0, nextCursor: null });
    }
    if (method === "GET") return json(route, 200, emptyList());
    return json(route, 200, { ok: true });
  },
  );

  return { getPolicy: () => ({ ...policy }) };
}

async function loginViaUi(page, credentials) {
  await page.goto(`${baseURL}/login`, { waitUntil: "domcontentloaded" });
  // AuthProvider shows "Đang tải..." until /auth/refresh settles (mocked 401 → anon).
  await page.locator("#email").waitFor({ state: "visible", timeout: 30_000 });
  await page.locator("#email").fill(credentials.email);
  await page.locator("#password").fill(credentials.password);
  await page.getByRole("button", { name: /đăng nhập|login/i }).click();
  await page.waitForURL((url) => !url.pathname.includes("/login"), { timeout: 30_000 });
}

async function selectedPolicyValue(page) {
  const automatic = page.locator(
    'input[name="content-publishing-approval-policy"][value="automatic"]',
  );
  if (await automatic.isChecked()) return "automatic";
  return "human_required";
}

async function waitForPolicyHydrated(page) {
  // StatusPill appears only after GET publishing-policy returns (e.g. "Cần người duyệt · v1").
  await page.getByText(/· v\d+/).first().waitFor({ state: "visible", timeout: 30_000 });
}

async function selectPolicy(page, value) {
  const radio = page.locator(
    `input[name="content-publishing-approval-policy"][value="${value}"]`,
  );
  await radio.waitFor({ state: "visible", timeout: 15_000 });
  // Wait until fieldset/radios leave loading/fetching disabled state.
  await expect(radio).toBeEnabled({ timeout: 15_000 });
  if (await radio.isChecked()) return;
  // Controlled React radio: Playwright .check() asserts native state flip before
  // the mutation resolves and fails. Click the wrapping label so React onChange runs.
  await radio.locator("xpath=ancestor::label[1]").click();
}

async function openContentPolicy(page) {
  await page.goto(`${baseURL}/content`);
  await page.getByRole("heading", { name: "Chính sách phát hành nội dung" }).waitFor({
    state: "visible",
    timeout: 30_000,
  });
  await waitForPolicyHydrated(page);
}

async function openAgentsApprovalConfig(page) {
  await page.goto(`${baseURL}/agents`);
  await page.getByRole("button", { name: /cấu hình duyệt/i }).click();
  await page.getByRole("heading", { name: "Chính sách phát hành nội dung" }).waitFor({
    state: "visible",
    timeout: 30_000,
  });
  await waitForPolicyHydrated(page);
}

async function poll(fn, { timeout = 15_000, interval = 250, label = "condition" } = {}) {
  const start = Date.now();
  let last;
  while (Date.now() - start < timeout) {
    last = await fn();
    if (last) return last;
    await new Promise((r) => setTimeout(r, interval));
  }
  throw new Error(`Timeout waiting for ${label}; last=${String(last)}`);
}

const results = [];
function pass(name) {
  results.push({ name, ok: true });
  console.log(`  PASS  ${name}`);
}
function fail(name, error) {
  results.push({ name, ok: false, error: String(error?.stack || error) });
  console.error(`  FAIL  ${name}`);
  console.error(error);
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

async function testContentToAgents() {
  await withPage(async (page) => {
    await installMockApi(page, {
      user: {
        email: ADMIN.email,
        password: ADMIN.password,
        displayName: "E2E Admin",
        permissions: ADMIN_PERMS,
      },
      initialPolicy: "human_required",
    });
    await loginViaUi(page, ADMIN);
    await openContentPolicy(page);

    await expect(page.getByText("Agent review nội dung chữ: Luôn bắt buộc")).toBeVisible();
    await expect(page.getByRole("radiogroup", { name: /chế độ phát hành nội dung/i })).toBeVisible();
    await expect(page.locator('input[name="content-publishing-approval-policy"]')).toHaveCount(2);

    const before = await selectedPolicyValue(page);
    const target = before === "human_required" ? "automatic" : "human_required";
    await selectPolicy(page, target);
    await expect(page.getByText(/Đã lưu chính sách phát hành/i)).toBeVisible({ timeout: 15_000 });
    await poll(async () => (await selectedPolicyValue(page)) === target, {
      label: "content selected policy",
    });

    await openAgentsApprovalConfig(page);
    await poll(async () => (await selectedPolicyValue(page)) === target, {
      label: "agents selected policy",
    });
  });
}

async function testAgentsToContent() {
  await withPage(async (page) => {
    await installMockApi(page, {
      user: {
        email: ADMIN.email,
        password: ADMIN.password,
        displayName: "E2E Admin",
        permissions: ADMIN_PERMS,
      },
      initialPolicy: "automatic",
    });
    await loginViaUi(page, ADMIN);
    await openAgentsApprovalConfig(page);

    const before = await selectedPolicyValue(page);
    const target = before === "automatic" ? "human_required" : "automatic";
    await selectPolicy(page, target);
    await expect(page.getByText(/Đã lưu chính sách phát hành/i)).toBeVisible({ timeout: 15_000 });
    await poll(async () => (await selectedPolicyValue(page)) === target, {
      label: "agents after change",
    });

    await openContentPolicy(page);
    await poll(async () => (await selectedPolicyValue(page)) === target, {
      label: "content after agents change",
    });
  });
}

async function testMarketerReadOnly() {
  await withPage(async (page) => {
    await installMockApi(page, {
      user: {
        email: MARKETER.email,
        password: MARKETER.password,
        displayName: "E2E Marketer",
        permissions: MARKETER_PERMS,
      },
      initialPolicy: "human_required",
    });
    await loginViaUi(page, MARKETER);
    await openContentPolicy(page);

    await expect(page.getByText(/Chỉ admin \(system:config\)/i)).toBeVisible();
    for (const radio of await page.locator('input[name="content-publishing-approval-policy"]').all()) {
      await expect(radio).toBeDisabled();
    }
    await page
      .locator('input[name="content-publishing-approval-policy"][value="automatic"]')
      .click({ force: true });
    await poll(async () => (await selectedPolicyValue(page)) === "human_required", {
      timeout: 3_000,
      label: "unchanged after force click",
    });

    await openAgentsApprovalConfig(page);
    for (const radio of await page.locator('input[name="content-publishing-approval-policy"]').all()) {
      await expect(radio).toBeDisabled();
    }
  });
}

async function testKeyboard() {
  await withPage(async (page) => {
    await installMockApi(page, {
      user: {
        email: ADMIN.email,
        password: ADMIN.password,
        displayName: "E2E Admin",
        permissions: ADMIN_PERMS,
      },
      initialPolicy: "human_required",
    });
    await loginViaUi(page, ADMIN);
    await openContentPolicy(page);

    const human = page.locator(
      'input[name="content-publishing-approval-policy"][value="human_required"]',
    );
    const automatic = page.locator(
      'input[name="content-publishing-approval-policy"][value="automatic"]',
    );
    await human.focus();
    await page.keyboard.press("ArrowDown");
    if (!(await automatic.isChecked())) {
      await automatic.focus();
      await page.keyboard.press("Space");
    }
    await poll(async () => (await selectedPolicyValue(page)) === "automatic", {
      label: "keyboard select automatic",
    });
    await expect(page.getByText(/Đã lưu chính sách phát hành/i)).toBeVisible({ timeout: 15_000 });
  });
}

async function main() {
  // Soft check FE is up
  try {
    const res = await fetch(`${baseURL}/login`);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
  } catch (error) {
    console.error(`FE not reachable at ${baseURL}/login — start with: npm run dev`);
    console.error(error);
    process.exit(2);
  }

  console.log(`Running dual-screen policy E2E against ${baseURL}`);
  const cases = [
    ["admin changes policy on /content and sees it on /agents", testContentToAgents],
    ["admin changes policy on /agents and sees it on /content", testAgentsToContent],
    ["non-admin sees read-only policy radios on both screens", testMarketerReadOnly],
    ["policy radio group is keyboard operable", testKeyboard],
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
