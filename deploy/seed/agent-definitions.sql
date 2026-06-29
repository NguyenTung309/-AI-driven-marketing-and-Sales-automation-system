/*
  ClawBot V2 orchestration sub-agent definitions seed.
  Idempotent per tenant via stable agent code.

  Usage:
    sqlcmd -S <server> -d <db> -v TenantSlug="demo" -i deploy/seed/agent-definitions.sql -C
*/

SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @tenant_slug NVARCHAR(64) = N'$(TenantSlug)';
DECLARE @tenant_id UNIQUEIDENTIFIER = (SELECT TOP 1 id FROM tenants WHERE slug = @tenant_slug AND deleted_at IS NULL);
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

IF @tenant_id IS NULL
BEGIN
    RAISERROR(N'Tenant slug "%s" not found. Seed aborted.', 16, 1, @tenant_slug);
    RETURN;
END

IF OBJECT_ID(N'dbo.agent_definitions', N'U') IS NULL
BEGIN
    RAISERROR(N'agent_definitions table not found. Apply migrations first.', 16, 1);
    RETURN;
END

BEGIN TRANSACTION;

DECLARE @expected_rows INT = 11;

-- allowed_tools_json mirrors DevDataSeeder.OrchestratorAgents: content-agent persists drafts, reviewer-agent
-- approves, publisher-agent schedules+publishes. The rest are text-only ([]). Kept in sync so a SQL-seeded
-- environment gets the same tool grants the app seeder applies — previously this wiped every grant to '[]'.
MERGE agent_definitions AS target
USING (VALUES
    (N'chat-agent',        N'Chat Agent',        N'chat',        N'Handle customer conversation context and produce safe handoff summaries.',          N'[]'),
    (N'sale-assist-agent', N'Sale Assist Agent', N'sale_assist', N'Help sales users draft replies, summarize conversations, and suggest next steps.', N'[]'),
    (N'lead-agent',        N'Lead Agent',        N'lead',        N'Score, classify, and prioritize leads from customer signals.',                     N'[]'),
    (N'content-agent',     N'Content Agent',     N'content',     N'Create campaign content briefs and channel-ready drafts.',                          N'["content-agent"]'),
    (N'research-agent',    N'Research Agent',    N'research',    N'Research competitors, trends, and knowledge gaps for campaigns.',                   N'[]'),
    (N'docs-agent',        N'Docs Agent',        N'docs',        N'Prepare quote, brochure, onboarding, and sales document artifacts.',                N'[]'),
    (N'report-agent',      N'Report Agent',      N'report',      N'Aggregate KPI, cost, and orchestration run outcomes into reports.',                 N'[]'),
    (N'ads-agent',         N'Ads Agent',         N'ads',         N'Plan ad tasks and review campaign signals without publishing automatically.',       N'[]'),
    (N'reviewer-agent',    N'Reviewer Agent',    N'reviewer',    N'Review sub-agent outputs for quality, safety, and policy gates.',                   N'["content.approve"]'),
    (N'publisher-agent',   N'Publisher Agent',   N'publisher',   N'Schedule and publish approved content to social channels via the graph publisher.', N'["content.schedule","content.publish"]'),
    (N'reporter-agent',    N'Reporter Agent',    N'reporter',    N'Summarize A2A run outputs, decisions, costs, and next actions.',                    N'[]')
) AS source (code, display_name, agent_type, persona_prompt, allowed_tools_json)
ON target.tenant_id = @tenant_id AND target.code = source.code
WHEN MATCHED THEN
    UPDATE SET
        display_name = source.display_name,
        agent_type = source.agent_type,
        persona_prompt = source.persona_prompt,
        allowed_tools_json = source.allowed_tools_json,
        input_schema_json = N'{}',
        output_schema_json = N'{}',
        memory_scope = N'session',
        is_orchestratable = 1,
        updated_at = @now,
        deleted_at = NULL
WHEN NOT MATCHED THEN
    INSERT (id, tenant_id, code, display_name, agent_type, persona_prompt, allowed_tools_json, input_schema_json, output_schema_json, memory_scope, llm_config_id, is_orchestratable, version, created_at, updated_at, deleted_at)
    VALUES (NEWID(), @tenant_id, source.code, source.display_name, source.agent_type, source.persona_prompt, source.allowed_tools_json, N'{}', N'{}', N'session', NULL, 1, 1, @now, @now, NULL);

DECLARE @actual_rows INT;

SELECT @actual_rows = COUNT(*)
FROM agent_definitions
WHERE tenant_id = @tenant_id
  AND code IN (N'chat-agent', N'sale-assist-agent', N'lead-agent', N'content-agent', N'research-agent', N'docs-agent', N'report-agent', N'ads-agent', N'reviewer-agent', N'publisher-agent', N'reporter-agent')
  AND deleted_at IS NULL;

IF @actual_rows <> @expected_rows
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'Expected %d agent_definitions rows for tenant "%s"; found %d. Seed aborted.', 16, 1, @expected_rows, @tenant_slug, @actual_rows);
    RETURN;
END

COMMIT TRANSACTION;

PRINT N'agent-definitions seed applied for tenant: ' + @tenant_slug;
