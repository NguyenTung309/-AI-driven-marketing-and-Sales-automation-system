```sql
CREATE TABLE tenants (
    id uniqueidentifier PRIMARY KEY,
    slug nvarchar(64) NOT NULL,
    display_name nvarchar(256) NOT NULL,
    brand_name nvarchar(256) NULL,
    logo_url nvarchar(512) NULL,
    primary_color nvarchar(16) NULL,
    accent_color nvarchar(16) NULL,
    support_name nvarchar(256) NULL,
    widget_greeting nvarchar(1024) NULL,
    plan_name nvarchar(32) NOT NULL,
    is_active bit NOT NULL,
    require_orchestration_approval bit NOT NULL,
    require_content_review bit NOT NULL,
    require_chat_reply_approval bit NOT NULL,
    skip_chat_reply_review bit NOT NULL,
    monthly_cost_cap_usd decimal(12,2) NULL,
    ai_auto_reply_resume_minutes int NOT NULL,
    idle_alert_minutes int NOT NULL,
    created_at datetimeoffset NOT NULL
);

CREATE TABLE users (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    display_name nvarchar(256) NULL,
    avatar_url nvarchar(512) NULL,
    is_active bit NOT NULL,
    date_of_birth date NULL,
    last_login_at datetimeoffset NULL,
    pancake_access_token_encrypted nvarchar(2048) NULL,
    pancake_access_token_updated_at datetimeoffset NULL,
    user_name nvarchar(256) NULL,
    normalized_user_name nvarchar(256) NULL,
    email nvarchar(256) NULL,
    normalized_email nvarchar(256) NULL,
    email_confirmed bit NOT NULL,
    password_hash nvarchar(max) NULL,
    security_stamp nvarchar(max) NULL,
    concurrency_stamp nvarchar(max) NULL,
    phone_number nvarchar(max) NULL,
    phone_number_confirmed bit NOT NULL,
    two_factor_enabled bit NOT NULL,
    lockout_end datetimeoffset NULL,
    lockout_enabled bit NOT NULL,
    access_failed_count int NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE AspNetRoles (
    id uniqueidentifier PRIMARY KEY,
    name nvarchar(256) NULL,
    normalized_name nvarchar(256) NULL,
    concurrency_stamp nvarchar(max) NULL
);

CREATE TABLE AspNetUserRoles (
    user_id uniqueidentifier NOT NULL,
    role_id uniqueidentifier NOT NULL,
    PRIMARY KEY (user_id, role_id),
    FOREIGN KEY (user_id) REFERENCES users(id),
    FOREIGN KEY (role_id) REFERENCES AspNetRoles(id)
);

CREATE TABLE AspNetUserClaims (
    id int PRIMARY KEY,
    user_id uniqueidentifier NOT NULL,
    claim_type nvarchar(max) NULL,
    claim_value nvarchar(max) NULL,
    FOREIGN KEY (user_id) REFERENCES users(id)
);

CREATE TABLE AspNetRoleClaims (
    id int PRIMARY KEY,
    role_id uniqueidentifier NOT NULL,
    claim_type nvarchar(max) NULL,
    claim_value nvarchar(max) NULL,
    FOREIGN KEY (role_id) REFERENCES AspNetRoles(id)
);

CREATE TABLE AspNetUserLogins (
    login_provider nvarchar(450) NOT NULL,
    provider_key nvarchar(450) NOT NULL,
    provider_display_name nvarchar(max) NULL,
    user_id uniqueidentifier NOT NULL,
    PRIMARY KEY (login_provider, provider_key),
    FOREIGN KEY (user_id) REFERENCES users(id)
);

CREATE TABLE AspNetUserTokens (
    user_id uniqueidentifier NOT NULL,
    login_provider nvarchar(450) NOT NULL,
    name nvarchar(450) NOT NULL,
    value nvarchar(max) NULL,
    PRIMARY KEY (user_id, login_provider, name),
    FOREIGN KEY (user_id) REFERENCES users(id)
);

CREATE TABLE refresh_tokens (
    id uniqueidentifier PRIMARY KEY,
    user_id uniqueidentifier NOT NULL,
    token_hash nvarchar(128) NOT NULL,
    family_id uniqueidentifier NOT NULL,
    created_ip nvarchar(max) NULL,
    replaced_by uniqueidentifier NULL,
    created_at datetimeoffset NOT NULL,
    expires_at datetimeoffset NOT NULL,
    revoked_at datetimeoffset NULL,
    FOREIGN KEY (user_id) REFERENCES users(id)
);

CREATE TABLE roles (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    name nvarchar(64) NOT NULL,
    description nvarchar(max) NULL,
    is_system bit NOT NULL,
    created_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE permissions (
    id uniqueidentifier PRIMARY KEY,
    code nvarchar(128) NOT NULL,
    description nvarchar(max) NULL
);

CREATE TABLE role_permissions (
    role_id uniqueidentifier NOT NULL,
    permission_id uniqueidentifier NOT NULL,
    PRIMARY KEY (role_id, permission_id),
    FOREIGN KEY (role_id) REFERENCES roles(id),
    FOREIGN KEY (permission_id) REFERENCES permissions(id)
);

CREATE TABLE api_keys (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    name nvarchar(128) NOT NULL,
    key_hash nvarchar(max) NOT NULL,
    scopes_json nvarchar(max) NOT NULL,
    created_by uniqueidentifier NULL,
    created_at datetimeoffset NOT NULL,
    expires_at datetimeoffset NULL,
    revoked_at datetimeoffset NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE audit_logs (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    action nvarchar(64) NOT NULL,
    resource_type nvarchar(64) NOT NULL,
    resource_id uniqueidentifier NULL,
    user_id uniqueidentifier NULL,
    diff_json nvarchar(max) NULL,
    ip_address nvarchar(45) NULL,
    user_agent nvarchar(max) NULL,
    occurred_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (user_id) REFERENCES users(id)
);

CREATE TABLE notifications (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    user_id uniqueidentifier NULL,
    type nvarchar(40) NOT NULL,
    severity nvarchar(10) NOT NULL,
    title nvarchar(256) NOT NULL,
    body nvarchar(max) NULL,
    link nvarchar(256) NULL,
    is_read bit NOT NULL,
    read_at datetimeoffset NULL,
    created_at datetimeoffset NOT NULL,
    group_key nvarchar(128) NULL,
    occurrence_count int NOT NULL,
    last_occurred_at datetimeoffset NULL,
    email_sent_at datetimeoffset NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (user_id) REFERENCES users(id)
);

CREATE TABLE notification_preferences (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    user_id uniqueidentifier NOT NULL,
    type nvarchar(64) NOT NULL,
    in_app bit NOT NULL,
    push bit NOT NULL,
    email bit NOT NULL,
    updated_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (user_id) REFERENCES users(id)
);

CREATE TABLE push_subscriptions (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    user_id uniqueidentifier NOT NULL,
    endpoint nvarchar(512) NOT NULL,
    p256dh nvarchar(256) NOT NULL,
    auth nvarchar(128) NOT NULL,
    created_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (user_id) REFERENCES users(id)
);

CREATE TABLE background_jobs (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    user_id uniqueidentifier NULL,
    type nvarchar(64) NOT NULL,
    title nvarchar(200) NOT NULL,
    status nvarchar(20) NOT NULL,
    progress int NOT NULL,
    progress_note nvarchar(200) NULL,
    payload_json nvarchar(max) NULL,
    result_link nvarchar(400) NULL,
    result_summary nvarchar(max) NULL,
    error nvarchar(1000) NULL,
    hangfire_job_id nvarchar(64) NULL,
    idempotency_key nvarchar(128) NULL,
    cancel_requested bit NOT NULL,
    created_at datetimeoffset NOT NULL,
    started_at datetimeoffset NULL,
    finished_at datetimeoffset NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (user_id) REFERENCES users(id)
);

CREATE TABLE contacts (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    display_name nvarchar(256) NOT NULL,
    phone nvarchar(32) NULL,
    email nvarchar(256) NULL,
    locale nvarchar(16) NOT NULL,
    lifecycle_stage nvarchar(32) NOT NULL,
    lifetime_score int NOT NULL,
    created_at datetimeoffset NOT NULL,
    deleted_at datetimeoffset NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE contact_external_ids (
    id uniqueidentifier PRIMARY KEY,
    contact_id uniqueidentifier NOT NULL,
    platform nvarchar(32) NOT NULL,
    external_id nvarchar(256) NOT NULL,
    first_seen_at datetimeoffset NOT NULL,
    FOREIGN KEY (contact_id) REFERENCES contacts(id)
);

CREATE TABLE contact_memories (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    contact_id uniqueidentifier NOT NULL,
    fact nvarchar(1024) NOT NULL,
    category nvarchar(32) NOT NULL,
    confidence decimal(3,2) NOT NULL,
    source_conversation_id uniqueidentifier NULL,
    is_active bit NOT NULL,
    superseded_by_id uniqueidentifier NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (contact_id) REFERENCES contacts(id)
);

CREATE TABLE leads (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    contact_id uniqueidentifier NULL,
    owner_user_id uniqueidentifier NULL,
    stage nvarchar(32) NOT NULL,
    source_platform nvarchar(32) NULL,
    score int NOT NULL,
    last_activity_at datetimeoffset NULL,
    created_at datetimeoffset NOT NULL,
    deleted_at datetimeoffset NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (contact_id) REFERENCES contacts(id),
    FOREIGN KEY (owner_user_id) REFERENCES users(id)
);

CREATE TABLE lead_activities (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    lead_id uniqueidentifier NOT NULL,
    activity_type nvarchar(64) NOT NULL,
    notes nvarchar(max) NULL,
    meta_json nvarchar(max) NOT NULL,
    occurred_at datetimeoffset NOT NULL,
    FOREIGN KEY (lead_id) REFERENCES leads(id)
);

CREATE TABLE lead_scoring_rules (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    event_code nvarchar(64) NOT NULL,
    platform nvarchar(32) NULL,
    weight int NOT NULL,
    description nvarchar(max) NULL,
    is_active bit NOT NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE drip_sequences (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    name nvarchar(256) NOT NULL,
    description nvarchar(max) NULL,
    trigger_event nvarchar(64) NOT NULL,
    is_active bit NOT NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE drip_sequence_steps (
    id uniqueidentifier PRIMARY KEY,
    sequence_id uniqueidentifier NOT NULL,
    step_order int NOT NULL,
    delay_hours int NOT NULL,
    channel nvarchar(32) NOT NULL,
    template_body nvarchar(max) NOT NULL,
    created_at datetimeoffset NOT NULL,
    FOREIGN KEY (sequence_id) REFERENCES drip_sequences(id)
);

CREATE TABLE drip_enrollments (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    sequence_id uniqueidentifier NOT NULL,
    lead_id uniqueidentifier NOT NULL,
    current_step int NOT NULL,
    next_send_at datetimeoffset NOT NULL,
    status nvarchar(32) NOT NULL,
    enrolled_at datetimeoffset NOT NULL,
    completed_at datetimeoffset NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (sequence_id) REFERENCES drip_sequences(id),
    FOREIGN KEY (lead_id) REFERENCES leads(id)
);

CREATE TABLE conversations (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    contact_id uniqueidentifier NULL,
    assigned_to uniqueidentifier NULL,
    platform nvarchar(32) NOT NULL,
    external_thread_id nvarchar(256) NOT NULL,
    status nvarchar(32) NOT NULL,
    created_at datetimeoffset NOT NULL,
    last_message_at datetimeoffset NULL,
    deleted_at datetimeoffset NULL,
    ai_auto_reply_enabled bit NOT NULL,
    ai_auto_reply_resume_at datetimeoffset NULL,
    row_version rowversion NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (contact_id) REFERENCES contacts(id),
    FOREIGN KEY (assigned_to) REFERENCES users(id)
);

CREATE TABLE messages (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    conversation_id uniqueidentifier NOT NULL,
    direction nvarchar(8) NOT NULL,
    sender_type nvarchar(16) NOT NULL,
    sender_user_id uniqueidentifier NULL,
    content nvarchar(max) NOT NULL,
    content_type nvarchar(32) NULL,
    attachment_url nvarchar(2048) NULL,
    status nvarchar(32) NOT NULL,
    sent_at datetimeoffset NOT NULL,
    FOREIGN KEY (conversation_id) REFERENCES conversations(id),
    FOREIGN KEY (sender_user_id) REFERENCES users(id)
);

CREATE TABLE conversation_notes (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    conversation_id uniqueidentifier NOT NULL,
    created_by_user_id uniqueidentifier NOT NULL,
    created_by_display_name nvarchar(256) NULL,
    content nvarchar(max) NOT NULL,
    type nvarchar(32) NOT NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    FOREIGN KEY (conversation_id) REFERENCES conversations(id)
);

CREATE TABLE labels (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    name nvarchar(128) NOT NULL,
    color nvarchar(32) NOT NULL,
    created_at datetimeoffset NOT NULL,
    deleted_at datetimeoffset NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE conversation_labels (
    conversation_id uniqueidentifier NOT NULL,
    label_id uniqueidentifier NOT NULL,
    created_at datetimeoffset NOT NULL,
    PRIMARY KEY (conversation_id, label_id),
    FOREIGN KEY (conversation_id) REFERENCES conversations(id),
    FOREIGN KEY (label_id) REFERENCES labels(id)
);

CREATE TABLE quick_reply_templates (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    code nvarchar(64) NOT NULL,
    body nvarchar(max) NOT NULL,
    category nvarchar(max) NULL,
    platforms nvarchar(max) NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE agents (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    code nvarchar(64) NOT NULL,
    display_name nvarchar(256) NOT NULL,
    agent_type nvarchar(32) NOT NULL,
    model nvarchar(128) NOT NULL,
    config_json nvarchar(max) NOT NULL,
    kb_modules_json nvarchar(max) NOT NULL,
    skill_files_json nvarchar(max) NOT NULL,
    status nvarchar(max) NOT NULL,
    llm_config_id uniqueidentifier NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    deleted_at datetimeoffset NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE agent_definitions (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    code nvarchar(64) NOT NULL,
    display_name nvarchar(256) NOT NULL,
    agent_type nvarchar(32) NOT NULL,
    persona_prompt nvarchar(max) NOT NULL,
    allowed_tools_json nvarchar(max) NOT NULL,
    input_schema_json nvarchar(max) NOT NULL,
    output_schema_json nvarchar(max) NOT NULL,
    memory_scope nvarchar(32) NOT NULL,
    kb_module_code nvarchar(64) NULL,
    llm_config_id uniqueidentifier NULL,
    is_orchestratable bit NOT NULL,
    version int NOT NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    deleted_at datetimeoffset NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE agent_sessions (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    agent_id uniqueidentifier NULL,
    conversation_id uniqueidentifier NULL,
    user_id uniqueidentifier NULL,
    goal nvarchar(max) NULL,
    plan_json nvarchar(max) NOT NULL,
    status nvarchar(max) NOT NULL,
    requires_approval bit NOT NULL,
    replan_count int NOT NULL,
    started_at datetimeoffset NOT NULL,
    finished_at datetimeoffset NULL,
    archived_at datetimeoffset NULL,
    row_version rowversion NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (agent_id) REFERENCES agents(id),
    FOREIGN KEY (conversation_id) REFERENCES conversations(id),
    FOREIGN KEY (user_id) REFERENCES users(id)
);

CREATE TABLE agent_traces (
    id uniqueidentifier PRIMARY KEY,
    session_id uniqueidentifier NOT NULL,
    agent_name nvarchar(max) NULL,
    phase nvarchar(max) NULL,
    task_id nvarchar(max) NULL,
    message nvarchar(max) NULL,
    occurred_at datetimeoffset NOT NULL,
    FOREIGN KEY (session_id) REFERENCES agent_sessions(id)
);

CREATE TABLE agent_a2a_messages (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    session_id uniqueidentifier NOT NULL,
    from_agent_definition_id uniqueidentifier NULL,
    to_agent_definition_id uniqueidentifier NOT NULL,
    task_id nvarchar(128) NOT NULL,
    intent nvarchar(32) NOT NULL,
    payload_json nvarchar(max) NOT NULL,
    status nvarchar(32) NOT NULL,
    error nvarchar(1024) NULL,
    created_at datetimeoffset NOT NULL,
    processed_at datetimeoffset NULL,
    FOREIGN KEY (session_id) REFERENCES agent_sessions(id),
    FOREIGN KEY (from_agent_definition_id) REFERENCES agent_definitions(id),
    FOREIGN KEY (to_agent_definition_id) REFERENCES agent_definitions(id)
);

CREATE TABLE agent_memories (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    agent_code nvarchar(64) NOT NULL,
    fact nvarchar(1024) NOT NULL,
    category nvarchar(32) NOT NULL,
    confidence decimal(3,2) NOT NULL,
    is_active bit NOT NULL,
    superseded_by_id uniqueidentifier NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE agent_schedules (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    name nvarchar(128) NOT NULL,
    goal_template nvarchar(max) NOT NULL,
    cadence nvarchar(16) NOT NULL,
    cron_expression nvarchar(128) NULL,
    timezone_id nvarchar(128) NOT NULL,
    next_run_at datetimeoffset NOT NULL,
    last_run_at datetimeoffset NULL,
    overlap_policy nvarchar(32) NOT NULL,
    misfire_policy nvarchar(32) NOT NULL,
    requires_approval bit NOT NULL,
    approval_policy_json nvarchar(max) NULL,
    trigger_type nvarchar(16) NOT NULL,
    event_key nvarchar(64) NULL,
    is_active bit NOT NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    deleted_at datetimeoffset NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE agent_schedule_runs (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    schedule_id uniqueidentifier NOT NULL,
    session_id uniqueidentifier NULL,
    window_key nvarchar(128) NOT NULL,
    status nvarchar(32) NOT NULL,
    error nvarchar(1024) NULL,
    started_at datetimeoffset NOT NULL,
    finished_at datetimeoffset NULL,
    FOREIGN KEY (schedule_id) REFERENCES agent_schedules(id),
    FOREIGN KEY (session_id) REFERENCES agent_sessions(id)
);

CREATE TABLE claude_cost_ledger (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    agent_code nvarchar(40) NOT NULL,
    model nvarchar(80) NOT NULL,
    input_tokens int NOT NULL,
    output_tokens int NOT NULL,
    usd decimal(12,6) NOT NULL,
    session_id uniqueidentifier NULL,
    created_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (session_id) REFERENCES agent_sessions(id)
);

CREATE TABLE kb_modules (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    code nvarchar(64) NOT NULL,
    name nvarchar(256) NOT NULL,
    description nvarchar(max) NULL,
    owner_role nvarchar(max) NULL,
    status nvarchar(max) NOT NULL,
    created_at datetimeoffset NOT NULL,
    deleted_at datetimeoffset NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE kb_versions (
    id uniqueidentifier PRIMARY KEY,
    kb_module_id uniqueidentifier NOT NULL,
    version int NOT NULL,
    content_md nvarchar(max) NOT NULL,
    embedding nvarchar(max) NULL,
    status nvarchar(32) NOT NULL,
    accuracy_score decimal(18,2) NULL,
    created_at datetimeoffset NOT NULL,
    deployed_at datetimeoffset NULL,
    FOREIGN KEY (kb_module_id) REFERENCES kb_modules(id)
);

CREATE TABLE kb_test_cases (
    id uniqueidentifier PRIMARY KEY,
    kb_module_id uniqueidentifier NOT NULL,
    question nvarchar(max) NOT NULL,
    expected_answer nvarchar(max) NOT NULL,
    is_active bit NOT NULL,
    created_at datetimeoffset NOT NULL,
    FOREIGN KEY (kb_module_id) REFERENCES kb_modules(id)
);

CREATE TABLE kb_suggestions (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    op nvarchar(16) NOT NULL,
    target_kb_module_id uniqueidentifier NULL,
    title nvarchar(256) NOT NULL,
    content_md nvarchar(max) NOT NULL,
    rationale nvarchar(max) NULL,
    evidence_json nvarchar(max) NULL,
    dedup_hash nvarchar(64) NOT NULL,
    reviewer_verdict nvarchar(16) NULL,
    reviewer_notes nvarchar(max) NULL,
    accuracy_before decimal(5,2) NULL,
    accuracy_after decimal(5,2) NULL,
    status nvarchar(16) NOT NULL,
    approval_mode nvarchar(8) NULL,
    rejected_reason nvarchar(1024) NULL,
    decided_by uniqueidentifier NULL,
    created_at datetimeoffset NOT NULL,
    decided_at datetimeoffset NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (target_kb_module_id) REFERENCES kb_modules(id)
);

CREATE TABLE skill_files (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    name nvarchar(128) NOT NULL,
    description nvarchar(512) NULL,
    content_md nvarchar(max) NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    deleted_at datetimeoffset NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE document_templates (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    code nvarchar(64) NOT NULL,
    doc_type nvarchar(32) NOT NULL,
    fields_json nvarchar(max) NOT NULL,
    template_html nvarchar(max) NOT NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    deleted_at datetimeoffset NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE generated_documents (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    template_id uniqueidentifier NOT NULL,
    contact_id uniqueidentifier NULL,
    file_url nvarchar(512) NOT NULL,
    file_hash nvarchar(max) NULL,
    sent_via nvarchar(32) NULL,
    sent_at datetimeoffset NULL,
    opened_at datetimeoffset NULL,
    generated_by uniqueidentifier NULL,
    created_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (template_id) REFERENCES document_templates(id),
    FOREIGN KEY (contact_id) REFERENCES contacts(id)
);

CREATE TABLE ads_campaigns (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    platform nvarchar(32) NOT NULL,
    external_campaign_id nvarchar(128) NOT NULL,
    daily_budget decimal(18,2) NULL,
    objective nvarchar(max) NULL,
    status nvarchar(32) NULL,
    synced_at datetimeoffset NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE ads_rules (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    platform nvarchar(32) NOT NULL,
    metric nvarchar(64) NOT NULL,
    comparator nvarchar(8) NOT NULL,
    threshold decimal(18,2) NOT NULL,
    action nvarchar(32) NOT NULL,
    is_active bit NOT NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE ads_creatives (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    campaign_id uniqueidentifier NOT NULL,
    external_creative_id nvarchar(128) NOT NULL,
    status nvarchar(16) NOT NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (campaign_id) REFERENCES ads_campaigns(id)
);

CREATE TABLE ads_actions (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    campaign_id uniqueidentifier NOT NULL,
    rule_id uniqueidentifier NULL,
    action_taken nvarchar(32) NOT NULL,
    payload_json nvarchar(max) NOT NULL,
    executed_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (campaign_id) REFERENCES ads_campaigns(id),
    FOREIGN KEY (rule_id) REFERENCES ads_rules(id)
);

CREATE TABLE ads_metrics_daily (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    campaign_id uniqueidentifier NOT NULL,
    metric_date date NOT NULL,
    cpl decimal(18,2) NULL,
    frequency decimal(18,2) NULL,
    ctr decimal(18,2) NULL,
    spend decimal(18,2) NULL,
    created_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (campaign_id) REFERENCES ads_campaigns(id)
);

CREATE TABLE content_briefs (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    platform nvarchar(32) NOT NULL,
    brief nvarchar(max) NOT NULL,
    status nvarchar(32) NOT NULL,
    created_by uniqueidentifier NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE content_items (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    brief_id uniqueidentifier NULL,
    platform nvarchar(32) NOT NULL,
    body nvarchar(max) NOT NULL,
    assets_json nvarchar(max) NOT NULL,
    status nvarchar(32) NOT NULL,
    rejected_reason nvarchar(1024) NULL,
    approved_by_agent_id uniqueidentifier NULL,
    approved_by uniqueidentifier NULL,
    approved_at datetimeoffset NULL,
    created_by uniqueidentifier NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    deleted_at datetimeoffset NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (brief_id) REFERENCES content_briefs(id)
);

CREATE TABLE content_schedule (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    content_item_id uniqueidentifier NOT NULL,
    meta_asset_id uniqueidentifier NULL,
    platform nvarchar(32) NOT NULL,
    scheduled_at datetimeoffset NOT NULL,
    posted_at datetimeoffset NULL,
    post_url nvarchar(512) NULL,
    status nvarchar(32) NOT NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (content_item_id) REFERENCES content_items(id)
);

CREATE TABLE social_credentials (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    provider nvarchar(32) NOT NULL,
    page_id nvarchar(128) NULL,
    credentials_encrypted nvarchar(max) NOT NULL,
    is_active bit NOT NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    deleted_at datetimeoffset NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE meta_connections (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    client_business_id nvarchar(128) NOT NULL,
    system_user_id nvarchar(128) NOT NULL,
    token_type nvarchar(64) NOT NULL,
    access_token_encrypted nvarchar(max) NOT NULL,
    granted_scopes_json nvarchar(max) NOT NULL,
    expires_at datetimeoffset NULL,
    data_access_expires_at datetimeoffset NULL,
    last_validated_at datetimeoffset NULL,
    status nvarchar(32) NOT NULL,
    last_error nvarchar(1024) NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE meta_assets (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    connection_id uniqueidentifier NOT NULL,
    asset_type nvarchar(32) NOT NULL,
    external_id nvarchar(128) NOT NULL,
    name nvarchar(256) NOT NULL,
    tasks_json nvarchar(max) NOT NULL,
    access_token_encrypted nvarchar(max) NOT NULL,
    is_default bit NOT NULL,
    is_active bit NOT NULL,
    last_synced_at datetimeoffset NOT NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (connection_id) REFERENCES meta_connections(id)
);

CREATE TABLE meta_oauth_states (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    user_id uniqueidentifier NOT NULL,
    state_hash nvarchar(64) NOT NULL,
    expires_at datetimeoffset NOT NULL,
    consumed_at datetimeoffset NULL,
    created_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (user_id) REFERENCES users(id)
);

CREATE TABLE pancake_configs (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    base_url nvarchar(256) NOT NULL,
    access_token_encrypted nvarchar(2048) NOT NULL,
    webhook_secret_encrypted nvarchar(2048) NOT NULL,
    signature_header nvarchar(64) NOT NULL,
    signature_algo nvarchar(32) NOT NULL,
    signature_encoding nvarchar(16) NOT NULL,
    send_path_template nvarchar(512) NOT NULL,
    auth_mode nvarchar(16) NOT NULL,
    is_active bit NOT NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE pancake_pages (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    page_id nvarchar(128) NOT NULL,
    name nvarchar(256) NOT NULL,
    platform nvarchar(64) NOT NULL,
    page_access_token_encrypted nvarchar(2048) NOT NULL,
    page_token_minted_at datetimeoffset NULL,
    is_active bit NOT NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    deleted_at datetimeoffset NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE llm_configs (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    provider nvarchar(32) NOT NULL,
    model_id nvarchar(128) NOT NULL,
    display_name nvarchar(128) NULL,
    api_key_encrypted nvarchar(max) NOT NULL,
    base_url nvarchar(512) NULL,
    input_usd_per_1m decimal(10,4) NULL,
    output_usd_per_1m decimal(10,4) NULL,
    timeout_seconds int NULL,
    max_output_tokens int NULL,
    is_active bit NOT NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE embedding_configs (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    provider nvarchar(32) NOT NULL,
    model_id nvarchar(128) NOT NULL,
    display_name nvarchar(128) NULL,
    api_key_encrypted nvarchar(max) NOT NULL,
    base_url nvarchar(512) NULL,
    dimension int NOT NULL,
    is_active bit NOT NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE processed_messages (
    id uniqueidentifier PRIMARY KEY,
    platform nvarchar(50) NOT NULL,
    external_message_id nvarchar(255) NOT NULL,
    conversation_external_id nvarchar(255) NOT NULL,
    processed_at datetime2 NOT NULL
);

CREATE TABLE competitor_sources (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    name nvarchar(200) NOT NULL,
    url nvarchar(1024) NOT NULL,
    source_type nvarchar(16) NOT NULL,
    is_active bit NOT NULL,
    created_at datetimeoffset NOT NULL,
    last_scanned_at datetimeoffset NULL,
    deleted_at datetimeoffset NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE competitor_posts (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    source_id uniqueidentifier NOT NULL,
    url nvarchar(1024) NOT NULL,
    title nvarchar(512) NOT NULL,
    snippet nvarchar(1024) NULL,
    published_at datetimeoffset NOT NULL,
    detected_at datetimeoffset NOT NULL,
    content_hash nvarchar(64) NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (source_id) REFERENCES competitor_sources(id)
);

CREATE TABLE kpi_daily (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    platform nvarchar(32) NOT NULL,
    date date NOT NULL,
    leads int NOT NULL,
    dms int NOT NULL,
    replies int NOT NULL,
    conversions int NOT NULL,
    ad_spend decimal(18,2) NULL,
    avg_response_time_sec decimal(18,2) NULL,
    created_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE kpi_forecast (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    platform nvarchar(32) NOT NULL,
    metric nvarchar(64) NOT NULL,
    forecast_date date NOT NULL,
    value decimal(18,2) NOT NULL,
    lower_bound decimal(18,2) NOT NULL,
    upper_bound decimal(18,2) NOT NULL,
    generated_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE experiments (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    code nvarchar(64) NOT NULL,
    target_type nvarchar(32) NOT NULL,
    target_id uniqueidentifier NOT NULL,
    name nvarchar(256) NOT NULL,
    status nvarchar(32) NOT NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NULL,
    deleted_at datetimeoffset NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE experiment_variants (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    experiment_id uniqueidentifier NOT NULL,
    code nvarchar(32) NOT NULL,
    name nvarchar(256) NOT NULL,
    weight int NOT NULL,
    chat_scenario_id uniqueidentifier NULL,
    kb_version_id uniqueidentifier NULL,
    created_at datetimeoffset NOT NULL,
    FOREIGN KEY (experiment_id) REFERENCES experiments(id)
);

CREATE TABLE experiment_assignments (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    experiment_id uniqueidentifier NOT NULL,
    variant_id uniqueidentifier NOT NULL,
    subject_key nvarchar(256) NOT NULL,
    assigned_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (experiment_id) REFERENCES experiments(id),
    FOREIGN KEY (variant_id) REFERENCES experiment_variants(id)
);

CREATE TABLE experiment_events (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    experiment_id uniqueidentifier NOT NULL,
    variant_id uniqueidentifier NOT NULL,
    subject_key nvarchar(256) NOT NULL,
    event_type nvarchar(32) NOT NULL,
    value decimal(18,2) NULL,
    occurred_at datetimeoffset NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (experiment_id) REFERENCES experiments(id),
    FOREIGN KEY (variant_id) REFERENCES experiment_variants(id)
);

CREATE TABLE inboxes (
    id uniqueidentifier PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    name nvarchar(256) NOT NULL,
    platform nvarchar(32) NOT NULL,
    external_page_id nvarchar(128) NOT NULL,
    avatar_url nvarchar(512) NULL,
    encrypted_access_token nvarchar(1024) NULL,
    sender_id nvarchar(max) NULL,
    is_active bit NOT NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    deleted_at datetimeoffset NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE inbox_members (
    inbox_id uniqueidentifier NOT NULL,
    agent_id uniqueidentifier NOT NULL,
    tenant_id uniqueidentifier NOT NULL,
    PRIMARY KEY (inbox_id, agent_id),
    FOREIGN KEY (inbox_id) REFERENCES inboxes(id),
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE channel_tokens (
    inbox_id uniqueidentifier PRIMARY KEY,
    access_token_encrypted nvarchar(max) NOT NULL,
    refresh_token_encrypted nvarchar(max) NULL,
    webhook_secret_encrypted nvarchar(max) NOT NULL,
    token_expires_at datetimeoffset NULL,
    is_active bit NOT NULL,
    created_at datetimeoffset NOT NULL,
    updated_at datetimeoffset NOT NULL,
    FOREIGN KEY (inbox_id) REFERENCES inboxes(id)
);
```
