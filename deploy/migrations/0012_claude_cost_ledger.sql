-- 0012: Claude cost ledger (M25) — one row per LLM call, for agent-cost reporting + history.
-- Replaces the in-memory tracker (kept as fallback). Mirrors CostEntry (agent_code, model, tokens, usd).
CREATE TABLE claude_cost_ledger (
    id            UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id     UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    agent_code    NVARCHAR(40)  NOT NULL,
    model         NVARCHAR(80)  NOT NULL,
    input_tokens  INT NOT NULL DEFAULT 0,
    output_tokens INT NOT NULL DEFAULT 0,
    usd           DECIMAL(12,6) NOT NULL DEFAULT 0,
    created_at    DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
CREATE INDEX ix_cost_ledger_tenant_agent_created
    ON claude_cost_ledger (tenant_id, agent_code, created_at DESC);
