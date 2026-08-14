import { expect, test, type Page, type Route } from "@playwright/test";
import { DEFAULT_ADMIN, loginViaUi } from "./fixtures/auth";
import { installMockApi } from "./fixtures/mockApi";

async function installAdminConsoleMock(page: Page): Promise<void> {
  await installMockApi(page, {
    user: {
      ...DEFAULT_ADMIN,
      displayName: "E2E Admin",
      permissions: ["admin.system"],
    },
  });

  await page.route("**/api/**", async (route: Route) => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/api/api-keys") || path.endsWith("/api/admin/tenant/branding")) {
      throw new Error(`Removed admin setting requested: ${path}`);
    }
    await route.fallback();
  });
  await page.route("**/api/rbac/roles", async (route: Route) => {
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify([]) });
  });
  await page.route("**/api/rbac/permissions", async (route: Route) => {
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify([]) });
  });
  await page.route("**/api/admin/users*", async (route: Route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ total: 0, page: 1, pageSize: 50, items: [] }),
    });
  });
}

test("hides integration keys and tenant branding from the admin console", async ({ page }) => {
  await installAdminConsoleMock(page);
  await loginViaUi(page, DEFAULT_ADMIN);
  await page.goto("/system");

  await expect(page.getByRole("button", { name: "Khóa tích hợp" })).toHaveCount(0);
  await expect(page.getByText("Khóa tích hợp hoạt động", { exact: true })).toHaveCount(0);

  await page.getByRole("button", { name: "Tích hợp" }).click();

  await expect(page.getByRole("heading", { name: "Kênh Pancake" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Kênh đăng bài" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Meta Facebook" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Instagram độc lập (tùy chọn)" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Zalo OA" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Thương hiệu đơn vị" })).toHaveCount(0);
  await expect(page.getByText("Tên thương hiệu", { exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Lưu thương hiệu" })).toHaveCount(0);
});
