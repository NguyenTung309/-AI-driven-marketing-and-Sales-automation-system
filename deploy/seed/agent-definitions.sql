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

DECLARE @expected_rows INT = 10;

-- allowed_tools_json mirrors DevDataSeeder.OrchestratorAgents (tool names = ToolRegistry).
-- lead/sale/report/research/chat/docs must NOT be [] — empty = text-only soft-fail ("thiếu list").
MERGE agent_definitions AS target
USING (VALUES
    (N'chat-agent',        N'Chat Agent',        N'chat',        N'Handle customer conversation context and produce safe handoff summaries. Call chat-agent tool when a reply is needed.', N'["chat-agent"]'),
    (N'sale-assist-agent', N'Sale Assist Agent', N'sale_assist', N'Draft/summarize/upsell for an EXISTING conversation. Call sale-assist with conversation_id + turns_json. Cannot blast cold leads without conversation context.', N'["sale-assist"]'),
    (N'lead-agent',        N'Lead Agent',        N'lead',        N'ALWAYS call lead-agent tool. Use operation=list|find_cold (stage, topN) to query CRM — do not invent lists or ask for lead IDs. Also score/create/batch_score.', N'["lead-agent"]'),
    (N'content-agent',     N'Content Agent',     N'content',     N'Create campaign content briefs and channel-ready drafts.',                          N'["content-agent"]'),
    (N'research-agent',    N'Research Agent',    N'research',    N'Research competitors, trends, and knowledge gaps. Call research-agent or web.search with geo/keywords.', N'["research-agent","web.search"]'),
    (N'docs-agent',        N'Docs Agent',        N'docs',        N'Prepare quote, brochure, onboarding documents via docs-agent tool.',                N'["docs-agent"]'),
    (N'report-agent',      N'Report Agent',      N'report',      N'USE THIS for any request about KPI, metrics, analytics, or a business report: it queries the tenant database via the report-agent tool (operation=snapshot/anomaly/forecast, date/platform/metric). Do NOT confuse with reporter-agent, which has no data access.', N'["report-agent"]'),
    (N'reviewer-agent',    N'Reviewer Agent',    N'reviewer',    N'Review sub-agent outputs for quality, safety, and policy gates.',                   N'["content.review"]'),
    (N'publisher-agent',   N'Publisher Agent',   N'publisher',   N'Publishing is handled by the durable worker; no autonomous publishing tools are granted by default.', N'[]'),
    (N'reporter-agent',    N'Reporter Agent',    N'reporter',    N'Write a prose wrap-up of THIS run only: what each sub-agent produced, decisions taken, costs, next actions. Has NO data tools and cannot read KPI — for metrics or analytics numbers plan report-agent instead.', N'[]')
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
  AND code IN (N'chat-agent', N'sale-assist-agent', N'lead-agent', N'content-agent', N'research-agent', N'docs-agent', N'report-agent', N'reviewer-agent', N'publisher-agent', N'reporter-agent')
  AND deleted_at IS NULL;

IF @actual_rows <> @expected_rows
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'Expected %d agent_definitions rows for tenant "%s"; found %d. Seed aborted.', 16, 1, @expected_rows, @tenant_slug, @actual_rows);
    RETURN;
END

COMMIT TRANSACTION;

PRINT N'agent-definitions seed applied for tenant: ' + @tenant_slug;
