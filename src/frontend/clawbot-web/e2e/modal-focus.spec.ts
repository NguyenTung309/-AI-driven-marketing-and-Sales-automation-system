import { expect, test, type Locator, type Page, type Route } from "@playwright/test";
import { DEFAULT_ADMIN, loginViaUi } from "./fixtures/auth";
import { installMockApi } from "./fixtures/mockApi";

const INSTAGRAM_ITEM_ID = "46464646-4646-4646-4646-464646464646";
const TIMESTAMP = "2026-07-22T08:00:00.000Z";

type InertSnapshot = Readonly<Record<string, string | null>>;

async function mountModalFocusHarness(page: Page): Promise<void> {
  await page.goto("/login", { waitUntil: "domcontentloaded" });
  await page.setContent(`
    <!doctype html>
    <html lang="en">
      <head><meta charset="utf-8"></head>
      <body>
        <header data-isolation-branch="body-header">
          <button type="button">Header action</button>
        </header>
        <div id="test-shell">
          <nav data-isolation-branch="pre-inert-navigation" inert="locked-before-open">
            <a href="#navigation">Pre-inert navigation</a>
          </nav>
          <main id="modal-focus-root"></main>
          <aside data-isolation-branch="shell-aside">
            <button type="button">Aside action</button>
          </aside>
        </div>
        <footer data-isolation-branch="body-footer">
          <button type="button">Footer action</button>
        </footer>
      </body>
    </html>
  `);
  await page.addScriptTag({
    type: "module",
    content: `
      import { mountModalFocusHarness } from "/e2e/modal-focus-harness.tsx";
      mountModalFocusHarness(document.getElementById("modal-focus-root"));
    `,
  });
  await page.waitForFunction(() => document.documentElement.dataset.modalFocusHarnessReady === "true");
}

async function captureInertSnapshot(page: Page): Promise<InertSnapshot> {
  return page.locator("[data-isolation-branch]").evaluateAll((elements) => Object.fromEntries(
    elements.map((element) => [
      element.getAttribute("data-isolation-branch") ?? "",
      element.getAttribute("inert"),
    ]),
  ));
}

async function clearOwnedInertPath(locator: Locator): Promise<void> {
  await locator.evaluate((target) => {
    let current: HTMLElement | null = target as HTMLElement;
    while (current && current !== document.body) {
      current.removeAttribute("inert");
      current = current.parentElement;
    }
  });
}

async function expectFocusInside(page: Page, dialogName: string): Promise<void> {
  await expect.poll(() => page.evaluate(() => (
    document.activeElement?.closest('[role="dialog"]')?.getAttribute("aria-label") ?? null
  ))).toBe(dialogName);
}

async function expectIsolationBranchesInert(page: Page): Promise<void> {
  await expect.poll(() => page.locator("[data-isolation-branch]").evaluateAll(
    (elements) => elements.every((element) => element.hasAttribute("inert")),
  )).toBe(true);
}

const instagramItem = {
  id: INSTAGRAM_ITEM_ID,
  briefId: null,
  platform: "instagram",
  status: "approved",
  body: "Instagram E2E approved item",
  assetsJson: "[]",
  createdBy: "e2e",
  approvedBy: "e2e-admin",
  approvedAt: TIMESTAMP,
  createdAt: TIMESTAMP,
  updatedAt: TIMESTAMP,
  contentRevision: 1,
  agentReview: {
    status: "passed",
    reviewedRevision: 1,
    reviewedByAgentId: "content-reviewer",
    reviewedAt: TIMESTAMP,
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
    approvedAt: TIMESTAMP,
    reason: null,
    requirementReason: null,
  },
  workflowState: "approved_for_publish",
  canApprove: false,
  canReject: false,
  canRetryReview: false,
  canSchedule: true,
  canPublish: false,
} as const;

function fulfillJson(route: Route, body: unknown): Promise<void> {
  return route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify(body),
  });
}

async function installInstagramSchedulingMocks(page: Page): Promise<void> {
  await page.route(
    (url) => {
      const path = new URL(url).pathname;
      return path === "/api/content/queue"
        || path === `/api/content/items/${INSTAGRAM_ITEM_ID}`
        || path === "/api/content/calendar"
        || path === "/api/content/publish-targets";
    },
    async (route) => {
      const path = new URL(route.request().url()).pathname;
      if (path === "/api/content/queue") {
        return fulfillJson(route, {
          items: [instagramItem],
          total: 1,
          page: 1,
          pageSize: 40,
          nextCursor: null,
        });
      }
      if (path === `/api/content/items/${INSTAGRAM_ITEM_ID}`) {
        return fulfillJson(route, instagramItem);
      }
      if (path === "/api/content/calendar") {
        return fulfillJson(route, { items: [] });
      }
      return fulfillJson(route, { mode: "standalone", items: [] });
    },
  );
}

async function openInstagramScheduleDialog(page: Page): Promise<{
  readonly dialog: Locator;
  readonly opener: Locator;
}> {
  await installMockApi(page);
  await installInstagramSchedulingMocks(page);
  await loginViaUi(page, DEFAULT_ADMIN);
  await page.goto("/content");

  await expect(page.getByRole("textbox", { name: /Nội dung bài viết/i })).toHaveValue(
    /Instagram E2E approved item/,
  );
  const opener = page.getByRole("button", { name: /Đổi lịch \(tuỳ chọn\)/i });
  await expect(opener).toBeEnabled();
  await opener.click();

  const dialog = page.getByRole("dialog", { name: "Lên lịch xuất bản nội dung" });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByText(/tài khoản Instagram độc lập/i)).toBeVisible();
  return { dialog, opener };
}

test.describe("shared Modal focus management", () => {
  test("focuses the first control and contains Tab in both directions", async ({ page }) => {
    const { dialog } = await openInstagramScheduleDialog(page);
    const closeButton = dialog.getByRole("button", { name: "Đóng" });
    const confirmButton = dialog.getByRole("button", { name: "Xác nhận lên lịch" });

    await expect(closeButton).toBeFocused();

    await confirmButton.focus();
    await page.keyboard.press("Tab");
    await expect(closeButton).toBeFocused();

    await closeButton.focus();
    await page.keyboard.press("Shift+Tab");
    await expect(confirmButton).toBeFocused();
  });

  test("Escape closes the dialog and restores focus to its opener", async ({ page }) => {
    const { dialog, opener } = await openInstagramScheduleDialog(page);

    await page.keyboard.press("Escape");

    await expect(dialog).toBeHidden();
    await expect(opener).toBeFocused();
  });

  test("dialog interactions stay open while an overlay click closes and restores focus", async ({ page }) => {
    const { dialog, opener } = await openInstagramScheduleDialog(page);

    await dialog.getByRole("button", { name: "Chọn thời điểm riêng" }).click();
    await expect(dialog).toBeVisible();

    const backdrop = page.locator('div.fixed.inset-0[role="presentation"]');
    await backdrop.click({ position: { x: 2, y: 2 } });

    await expect(dialog).toBeHidden();
    await expect(opener).toBeFocused();
  });

  test("isolates ancestry branches and rejects programmatic or pointer focus escape", async ({ page }) => {
    await mountModalFocusHarness(page);
    const originalInertState = await captureInertSnapshot(page);
    const opener = page.getByTestId("outer-opener");
    const background = page.getByTestId("background-button");

    await opener.click();
    const dialog = page.getByRole("dialog", { name: "Outer dialog" });
    await expect(dialog).toBeVisible();
    await expect(page.getByTestId("outer-initial")).toBeFocused();
    await expectIsolationBranchesInert(page);
    await expect(page.locator("#test-shell")).not.toHaveAttribute("inert", /.*/);
    await expect(page.locator("#modal-focus-root")).not.toHaveAttribute("inert", /.*/);

    await background.evaluate((element) => element.focus());
    await expect(page.getByTestId("outer-initial")).toBeFocused();

    await clearOwnedInertPath(background);
    await background.evaluate((element) => element.focus());
    await expect(background).not.toBeFocused();
    await expectFocusInside(page, "Outer dialog");

    const backdrop = dialog.locator("xpath=..");
    await backdrop.evaluate((element) => {
      (element as HTMLElement).style.pointerEvents = "none";
    });
    const backgroundBox = await background.boundingBox();
    if (!backgroundBox) throw new Error("Background button has no bounding box.");
    await page.mouse.click(
      backgroundBox.x + backgroundBox.width / 2,
      backgroundBox.y + backgroundBox.height / 2,
    );
    await expect(page.getByTestId("background-click-count")).toHaveText("1");
    await expect(background).not.toBeFocused();
    await expectFocusInside(page, "Outer dialog");

    await page.keyboard.press("Escape");
    await expect(dialog).toHaveCount(0);
    await expect(opener).toBeFocused();
    expect(await captureInertSnapshot(page)).toEqual(originalInertState);
  });

  test("keeps only the topmost nested dialog interactive and restores each layer", async ({ page }) => {
    await mountModalFocusHarness(page);
    const originalInertState = await captureInertSnapshot(page);
    const outerOpener = page.getByTestId("outer-opener");

    await outerOpener.click();
    const outerDialog = page.getByRole("dialog", { name: "Outer dialog" });
    const outerBackdrop = outerDialog.locator("xpath=..");
    const innerOpener = page.getByTestId("inner-opener");
    await innerOpener.click();

    let innerDialog = page.getByRole("dialog", { name: "Inner dialog" });
    let innerBackdrop = innerDialog.locator("xpath=..");
    await expect(page.getByTestId("inner-initial")).toBeFocused();
    await expect.poll(() => outerBackdrop.evaluate((element) => element.hasAttribute("inert"))).toBe(true);
    await expect.poll(() => innerBackdrop.evaluate((element) => element.hasAttribute("inert"))).toBe(false);
    await expectIsolationBranchesInert(page);

    await outerBackdrop.dispatchEvent("pointerdown", { pointerId: 1, button: 0 });
    await outerBackdrop.dispatchEvent("pointerup", { pointerId: 1, button: 0 });
    await expect(outerDialog).toBeVisible();
    await expect(innerDialog).toBeVisible();

    await innerBackdrop.dispatchEvent("pointerdown", { pointerId: 2, button: 0 });
    await innerBackdrop.dispatchEvent("pointerup", { pointerId: 2, button: 0 });
    await expect(page.getByRole("dialog", { name: "Inner dialog" })).toHaveCount(0);
    await expect(outerDialog).toBeVisible();
    await expect(innerOpener).toBeFocused();
    await expect.poll(() => outerBackdrop.evaluate((element) => element.hasAttribute("inert"))).toBe(false);
    await expectIsolationBranchesInert(page);

    await innerOpener.click();
    innerDialog = page.getByRole("dialog", { name: "Inner dialog" });
    innerBackdrop = innerDialog.locator("xpath=..");
    await expect(innerDialog).toBeVisible();
    await expect.poll(() => innerBackdrop.evaluate((element) => element.hasAttribute("inert"))).toBe(false);
    await page.keyboard.press("Escape");
    await expect(innerDialog).toHaveCount(0);
    await expect(outerDialog).toBeVisible();
    await expect(innerOpener).toBeFocused();

    await page.keyboard.press("Escape");
    await expect(outerDialog).toHaveCount(0);
    await expect(outerOpener).toBeFocused();
    expect(await captureInertSnapshot(page)).toEqual(originalInertState);
  });

  test("makes dismissible false non-dismissible while progress remains accessible", async ({ page }) => {
    await mountModalFocusHarness(page);
    const originalInertState = await captureInertSnapshot(page);
    const opener = page.getByTestId("progress-opener");

    await opener.click();
    const dialog = page.getByRole("dialog", { name: "Progress dialog" });
    const backdrop = dialog.locator("xpath=..");
    const progress = dialog.getByRole("status");
    const finish = page.getByTestId("finish-progress");

    await expect(dialog).toHaveAttribute("aria-modal", "true");
    await expect(progress).toHaveText("Processing remains visible");
    await expect(progress).toHaveAttribute("aria-live", "polite");
    await expect(dialog.getByRole("button", { name: "Đóng" })).toHaveCount(0);
    await expect(finish).toBeFocused();

    await page.keyboard.press("Escape");
    await expect(dialog).toBeVisible();
    await expect(progress).toBeVisible();
    await backdrop.dispatchEvent("pointerdown", { pointerId: 3, button: 0 });
    await backdrop.dispatchEvent("pointerup", { pointerId: 3, button: 0 });
    await expect(dialog).toBeVisible();
    await expect(progress).toBeVisible();

    await finish.click();
    await expect(dialog).toHaveCount(0);
    await expect(opener).toBeFocused();
    expect(await captureInertSnapshot(page)).toEqual(originalInertState);
  });
});
