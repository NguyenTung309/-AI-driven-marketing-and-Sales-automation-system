---
phase: planning
title: Project Planning & Task Breakdown
description: Break down work into actionable tasks and estimate timeline
---

# Project Planning & Task Breakdown

> M19 Ads Automation (Meta + TikTok). Full scope: 8 use cases (H01–H08). HttpClient connectors (no vendor SDKs), pure rule engine, config-gated, SignalR alerts.

## Milestones
**What are the major checkpoints?**

- [ ] **M-A Schema + domain + rule engine** — migrations, domain behaviors, AdsRuleEngine unit-tested, build 0/0.
- [ ] **M-B Connectors** — MetaAdsConnector + TikTokAdsConnector, config-gated, graceful, fixture-tested.
- [ ] **M-C Proto + agent + gRPC** — proto extended, AdsAgent + AdsAgentGrpcService wired, build 0/0.
- [ ] **M-D API: CRUD + webhooks** — AdsEndpoints + webhook routes + gRPC client, build 0/0.
- [ ] **M-E Jobs + seed + config** — 7 Hangfire jobs, ads-rules seed, appsettings + .env.example, build 0/0.
- [ ] **M-F Tests + checklist** — full test run green, M19 ticked in module-checklist.

## Task Breakdown
**What specific work needs to be done?**

### Phase A: Schema + domain + pure rule engine
- [ ] A.1 Migration `0003_ads_target_cpl.sql`: `ALTER TABLE ads_campaigns ADD target_cpl DECIMAL(12,2) NULL`, `ADD daypart_paused BIT NOT NULL DEFAULT 0`.
- [ ] A.2 Migration `0004_ads_creatives.sql`: `CREATE TABLE ads_creatives(id UNIQUEIDENTIFIER PK, tenant_id, campaign_id FK, external_creative_id, status NVARCHAR(16), created_at, updated_at)` + index.
- [ ] A.3 Migration `0005_ads_metrics_daily.sql`: `CREATE TABLE ads_metrics_daily(id, tenant_id, campaign_id FK, metric_date DATE, cpl, frequency, ctr, spend DECIMAL, created_at)` + UNIQUE(campaign_id, metric_date).
- [ ] A.4 `AdsCampaign.cs`: add `TargetCpl`, `DaypartPaused` props + `MarkSynced(...)`, `Pause(at)`, `Resume(at)`, `ScaleBudget(...)`, `UpdateStatus(...)`, `MarkDaypartPaused(bool, at)`. Update `AdsCampaignConfiguration`.
- [ ] A.5 `AdsRule.cs`: add `Update(platform, metric, comparator, threshold, action, at)` for PUT.
- [ ] A.6 New `AdsCreative.cs` + `AdsMetricsDaily.cs` under `Domain/Ads/`; EF configs; DbSets in `AppDbContext`.
- [ ] A.7 New `AdsRuleEngine.cs` in `Agents.Core/Ads/`: `record AdsMetricSnapshot`, `record AdsDecision`, `static Evaluate(...)` — relative CPL, absolute metrics, 3-day streak gate, 24h cooldown, dayparting helper. All pure.

### Phase B: Connectors (Infrastructure, config-gated)
- [ ] B.1 `MetaAdsOptions` + `TikTokAdsOptions` (config sections `Ads:Meta`, `Ads:TikTok`).
- [ ] B.2 `IAdsPlatformConnector` interface: `FetchMetricsAsync`, `ApplyActionAsync`, `BuildLookalikeAsync`, `BuildRemarketingAsync`.
- [ ] B.3 `IAdsConnectorResolver.For(platform)` resolver.
- [ ] B.4 `MetaAdsConnector` — HttpClient + IOptions, graceful catch, cred guard.
- [ ] B.5 `TikTokAdsConnector` — same pattern.
- [ ] B.6 Register `AddHttpClient<>` + `HttpResiliencePolicies` in `DependencyInjection.cs`.

### Phase C: Proto + agent + gRPC service
- [ ] C.1 Extend `agent_ads.proto`: add `rpc BuildLookalike`, `rpc Remarket`, `rpc HandleSignal` + new messages. Regen.
- [ ] C.2 New `AdsAgent.cs` + `AdsModule.AddClawbotAds(config)` in `Agents.Core/Ads/`.
- [ ] C.3 Fill `AdsAgentGrpcService`: `Evaluate`, `BuildLookalike`, `Remarket`, `HandleSignal` — validate, load rules/campaigns, resolve connector, call agent, persist AdsAction, SaveChanges, structured logging.
- [ ] C.4 `AddClawbotAds(builder.Configuration)` in `AgentService/Program.cs`.

### Phase D: API: CRUD + campaigns + webhooks + client
- [ ] D.1 New `AdsDtos.cs` in `Api.Contracts/Ads/`.
- [ ] D.2 New `AdsEndpoints.cs`: `/api/ads/rules` CRUD, `/api/ads/campaigns` GET + PUT target-cpl, `/api/ads/actions` GET, POST evaluate, POST lookalike. Auth + tenant + error format.
- [ ] D.3 Webhook routes `POST /webhooks/ads/meta/{tenantSlug}` + `/tiktok/{tenantSlug}`: HMAC verify → parse → gRPC HandleSignal.
- [ ] D.4 Register `AddGrpcClient<AdsAgentClient>` + `app.MapAds()` in `Api/Program.cs`.

### Phase E: Jobs + seed + config
- [ ] E.1 `AdsRuleEvaluationJob` (every 4h): fetch metrics → write snapshot → evaluate.
- [ ] E.2 `AdsCreativeRotationJob` (daily): freq>2 → rotate active↔standby.
- [ ] E.3 `AdsRemarketingJob` (daily): cold leads → connector remarket.
- [ ] E.4 `AdsLookalikeRefreshJob` (weekly): hot/won leads → contacts → hash seed → BuildLookalike.
- [ ] E.5 `AdsDaypartPauseJob` (02:00 GMT+7) + `AdsDaypartResumeJob` (05:00 GMT+7).
- [ ] E.6 `WeeklyAdsReportJob` (Mon, H08 → SignalR).
- [ ] E.7 Register all in `HangfireModule.cs`; add `"ads"` queue + crons.
- [ ] E.8 `deploy/seed/ads-rules.sql`: idempotent MERGE default rules per platform.
- [ ] E.9 Config: `Ads:Meta` + `Ads:TikTok` in both `appsettings.json` + `deploy/.env.example`.

### Phase F: Tests + checklist
- [ ] F.1 `AdsRuleEngineTests.cs` — relative CPL, absolute metrics, no/multi-match, scale clamp, quiet-hour.
- [ ] F.2 `AdsCampaignTests.cs` — Pause/ScaleBudget/MarkSynced/target_cpl + UpdatedAt.
- [ ] F.3 `AdsConnectorTests.cs` — request build + JSON parse fixtures; Enabled=false graceful.
- [ ] F.4 Final build 0/0 + full test run; tick M19 in `docs/module-checklist.md`.

## Dependencies
**What needs to happen in what order?**

- A → B → C → D → E → F (each gated on green build).
- A is fully pure (no IO). B depends on A (AdsMetricSnapshot type). C depends on A+B. D depends on C. E depends on C+D. F after all.
- Reuses: `HttpResiliencePolicies` (M01), Hangfire (M12), `HmacSignatureVerifier` (M13), `IContentNotifier`/SignalR (M18), `LeadScoringEngine` pattern (M15), `HttpSocialPublisher` connector pattern (M18).

## Risks & Mitigations

- **Meta/TikTok API shape drift** — HttpClient connectors are thin; swap payloads without SDK churn.
- **No creds in dev** — graceful degrade (connector returns null/false), pipeline build+test verifiable.
- **3-day CPL streak** — requires `ads_metrics_daily` history; cold start = no scale (safe default).
- **Scaling cap** — 24h cooldown via `lastScaledAt` field; `ClampScaleFactor` ≤ 1.5×.
