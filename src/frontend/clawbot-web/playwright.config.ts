import { defineConfig, devices } from "@playwright/test";

const baseURL = process.env.E2E_BASE_URL ?? "http://127.0.0.1:15876";
// Set E2E_START_WEB=1 to auto-start Vite. Default assumes `npm run dev` already running
// (avoids hang in some Windows/Git-Bash shells when Playwright spawns webServer).
const startWeb = process.env.E2E_START_WEB === "1";

/**
 * Content publishing policy dual-screen E2E.
 * Default: mock API (no backend required). Live stack: E2E_LIVE=1 + FE+API running.
 */
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  timeout: 60_000,
  expect: { timeout: 15_000 },
  reporter: [["list"], ["html", { open: "never", outputFolder: "playwright-report" }]],
  use: {
    baseURL,
    headless: true,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "retain-on-failure",
    locale: "vi-VN",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
  webServer: startWeb
    ? {
        command: "npm run dev -- --host 127.0.0.1 --port 15876 --strictPort",
        url: `${baseURL}/login`,
        reuseExistingServer: true,
        timeout: 180_000,
      }
    : undefined,
});
