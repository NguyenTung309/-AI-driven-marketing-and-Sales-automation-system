import { expect, type Page } from "@playwright/test";

export const DEFAULT_ADMIN = {
  email: process.env.E2E_ADMIN_EMAIL ?? "admin@clawbot.local",
  password: process.env.E2E_ADMIN_PASSWORD ?? "Admin@12345",
} as const;

export const DEFAULT_MARKETER = {
  email: process.env.E2E_MARKETER_EMAIL ?? "marketer@clawbot.local",
  password: process.env.E2E_MARKETER_PASSWORD ?? "Marketer@12345",
} as const;

/** Live-stack login through the real LoginPage form. */
export async function loginViaUi(
  page: Page,
  credentials: { readonly email: string; readonly password: string } = DEFAULT_ADMIN,
): Promise<void> {
  await page.goto("/login");
  await page.locator("#email").fill(credentials.email);
  await page.locator("#password").fill(credentials.password);
  await page.getByRole("button", { name: /đăng nhập|login/i }).click();
  await page.waitForURL((url) => !url.pathname.includes("/login"), { timeout: 30_000 });
}

export function policyRadios(page: Page) {
  return page.locator('input[name="content-publishing-approval-policy"]');
}

export function policySection(page: Page) {
  return page.getByRole("heading", { name: "Chính sách phát hành nội dung" });
}

async function waitForPolicyHydrated(page: Page): Promise<void> {
  // StatusPill appears only after GET publishing-policy returns (e.g. "Cần người duyệt · v1").
  await page.getByText(/· v\d+/).first().waitFor({ state: "visible", timeout: 30_000 });
}

export async function openAgentsApprovalConfig(page: Page): Promise<void> {
  await page.goto("/agents");
  await page.getByRole("button", { name: /cấu hình duyệt/i }).click();
  await policySection(page).waitFor({ state: "visible" });
  await waitForPolicyHydrated(page);
}

export async function openContentPolicy(page: Page): Promise<void> {
  await page.goto("/content");
  await policySection(page).waitFor({ state: "visible" });
  await waitForPolicyHydrated(page);
}

export async function selectedPolicyValue(page: Page): Promise<"automatic" | "human_required"> {
  const automatic = page.locator(
    'input[name="content-publishing-approval-policy"][value="automatic"]',
  );
  if (await automatic.isChecked()) return "automatic";
  return "human_required";
}

export async function selectPolicy(
  page: Page,
  value: "automatic" | "human_required",
): Promise<void> {
  const radio = page.locator(
    `input[name="content-publishing-approval-policy"][value="${value}"]`,
  );
  await radio.waitFor({ state: "visible", timeout: 15_000 });
  await expect(radio).toBeEnabled({ timeout: 15_000 });
  if (await radio.isChecked()) return;
  // Controlled React radio: Playwright .check() asserts native state flip before
  // the mutation resolves and fails. Click the wrapping label so React onChange runs.
  await radio.locator("xpath=ancestor::label[1]").click();
}
