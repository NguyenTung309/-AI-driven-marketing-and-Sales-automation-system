import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  testMatch: "**/content-publishing-policy.spec.ts",
  reporter: "line",
  use: {
    baseURL: "http://127.0.0.1:15876",
    headless: true,
  },
});
