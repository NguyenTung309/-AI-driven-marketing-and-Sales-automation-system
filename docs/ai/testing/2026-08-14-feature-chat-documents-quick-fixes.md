---
phase: testing
title: Chat and Documents Quick Fixes
feature: chat-documents-quick-fixes
date: 2026-08-14
status: partial
---

# Chat and Documents Quick Fixes — Testing Strategy

## Test Coverage Goals

- Protect conversation counters from depending on the number of infinite-query pages loaded.
- Preserve tenant and inbox authorization boundaries in list and aggregate queries.
- Restore the newest completed conversation summary after navigation or reload.
- Enforce resolved inbox scope across SaleAssist drafts, feedback, upsell suggestions, and daily metrics before reading PII or starting side effects.
- Validate direct document recipients before enqueueing or sending email.
- Keep queued legacy document jobs compatible when `recipientEmail` is absent.
- Avoid persisting a typed email when the user selects create-only delivery.
- Prevent AgentService authentication regressions for background unary and streaming gRPC calls.
- Keep the Orchestrator client on its separate, fail-closed user-identity path.
- Verify the affected frontend compiles, lints, and produces a production bundle.

## Unit Tests

### Conversation counts

- [x] Counts all 75 matching conversations, including rows beyond the first 40-item page.
- [x] Counts open, escalated, resolved, and current-user assignments independently.
- [x] Intersects an explicit inbox filter with the inboxes available to the user.
- [x] Preserves unrestricted (`[]`) and no-access (`[Guid.Empty]`) resolver semantics.
- [x] Applies platform and search filters shared with the conversation list.

Test file: `tests/Clawbot.Api.Tests/ConversationCountsTests.cs`

### Persisted sale-assist summary

- [x] Returns the newest succeeded summary for the requested tenant and conversation.
- [x] Ignores failed jobs and returns no summary when none succeeded.

Test file: `tests/Clawbot.Api.Tests/SaleAssistSummaryEndpointTests.cs`

### SaleAssist inbox authorization

- [x] Allows assigned-inbox and unrestricted users to launch draft jobs.
- [x] Treats `[Guid.Empty]` as deny-all and rejects foreign conversations with a generic 404 before enqueueing.
- [x] Rejects draft feedback before PII redaction or session/trace persistence.
- [x] Rejects direct upsell requests before returning cached content or launching a job.
- [x] Filters candidate leads before score ordering and `Take`, so foreign high-score leads cannot consume visible slots.
- [x] Selects the newest allowed conversation when a contact also has older allowed and newer foreign conversations.
- [x] Applies the same scope to new leads, conversations, outbound messages, and hot-lead daily metrics.
- [x] Preserves unrestricted (`[]`) and deny-all (`[Guid.Empty]`) resolver semantics through SQLite-translated queries.

Test files:

- `tests/Clawbot.Api.Tests/SaleAssistInboxScopeTests.cs`
- `tests/Clawbot.Api.Tests/Services/SaleAssistUpsellSuggestionServiceTests.cs`

### LLM base URL guard (production "invalid_base_url")

- [x] Public HTTPS host resolves to a public address and is allowed.
- [x] Unresolvable or empty DNS for an HTTPS host no longer reads as "invalid URL": it is allowed at validation time as `AllowedDnsUnverified`, with the real block moved to connect time.
- [x] Private, loopback, link-local, and carrier-grade addresses are rejected with `base_url_private_host`.
- [x] A mixed public+private DNS answer is rejected as rebinding (`base_url_mixed_dns`).
- [x] Operator grant (`allowPrivateHosts`) still admits private hosts.
- [x] `localhost` is classified private without touching DNS.
- [x] Non-HTTPS and malformed URLs keep their own distinct verdicts and error codes.
- [x] Tenant-overridden trend URLs remain strictly fail-closed because that path fetches with an unguarded client.

Test file: `tests/Clawbot.Agents.Tests/Chat/LlmBaseUrlGuardTests.cs` (19 tests).

### Realtime conversation list cache

- [x] Moves a conversation from page two to the front and updates preview/time without refetching.
- [x] Keeps page sizes, `nextCursor`, `total`, and `pageParams` intact so the next cursor page neither skips nor duplicates rows.
- [x] Never leaves a duplicate copy of the reordered conversation.
- [x] Does not mutate the input cache.
- [x] Requests a list refresh instead of fabricating an incomplete row when the conversation is absent.
- [x] Skips the refresh when a known `inboxId` or `assignedTo` mismatch already explains the absence.
- [x] Patches `resolved` and `snoozed` conversations to `open` when the customer replies.
- [x] Ignores a late event that is older than the cached `lastMessageAt`.
- [x] Truncates the optimistic preview at 140 characters exactly like the API.
- [x] Keeps the cached status when an older server omits `conversationStatus`.
- [x] Treats the `all` sentinel in the query key as no filter.

Test file: `src/frontend/clawbot-web/e2e/inbox-realtime-cache.spec.ts`

### Document date fields

- [x] Keeps an ISO value and converts `dd/MM/yyyy` so `<input type="date">` stops silently discarding it.
- [x] Returns empty for a non-date hint such as the legacy `dd/MM/yyyy` placeholder.
- [x] Renders the quote expiry as `dd/MM/yyyy` in the generated document.
- [x] Converts only date-typed fields and leaves an empty value empty.

Test file: `src/frontend/clawbot-web/e2e/document-date-field.spec.ts`

### Document recipient validation

- [x] Rejects malformed addresses, display-name syntax, multiple recipients, and CR/LF injection.
- [x] Trims a valid single mailbox.
- [x] Requires a direct recipient or contact for email delivery.
- [x] Allows contact-email fallback.
- [x] Discards an unused direct email for create-only jobs.
- [x] Keeps request construction and legacy queued JSON compatible when `recipientEmail` is absent.

Test file: `tests/Clawbot.Api.Tests/DocumentRecipientValidationTests.cs`

### Document delivery

- [x] Gives a valid direct recipient precedence over the contact email.
- [x] Falls back to the contact email when no direct recipient is supplied.
- [x] Rejects an invalid direct recipient instead of silently changing recipients.
- [x] Does not mark the document as sent when the email sender fails.

Test file: `tests/Clawbot.Api.Tests/DocumentDeliveryServiceTests.cs`

### AgentService gRPC authentication

- [x] Registers all six non-Orchestrator API clients with `AgentServiceClientAuthInterceptor`.
- [x] Keeps `OrchestratorClient` on `OrchestratorServiceAuthInterceptor`.
- [x] Adds and cryptographically validates a service bearer token for a background SaleAssist unary call.
- [x] Adds and cryptographically validates a service bearer token for a Chat server-streaming call.
- [x] Preserves authenticated HTTP user, tenant, and role claims for a matching request tenant.
- [x] Rejects anonymous HTTP contexts instead of elevating them to the background service identity.
- [x] Rejects authenticated caller/request tenant mismatches before invoking the gRPC continuation.
- [x] Rejects missing background request tenants before issuing a service token.
- [x] Fails closed when Orchestrator has no authenticated caller identity.
- [x] Adds the caller token to Orchestrator server-streaming calls as well as unary calls.
- [x] Verifies the API production composition root calls the guarded registration method.
- [x] Verifies shared `AddInfrastructure` keeps the Chat client interceptor registration.
- [x] Enforces token/request tenant equality again at the AgentService server boundary.
- [x] Rejects AgentService RPC contracts that omit the required top-level `TenantId` instead of failing open.
- [x] Exercises signed matching and mismatched tokens through both unary and streaming server-interceptor paths.
- [x] Rejects empty user, tenant, and role identifiers in the token issuer.

Test files:

- `tests/Clawbot.Api.Tests/Services/AgentServiceGrpcAuthenticationRegressionTests.cs`
- `tests/Clawbot.Api.Tests/Services/AgentServiceTokenIssuerTests.cs`
- `tests/Clawbot.Agents.Tests/AgentServiceAuthInterceptorTests.cs`
- `tests/Clawbot.Infrastructure.Tests/AgentServiceGrpcClientRegistrationTests.cs`
- `tests/Clawbot.Infrastructure.Tests/AgentServiceTenantBindingTests.cs`

## Integration Tests

- [ ] Exercise `POST /api/docs/generate` through HTTP and verify invalid recipients return 400 before a job is queued.
- [ ] Exercise the same endpoint with create-only delivery and verify the persisted job payload omits the typed email.
- [ ] Exercise `GET /api/inbox/conversations/counts` through authenticated HTTP for unrestricted, restricted, and no-inbox users.
- [ ] Exercise `GET /api/sale-assist/summary/{conversationId}` through authenticated HTTP and verify an out-of-scope conversation returns 404.
- [ ] Exercise SaleAssist draft, feedback, and direct-upsell routes through authenticated HTTP and verify out-of-scope conversations return 404 without a queued job or persisted trace.
- [ ] Exercise SaleAssist suggestions and daily-summary routes through authenticated HTTP and verify every returned item or metric respects assigned inboxes.

The unit-level query and policy tests cover the high-risk branches, but endpoint serialization, authorization middleware, and persisted job JSON still need HTTP-level regression tests.

## End-to-End Tests

- [ ] Load the inbox with 75 conversations and verify the total remains 75 before and after loading another page.
- [ ] Change assignment or status and verify count badges refresh without reloading the page.
- [ ] Open the running-jobs icon from a non-Agent route and verify the dialog opens without changing the URL.
- [ ] Generate a summary, leave the conversation, return, and verify the completed summary remains visible.
- [ ] Reload after summary completion and verify the persisted result is restored.
- [ ] Enter an invalid document recipient and verify no generate request is sent.
- [ ] Enter a valid email, choose Email, and verify the request carries `recipientEmail` with `contactId: null`.
- [ ] Enter a valid email, choose create-only, and verify the request does not carry the email.
- [ ] Verify direct delivery reaches a test mailbox in a non-production environment.

## Test Data

- EF Core InMemory databases use a unique database name per test.
- Conversation fixture: 75 rows in one tenant, with mixed statuses and seven rows assigned to the current user.
- Summary fixture: succeeded, failed, cross-conversation, and cross-tenant background jobs.
- Email fixtures use reserved `example.com` addresses and never real customer PII.
- Time-dependent delivery assertions use `2026-08-14T08:00:00Z` through a fixed clock.

## Test Reporting & Coverage

Commands executed on 2026-08-14:

```text
dotnet test tests/Clawbot.Api.Tests/Clawbot.Api.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ConversationCountsTests|FullyQualifiedName~SaleAssistSummaryEndpointTests|FullyQualifiedName~DocumentRecipientValidationTests|FullyQualifiedName~DocumentDeliveryServiceTests"
```

Result: 19 passed, 0 failed, 0 skipped.

```text
dotnet test tests/Clawbot.Api.Tests/Clawbot.Api.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~SaleAssistInboxScopeTests|FullyQualifiedName~SaleAssistUpsellSuggestionServiceTests"
```

Result: 17 passed, 0 failed, 0 skipped. The tests run against SQLite so correlated inbox-scope subqueries are translated and executed by a relational provider.

The two new frontend specs were executed by loading the real TypeScript modules through the repository's `jiti` loader, because Playwright 1.52 still hangs on the active Node 25.6.1 runtime.

Result: 17 assertions passed, 0 failed, covering `inboxRealtimeCache.ts` and the `templateModel.ts` date helpers.

```text
dotnet test tests/Clawbot.Api.Tests/Clawbot.Api.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AgentServiceGrpcAuthenticationRegressionTests"
```

Result: 21 passed, 0 failed, 0 skipped across the authentication interceptor and token issuer tests.

```text
dotnet test tests/Clawbot.Infrastructure.Tests/Clawbot.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AgentServiceTenantBindingTests|FullyQualifiedName~AgentServiceGrpcClientRegistrationTests"
```

Result: 5 passed, 0 failed, 0 skipped.

```text
dotnet test tests/Clawbot.Agents.Tests/Clawbot.Agents.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AgentServiceAuthInterceptorTests"
```

Result: 3 passed, 0 failed, 0 skipped.

```text
npm --prefix src/frontend/clawbot-web run build
```

Result: production build passed.

```text
dotnet build Clawbot.sln -c Release --no-restore
```

Result: full solution build passed with 0 warnings and 0 errors.

```text
dotnet test Clawbot.sln -c Release --no-build --no-restore
```

Result: 380 passed, 0 failed, 9 skipped across the API, Agent, and Infrastructure test projects.

```text
npm --prefix src/frontend/clawbot-web run lint
```

Result: 0 errors and one unrelated existing `react-hooks/exhaustive-deps` warning in `PixelAgentsOfficePage.tsx`.

Targeted TypeScript and ESLint checks for `DocumentsPage.tsx` also passed.

Coverage collection was not run. Frontend recipient parsing currently has no unit-test harness; the production build catches type integration, while the request-shape scenarios remain in the E2E backlog above.

Browser E2E was not run in this session because the active runtime is Node.js 25.6.1 and the repository's Playwright 1.52 setup is known to hang in the Node 25 ESM loader. Run the E2E backlog with Node 20.19+ or a supported Node 22 LTS runtime.

## Manual Testing

- [ ] Check labels and helper text for create-only, Email, valid email, valid UUID, empty value, and invalid value.
- [ ] Confirm keyboard focus and warning visibility after blocked submission.
- [ ] Confirm no raw recipient email appears in server logs, job titles, notifications, or error responses.
- [ ] Confirm the conversation counter and summary behavior at desktop and mobile breakpoints.
- [ ] Sign in as a sale user assigned to one inbox and confirm SaleAssist drafts, feedback, upsell cards, and every daily metric exclude another inbox.
- [ ] Confirm the running-jobs dialog closes without losing page state.

## Performance Testing

- The count endpoint uses one conditional aggregate query instead of loading all matching rows.
- [ ] Capture the SQL query plan on production-like data and confirm inbox, tenant, status, and assignment indexes are used.
- [ ] Inspect SaleAssist lead/message scope `EXISTS` plans and confirm conversation tenant/contact/inbox lookups stay indexed before increasing suggestion or metric limits.
- [ ] Observe aggregate endpoint latency with a tenant containing at least 100,000 conversations.
- [ ] Verify realtime invalidation does not refetch counts for every message event.

## Bug Tracking

- CRITICAL/HIGH findings block completion.
- MEDIUM findings require a documented decision or follow-up.
- Deferred HTTP integration and browser E2E scenarios remain part of verification task #4.
- The existing frontend lint warning is unrelated to these quick fixes and must not be attributed to this change.
