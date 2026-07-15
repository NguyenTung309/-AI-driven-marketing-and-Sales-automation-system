-- 0067: Composite indexes for keyset/offset list feeds (tenant_id, sort_col DESC, id DESC).
-- Columns match actual ORDER BY keys in endpoints (not guessed created_at).
-- One SqlCommand, no GO. Safe to re-run (IF NOT EXISTS).

-- conversations: feed orders by COALESCE(last_message_at, created_at); index last_message_at
-- still helps the common path (most rows have last_message_at). COALESCE residual is acceptable.
IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_conversations_tenant_last_message_id' AND object_id = OBJECT_ID(N'dbo.conversations'))
    CREATE INDEX IX_conversations_tenant_last_message_id
        ON dbo.conversations (tenant_id, last_message_at DESC, id DESC);

IF OBJECT_ID(N'dbo.notifications', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_notifications_tenant_created_id' AND object_id = OBJECT_ID(N'dbo.notifications'))
    CREATE INDEX IX_notifications_tenant_created_id
        ON dbo.notifications (tenant_id, created_at DESC, id DESC);

IF OBJECT_ID(N'dbo.background_jobs', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_background_jobs_tenant_created_id' AND object_id = OBJECT_ID(N'dbo.background_jobs'))
    CREATE INDEX IX_background_jobs_tenant_created_id
        ON dbo.background_jobs (tenant_id, created_at DESC, id DESC);

-- orchestration runs list uses agent_sessions ordered by started_at
IF OBJECT_ID(N'dbo.agent_sessions', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_sessions_tenant_started_id' AND object_id = OBJECT_ID(N'dbo.agent_sessions'))
    CREATE INDEX IX_agent_sessions_tenant_started_id
        ON dbo.agent_sessions (tenant_id, started_at DESC, id DESC);

IF OBJECT_ID(N'dbo.audit_logs', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_audit_logs_tenant_occurred_id' AND object_id = OBJECT_ID(N'dbo.audit_logs'))
    CREATE INDEX IX_audit_logs_tenant_occurred_id
        ON dbo.audit_logs (tenant_id, occurred_at DESC, id DESC);

IF OBJECT_ID(N'dbo.generated_documents', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_generated_documents_tenant_created_id' AND object_id = OBJECT_ID(N'dbo.generated_documents'))
    CREATE INDEX IX_generated_documents_tenant_created_id
        ON dbo.generated_documents (tenant_id, created_at DESC, id DESC);

-- competitor posts ordered by detected_at (not created_at)
IF OBJECT_ID(N'dbo.competitor_posts', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_competitor_posts_tenant_detected_id' AND object_id = OBJECT_ID(N'dbo.competitor_posts'))
    CREATE INDEX IX_competitor_posts_tenant_detected_id
        ON dbo.competitor_posts (tenant_id, detected_at DESC, id DESC);

-- ad actions ordered by executed_at
IF OBJECT_ID(N'dbo.ad_actions', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ad_actions_tenant_executed_id' AND object_id = OBJECT_ID(N'dbo.ad_actions'))
    CREATE INDEX IX_ad_actions_tenant_executed_id
        ON dbo.ad_actions (tenant_id, executed_at DESC, id DESC);

-- content queue keyset uses updated_at
IF OBJECT_ID(N'dbo.content_items', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_content_items_tenant_updated_id' AND object_id = OBJECT_ID(N'dbo.content_items'))
    CREATE INDEX IX_content_items_tenant_updated_id
        ON dbo.content_items (tenant_id, updated_at DESC, id DESC);
