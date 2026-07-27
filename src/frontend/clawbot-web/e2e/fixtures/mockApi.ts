import type { Page, Route } from "@playwright/test";

export type PublishingPolicyValue = "automatic" | "human_required";

export interface MockPolicyState {
  publishingApprovalPolicy: PublishingPolicyValue;
  policyVersion: number;
  reviewerVisionCapability: "available" | "unavailable" | "unknown";
  agentReviewRequired: boolean;
  agentReviewMode: string;
  updatedAt: string;
}

export interface MockUser {
  email: string;
  password: string;
  permissions: readonly string[];
  displayName: string;
}

const ADMIN_PERMS = [
  "system:config",
  "content:read",
  "content:write",
  "content:approve",
  "content:publish",
  "agents:read",
  "agents:manage",
] as const;

const MARKETER_PERMS = [
  "content:read",
  "content:write",
  "content:approve",
  "agents:read",
] as const;

function json(route: Route, status: number, body: unknown): Promise<void> {
  return route.fulfill({
    status,
    contentType: "application/json",
    body: JSON.stringify(body),
  });
}

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

/**
 * Install in-page route mocks so dual-screen policy E2E runs without API/SQL.
 * Shared query key `['content','publishing-policy']` is exercised through real FE code.
 */
export async function installMockApi(
  page: Page,
  options?: {
    readonly user?: MockUser;
    readonly initialPolicy?: PublishingPolicyValue;
  },
): Promise<{ getPolicy: () => MockPolicyState }> {
  const user: MockUser = options?.user ?? {
    email: "admin@clawbot.local",
    password: "Admin@12345",
    displayName: "E2E Admin",
    permissions: [...ADMIN_PERMS],
  };

  const policy: MockPolicyState = {
    publishingApprovalPolicy: options?.initialPolicy ?? "human_required",
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
      const path = new URL(request.url()).pathname;

      if (method === "POST" && path.endsWith("/auth/refresh")) {
        if (!sessionActive) {
          return json(route, 401, { error: "no_session" });
        }
        return json(route, 200, { accessToken, expiresAt: expiresAt() });
      }

      if (method === "POST" && path.endsWith("/auth/login")) {
        const body = request.postDataJSON() as { email?: string; password?: string };
        if (body.email === user.email && body.password === user.password) {
          sessionActive = true;
          return json(route, 200, { accessToken, expiresAt: expiresAt() });
        }
        return json(route, 401, { error: "invalid_credentials" });
      }

      if (method === "GET" && path.endsWith("/auth/me")) {
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

      if (method === "POST" && path.endsWith("/auth/logout")) {
        sessionActive = false;
        return json(route, 204, null);
      }

      return json(route, 404, { error: `unmocked auth ${method} ${path}` });
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
    const url = new URL(request.url());
    const path = url.pathname;

    if (method === "GET" && path.endsWith("/api/content/settings/publishing-policy")) {
      return json(route, 200, { ...policy });
    }

    if (method === "PUT" && path.endsWith("/api/content/settings/publishing-policy")) {
      if (!user.permissions.includes("system:config")) {
        return json(route, 403, {
          code: "forbidden",
          message: "system:config required",
        });
      }
      const body = request.postDataJSON() as { publishingApprovalPolicy?: string };
      const next = body.publishingApprovalPolicy;
      if (next !== "automatic" && next !== "human_required") {
        return json(route, 400, {
          code: "content.publishing_policy_invalid",
          message: "invalid policy",
        });
      }
      if (policy.publishingApprovalPolicy !== next) {
        policy.publishingApprovalPolicy = next;
        policy.policyVersion += 1;
        policy.updatedAt = new Date().toISOString();
      }
      return json(route, 200, { ...policy });
    }

    if (method === "GET" && path.endsWith("/api/admin/tenant/orchestration")) {
      return json(route, 200, orchestrationDefaults());
    }

    if (method === "PUT" && path.endsWith("/api/admin/tenant/orchestration")) {
      return json(route, 200, orchestrationDefaults());
    }

    if (method === "GET" && path.endsWith("/api/agents")) {
      return json(route, 200, { items: [] });
    }

    if (method === "GET" && /\/api\/agents\/[^/]+\/traces$/.test(path)) {
      return json(route, 200, { items: [], total: 0, page: 1, pageSize: 50 });
    }

    if (method === "GET" && /\/api\/agents\/[^/]+\/settings$/.test(path)) {
      return json(route, 200, {
        code: "content-agent",
        systemPrompt: "",
        temperature: 0.2,
        maxTokens: 1024,
        tools: [],
      });
    }

    if (method === "GET" && path.endsWith("/api/analytics/agent-cost")) {
      return json(route, 200, {
        from: new Date(0).toISOString(),
        to: new Date().toISOString(),
        items: [],
      });
    }

    if (method === "GET" && path.includes("/api/orchestration-v2/runs")) {
      return json(route, 200, []);
    }

    if (method === "GET" && path.includes("/api/orchestration-v2/agents")) {
      return json(route, 200, []);
    }

    if (method === "GET" && path.includes("/api/orchestration-v2/schedules")) {
      return json(route, 200, []);
    }

    if (method === "GET" && path.includes("/api/orchestration")) {
      return json(route, 200, []);
    }

    if (method === "GET" && path.endsWith("/api/jobs")) {
      return json(route, 200, { items: [], total: 0 });
    }

    if (method === "GET" && path.endsWith("/api/llm-configs")) {
      return json(route, 200, { items: [] });
    }

    if (method === "GET" && path.endsWith("/api/content/briefs")) {
      return json(route, 200, emptyList());
    }

    if (method === "GET" && (path.endsWith("/api/content/queue") || path.endsWith("/api/content/items"))) {
      return json(route, 200, { items: [], total: 0, page: 1, pageSize: 50, nextCursor: null });
    }

    if (method === "GET" && path.endsWith("/api/content/calendar")) {
      return json(route, 200, { items: [] });
    }

    if (method === "GET" && path.endsWith("/api/content/trends")) {
      return json(route, 200, { items: [] });
    }

    if (method === "GET" && path.endsWith("/api/content/publish-targets")) {
      return json(route, 200, { items: [] });
    }

    // Notifications / hubs bootstrap — keep UI quiet.
    if (method === "GET" && path.includes("/api/notifications")) {
      return json(route, 200, { items: [], total: 0, nextCursor: null });
    }

    if (method === "GET") {
      return json(route, 200, emptyList());
    }

    return json(route, 200, { ok: true });
  },
  );

  return {
    getPolicy: () => ({ ...policy }),
  };
}

export function marketerUser(): MockUser {
  return {
    email: "marketer@clawbot.local",
    password: "Marketer@12345",
    displayName: "E2E Marketer",
    permissions: [...MARKETER_PERMS],
  };
}
