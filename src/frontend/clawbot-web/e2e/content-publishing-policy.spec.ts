import { expect, test } from "@playwright/test";
import {
  loginViaUi,
  openAgentsApprovalConfig,
  openContentPolicy,
  policyRadios,
  policySection,
  selectPolicy,
  selectedPolicyValue,
  DEFAULT_ADMIN,
  DEFAULT_MARKETER,
} from "./fixtures/auth";
import { installMockApi, marketerUser } from "./fixtures/mockApi";

const live = process.env.E2E_LIVE === "1";

test.describe("content publishing policy dual-screen", () => {
  test.describe.configure({ mode: "serial" });

  test("admin can change policy on /content and see it on /agents", async ({ page }) => {
    if (!live) {
      await installMockApi(page, { initialPolicy: "human_required" });
    }

    await loginViaUi(page, DEFAULT_ADMIN);
    await openContentPolicy(page);

    await expect(policySection(page)).toBeVisible();
    await expect(page.getByText("Agent review nội dung chữ: Luôn bắt buộc")).toBeVisible();

    const group = page.getByRole("radiogroup", { name: /chế độ phát hành nội dung/i });
    await expect(group).toBeVisible();
    await expect(policyRadios(page)).toHaveCount(2);

    // Prefer switching to automatic when currently human_required (default seed/mock).
    const before = await selectedPolicyValue(page);
    const target = before === "human_required" ? "automatic" : "human_required";
    await selectPolicy(page, target);

    await expect(
      page.getByText(/Đã lưu chính sách phát hành/i),
    ).toBeVisible({ timeout: 15_000 });
    await expect
      .poll(async () => selectedPolicyValue(page), { timeout: 10_000 })
      .toBe(target);

    // Shared RQ key: agents surface must show the same value after navigation.
    await openAgentsApprovalConfig(page);
    await expect
      .poll(async () => selectedPolicyValue(page), { timeout: 15_000 })
      .toBe(target);

    // Version pill updates (mock starts at v1 → v2 on first change).
    await expect(page.getByText(new RegExp(`${target === "automatic" ? "Tự động phát hành" : "Cần người duyệt"} · v\\d+`))).toBeVisible();
  });

  test("admin can change policy on /agents and see it on /content", async ({ page }) => {
    if (!live) {
      await installMockApi(page, { initialPolicy: "automatic" });
    }

    await loginViaUi(page, DEFAULT_ADMIN);
    await openAgentsApprovalConfig(page);

    const before = await selectedPolicyValue(page);
    const target = before === "automatic" ? "human_required" : "automatic";
    await selectPolicy(page, target);

    await expect(page.getByText(/Đã lưu chính sách phát hành/i)).toBeVisible({ timeout: 15_000 });
    await expect
      .poll(async () => selectedPolicyValue(page), { timeout: 10_000 })
      .toBe(target);

    await openContentPolicy(page);
    await expect
      .poll(async () => selectedPolicyValue(page), { timeout: 15_000 })
      .toBe(target);
  });

  test("non-admin sees read-only policy radios on both screens", async ({ page }) => {
    test.skip(live, "Live marketer account is optional; run mock mode for read-only coverage.");

    await installMockApi(page, {
      user: marketerUser(),
      initialPolicy: "human_required",
    });

    await loginViaUi(page, DEFAULT_MARKETER);
    await openContentPolicy(page);

    await expect(page.getByText(/Chỉ admin \(system:config\)/i)).toBeVisible();
    for (const radio of await policyRadios(page).all()) {
      await expect(radio).toBeDisabled();
    }

    // Attempting a check should not mutate (disabled).
    await page.locator('input[name="content-publishing-approval-policy"][value="automatic"]').click({ force: true });
    await expect
      .poll(async () => selectedPolicyValue(page), { timeout: 3_000 })
      .toBe("human_required");

    await openAgentsApprovalConfig(page);
    for (const radio of await policyRadios(page).all()) {
      await expect(radio).toBeDisabled();
    }
    await expect
      .poll(async () => selectedPolicyValue(page), { timeout: 10_000 })
      .toBe("human_required");
  });

  test("policy radio group is keyboard operable", async ({ page }) => {
    if (!live) {
      await installMockApi(page, { initialPolicy: "human_required" });
    }

    await loginViaUi(page, DEFAULT_ADMIN);
    await openContentPolicy(page);

    const human = page.locator(
      'input[name="content-publishing-approval-policy"][value="human_required"]',
    );
    const automatic = page.locator(
      'input[name="content-publishing-approval-policy"][value="automatic"]',
    );

    await human.focus();
    await expect(human).toBeFocused();
    // ArrowRight / Space patterns vary by browser; use keyboard on focused radio.
    await page.keyboard.press("ArrowDown");
    // Some browsers move focus without selecting; force selection via Space if needed.
    if (!(await automatic.isChecked())) {
      await automatic.focus();
      await page.keyboard.press("Space");
    }

    await expect
      .poll(async () => selectedPolicyValue(page), { timeout: 15_000 })
      .toBe("automatic");
    await expect(page.getByText(/Đã lưu chính sách phát hành/i)).toBeVisible({ timeout: 15_000 });
  });
});
