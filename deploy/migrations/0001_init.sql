-- =============================================================
-- ClawBot SaleMkt — Initial schema (SQL Server Target)
-- Conventions:
--   * snake_case identifiers
--   * UNIQUEIDENTIFIER PK via NEWID()
--   * DATETIMEOFFSET; created_at on every mutable table
--   * tenant_id NOT NULL on every tenant-scoped table
--   * Soft-delete via deleted_at on aggregate roots
-- =============================================================

-- ------------- Tenants & RBAC -------------

CREATE TABLE tenants (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    slug            NVARCHAR(64) NOT NULL UNIQUE,
    display_name    NVARCHAR(256) NOT NULL,
    plan_name       NVARCHAR(32) NOT NULL DEFAULT 'free', -- 'plan' is a keyword, changed to plan_name
    is_active       BIT NOT NULL DEFAULT 1,
    settings_json   NVARCHAR(MAX) NOT NULL DEFAULT '{}',
    created_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    deleted_at      DATETIMEOFFSET
);

CREATE TABLE users (
    id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id           UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    display_name        NVARCHAR(256) NOT NULL,
    email               NVARCHAR(256) NOT NULL,
    phone               NVARCHAR(32),
    password_hash       NVARCHAR(MAX) NOT NULL,
    security_stamp      NVARCHAR(64),
    access_failed_count INT NOT NULL DEFAULT 0,
    lockout_end         DATETIMEOFFSET,
    is_active           BIT NOT NULL DEFAULT 1,
    last_login_at       DATETIMEOFFSET,
    created_at          DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at          DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    deleted_at          DATETIMEOFFSET,
    UNIQUE (tenant_id, email)
);
CREATE INDEX ix_users_tenant_created  ON users (tenant_id, created_at DESC);

CREATE TABLE roles (
    id          UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id   UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    name        NVARCHAR(64) NOT NULL,
    description NVARCHAR(256),
    is_system   BIT NOT NULL DEFAULT 0,
    created_at  DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    UNIQUE (tenant_id, name)
);

CREATE TABLE permissions (
    id          UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    code        NVARCHAR(128) NOT NULL UNIQUE,
    description NVARCHAR(256)
);

CREATE TABLE role_permissions (
    role_id       UNIQUEIDENTIFIER NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
    permission_id UNIQUEIDENTIFIER NOT NULL REFERENCES permissions(id) ON DELETE CASCADE,
    PRIMARY KEY (role_id, permission_id)
);

CREATE TABLE user_roles (
    user_id UNIQUEIDENTIFIER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    -- Prevent multiple cascade paths error in SQL Server
    role_id UNIQUEIDENTIFIER NOT NULL REFERENCES roles(id) ON DELETE NO ACTION,
    PRIMARY KEY (user_id, role_id)
);

CREATE TABLE api_keys (
    id          UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id   UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    name        NVARCHAR(128) NOT NULL,
    key_hash    NVARCHAR(MAX) NOT NULL,
    scopes      NVARCHAR(MAX) NOT NULL DEFAULT '[]',
    expires_at  DATETIMEOFFSET,
    revoked_at  DATETIMEOFFSET,
    created_by  UNIQUEIDENTIFIER REFERENCES users(id) ON DELETE NO ACTION,
    created_at  DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
-- SQL Server does not support UNIQUE constraint on NVARCHAR(MAX), 
-- if you need it unique, change key_hash to NVARCHAR(450) and apply UNIQUE
CREATE INDEX ix_api_keys_tenant ON api_keys (tenant_id);

CREATE TABLE audit_logs (
    id            UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id     UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    user_id       UNIQUEIDENTIFIER REFERENCES users(id) ON DELETE NO ACTION,
    action        NVARCHAR(64) NOT NULL,
    resource_type NVARCHAR(64) NOT NULL,
    resource_id   UNIQUEIDENTIFIER,
    diff_json     NVARCHAR(MAX),
    ip_address    VARCHAR(45), -- For IPv4/IPv6 compatibility
    user_agent    NVARCHAR(512),
    occurred_at   DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
CREATE INDEX ix_audit_logs_tenant_occurred ON audit_logs (tenant_id, occurred_at DESC);
CREATE INDEX ix_audit_logs_resource        ON audit_logs (resource_type, resource_id);

-- ------------- Contacts -------------

CREATE TABLE contacts (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id       UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    display_name    NVARCHAR(256) NOT NULL,
    phone           NVARCHAR(32),
    email           NVARCHAR(256),
    locale          NVARCHAR(16) NOT NULL DEFAULT 'vi-VN',
    lifetime_score  INT NOT NULL DEFAULT 0,
    lifecycle_stage NVARCHAR(32) NOT NULL DEFAULT 'visitor',
    meta_json       NVARCHAR(MAX) NOT NULL DEFAULT '{}',
    created_by      UNIQUEIDENTIFIER REFERENCES users(id) ON DELETE NO ACTION,
    updated_by      UNIQUEIDENTIFIER REFERENCES users(id) ON DELETE NO ACTION,
    created_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    deleted_at      DATETIMEOFFSET
);
CREATE INDEX ix_contacts_tenant_created ON contacts (tenant_id, created_at DESC);
-- Filtered indexes in SQL Server
CREATE INDEX ix_contacts_tenant_phone   ON contacts (tenant_id, phone) WHERE phone IS NOT NULL;
CREATE INDEX ix_contacts_tenant_email   ON contacts (tenant_id, email) WHERE email IS NOT NULL;

CREATE TABLE contact_external_ids (
    id            UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    contact_id    UNIQUEIDENTIFIER NOT NULL REFERENCES contacts(id) ON DELETE CASCADE,
    platform      NVARCHAR(32) NOT NULL,    -- zalo|facebook|tiktok|instagram|youtube
    external_id   NVARCHAR(256) NOT NULL,
    first_seen_at DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    UNIQUE (platform, external_id)
);
CREATE INDEX ix_contact_external_ids_contact ON contact_external_ids (contact_id);

-- ------------- Conversations & Messages -------------

CREATE TABLE conversations (
    id                 UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id          UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    contact_id         UNIQUEIDENTIFIER REFERENCES contacts(id) ON DELETE NO ACTION,
    platform           NVARCHAR(32) NOT NULL,
    external_thread_id NVARCHAR(256) NOT NULL,
    status             NVARCHAR(32) NOT NULL DEFAULT 'open', -- open|pending|resolved|escalated
    assigned_to        UNIQUEIDENTIFIER REFERENCES users(id) ON DELETE NO ACTION,
    last_msg_at        DATETIMEOFFSET,
    created_at         DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at         DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    deleted_at         DATETIMEOFFSET,
    UNIQUE (tenant_id, platform, external_thread_id)
);
CREATE INDEX ix_conv_tenant_status_last ON conversations (tenant_id, status, last_msg_at DESC);
CREATE INDEX ix_conv_contact            ON conversations (contact_id) WHERE contact_id IS NOT NULL;

-- IMMUTABLE message log — no updated_at, no soft-delete
CREATE TABLE messages (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    conversation_id UNIQUEIDENTIFIER NOT NULL REFERENCES conversations(id) ON DELETE NO ACTION, -- Changed from RESTRICT
    tenant_id       UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    direction       NVARCHAR(8)  NOT NULL,  -- in|out
    sender_type     NVARCHAR(16) NOT NULL,  -- contact|user|agent|system
    sender_user_id  UNIQUEIDENTIFIER REFERENCES users(id) ON DELETE NO ACTION,
    content         NVARCHAR(MAX) NOT NULL,
    content_type    NVARCHAR(32) NOT NULL DEFAULT 'text',
    metadata_json   NVARCHAR(MAX) NOT NULL DEFAULT '{}',
    sent_at         DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
CREATE INDEX ix_msg_conv_sent   ON messages (conversation_id, sent_at DESC);
CREATE INDEX ix_msg_tenant_sent ON messages (tenant_id, sent_at DESC);

-- ------------- Leads -------------

CREATE TABLE leads (
    id               UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id        UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    contact_id       UNIQUEIDENTIFIER REFERENCES contacts(id) ON DELETE NO ACTION,
    owner_user_id    UNIQUEIDENTIFIER REFERENCES users(id) ON DELETE NO ACTION,
    score            INT NOT NULL DEFAULT 0,
    stage            NVARCHAR(32) NOT NULL DEFAULT 'cold',  -- cold|warm|hot|customer|lost
    source_platform  NVARCHAR(32),
    last_activity_at DATETIMEOFFSET,
    created_at       DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at       DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    deleted_at       DATETIMEOFFSET
);
CREATE INDEX ix_leads_tenant_stage_score ON leads (tenant_id, stage, score DESC);
CREATE INDEX ix_leads_owner              ON leads (owner_user_id) WHERE owner_user_id IS NOT NULL;
CREATE INDEX ix_leads_contact            ON leads (contact_id) WHERE contact_id IS NOT NULL;

CREATE TABLE lead_scoring_rules (
    id          UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id   UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    platform    NVARCHAR(32),
    event_code  NVARCHAR(64) NOT NULL,
    weight      INT NOT NULL,
    description NVARCHAR(256),
    is_active   BIT NOT NULL DEFAULT 1,
    created_at  DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at  DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
CREATE INDEX ix_score_rules_tenant_event ON lead_scoring_rules (tenant_id, event_code);

-- IMMUTABLE
CREATE TABLE lead_activities (
    id            UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id     UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    lead_id       UNIQUEIDENTIFIER NOT NULL REFERENCES leads(id) ON DELETE NO ACTION,
    activity_type NVARCHAR(64) NOT NULL,
    notes         NVARCHAR(MAX),
    meta_json     NVARCHAR(MAX) NOT NULL DEFAULT '{}',
    occurred_at   DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
CREATE INDEX ix_lead_act_lead_occurred ON lead_activities (lead_id, occurred_at DESC);

-- ------------- Knowledge Base -------------

CREATE TABLE kb_modules (
    id          UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id   UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    code        NVARCHAR(64) NOT NULL,    -- KB-01..KB-06
    name        NVARCHAR(256) NOT NULL,
    description NVARCHAR(MAX),
    owner_role  NVARCHAR(64),
    status      NVARCHAR(32) NOT NULL DEFAULT 'active',
    created_at  DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at  DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    deleted_at  DATETIMEOFFSET,
    UNIQUE (tenant_id, code)
);

CREATE TABLE kb_versions (
    id             UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    kb_module_id   UNIQUEIDENTIFIER NOT NULL REFERENCES kb_modules(id) ON DELETE CASCADE,
    version        INT NOT NULL,
    content_md     NVARCHAR(MAX) NOT NULL,
    -- SQL Server currently requires workarounds for vector types (e.g. storing as JSON array)
    embedding      NVARCHAR(MAX), 
    accuracy_score DECIMAL(5,2),
    status         NVARCHAR(32) NOT NULL DEFAULT 'draft',  -- draft|deployed|archived
    deployed_at    DATETIMEOFFSET,
    created_by     UNIQUEIDENTIFIER REFERENCES users(id) ON DELETE NO ACTION,
    created_at     DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    UNIQUE (kb_module_id, version)
);
CREATE INDEX ix_kb_versions_module_ver ON kb_versions (kb_module_id, version DESC);

CREATE TABLE kb_test_cases (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    kb_module_id    UNIQUEIDENTIFIER NOT NULL REFERENCES kb_modules(id) ON DELETE CASCADE,
    question        NVARCHAR(MAX) NOT NULL,
    expected_answer NVARCHAR(MAX) NOT NULL,
    is_active       BIT NOT NULL DEFAULT 1,
    created_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
CREATE INDEX ix_kb_test_module ON kb_test_cases (kb_module_id);

-- ------------- Chat Scenarios -------------

CREATE TABLE chat_scenarios (
    id                UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id         UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    code              NVARCHAR(32) NOT NULL,    -- KB-001..KB-050
    group_name        NVARCHAR(64) NOT NULL,
    trigger_text      NVARCHAR(MAX) NOT NULL,   -- 'trigger' is a reserved keyword
    response_template NVARCHAR(MAX) NOT NULL,
    tone_voice        NVARCHAR(32),
    platforms         NVARCHAR(128) NOT NULL,   -- csv: zalo,facebook,tiktok,instagram,youtube
    success_rate      DECIMAL(5,2),
    created_at        DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at        DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    UNIQUE (tenant_id, code)
);
CREATE INDEX ix_scenarios_tenant_group ON chat_scenarios (tenant_id, group_name);

-- ------------- Agents -------------

CREATE TABLE agents (
    id               UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id        UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    code             NVARCHAR(64) NOT NULL,
    display_name     NVARCHAR(256) NOT NULL,
    agent_type       NVARCHAR(32) NOT NULL,    -- chat|sale_assist|lead|content|docs|ads|report|research
    model            NVARCHAR(128) NOT NULL,
    status           NVARCHAR(32) NOT NULL DEFAULT 'stopped',  -- running|stopped|error
    skill_files_json NVARCHAR(MAX) NOT NULL DEFAULT '[]',
    kb_modules_json  NVARCHAR(MAX) NOT NULL DEFAULT '[]',
    config_json      NVARCHAR(MAX) NOT NULL DEFAULT '{}',
    created_at       DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at       DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    deleted_at       DATETIMEOFFSET,
    UNIQUE (tenant_id, code)
);
CREATE INDEX ix_agents_tenant_type ON agents (tenant_id, agent_type);

CREATE TABLE agent_sessions (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id       UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    agent_id        UNIQUEIDENTIFIER REFERENCES agents(id) ON DELETE NO ACTION,
    conversation_id UNIQUEIDENTIFIER REFERENCES conversations(id) ON DELETE NO ACTION,
    goal            NVARCHAR(256),
    status          NVARCHAR(32) NOT NULL DEFAULT 'pending',
    plan_json       NVARCHAR(MAX) NOT NULL DEFAULT '{}',
    started_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    finished_at     DATETIMEOFFSET
);
CREATE INDEX ix_agent_sessions_tenant_started ON agent_sessions (tenant_id, started_at DESC);
CREATE INDEX ix_agent_sessions_conv           ON agent_sessions (conversation_id) WHERE conversation_id IS NOT NULL;

-- IMMUTABLE
CREATE TABLE agent_traces (
    id          UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    session_id  UNIQUEIDENTIFIER NOT NULL REFERENCES agent_sessions(id) ON DELETE CASCADE,
    task_id     NVARCHAR(64),
    agent_name  NVARCHAR(64),
    phase       NVARCHAR(32),
    message     NVARCHAR(MAX),
    occurred_at DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
CREATE INDEX ix_agent_traces_session_occurred ON agent_traces (session_id, occurred_at);

-- ------------- Sale Assist -------------

CREATE TABLE quick_reply_templates (
    id          UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id   UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    code        NVARCHAR(64) NOT NULL,
    category    NVARCHAR(64),
    body        NVARCHAR(MAX) NOT NULL,
    platforms   NVARCHAR(128),
    created_at  DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at  DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    UNIQUE (tenant_id, code)
);

-- ------------- Documents -------------

CREATE TABLE document_templates (
    id            UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id     UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    code          NVARCHAR(64) NOT NULL,
    doc_type      NVARCHAR(32) NOT NULL,  -- quote|brochure|slide|onboarding
    template_html NVARCHAR(MAX) NOT NULL,
    fields_json   NVARCHAR(MAX) NOT NULL DEFAULT '{}',
    created_at    DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at    DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    deleted_at    DATETIMEOFFSET,
    UNIQUE (tenant_id, code)
);

CREATE TABLE generated_documents (
    id           UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id    UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    contact_id   UNIQUEIDENTIFIER REFERENCES contacts(id) ON DELETE NO ACTION,
    template_id  UNIQUEIDENTIFIER NOT NULL REFERENCES document_templates(id) ON DELETE NO ACTION,
    generated_by UNIQUEIDENTIFIER REFERENCES users(id) ON DELETE NO ACTION,
    file_url     NVARCHAR(512) NOT NULL,
    file_hash    NVARCHAR(128),
    sent_via     NVARCHAR(32),
    sent_at      DATETIMEOFFSET,
    opened_at    DATETIMEOFFSET,
    created_at   DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
CREATE INDEX ix_gen_docs_tenant_created ON generated_documents (tenant_id, created_at DESC);
CREATE INDEX ix_gen_docs_contact        ON generated_documents (contact_id) WHERE contact_id IS NOT NULL;

-- ------------- Content -------------

CREATE TABLE content_briefs (
    id         UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id  UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    platform   NVARCHAR(32) NOT NULL,
    brief      NVARCHAR(MAX) NOT NULL,
    status     NVARCHAR(32) NOT NULL DEFAULT 'pending',
    created_by UNIQUEIDENTIFIER REFERENCES users(id) ON DELETE NO ACTION,
    created_at DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
CREATE INDEX ix_content_briefs_tenant_status ON content_briefs (tenant_id, status);

CREATE TABLE content_items (
    id          UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id   UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    brief_id    UNIQUEIDENTIFIER REFERENCES content_briefs(id) ON DELETE NO ACTION,
    platform    NVARCHAR(32) NOT NULL,
    status      NVARCHAR(32) NOT NULL DEFAULT 'draft',  -- draft|approved|scheduled|published|rejected
    body        NVARCHAR(MAX) NOT NULL,
    assets_json NVARCHAR(MAX) NOT NULL DEFAULT '[]',
    created_by  UNIQUEIDENTIFIER REFERENCES users(id) ON DELETE NO ACTION,
    approved_by UNIQUEIDENTIFIER REFERENCES users(id) ON DELETE NO ACTION,
    approved_at DATETIMEOFFSET,
    created_at  DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at  DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    deleted_at  DATETIMEOFFSET
);
CREATE INDEX ix_content_items_tenant_status ON content_items (tenant_id, status, created_at DESC);

CREATE TABLE content_schedule (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id       UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    content_item_id UNIQUEIDENTIFIER NOT NULL REFERENCES content_items(id) ON DELETE NO ACTION,
    platform        NVARCHAR(32) NOT NULL,
    scheduled_at    DATETIMEOFFSET NOT NULL,
    posted_at       DATETIMEOFFSET,
    status          NVARCHAR(32) NOT NULL DEFAULT 'pending', -- pending|posted|failed
    post_url        NVARCHAR(512),
    created_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
CREATE INDEX ix_content_sched_tenant_scheduled ON content_schedule (tenant_id, scheduled_at);

-- ------------- Ads -------------

CREATE TABLE ads_campaigns (
    id                   UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id            UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    platform             NVARCHAR(32) NOT NULL,    -- meta|tiktok
    external_campaign_id NVARCHAR(128) NOT NULL,
    objective            NVARCHAR(64),
    daily_budget         DECIMAL(12,2),
    status               NVARCHAR(32),
    synced_at            DATETIMEOFFSET,
    created_at           DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at           DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    UNIQUE (tenant_id, platform, external_campaign_id)
);
CREATE INDEX ix_ads_campaigns_tenant ON ads_campaigns (tenant_id, status);

CREATE TABLE ads_rules (
    id          UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id   UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    platform    NVARCHAR(32) NOT NULL,
    metric      NVARCHAR(64) NOT NULL,      -- cpl|frequency|ctr|spend
    comparator  NVARCHAR(8) NOT NULL,       -- gt|lt|gte|lte|eq
    threshold   DECIMAL(12,4) NOT NULL,
    action      NVARCHAR(32) NOT NULL,      -- pause|scale_up|scale_down|alert
    is_active   BIT NOT NULL DEFAULT 1,
    created_at  DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at  DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
CREATE INDEX ix_ads_rules_tenant_active ON ads_rules (tenant_id, is_active);

-- IMMUTABLE
CREATE TABLE ads_actions (
    id           UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id    UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    campaign_id  UNIQUEIDENTIFIER NOT NULL REFERENCES ads_campaigns(id) ON DELETE NO ACTION,
    rule_id      UNIQUEIDENTIFIER REFERENCES ads_rules(id) ON DELETE NO ACTION,
    action_taken NVARCHAR(32) NOT NULL,
    payload_json NVARCHAR(MAX) NOT NULL DEFAULT '{}',
    executed_at  DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
CREATE INDEX ix_ads_actions_campaign ON ads_actions (campaign_id, executed_at DESC);

-- ------------- Analytics -------------

-- IMMUTABLE daily snapshot
CREATE TABLE kpi_daily (
    id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id           UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    date                DATE NOT NULL,
    platform            NVARCHAR(32) NOT NULL,
    leads               INT NOT NULL DEFAULT 0,
    dms                 INT NOT NULL DEFAULT 0,
    replies             INT NOT NULL DEFAULT 0,
    conversions         INT NOT NULL DEFAULT 0,
    avg_response_time_sec DECIMAL(10,2),
    ad_spend            DECIMAL(12,2),
    created_at          DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    UNIQUE (tenant_id, date, platform)
);
CREATE INDEX ix_kpi_tenant_date ON kpi_daily (tenant_id, date DESC, platform);

-- ------------- Channel & LLM configs -------------

CREATE TABLE pancake_configs (
    id                       UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id                UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    channel                  NVARCHAR(32) NOT NULL,
    page_id                  NVARCHAR(128) NOT NULL,
    access_token_encrypted   NVARCHAR(MAX) NOT NULL,
    webhook_secret_encrypted NVARCHAR(MAX) NOT NULL,
    is_active                BIT NOT NULL DEFAULT 1,
    created_at               DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at               DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    UNIQUE (tenant_id, channel, page_id)
);

CREATE TABLE llm_configs (
    id                 UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id          UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    provider           NVARCHAR(32) NOT NULL,
    model_id           NVARCHAR(128) NOT NULL,
    api_key_encrypted  NVARCHAR(MAX) NOT NULL,
    base_url           NVARCHAR(512),
    is_active          BIT NOT NULL DEFAULT 1,
    max_tokens         INT,
    temperature        DECIMAL(3,2),
    created_at         DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at         DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
CREATE INDEX ix_llm_configs_tenant_active ON llm_configs (tenant_id, is_active);

-- =============================================================
-- Seed bootstrap permissions (idempotent)
-- =============================================================
MERGE INTO permissions AS target
USING (VALUES
    ('inbox.read',     'View unified inbox'),
    ('inbox.assign',   'Assign conversation'),
    ('kb.read',        'Read Knowledge Base'),
    ('kb.write',       'Edit Knowledge Base'),
    ('kb.deploy',      'Deploy KB version'),
    ('agent.read',     'View agents'),
    ('agent.manage',   'Start / stop / configure agent'),
    ('lead.read',      'Read leads'),
    ('lead.write',     'Edit leads'),
    ('content.read',   'Read content'),
    ('content.write',  'Author content'),
    ('content.approve','Approve content'),
    ('docs.generate',  'Generate documents'),
    ('ads.read',       'View ads campaigns'),
    ('ads.manage',     'Configure ads rules'),
    ('analytics.read', 'View analytics'),
    ('admin.system',   'System administration'),
    ('admin.audit',    'View audit logs')
) AS source (code, description)
ON target.code = source.code
WHEN NOT MATCHED THEN
    INSERT (code, description) VALUES (source.code, source.description);