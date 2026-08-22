import { chromium, expect } from "@playwright/test";

const baseURL = process.env.E2E_BASE_URL ?? "http://127.0.0.1:15876";
const ADMIN = {
  email: process.env.E2E_ADMIN_EMAIL ?? "admin@clawbot.local",
  password: process.env.E2E_ADMIN_PASSWORD ?? "Admin@12345",
};
// AdminConsolePage gates the "Tích hợp" tab on the fine-grained "admin:integration" permission
// since a12294c (2026-08-16) split the old coarse "admin.system" check apart. This fixture kept
// granting the pre-split permissions, so the tab button never rendered and every flow here timed
// out waiting to click it.
const ADMIN_PERMISSIONS = ["admin:integration", "system:config"];
const INITIAL_USER_ID = "17841400000000000";
const REPLACEMENT_USER_ID = "17841400000000001";
const SECRET_TOKEN = "instagram-standalone-e2e-secret";

function emptyPage() {
  return { items: [], total: 0, page: 1, pageSize: 50, nextCursor: null };
}

function metaStatus() {
  return {
    configured: false,
    businessWebhookConfigured: false,
    appConfiguration: {
      configured: false,
      businessWebhookConfigured: false,
      source: "database",
      appId: "",
      configurationId: "",
      authorizationMode: "development_user",
      hasAppSecret: false,
      hasWebhookVerifyToken: false,
      redirectUri: "",
      frontendReturnUrl: "",
      apiVersion: "v25.0",
      updatedAt: null,
    },
    connected: false,
    status: "disconnected",
    clientBusinessId: "",
    systemUserId: "",
    tokenType: "",
    grantedScopes: [],
    expiresAt: null,
    dataAccessExpiresAt: null,
    lastValidatedAt: null,
    lastError: null,
    assets: [],
  };
}

function instagramCredential(overrides = {}) {
  return {
    provider: "instagram",
    resolutionState: "absent",
    enabled: false,
    endpoint: "",
    pageId: "",
    oaId: "",
    hasPageAccessToken: false,
    hasOaAccessToken: false,
    updatedAt: null,
    ...overrides,
  };
}

async function installMockApi(page, options = {}) {
  let sessionActive = false;
  let instagramUpdateFailuresRemaining = options.instagramUpdateFailures ?? 0;
  let instagram = instagramCredential(options.instagram);
  let hasStoredToken = instagram.hasPageAccessToken;
  let socialCredentialReadCount = 0;
  let releaseSocialCredentials = () => {};
  const socialCredentialsReady = options.holdSocialCredentials
    ? new Promise((resolve) => {
        releaseSocialCredentials = resolve;
      })
    : Promise.resolve();
  const requests = [];

  const json = async (route, status, body) => {
    await route.fulfill({
      status,
      contentType: "application/json",
      body: body == null ? "" : JSON.stringify(body),
    });
  };

  await page.route(
    (url) => new URL(url).pathname.startsWith("/auth"),
    async (route) => {
      const request = route.request();
      const pathName = new URL(request.url()).pathname;
      if (request.method() === "POST" && pathName.endsWith("/auth/refresh")) {
        return sessionActive
          ? json(route, 200, { accessToken: "mock-access-token", expiresAt: new Date(Date.now() + 3600_000).toISOString() })
          : json(route, 401, { error: "no_session" });
      }
      if (request.method() === "POST" && pathName.endsWith("/auth/login")) {
        const body = request.postDataJSON();
        if (body.email !== ADMIN.email || body.password !== ADMIN.password) {
          return json(route, 401, { error: "invalid_credentials" });
        }
        sessionActive = true;
        return json(route, 200, {
          accessToken: "mock-access-token",
          expiresAt: new Date(Date.now() + 3600_000).toISOString(),
        });
      }
      if (request.method() === "GET" && pathName.endsWith("/auth/me")) {
        return sessionActive
          ? json(route, 200, {
              id: "00000000-0000-0000-0000-000000000002",
              email: ADMIN.email,
              displayName: "Instagram E2E Admin",
              permissions: ADMIN_PERMISSIONS,
            })
          : json(route, 401, { error: "unauthorized" });
      }
      if (request.method() === "POST" && pathName.endsWith("/auth/logout")) {
        sessionActive = false;
        return json(route, 204, null);
      }
      return json(route, 404, { error: `unmocked auth request ${request.method()} ${pathName}` });
    },
  );

  await page.route(
    (url) => new URL(url).pathname.startsWith("/api"),
    async (route) => {
      const request = route.request();
      const method = request.method();
      const pathName = new URL(request.url()).pathname;
      requests.push({ method, url: request.url(), body: request.postData() });

      if (method === "GET" && pathName === "/api/admin/users") {
        return json(route, 200, emptyPage());
      }
      if (method === "GET" && pathName === "/api/rbac/roles") {
        return json(route, 200, []);
      }
      if (method === "GET" && pathName === "/api/rbac/permissions") {
        return json(route, 200, []);
      }
      if (method === "GET" && pathName === "/api/api-keys") {
        return json(route, 200, []);
      }
      if (method === "GET" && pathName === "/api/admin/tenant/branding") {
        return json(route, 200, {
          brandName: "ClawBot E2E",
          logoUrl: null,
          primaryColor: "#006c4c",
          accentColor: "#6750a4",
          supportName: "ClawBot",
          widgetGreeting: "Xin chào",
        });
      }
      if (method === "GET" && pathName === "/api/channels/pancake/config") {
        return json(route, 404, { error: "not_configured" });
      }
      if (method === "GET" && pathName === "/api/channels/pancake/webhook-url") {
        return json(route, 200, {
          webhookUrl: "https://example.test/api/webhooks/pancake/e2e",
          tenantSlug: "e2e",
        });
      }
      if (method === "GET" && pathName === "/api/admin/meta") {
        return json(route, 200, metaStatus());
      }
      if (method === "GET" && pathName === "/api/admin/social-credentials") {
        socialCredentialReadCount += 1;
        await socialCredentialsReady;
        if (socialCredentialReadCount > 1) {
          return json(route, 503, { errorCode: "mock_refetch_unavailable", message: "refetch unavailable" });
        }
        return json(route, 200, {
          items: [
            {
              provider: "zalo",
              resolutionState: "absent",
              enabled: false,
              endpoint: "",
              pageId: "",
              oaId: "",
              hasPageAccessToken: false,
              hasOaAccessToken: false,
              updatedAt: null,
            },
            instagram,
          ],
        });
      }
      if (method === "PUT" && pathName === "/api/admin/social-credentials/instagram") {
        const body = request.postDataJSON();
        if (instagramUpdateFailuresRemaining > 0) {
          instagramUpdateFailuresRemaining -= 1;
          return json(route, 500, { message: "Simulated Instagram save failure." });
        }

        const replacesInvalidCredential = body.enabled !== undefined
          && body.pageId !== null
          && body.pageId !== undefined
          && body.pageAccessToken !== null
          && body.pageAccessToken !== undefined;
        if (instagram.resolutionState === "invalid" && !replacesInvalidCredential) {
          return json(route, 400, { message: "Replace all Instagram credential fields." });
        }

        const nextHasStoredToken = body.pageAccessToken === null || body.pageAccessToken === undefined
          ? hasStoredToken
          : body.pageAccessToken !== "";
        const nextEnabled = body.enabled ?? instagram.enabled;
        const nextPageId = body.pageId ?? instagram.pageId;
        if (nextEnabled && (!/^\d+$/.test(nextPageId) || !nextHasStoredToken)) {
          return json(route, 400, { message: "Enabled Instagram credentials require a numeric user ID and access token." });
        }

        hasStoredToken = nextHasStoredToken;
        instagram = {
          ...instagram,
          resolutionState: nextEnabled ? "resolved" : "disabled",
          enabled: nextEnabled,
          pageId: nextPageId,
          hasPageAccessToken: hasStoredToken,
          updatedAt: instagram.updatedAt,
        };
        return json(route, 200, instagram);
      }
      if (method === "GET" && pathName === "/api/notifications/unread-count") {
        return json(route, 200, { count: 0 });
      }
      if (method === "GET" && pathName.startsWith("/api/notifications")) {
        return json(route, 200, { items: [], total: 0, nextCursor: null, unreadCount: 0 });
      }
      if (method === "GET") {
        return json(route, 200, emptyPage());
      }
      return json(route, 200, { ok: true });
    },
  );

  return {
    requests,
    releaseSocialCredentials,
    socialCredentialReads: () => socialCredentialReadCount,
    instagramUpdates: () => requests
      .filter((request) => request.method === "PUT" && request.url.includes("/api/admin/social-credentials/instagram"))
      .map((request) => JSON.parse(request.body ?? "{}")),
  };
}

async function loginAndOpenIntegrations(page) {
  await page.goto(`${baseURL}/login`, { waitUntil: "domcontentloaded" });
  await page.locator("#email").fill(ADMIN.email);
  await page.locator("#password").fill(ADMIN.password);
  await page.getByRole("button", { name: /đăng nhập|login/i }).click();
  await page.waitForURL((url) => !url.pathname.includes("/login"), { timeout: 30_000 });
  await page.goto(`${baseURL}/system`, { waitUntil: "domcontentloaded" });
  await page.getByRole("button", { name: "Tích hợp", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Kênh đăng bài" })).toBeVisible({ timeout: 15_000 });
}

function instagramControls(page) {
  return {
    enabled: page.getByRole("switch", { name: /Instagram.*(?:riêng|độc lập)|dùng.*Instagram.*riêng/i }),
    userId: page.getByLabel(/Instagram User ID|ID người dùng Instagram/i),
    token: page.getByLabel(/Access token Instagram|Mã truy cập Instagram|token Instagram/i),
    save: page.getByRole("button", { name: /Lưu Instagram/i }),
  };
}

async function runCredentialsLoadingGuardFlow(page) {
  const audit = await installMockApi(page, { holdSocialCredentials: true });
  await loginAndOpenIntegrations(page);

  const { enabled, userId, token, save } = instagramControls(page);
  await expect.poll(() => audit.socialCredentialReads(), { timeout: 10_000 }).toBe(1);
  await expect(enabled).toBeDisabled();
  await expect(userId).toBeDisabled();
  await expect(token).toBeDisabled();
  await expect(save).toBeDisabled();

  audit.releaseSocialCredentials();
  await expect(enabled).toBeEnabled();
  await expect(userId).toBeEnabled();
  await expect(token).toBeEnabled();
  await expect(save).toBeEnabled();
}

async function runInstagramCredentialFlow(page) {
  const audit = await installMockApi(page);
  await loginAndOpenIntegrations(page);

  await expect(page.getByRole("heading", { name: /Instagram.*(?:độc lập|tùy chọn|tuỳ chọn)/i })).toBeVisible();
  await expect(page.getByText(/(?:Meta.*(?:liên kết|dùng chung)|(?:liên kết|dùng chung).*Meta)/i)).toBeVisible();

  const { enabled, userId, token, save } = instagramControls(page);

  await expect(token).toHaveAttribute("type", "password");
  await expect(token).toHaveValue("");
  await expect(page.locator("body")).not.toContainText(SECRET_TOKEN);

  await enabled.click();
  await expect(enabled).toHaveAttribute("aria-checked", "true");
  await userId.fill(INITIAL_USER_ID);
  await token.fill(SECRET_TOKEN);
  await save.click();
  await expect.poll(() => audit.instagramUpdates().length, { timeout: 10_000 }).toBe(1);

  const createPayload = audit.instagramUpdates()[0];
  expect(createPayload).toEqual({
    enabled: true,
    pageId: INITIAL_USER_ID,
    pageAccessToken: SECRET_TOKEN,
  });
  expect(createPayload).not.toHaveProperty("endpoint");
  expect(createPayload).not.toHaveProperty("oaId");
  expect(createPayload).not.toHaveProperty("oaAccessToken");
  await expect(token).toHaveValue("");
  await expect(token).toHaveAttribute("type", "password");
  await expect(page.locator("body")).not.toContainText(SECRET_TOKEN);
  await expect(page.getByText(/để trống.*giữ|giữ.*mã.*đã lưu/i)).toBeVisible();

  await userId.fill(REPLACEMENT_USER_ID);
  await save.click();
  await expect.poll(() => audit.instagramUpdates().length, { timeout: 10_000 }).toBe(2);
  expect(audit.instagramUpdates()[1]).toEqual({
    enabled: true,
    pageId: REPLACEMENT_USER_ID,
    pageAccessToken: null,
  });

  await enabled.click();
  await expect(enabled).toHaveAttribute("aria-checked", "false");
  await page.getByRole("checkbox", { name: /Xóa mã truy cập|Xoá mã truy cập|Xóa token|Xoá token/i }).check();
  await save.click();
  await expect.poll(() => audit.instagramUpdates().length, { timeout: 10_000 }).toBe(3);
  expect(audit.instagramUpdates()[2]).toEqual({
    enabled: false,
    pageId: REPLACEMENT_USER_ID,
    pageAccessToken: "",
  });

  expect(audit.socialCredentialReads()).toBe(1);
  const secretOnReadPath = audit.requests.some((request) =>
    request.method === "GET"
    && (request.url.includes(SECRET_TOKEN) || (request.body ?? "").includes(SECRET_TOKEN)));
  expect(secretOnReadPath).toBe(false);
  await expect(page.locator("body")).not.toContainText(SECRET_TOKEN);
}

async function runFailedSaveClearsSecretFlow(page) {
  const retryToken = `${SECRET_TOKEN}-retry`;
  const audit = await installMockApi(page, { instagramUpdateFailures: 1 });
  await loginAndOpenIntegrations(page);

  const { enabled, userId, token, save } = instagramControls(page);
  await enabled.click();
  await userId.fill(INITIAL_USER_ID);
  await token.fill(SECRET_TOKEN);
  await save.click();

  await expect(page.getByText("Hệ thống đang gặp sự cố. Vui lòng thử lại sau.", { exact: true })).toBeVisible();
  await expect.poll(() => audit.instagramUpdates().length, { timeout: 10_000 }).toBe(1);
  expect(audit.instagramUpdates()[0]).toEqual({
    enabled: true,
    pageId: INITIAL_USER_ID,
    pageAccessToken: SECRET_TOKEN,
  });
  await expect(token).toHaveValue("");
  await expect(save).toHaveText("Lưu Instagram");
  await expect(page.locator("body")).not.toContainText(SECRET_TOKEN);

  await token.fill(retryToken);
  await expect(save).toBeEnabled();
  await save.click();
  await expect.poll(() => audit.instagramUpdates().length, { timeout: 10_000 }).toBe(2);
  expect(audit.instagramUpdates()[1]).toEqual({
    enabled: true,
    pageId: INITIAL_USER_ID,
    pageAccessToken: retryToken,
  });
  await expect(token).toHaveValue("");
  await expect(page.getByText("Hệ thống đang gặp sự cố. Vui lòng thử lại sau.", { exact: true })).toHaveCount(0);
  await expect(page.locator("body")).not.toContainText(retryToken);
}

async function runInvalidCredentialReplacementFlow(page) {
  const audit = await installMockApi(page, {
    instagram: instagramCredential({
      resolutionState: "invalid",
      updatedAt: "2026-07-22T07:00:00.000Z",
    }),
  });
  await loginAndOpenIntegrations(page);

  const { enabled, userId, token, save } = instagramControls(page);
  await enabled.click();
  await userId.fill(INITIAL_USER_ID);
  await token.fill(SECRET_TOKEN);
  await save.click();

  await expect.poll(() => audit.instagramUpdates().length, { timeout: 10_000 }).toBe(1);
  expect(audit.instagramUpdates()[0]).toEqual({
    enabled: true,
    pageId: INITIAL_USER_ID,
    pageAccessToken: SECRET_TOKEN,
  });
  await expect(page.getByText(/Không đọc được thông tin Instagram đã lưu/i)).toHaveCount(0);
  await expect(page.getByText("Đang dùng thông tin riêng", { exact: true })).toBeVisible();
  await expect(token).toHaveValue("");
  await expect(page.locator("body")).not.toContainText(SECRET_TOKEN);
}

async function runInvalidCredentialRepairFlow(page) {
  const audit = await installMockApi(page, {
    instagram: instagramCredential({
      resolutionState: "invalid",
      updatedAt: "2026-07-22T07:00:00.000Z",
    }),
  });
  await loginAndOpenIntegrations(page);

  await expect(page.getByText(/Không đọc được thông tin Instagram đã lưu/i)).toBeVisible();
  await expect(instagramControls(page).save).toBeDisabled();
  const clearAndDisable = page.getByRole("button", { name: "Tắt và xóa thông tin Instagram riêng", exact: true });
  await expect(clearAndDisable).toBeEnabled();
  await clearAndDisable.click();

  await expect.poll(() => audit.instagramUpdates().length, { timeout: 10_000 }).toBe(1);
  expect(audit.instagramUpdates()[0]).toEqual({
    enabled: false,
    pageId: "",
    pageAccessToken: "",
  });
  await expect(page.getByText(/Không đọc được thông tin Instagram đã lưu/i)).toHaveCount(0);
  await expect(page.getByText("Đang dùng mặc định", { exact: true })).toBeVisible();
  await expect(instagramControls(page).token).toHaveValue("");
  expect(audit.socialCredentialReads()).toBe(1);
}

async function main() {
  try {
    const response = await fetch(`${baseURL}/login`);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
  } catch (error) {
    console.error(`Frontend is not reachable at ${baseURL}/login. Start Vite before this runner.`);
    console.error(error);
    process.exit(2);
  }

  const cases = [
    ["Instagram controls wait for credential loading", runCredentialsLoadingGuardFlow],
    ["Instagram standalone credential admin flow", runInstagramCredentialFlow],
    ["failed Instagram save clears secrets and can retry", runFailedSaveClearsSecretFlow],
    ["invalid Instagram credential can be fully replaced", runInvalidCredentialReplacementFlow],
    ["invalid Instagram credential can be cleared and disabled", runInvalidCredentialRepairFlow],
  ];
  const browser = await chromium.launch({ headless: true });
  let failed = 0;
  try {
    for (const [name, run] of cases) {
      const context = await browser.newContext({ locale: "vi-VN" });
      const page = await context.newPage();
      try {
        await run(page);
        console.log(`PASS ${name}`);
      } catch (error) {
        failed += 1;
        console.error(`FAIL ${name}`);
        console.error(error);
      } finally {
        await context.close();
      }
    }
  } finally {
    await browser.close();
  }
  process.exitCode = failed ? 1 : 0;
}

await main();
