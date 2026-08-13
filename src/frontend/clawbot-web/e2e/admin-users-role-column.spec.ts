import { expect, test, type Page, type Route } from "@playwright/test";
import { DEFAULT_ADMIN, loginViaUi } from "./fixtures/auth";
import { installMockApi } from "./fixtures/mockApi";

async function installAdminUsersMock(page: Page): Promise<void> {
  await installMockApi(page, {
    user: {
      ...DEFAULT_ADMIN,
      displayName: "E2E Admin",
      permissions: ["admin.system"],
    },
  });

  await page.route("**/api/rbac/roles", async (route: Route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify([]),
    });
  });
  await page.route("**/api/rbac/permissions", async (route: Route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify([]),
    });
  });
  await page.route("**/api/api-keys", async (route: Route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify([]),
    });
  });
  await page.route("**/api/admin/users*", async (route: Route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        total: 2,
        page: 1,
        pageSize: 50,
        items: [
          {
            id: "00000000-0000-0000-0000-000000000011",
            email: "multi-role@example.test",
            displayName: "Nhiều vai trò",
            phone: null,
            isActive: true,
            lastLoginAt: null,
            roles: ["Admin", "SalesLead"],
            pancakeChannels: [],
          },
          {
            id: "00000000-0000-0000-0000-000000000012",
            email: "unassigned@example.test",
            displayName: "Chưa được gán",
            phone: null,
            isActive: true,
            lastLoginAt: null,
            roles: [],
            pancakeChannels: [],
          },
        ],
      }),
    });
  });
}

test("shows each account's Identity roles in the user table", async ({ page }) => {
  await installAdminUsersMock(page);
  await loginViaUi(page, DEFAULT_ADMIN);
  await page.goto("/system");

  const usersTable = page.getByRole("table").filter({ hasText: "multi-role@example.test" });
  await expect(usersTable.locator("thead").getByText("Vai trò", { exact: true })).toBeVisible();

  const multiRoleRow = usersTable.getByRole("row", { name: /multi-role@example\.test/i });
  await expect(multiRoleRow.getByText("Admin", { exact: true })).toBeVisible();
  await expect(multiRoleRow.getByText("SalesLead", { exact: true })).toBeVisible();

  const unassignedRow = usersTable.getByRole("row", { name: /unassigned@example\.test/i });
  await expect(unassignedRow.getByText("Chưa gán", { exact: true })).toBeVisible();
});
