import { expect, test, type Locator, type Page, type Route } from "@playwright/test";
import { DEFAULT_ADMIN, loginViaUi } from "./fixtures/auth";
import { installMockApi } from "./fixtures/mockApi";

const ITEM_A_ID = "71717171-7171-7171-7171-717171717171";
const ITEM_B_ID = "72727272-7272-7272-7272-727272727272";
const ITEM_A_BODY = "Schedule session item A";
const ITEM_B_BODY = "Schedule session item B";
const TIMESTAMP = "2026-07-22T08:00:00.000Z";
const CONFIRMATION_LABEL = "Tôi xác nhận dùng tài khoản Instagram độc lập hiện đang cấu hình";
const RESELECTION_GUIDANCE = "Lịch Instagram này cần chọn lại đích đăng. Hãy xác nhận tài khoản Instagram độc lập hiện đang cấu hình hoặc chọn Meta Page liên kết rồi thử lại.";

type ScheduleOutcome = "success" | "conflict";

interface SchedulePayload {
  readonly scheduledAt: string | null;
  readonly metaAssetId: string | null;
  readonly confirmInstagramAccount: boolean;
}

interface CapturedScheduleRequest {
  readonly itemId: string;
  readonly payload: SchedulePayload;
}

interface ScheduleRequestCounts {
  readonly queue: number;
  readonly calendar: number;
}

interface ScheduleSessionMocks {
  readonly getRequests: () => readonly CapturedScheduleRequest[];
  readonly getRequestCounts: () => ScheduleRequestCounts;
  readonly releaseRequest: (index: number, outcome: ScheduleOutcome) => void;
}

interface Deferred<T> {
  readonly promise: Promise<T>;
  readonly resolve: (value: T) => void;
}

interface PendingScheduleRequest extends CapturedScheduleRequest {
  readonly outcome: Deferred<ScheduleOutcome>;
}

function createDeferred<T>(): Deferred<T> {
  let resolvePromise: ((value: T) => void) | null = null;
  const promise = new Promise<T>((resolve) => {
    resolvePromise = resolve;
  });
  return {
    promise,
    resolve: (value) => {
      if (!resolvePromise) throw new Error("Deferred resolver is unavailable.");
      resolvePromise(value);
    },
  };
}

function contentItem(id: string, body: string) {
  return {
    id,
    briefId: null,
    platform: "instagram",
    status: "scheduled",
    body,
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
    workflowState: "scheduled",
    canApprove: false,
    canReject: false,
    canRetryReview: false,
    canSchedule: true,
    canPublish: false,
  } as const;
}

const itemA = contentItem(ITEM_A_ID, ITEM_A_BODY);
const itemB = contentItem(ITEM_B_ID, ITEM_B_BODY);

function calendarItem(item: typeof itemA | typeof itemB, scheduleId: string) {
  return {
    scheduleId,
    contentItemId: item.id,
    platform: item.platform,
    status: "held",
    body: item.body,
    scheduledAt: "2026-07-25T02:00:00.000Z",
    postedAt: null,
    postUrl: null,
    metaAssetId: null,
    likeCount: null,
    commentCount: null,
    retryCount: 0,
    lastError: "Instagram target must be reselected.",
    requiresInstagramAccountConfirmation: true,
  } as const;
}

function fulfillJson(route: Route, status: number, body: unknown): Promise<void> {
  return route.fulfill({
    status,
    contentType: "application/json",
    body: JSON.stringify(body),
  });
}

async function installScheduleSessionMocks(page: Page): Promise<ScheduleSessionMocks> {
  let pendingRequests: readonly PendingScheduleRequest[] = [];
  let requestCounts: ScheduleRequestCounts = { queue: 0, calendar: 0 };

  await page.route(
    (url) => {
      const path = new URL(url).pathname;
      return path === "/api/content/queue"
        || path === `/api/content/items/${ITEM_A_ID}`
        || path === `/api/content/items/${ITEM_B_ID}`
        || path === "/api/content/calendar"
        || path === "/api/content/publish-targets"
        || path === `/api/content/items/${ITEM_A_ID}/schedule`
        || path === `/api/content/items/${ITEM_B_ID}/schedule`;
    },
    async (route) => {
      const request = route.request();
      const path = new URL(request.url()).pathname;

      if (request.method() === "GET" && path === "/api/content/queue") {
        requestCounts = { ...requestCounts, queue: requestCounts.queue + 1 };
        return fulfillJson(route, 200, {
          items: [itemA, itemB],
          total: 2,
          page: 1,
          pageSize: 40,
          nextCursor: null,
        });
      }
      if (request.method() === "GET" && path === `/api/content/items/${ITEM_A_ID}`) {
        return fulfillJson(route, 200, itemA);
      }
      if (request.method() === "GET" && path === `/api/content/items/${ITEM_B_ID}`) {
        return fulfillJson(route, 200, itemB);
      }
      if (request.method() === "GET" && path === "/api/content/calendar") {
        requestCounts = { ...requestCounts, calendar: requestCounts.calendar + 1 };
        return fulfillJson(route, 200, {
          items: [
            calendarItem(itemA, "73737373-7373-7373-7373-737373737373"),
            calendarItem(itemB, "74747474-7474-7474-7474-747474747474"),
          ],
        });
      }
      if (request.method() === "GET" && path === "/api/content/publish-targets") {
        return fulfillJson(route, 200, { mode: "standalone", items: [] });
      }
      if (request.method() === "POST" && path.endsWith("/schedule")) {
        const itemId = path.split("/").at(-2);
        if (!itemId) throw new Error(`Missing item id in schedule path: ${path}`);
        const payload = request.postDataJSON() as SchedulePayload;
        const pendingRequest: PendingScheduleRequest = {
          itemId,
          payload: { ...payload },
          outcome: createDeferred<ScheduleOutcome>(),
        };
        pendingRequests = [...pendingRequests, pendingRequest];
        const outcome = await pendingRequest.outcome.promise;

        if (outcome === "conflict") {
          return fulfillJson(route, 409, {
            errorCode: "content.instagram_target_reselection_required",
            message: "Instagram target must be explicitly reselected before this schedule can be changed.",
          });
        }

        return fulfillJson(route, 201, {
          id: itemId === ITEM_A_ID
            ? "75757575-7575-7575-7575-757575757575"
            : "76767676-7676-7676-7676-767676767676",
          contentItemId: itemId,
          platform: "instagram",
          scheduledAt: payload.scheduledAt ?? "2026-07-25T02:00:00.000Z",
          postedAt: null,
          status: "pending",
          postUrl: null,
          createdAt: TIMESTAMP,
          updatedAt: TIMESTAMP,
          metaAssetId: payload.metaAssetId,
          likeCount: null,
          commentCount: null,
          engagementSyncedAt: null,
          retryCount: 0,
          lastError: null,
        });
      }

      throw new Error(`Unhandled schedule-session request: ${request.method()} ${path}`);
    },
  );

  return {
    getRequests: () => pendingRequests.map(({ itemId, payload }) => ({ itemId, payload: { ...payload } })),
    getRequestCounts: () => ({ ...requestCounts }),
    releaseRequest: (index, outcome) => {
      const pendingRequest = pendingRequests[index];
      if (!pendingRequest) throw new Error(`Schedule request ${index} has not arrived.`);
      pendingRequest.outcome.resolve(outcome);
    },
  };
}

function scheduleDialog(page: Page): Locator {
  return page.getByRole("dialog", { name: "Lên lịch xuất bản nội dung" });
}

async function openInitialDialog(page: Page): Promise<Locator> {
  await expect(page.getByRole("textbox", { name: /Nội dung bài viết/i })).toHaveValue(ITEM_A_BODY);
  await page.getByRole("button", { name: "Đổi lịch (tuỳ chọn)", exact: true }).click();
  const dialog = scheduleDialog(page);
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole("checkbox", { name: CONFIRMATION_LABEL, exact: true })).toBeVisible();
  return dialog;
}

async function setSpecificSchedule(
  dialog: Locator,
  date: string,
  time: string,
  confirmStandaloneAccount = true,
): Promise<void> {
  await dialog.getByRole("button", { name: /Chọn thời điểm riêng/ }).click();
  await dialog.getByLabel("Ngày").fill(date);
  await dialog.getByLabel("Giờ").fill(time);
  const confirmation = dialog.getByRole("checkbox", { name: CONFIRMATION_LABEL, exact: true });
  if (confirmStandaloneAccount) await confirmation.check();
}

async function submitAndWaitForRequest(
  dialog: Locator,
  mocks: ScheduleSessionMocks,
  expectedRequestCount: number,
): Promise<void> {
  await dialog.getByRole("button", { name: "Xác nhận lên lịch", exact: true }).click();
  await expect.poll(() => mocks.getRequests().length).toBe(expectedRequestCount);
}

async function replacePendingDialogWithItemB(page: Page): Promise<Locator> {
  await page.getByRole("button", { name: new RegExp(ITEM_B_BODY) }).dispatchEvent("click");
  await expect(page.getByRole("textbox", { name: /Nội dung bài viết/i })).toHaveValue(ITEM_B_BODY);
  await page.getByRole("button", { name: "Đổi lịch (tuỳ chọn)", exact: true }).dispatchEvent("click");

  const dialog = scheduleDialog(page);
  await expect(dialog).toBeVisible();
  await expect(dialog.getByLabel("Giờ")).toHaveValue("09:00");
  await expect(dialog.getByRole("checkbox", { name: CONFIRMATION_LABEL, exact: true })).not.toBeChecked();
  return dialog;
}

async function setup(page: Page): Promise<ScheduleSessionMocks> {
  await installMockApi(page);
  const mocks = await installScheduleSessionMocks(page);
  await loginViaUi(page, DEFAULT_ADMIN);
  await page.goto("/content");
  return mocks;
}

function scheduledAtIso(date: string, time: string): string {
  return new Date(`${date}T${time}:00`).toISOString();
}

test.describe("content schedule dialog session fencing", () => {
  test("Escape, backdrop, and header close stay unavailable while a schedule request is pending", async ({ page }) => {
    const mocks = await setup(page);
    const dialog = await openInitialDialog(page);
    await setSpecificSchedule(dialog, "2026-08-10", "10:15");
    await submitAndWaitForRequest(dialog, mocks, 1);

    try {
      await page.keyboard.press("Escape");
      await expect(dialog).toBeVisible();

      await page.locator('div.fixed.inset-0[role="presentation"]').click({ position: { x: 2, y: 2 } });
      await expect(dialog).toBeVisible();

      const headerClose = dialog.getByRole("button", { name: "Đóng", exact: true });
      await expect(headerClose).toBeDisabled();
      await headerClose.click({ force: true });
      await expect(dialog).toBeVisible();
      await expect(dialog.getByRole("button", { name: "Hủy bỏ", exact: true })).toBeDisabled();
    } finally {
      mocks.releaseRequest(0, "success");
    }
  });

  test("a delayed success for item A invalidates data without closing or overwriting item B", async ({ page }) => {
    const mocks = await setup(page);
    const itemADialog = await openInitialDialog(page);
    await setSpecificSchedule(itemADialog, "2026-08-11", "10:30");
    await submitAndWaitForRequest(itemADialog, mocks, 1);

    expect(mocks.getRequests()[0]).toEqual({
      itemId: ITEM_A_ID,
      payload: {
        scheduledAt: scheduledAtIso("2026-08-11", "10:30"),
        metaAssetId: null,
        confirmInstagramAccount: true,
      },
    });

    const itemBDialog = await replacePendingDialogWithItemB(page);
    await setSpecificSchedule(itemBDialog, "2026-08-21", "17:45");
    const itemBConfirmation = itemBDialog.getByRole("checkbox", { name: CONFIRMATION_LABEL, exact: true });
    const countsBeforeCompletion = mocks.getRequestCounts();

    mocks.releaseRequest(0, "success");

    await expect(itemBDialog).toBeVisible();
    await expect(itemBDialog.getByLabel("Ngày")).toHaveValue("2026-08-21");
    await expect(itemBDialog.getByLabel("Giờ")).toHaveValue("17:45");
    await expect(itemBConfirmation).toBeChecked();
    await expect(page.getByText("Đã đổi lịch xuất bản theo thời điểm bạn chọn.", { exact: true })).toHaveCount(0);
    await expect(page.getByText("Đã tạo/cập nhật lịch giờ vàng cho bài viết.", { exact: true })).toHaveCount(0);
    await expect.poll(() => mocks.getRequestCounts().queue).toBeGreaterThan(countsBeforeCompletion.queue);
    await expect.poll(() => mocks.getRequestCounts().calendar).toBeGreaterThan(countsBeforeCompletion.calendar);
    expect(mocks.getRequests()[0].payload).toEqual({
      scheduledAt: scheduledAtIso("2026-08-11", "10:30"),
      metaAssetId: null,
      confirmInstagramAccount: true,
    });
  });

  test("a delayed 409 for item A cannot alter item B, while an active item B 409 remains visible", async ({ page }) => {
    const mocks = await setup(page);
    const itemADialog = await openInitialDialog(page);
    await setSpecificSchedule(itemADialog, "2026-08-12", "11:00");
    await submitAndWaitForRequest(itemADialog, mocks, 1);

    const itemBDialog = await replacePendingDialogWithItemB(page);
    await setSpecificSchedule(itemBDialog, "2026-08-22", "18:20");
    const itemBConfirmation = itemBDialog.getByRole("checkbox", { name: CONFIRMATION_LABEL, exact: true });

    mocks.releaseRequest(0, "conflict");

    await expect(itemBDialog).toBeVisible();
    await expect(itemBDialog.getByLabel("Ngày")).toHaveValue("2026-08-22");
    await expect(itemBDialog.getByLabel("Giờ")).toHaveValue("18:20");
    await expect(itemBConfirmation).toBeChecked();
    await expect(itemBDialog.getByText(RESELECTION_GUIDANCE, { exact: true })).toHaveCount(0);

    await submitAndWaitForRequest(itemBDialog, mocks, 2);
    expect(mocks.getRequests()[1]).toEqual({
      itemId: ITEM_B_ID,
      payload: {
        scheduledAt: scheduledAtIso("2026-08-22", "18:20"),
        metaAssetId: null,
        confirmInstagramAccount: true,
      },
    });
    mocks.releaseRequest(1, "conflict");

    await expect(itemBDialog.getByText(RESELECTION_GUIDANCE, { exact: true })).toBeVisible();
    await expect(itemBConfirmation).not.toBeChecked();
    await expect(itemBDialog.getByLabel("Ngày")).toHaveValue("2026-08-22");
    await expect(itemBDialog.getByLabel("Giờ")).toHaveValue("18:20");
  });
});
